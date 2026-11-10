using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml;
using System.Xml.XPath;
using FatcaValidator.Core.Ffi;

namespace FatcaValidator.Core;

/// <summary>One row of the IRS FATCA FFI list.</summary>
/// <param name="Giin">The GIIN.</param>
/// <param name="Name">The financial institution's name as the IRS lists it.</param>
/// <param name="Country">The country or jurisdiction as the IRS lists it.</param>
public sealed record FfiListEntry(string Giin, string Name, string Country);

/// <summary>
/// The IRS FATCA Foreign Financial Institution (FFI) list, loaded for GIIN checks (see
/// <see cref="ValidatorEngine.FfiList"/>). The IRS publishes it on the 1st of each month as FFIListFullXml.zip or
/// FFIListFull.xml; both are accepted.
/// </summary>
/// <remarks>The whole list is kept in memory: for the November 2026 list (526,554 GIINs) about 150 MB.</remarks>
public sealed class FfiList
{
    private readonly Dictionary<string, FfiListEntry> _entries;

    private FfiList(string source, Dictionary<string, FfiListEntry> entries, long leftOutRows, DateTime? fileTimestamp)
    {
        Source = source;
        _entries = entries;
        LeftOutRows = leftOutRows;
        FileTimestamp = fileTimestamp;
    }

    /// <summary>The full path of the list file.</summary>
    public string Source { get; }

    /// <summary>How many GIINs the list holds.</summary>
    public int Count => _entries.Count;

    /// <summary>Rows left out while loading: a malformed GIIN, or an empty or overlong name or country.</summary>
    public long LeftOutRows { get; }

    /// <summary>The time of the XML inside a zip, the only date an IRS list file carries; null for a plain XML file.</summary>
    public DateTime? FileTimestamp { get; }

    /// <summary>Loads a list file: the IRS's zip (one XML inside) or its XML.</summary>
    /// <exception cref="FfiListException">The file cannot be read or is not the FFI list's structure.</exception>
    public static FfiList Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string fullPath = Path.GetFullPath(path);
        try
        {
            FfiListFile file = FfiListFile.Open(fullPath);
            var statistics = new FfiReadStatistics();
            var entries = new Dictionary<string, FfiListEntry>(StringComparer.Ordinal);
            foreach (FfiRow row in file.ReadRows(statistics))
            {
                entries.TryAdd(row.Giin, new FfiListEntry(row.Giin, row.Name, row.Country));
            }

            if (entries.Count == 0)
            {
                throw new FfiListException($"The FFI list '{fullPath}' holds no GIINs.");
            }

            return new FfiList(fullPath, entries, statistics.LeftOutRows, file.XmlEntryTimestamp);
        }
        catch (Exception ex) when (ex is FfiListFormatException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new FfiListException($"The FFI list '{fullPath}' could not be read: {ex.Message}", ex);
        }
    }

    /// <summary>Looks up a GIIN (exact, upper case).</summary>
    public bool TryGetEntry(string giin, [NotNullWhen(true)] out FfiListEntry? entry) => _entries.TryGetValue(giin, out entry);

    /// <summary>A short description for messages and reports, for example "FFIListFullXml.zip, 526,554 GIINs, file dated 2026-10-27".</summary>
    public string Describe()
    {
        string dated = FileTimestamp is { } time ? $", file dated {time:yyyy-MM-dd}" : "";
        return $"{Path.GetFileName(Source)}, {Count.ToString("N0", CultureInfo.InvariantCulture)} GIINs{dated}";
    }
}

/// <summary>An FFI list file could not be read.</summary>
public sealed class FfiListException : Exception
{
    /// <summary>Creates the exception.</summary>
    public FfiListException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with its cause.</summary>
    public FfiListException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Codes of the GIIN checks against the IRS FFI list.</summary>
public static class GiinCheckCodes
{
    /// <summary>A field that should hold a GIIN does not hold a well-formed one.</summary>
    public const string Format = "FFI-GIIN-FORMAT";

    /// <summary>The GIIN is not on the FFI list.</summary>
    public const string NotListed = "FFI-GIIN-NOT-LISTED";

