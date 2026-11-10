using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;

namespace FatcaValidator.Core;

/// <summary>Per-file settings for <see cref="ValidatorEngine"/>.</summary>
public sealed record ValidationOptions
{
    /// <summary>
    /// A main .xsd file to use instead of the bundled schemas. The schemas it imports are read from its folder; nothing
    /// is fetched over the network.
    /// </summary>
    public string? SchemaPath { get; init; }

    /// <summary>
    /// The IDES sender metadata file that goes with this FATCA report. With <see cref="ValidatorEngine.FatcaPreflight"/>,
    /// it is validated against its schema and compared with the report.
    /// </summary>
    public string? MetadataPath { get; init; }
}

/// <summary>
/// Validates FATCA and CRS XML reports against the official XML schemas, and optionally against business rules and the
/// FATCA upload pre-flight. Files are streamed through the schema validator, so a large report is never loaded into
/// memory whole for that step; rules need the document in memory. Every finding has its line and position.
/// </summary>
/// <remarks>
/// <para>
/// Without a schema file, the engine picks the bundled schema whose root element matches the document's (see
/// <see cref="BundledSchemas"/>).
/// </para>
/// <para>
/// Safety: DTDs are refused, so there is no external-entity (XXE) access and no entity expansion;
/// <c>xsi:schemaLocation</c> hints in the document are ignored, so a document cannot choose its own schema; and nothing
/// is fetched over the network.
/// </para>
/// <para>
/// An engine compiles each schema the first time it needs it and then reuses it. It is not thread-safe: use one engine
/// (and its own rule sets) per thread.
/// </para>
/// </remarks>
public sealed partial class ValidatorEngine
{
    private const string IrsRulesResource = "Rules/irs-fatca.rules.json";

    private readonly Dictionary<SchemaPackage, XmlSchemaSet> _bundledSchemas = [];
    private readonly Dictionary<string, XmlSchemaSet> _fileSchemas = new(StringComparer.Ordinal);
    private readonly int _maxIssuesPerSeverity = 1000;
    private RuleSet? _irsRules;

