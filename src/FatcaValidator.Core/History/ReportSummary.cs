using System.Globalization;
using System.Text;
using System.Xml;

namespace FatcaValidator.Core;

/// <summary>One record of a report: an element with its own DocSpec, such as ReportingFI, AccountReport or CbcReports.</summary>
/// <param name="Kind">The element that holds the DocSpec, for example ReportingFI, AccountReport, PoolReport, NilReport or Sponsor.</param>
/// <param name="DocRefId">The record's DocRefId.</param>
/// <param name="DocTypeIndic">FATCA1 to FATCA4 and FATCA11 to FATCA14, or OECD0 to OECD3 and OECD10 to OECD13.</param>
/// <param name="CorrDocRefId">The DocRefId of the record this one corrects, amends or voids, or <see langword="null"/>.</param>
/// <param name="CorrMessageRefId">The MessageRefId of the message that held that record (FATCA), or <see langword="null"/>.</param>
/// <param name="LineNumber">The line of the DocRefId element, or 0.</param>
/// <param name="LinePosition">The position of the DocRefId element in its line, or 0.</param>
/// <param name="CorrLineNumber">The line of the CorrDocRefId element, or 0.</param>
/// <param name="CorrLinePosition">The position of the CorrDocRefId element in its line, or 0.</param>
public sealed record ReportRecord(
    string Kind,
    string DocRefId,
    string DocTypeIndic,
    string? CorrDocRefId,
    string? CorrMessageRefId,
    int LineNumber,
    int LinePosition,
    int CorrLineNumber,
    int CorrLinePosition)
{
    /// <summary>Test data: FATCA11 to FATCA14, or OECD10 to OECD13.</summary>
    public bool IsTest => DocType.Parse(DocTypeIndic).Test;

    /// <summary>
    /// Corrected, amended or void data (FATCA2 to FATCA4, OECD2 and OECD3, and their test codes): the record replaces
    /// the one <see cref="CorrDocRefId"/> names.
    /// </summary>
    public bool IsCorrection => DocType.Parse(DocTypeIndic).Replaces;

    /// <summary>Void or deletion data (FATCA3, FATCA13, OECD3, OECD13): the record cancels the one <see cref="CorrDocRefId"/> names.</summary>
    public bool IsVoid => DocType.Parse(DocTypeIndic).Voids;

    /// <summary>Resent data (OECD0, OECD10): an unchanged record sent again next to corrections.</summary>
    public bool IsResent => DocType.Parse(DocTypeIndic).Resent;
}

/// <summary>
/// What a submission history keeps of a report: its MessageSpec and the DocSpec of each record. <see cref="Read(Stream)"/>
/// streams the file and goes by element names whatever their namespace, so it reads FATCA, CRS, CbC and the other
/// OECD formats alike.
/// </summary>
public sealed class ReportSummary
{
    private static readonly HashSet<string> MessageSpecFields = new(StringComparer.Ordinal)
    {
        "SendingCompanyIN", "TransmittingCountry", "ReceivingCountry", "MessageType", "MessageRefId", "ReportingPeriod", "Timestamp",
    };

    private static readonly HashSet<string> DocSpecFields = new(StringComparer.Ordinal)
    {
        "DocTypeIndic", "DocRefId", "CorrMessageRefId", "CorrDocRefId",
    };

    private readonly Dictionary<string, Field> _messageSpec;

    private ReportSummary(Dictionary<string, Field> messageSpec, IReadOnlyList<ReportRecord> records)
    {
        _messageSpec = messageSpec;
        Records = records;
    }

    /// <summary>MessageSpec/MessageRefId, or <see langword="null"/> when the document has none.</summary>
    public string? MessageRefId => Value("MessageRefId");

    /// <summary>The line of the MessageRefId element, or 0.</summary>
    public int MessageRefIdLine => _messageSpec.TryGetValue("MessageRefId", out Field f) ? f.Line : 0;

    /// <summary>The position of the MessageRefId element in its line, or 0.</summary>
    public int MessageRefIdPosition => _messageSpec.TryGetValue("MessageRefId", out Field f) ? f.Position : 0;

    /// <summary>MessageSpec/MessageType, for example FATCA, CRS or CBC.</summary>
    public string? MessageType => Value("MessageType");

    /// <summary>MessageSpec/SendingCompanyIN: the sender's GIIN in FATCA reports.</summary>
    public string? SendingCompanyIN => Value("SendingCompanyIN");

    /// <summary>MessageSpec/TransmittingCountry.</summary>
    public string? TransmittingCountry => Value("TransmittingCountry");

    /// <summary>MessageSpec/ReceivingCountry (the first one).</summary>
    public string? ReceivingCountry => Value("ReceivingCountry");

    /// <summary>MessageSpec/ReportingPeriod, as written.</summary>
    public string? ReportingPeriod => Value("ReportingPeriod");

    /// <summary>MessageSpec/Timestamp, as written.</summary>
    public string? Timestamp => Value("Timestamp");

    /// <summary>The records, in document order.</summary>
    public IReadOnlyList<ReportRecord> Records { get; }

    /// <summary>Whether the report holds test data (FATCA11 to FATCA14, OECD10 to OECD13).</summary>
    public bool IsTest => Records.Any(record => record.IsTest);