    /// <summary>The name in the report differs from the name the FFI list gives the GIIN.</summary>
    public const string NameDiffers = "FFI-NAME-DIFFERS";
}

/// <summary>
/// Checks the GIINs of a FATCA XML v2 report against the FFI list: the sender, the reporting FI, sponsors and
/// intermediaries. Everything is a warning, because a list is a monthly snapshot: an institution approved after the
/// list's cutoff only appears the month after.
/// </summary>
internal static class GiinChecks
{
    private const string Ftc = "urn:oecd:ties:fatca:v2";
    private const string Sfa = "urn:oecd:ties:stffatcatypes:v2";

    // An intermediary may have no GIIN (the IRS's own sample uses a plain TIN), so its TIN is only looked up when it is one.
    private static readonly Field[] Fields =
    [
        new("/ftc:FATCA_OECD/ftc:MessageSpec/sfa:SendingCompanyIN", "MessageSpec/SendingCompanyIN", NamePath: null, GiinRequired: true),
        new("/ftc:FATCA_OECD/ftc:FATCA/ftc:ReportingFI/sfa:TIN", "ReportingFI/TIN", "../sfa:Name", GiinRequired: true),
        new("/ftc:FATCA_OECD/ftc:FATCA/ftc:ReportingGroup/ftc:Sponsor/sfa:TIN", "Sponsor/TIN", "../sfa:Name", GiinRequired: true),
        new("/ftc:FATCA_OECD/ftc:FATCA/ftc:ReportingGroup/ftc:Intermediary/sfa:TIN", "Intermediary/TIN", "../sfa:Name", GiinRequired: false),
    ];

    public static void Run(XPathNavigator document, FfiList list, IssueCollector issues)
    {
        var ns = new XmlNamespaceManager(document.NameTable);
        ns.AddNamespace("ftc", Ftc);
        ns.AddNamespace("sfa", Sfa);

        foreach (Field field in Fields)
        {
            XPathNodeIterator nodes = document.Select(field.Path, ns);
            while (nodes.MoveNext())
            {
                Check(nodes.Current!.Clone(), field, list, ns, issues);
            }
        }
    }

    private static void Check(XPathNavigator node, Field field, FfiList list, XmlNamespaceManager ns, IssueCollector issues)
    {
        string value = node.Value.Trim();
        string giin = value;
        if (!Giin.IsValid(value))
        {
            if (!field.GiinRequired)
            {
                return;
            }

            string? normalized = Giin.Normalize(value);
            Report(node, issues, GiinCheckCodes.Format, normalized is null
                ? $"'{value}' in {field.Label} is not a well-formed GIIN (XXXXXX.XXXXX.XX.XXX)."
                : $"'{value}' in {field.Label} should be written {normalized}: GIINs are upper case.");
            if (normalized is null)
            {
                return;
            }

            giin = normalized;
        }

        if (Giin.Category(giin) == "TA")
        {
            return;   // tax authorities (the IRS and partner jurisdictions) are not on the FFI list
        }

        if (!list.TryGetEntry(giin, out FfiListEntry? entry))
        {
            Report(node, issues, GiinCheckCodes.NotListed,
                $"GIIN {giin} ({field.Label}) is not on the IRS FFI list ({list.Describe()}). Check it for a typo; an institution approved after the list's cutoff only appears in the next month's list.");
            return;
        }

        if (field.NamePath is not null && node.SelectSingleNode(field.NamePath, ns) is { } nameNode && !NamesMatch(nameNode.Value, entry.Name))
        {
            string party = field.Label[..field.Label.IndexOf('/', StringComparison.Ordinal)];
            Report(nameNode, issues, GiinCheckCodes.NameDiffers,
                $"The FFI list names GIIN {giin} '{entry.Name}', but {party}/Name is '{nameNode.Value.Trim()}'.");
        }
    }

    /// <summary>Names match when, reduced to upper-case letters and digits, one contains the other ("Bank of NN" and "BANK OF NN LTD").</summary>
    private static bool NamesMatch(string reported, string listed)
    {
        string a = Reduce(reported);
        string b = Reduce(listed);
        return a.Length == 0 || b.Length == 0 || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);
    }

    private static string Reduce(string name) =>
        string.Concat(name.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant));

    private static void Report(XPathNavigator at, IssueCollector issues, string code, string message)
    {
        var position = at as IXmlLineInfo;
        issues.Add(new ValidationIssue(ValidationSeverity.Warning, message, position?.LineNumber ?? 0, position?.LinePosition ?? 0, code, IssueSource.FfiList));
    }

    private sealed record Field(string Path, string Label, string? NamePath, bool GiinRequired);
}