    /// <summary>
    /// The most errors, and separately the most warnings, kept in <see cref="ValidationResult.Issues"/>.
    /// The totals are always counted. The default is 1000.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxIssuesPerSeverity
    {
        get => _maxIssuesPerSeverity;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxIssuesPerSeverity = value;
        }
    }

    /// <summary>
    /// Business rule sets to run on every file that passes its schema; each runs only on documents whose root namespace
    /// it applies to (<see cref="RuleSet.AppliesTo"/>).
    /// </summary>
    public IReadOnlyList<RuleSet> RuleSets { get; init; } = [];

    /// <summary>
    /// Runs the FATCA upload pre-flight on FATCA XML v2 reports: the checks IDES applies before the IRS processes a
    /// record (UTF-8, the prohibited "--", "/*" and "&amp;#" patterns, the recommended maximum compressed size), the
    /// built-in IRS record checks (DocRefId format and uniqueness, correction references, test data), and, with
    /// <see cref="ValidationOptions.MetadataPath"/>, the sender metadata file.
    /// </summary>
    public bool FatcaPreflight { get; init; }

    /// <summary>The pre-flight's compressed-size threshold in bytes. The default, 200 MB, is the IRS's recommended maximum.</summary>
    public long PreflightMaxCompressedBytes { get; init; } = 200_000_000;

    /// <summary>
    /// Validates an XML file against the bundled schema that matches its root element, or against
    /// <paramref name="schemaPath"/> when one is given.
    /// </summary>
    /// <param name="xmlPath">The FATCA or CRS XML file.</param>
    /// <param name="schemaPath">A main .xsd file to use instead of the bundled schemas, or <see langword="null"/>.</param>
    public ValidationResult Validate(string xmlPath, string? schemaPath) =>
        Validate(xmlPath, new ValidationOptions { SchemaPath = schemaPath });

    /// <summary>Validates an XML file. See <see cref="ValidationOptions"/> for the per-file settings.</summary>
    /// <param name="xmlPath">The FATCA or CRS XML file.</param>
    /// <param name="options">The schema file and metadata file to use, or <see langword="null"/> for none.</param>
    public ValidationResult Validate(string xmlPath, ValidationOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(xmlPath);

        FileStream stream;
        try
        {
            stream = new FileStream(xmlPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16, FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return NotValidated(FullPathOrNull(options?.SchemaPath), IssueCodes.FileUnreadable, $"The file '{xmlPath}' could not be read: {ex.Message}");
        }

        using (stream)
        {
            return Validate(stream, options);
        }
    }

    /// <summary>Validates XML read from a seekable stream. See <see cref="Validate(string, string?)"/>.</summary>
    public ValidationResult Validate(Stream xml, string? schemaPath) =>
        Validate(xml, new ValidationOptions { SchemaPath = schemaPath });

    /// <summary>
    /// Validates XML read from a seekable stream, starting at its current position. The stream is left open.
    /// </summary>
    /// <param name="xml">The document. It is read more than once: for the root element, the schema and any later checks.</param>
    /// <param name="options">The schema file and metadata file to use, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentException">The stream does not support seeking.</exception>
    public ValidationResult Validate(Stream xml, ValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(xml);
        if (!xml.CanSeek)
        {
            throw new ArgumentException("The stream must support seeking: the root element is read before validation starts.", nameof(xml));
        }

        string? schemaFile = FullPathOrNull(options?.SchemaPath);
        string? metadataFile = FullPathOrNull(options?.MetadataPath);
        var issues = new IssueCollector(_maxIssuesPerSeverity);
        long start = xml.Position;

        try
        {
            // 1. Read the root element without validating: it decides which schema applies.
            XmlQualifiedName root;
            int rootLine, rootPosition;
            try
            {
                (root, rootLine, rootPosition) = ReadRootElement(xml);
            }
            catch (XmlException ex)
            {
                issues.Add(FromXmlException(ex));
                return issues.ToResult(ValidationStatus.Invalid, schemaFile);
            }

            // 2. Choose the schema.
            string schemaName;
            XmlSchemaSet schemas;
            if (schemaFile is not null)
            {
                if (!TryGetFileSchemas(schemaFile, out XmlSchemaSet? fileSchemas, out string? loadError))
                {
                    return NotValidated(schemaFile, IssueCodes.SchemaNotLoaded, loadError);
                }

                schemaName = schemaFile;
                schemas = fileSchemas;
            }
            else
            {
                SchemaPackage? package = BundledSchemas.FindByRootElement(root);
                if (package is null)
                {
                    return NotValidated(null, IssueCodes.NoBundledSchema,
                        $"No bundled schema matches the root element '{root.Name}' in namespace '{root.Namespace}'. " +
                        $"The bundled schemas cover {DescribeBundledRoots()}. Supply the schema file to validate against.");
                }

                schemaName = package.Name;
                schemas = GetBundledSchemas(package);
            }

            // 3. The schema must declare the root element. Otherwise the .NET validator only raises
            //    "could not find schema information" warnings, and any XML would pass as valid.
            if (!schemas.GlobalElements.Contains(root))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    $"The root element '{root.Name}' in namespace '{root.Namespace}' is not declared by the schema, which {DescribeRoots(schemas)}.",
                    rootLine,
                    rootPosition,
                    IssueCodes.RootNotDeclared,
                    IssueSource.Schema));
                return issues.ToResult(ValidationStatus.Invalid, schemaName);
            }

            // 4. Stream the whole document through the validating reader.
            bool wellFormed = true;
            xml.Position = start;
            try
            {
                using XmlReader reader = XmlReader.Create(xml, CreateValidatingSettings(schemas, issues));
                while (reader.Read())
                {
                }
            }
            catch (XmlException ex)
            {
                issues.Add(FromXmlException(ex));
                wellFormed = false;
            }

            bool schemaValid = issues.ErrorCount == 0;

            // 5. Business rules, which need the document in memory and a schema-valid file.
            bool preflight = FatcaPreflight && root == BundledSchemas.FatcaV2_0_1.RootElement;
            var ruleSets = RuleSets.Where(ruleSet => ruleSet.AppliesToNamespace(root.Namespace)).ToList();
            if (preflight)
            {
                ruleSets.Insert(0, _irsRules ??= RuleSet.LoadEmbedded(IrsRulesResource, IssueSource.Preflight));
            }

            XPathNavigator? document = null;
            if (schemaValid && (ruleSets.Count > 0 || (preflight && metadataFile is not null)))
            {
                xml.Position = start;
                document = LoadDocument(xml);
                foreach (RuleSet ruleSet in ruleSets)
                {
                    ruleSet.Evaluate(document, issues);
                }
            }
            else if (!schemaValid && ruleSets.Count > 0)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning,
                    "Business rules were not run because the file breaks its schema; fix the schema errors first.",
                    0, 0, IssueCodes.RulesSkipped, IssueSource.Validator));
            }

            // 6. The FATCA upload pre-flight's file checks (and the metadata file).
            if (preflight && wellFormed)
            {
                FatcaPreflightChecks.Run(xml, start, metadataFile, document, PreflightMaxCompressedBytes, issues);
            }

            return issues.ToResult(issues.ErrorCount == 0 ? ValidationStatus.Valid : ValidationStatus.Invalid, schemaName);
        }
        catch (IOException ex)
        {
            return NotValidated(schemaFile, IssueCodes.FileUnreadable, $"The document could not be read: {ex.Message}");
        }
    }

    private static (XmlQualifiedName Root, int Line, int Position) ReadRootElement(Stream xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CloseInput = false,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        };

        using XmlReader reader = XmlReader.Create(xml, settings);
        reader.MoveToContent();
        var position = reader as IXmlLineInfo;
        return (new XmlQualifiedName(reader.LocalName, reader.NamespaceURI), position?.LineNumber ?? 0, position?.LinePosition ?? 0);
    }

    private static XPathNavigator LoadDocument(Stream xml)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = false };
        using XmlReader reader = XmlReader.Create(xml, settings);
        return new XPathDocument(reader).CreateNavigator();
    }

    private static XmlReaderSettings CreateValidatingSettings(XmlSchemaSet schemas, IssueCollector issues)
    {
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            // The defaults plus warnings. Never ProcessSchemaLocation or ProcessInlineSchema:
            // a document must not be able to choose the schema it is checked against.
            ValidationFlags = XmlSchemaValidationFlags.ProcessIdentityConstraints
                | XmlSchemaValidationFlags.AllowXmlAttributes
                | XmlSchemaValidationFlags.ReportValidationWarnings,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CloseInput = false,
            Schemas = schemas,
        };

        settings.ValidationEventHandler += (_, e) =>
        {
            bool error = e.Severity == XmlSeverityType.Error;
            issues.Add(new ValidationIssue(
                error ? ValidationSeverity.Error : ValidationSeverity.Warning,
                e.Message,
                e.Exception?.LineNumber ?? 0,
                e.Exception?.LinePosition ?? 0,
                error ? IssueCodes.SchemaError : IssueCodes.SchemaWarning,
                IssueSource.Schema));
        };
        return settings;
    }

    private XmlSchemaSet GetBundledSchemas(SchemaPackage package)
    {
        if (!_bundledSchemas.TryGetValue(package, out XmlSchemaSet? schemas))
        {
            schemas = package.CreateSchemaSet();
            _bundledSchemas.Add(package, schemas);
        }

        return schemas;
    }

    private bool TryGetFileSchemas(
        string schemaFile,
        [NotNullWhen(true)] out XmlSchemaSet? schemas,
        [NotNullWhen(false)] out string? error)
    {
        error = null;
        if (_fileSchemas.TryGetValue(schemaFile, out schemas))
        {
            return true;
        }

        try
        {
            schemas = SchemaSetLoader.Load(new SchemaFolderResolver(schemaFile), new Uri(schemaFile));
        }
        catch (Exception ex) when (ex is XmlSchemaException or XmlException or IOException or UnauthorizedAccessException)
        {
            error = $"The schema '{schemaFile}' could not be loaded: {ex.Message}";
            return false;
        }

        _fileSchemas.Add(schemaFile, schemas);
        return true;
    }

    private static ValidationResult NotValidated(string? schema, string code, string reason)
    {
        var issues = new IssueCollector(1);
        issues.Add(new ValidationIssue(ValidationSeverity.Error, reason, 0, 0, code, IssueSource.Validator));
        return issues.ToResult(ValidationStatus.NotValidated, schema);
    }

    private static ValidationIssue FromXmlException(XmlException ex)
    {
        bool dtd = ex.Message.Contains("DTD is prohibited", StringComparison.Ordinal);
        string message = dtd
            ? "The file contains a DTD (<!DOCTYPE ...>). DTDs are refused for security reasons and were not processed."
            : "The file is not well-formed XML: " + TrailingPosition().Replace(ex.Message, "");
        return new ValidationIssue(ValidationSeverity.Error, message, ex.LineNumber, ex.LinePosition,
            dtd ? IssueCodes.DtdRefused : IssueCodes.NotWellFormed, IssueSource.Xml);
    }

    private static string DescribeBundledRoots() =>
        string.Join(", ", BundledSchemas.All.Select(p => $"'{p.RootElement.Name}' in '{p.RootElement.Namespace}' ({p.Name})"));

    private static string DescribeRoots(XmlSchemaSet schemas)
    {
        string[] roots = schemas.GlobalElements.Names
            .Cast<XmlQualifiedName>()
            .Select(name => $"'{name.Name}' in namespace '{name.Namespace}'")
            .ToArray();
        return roots.Length == 0 ? "declares no root element" : "expects " + string.Join(" or ", roots);
    }

    private static string? FullPathOrNull(string? path) => path is null ? null : Path.GetFullPath(path);

    // XmlException messages end with " Line 3, position 1."; the issue carries the position already.
    [GeneratedRegex(@"\s*Line \d+, position \d+\.$")]
    private static partial Regex TrailingPosition();
}