    /// <summary>Reads a report file. See <see cref="Read(Stream)"/>.</summary>
    /// <exception cref="XmlException">The file is not well-formed XML.</exception>
    public static ReportSummary Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16, FileOptions.SequentialScan);
        return Read(stream);
    }

    /// <summary>Reads the MessageSpec and DocSpecs of a report from its current position. The stream is left open.</summary>
    /// <exception cref="XmlException">The document is not well-formed XML.</exception>
    public static ReportSummary Read(Stream xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CloseInput = false,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        var messageSpec = new Dictionary<string, Field>(StringComparer.Ordinal);
        var records = new List<ReportRecord>();
        var path = new List<string>();      // path[depth] = local name of the open element at that depth

        int docSpecDepth = -1;              // the DocSpec being read, if any
        string docSpecKind = "";
        var docSpec = new Dictionary<string, Field>(StringComparer.Ordinal);

        string? capture = null;             // the element whose text is being collected
        int captureDepth = -1;
        bool captureInDocSpec = false;
        int captureLine = 0, capturePosition = 0;
        var text = new StringBuilder();

        using XmlReader reader = XmlReader.Create(xml, settings);
        var lineInfo = reader as IXmlLineInfo;
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                {
                    int depth = reader.Depth;
                    if (path.Count > depth)
                    {
                        path.RemoveRange(depth, path.Count - depth);
                    }

                    string name = reader.LocalName;
                    string? parent = depth > 0 ? path[depth - 1] : null;
                    path.Add(name);
                    int line = lineInfo?.LineNumber ?? 0;
                    int position = lineInfo?.LinePosition ?? 0;

                    bool inDocSpec;
                    if (name == "DocSpec" && docSpecDepth < 0 && parent is not null)
                    {
                        if (!reader.IsEmptyElement)
                        {
                            docSpecDepth = depth;
                            docSpecKind = parent;
                            docSpec.Clear();
                        }

                        break;
                    }
                    else if (docSpecDepth >= 0 && depth == docSpecDepth + 1 && DocSpecFields.Contains(name))
                    {
                        inDocSpec = true;
                    }
                    else if (depth == 2 && parent == "MessageSpec" && MessageSpecFields.Contains(name) && !messageSpec.ContainsKey(name))
                    {
                        inDocSpec = false;
                    }
                    else
                    {
                        break;
                    }

                    if (reader.IsEmptyElement)
                    {
                        (inDocSpec ? docSpec : messageSpec)[name] = new Field("", line, position);
                    }
                    else
                    {
                        capture = name;
                        captureDepth = depth;
                        captureInDocSpec = inDocSpec;
                        captureLine = line;
                        capturePosition = position;
                        text.Clear();
                    }

                    break;
                }

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    if (capture is not null)
                    {
                        text.Append(reader.Value);
                    }

                    break;

                case XmlNodeType.EndElement:
                    if (capture is not null && reader.Depth == captureDepth)
                    {
                        (captureInDocSpec ? docSpec : messageSpec)[capture] = new Field(text.ToString().Trim(), captureLine, capturePosition);
                        capture = null;
                    }
                    else if (docSpecDepth >= 0 && reader.Depth == docSpecDepth)
                    {
                        AddRecord(records, docSpecKind, docSpec);
                        docSpecDepth = -1;
                    }

                    break;
            }
        }

        return new ReportSummary(messageSpec, records);
    }

    private static void AddRecord(List<ReportRecord> records, string kind, Dictionary<string, Field> docSpec)
    {
        if (!docSpec.TryGetValue("DocRefId", out Field docRefId) || docRefId.Value.Length == 0)
        {
            return;
        }

        docSpec.TryGetValue("CorrDocRefId", out Field corrDocRefId);
        docSpec.TryGetValue("CorrMessageRefId", out Field corrMessageRefId);
        docSpec.TryGetValue("DocTypeIndic", out Field docTypeIndic);
        records.Add(new ReportRecord(
            kind,
            docRefId.Value,
            docTypeIndic.Value ?? "",
            NullIfEmpty(corrDocRefId.Value),
            NullIfEmpty(corrMessageRefId.Value),
            docRefId.Line,
            docRefId.Position,
            corrDocRefId.Line,
            corrDocRefId.Position));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private string? Value(string name) => _messageSpec.TryGetValue(name, out Field f) && f.Value.Length > 0 ? f.Value : null;

    private readonly record struct Field(string Value, int Line, int Position);
}

/// <summary>A DocTypeIndic value taken apart: FATCA1–4 and FATCA11–14, OECD0–3 and OECD10–13.</summary>
internal readonly record struct DocType(bool Known, bool Fatca, int Base, bool Test)
{
    /// <summary>Corrected, amended or void data: it names the record it replaces in CorrDocRefId.</summary>
    public bool Replaces => Known && (Fatca ? Base is 2 or 3 or 4 : Base is 2 or 3);

    /// <summary>Void or deletion data.</summary>
    public bool Voids => Known && Base == 3;

    /// <summary>Resent data (OECD0 and OECD10).</summary>
    public bool Resent => Known && !Fatca && Base == 0;

    public static DocType Parse(string? value)
    {
        if (value is null)
        {
            return default;
        }

        bool fatca = value.StartsWith("FATCA", StringComparison.Ordinal);
        if (!fatca && !value.StartsWith("OECD", StringComparison.Ordinal))
        {
            return default;
        }

        if (!int.TryParse(value.AsSpan(fatca ? 5 : 4), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
        {
            return default;
        }

        bool test = number >= 10;
        int code = test ? number - 10 : number;
        bool known = fatca ? code is >= 1 and <= 4 : code is >= 0 and <= 3;
        return known ? new DocType(true, fatca, code, test) : default;
    }
}
