using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace FatcaValidator.Core;

/// <summary>Codes of the FATCA upload pre-flight findings.</summary>
public static class PreflightCodes
{
    /// <summary>The text contains "--", "/*" or "&amp;#", which IDES rejects (failed threat detection).</summary>
    public const string ProhibitedCharacters = "IDES-PROHIBITED-CHARACTERS";

    /// <summary>The file is not UTF-8.</summary>
    public const string Encoding = "IDES-ENCODING";

    /// <summary>The file compresses to more than the IRS's recommended maximum.</summary>
    public const string Size = "IDES-SIZE";

    /// <summary>The sender metadata file could not be read or validated.</summary>
    public const string MetadataUnreadable = "IDES-METADATA-UNREADABLE";

    /// <summary>The sender metadata file breaks its schema.</summary>
    public const string MetadataSchema = "IDES-METADATA-SCHEMA";

    /// <summary>The metadata sender differs from MessageSpec/SendingCompanyIN.</summary>
    public const string MetadataSender = "IDES-METADATA-SENDER";

    /// <summary>The metadata receiver is not the IRS.</summary>
    public const string MetadataReceiver = "IDES-METADATA-RECEIVER";

    /// <summary>The metadata communication type is not RPT.</summary>
    public const string MetadataCommunicationType = "IDES-METADATA-COMMUNICATION-TYPE";

    /// <summary>The metadata file format is not XML.</summary>
    public const string MetadataFileFormat = "IDES-METADATA-FILE-FORMAT";

    /// <summary>The metadata binary encoding is not NONE.</summary>
    public const string MetadataBinaryEncoding = "IDES-METADATA-BINARY-ENCODING";

    /// <summary>The metadata tax year differs from the report's reporting period.</summary>
    public const string MetadataTaxYear = "IDES-METADATA-TAX-YEAR";
}

/// <summary>
/// The FATCA upload pre-flight's file checks: what IDES rejects before the IRS processes a single record. Sources: the
/// IRS FATCA IDES technical FAQs (C5 UTF-8, C6 recommended maximum of 200 MB compressed, C17 prohibited characters) and
/// the FATCA XML schema best-practices page. Record checks are the built-in rule set Rules/irs-fatca.rules.json.
/// </summary>
internal static class FatcaPreflightChecks
{
    /// <summary>The IRS's own IDES id: the receiver of every FATCA report.</summary>
    internal const string IrsIdesId = "000000.00000.TA.840";

    private const string MetadataNamespace = "urn:fatca:idessenderfilemetadata";
    private const string FatcaNamespace = "urn:oecd:ties:fatca:v2";
    private const string StfNamespace = "urn:oecd:ties:stffatcatypes:v2";

    public static void Run(Stream xml, long start, string? metadataPath, XPathNavigator? document, long maxCompressedBytes, IssueCollector issues)
    {
        xml.Position = start;
        CheckEncoding(xml, issues);
        xml.Position = start;
        CheckProhibitedCharacters(xml, issues);
        xml.Position = start;
        CheckCompressedSize(xml, maxCompressedBytes, issues);
        if (metadataPath is not null)
        {
            CheckMetadata(metadataPath, document, issues);
        }
    }

    /// <summary>IDES FAQ C5: FATCA XML is UTF-8. Refuses a UTF-16 byte order mark and any other declared encoding.</summary>
    private static void CheckEncoding(Stream xml, IssueCollector issues)
    {
        long start = xml.Position;
        Span<byte> head = stackalloc byte[2];
        int read = xml.ReadAtLeast(head, 2, throwOnEndOfStream: false);
        xml.Position = start;
        if (read == 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF)))
        {
            issues.Add(Issue(ValidationSeverity.Error, PreflightCodes.Encoding, "The file is UTF-16 (it starts with a UTF-16 byte order mark); FATCA XML files must be UTF-8.", 1, 1));
            return;
        }

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = false };
        using XmlReader reader = XmlReader.Create(xml, settings);
        if (reader.Read() && reader.NodeType == XmlNodeType.XmlDeclaration)
        {
            string? encoding = reader.GetAttribute("encoding");
            if (encoding is not null && !encoding.Equals("UTF-8", StringComparison.OrdinalIgnoreCase) && !encoding.Equals("UTF8", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(Issue(ValidationSeverity.Error, PreflightCodes.Encoding, $"The XML declaration says encoding=\"{encoding}\"; FATCA XML files must be UTF-8.", 1, 1));
            }
        }
    }

    /// <summary>
    /// IDES FAQ C17 and the best-practices page: "--", "/*" and "&amp;#" in the XML text content get the data packet
    /// rejected (failed threat detection). Scans the raw text, so character references count; comments and processing
    /// instructions are skipped (the IRS's own sample files carry comments).
    /// </summary>
    private static void CheckProhibitedCharacters(Stream xml, IssueCollector issues)
    {
        using var text = new StreamReader(xml, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16, leaveOpen: true);
        var state = ScanState.Content;
        int line = 1, column = 0;
        int previousLine = 1, previousColumn = 0;
        char previous = '\0';
        var tail = new char[9];   // the last nine characters, for "<!--", "-->", "<?", "?>", "<![CDATA[" and "]]>"
        bool crPending = false;
        bool lastWasMatch = false;
        var buffer = new char[1 << 16];
        int count;
        while ((count = text.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < count; i++)
            {
                char c = buffer[i];
                if (c == '\n' && crPending)
                {
                    crPending = false;
                    continue;   // the LF of a CRLF: the line was already counted at the CR
                }

                crPending = c == '\r';
                if (c is '\r' or '\n')
                {
                    line++;
                    column = 0;
                }
                else
                {
                    column++;
                }

                Array.Copy(tail, 1, tail, 0, tail.Length - 1);
                tail[^1] = c;

                switch (state)
                {
                    case ScanState.Comment:
                        if (EndsWith(tail, "-->")) { state = ScanState.Content; }
                        break;
                    case ScanState.Instruction:
                        if (EndsWith(tail, "?>")) { state = ScanState.Content; }
                        break;
                    default:
                        if (state == ScanState.Content && EndsWith(tail, "<!--")) { state = ScanState.Comment; break; }
                        if (state == ScanState.Content && EndsWith(tail, "<?")) { state = ScanState.Instruction; break; }
                        if (state == ScanState.Content && EndsWith(tail, "<![CDATA[")) { state = ScanState.CData; break; }
                        if (state == ScanState.CData && EndsWith(tail, "]]>")) { state = ScanState.Content; break; }

                        string? found = (previous, c) switch
                        {
                            ('-', '-') => "--",
                            ('/', '*') => "/*",
                            ('&', '#') => "&#",
                            _ => null,
                        };
                        if (found is not null && !lastWasMatch)
                        {
                            issues.Add(Issue(ValidationSeverity.Error, PreflightCodes.ProhibitedCharacters, Describe(found), previousLine, previousColumn));
                        }

                        lastWasMatch = found is not null;
                        break;
                }

                previous = c;
                previousLine = line;
                previousColumn = column;
            }
        }
    }

    private static string Describe(string pattern) => pattern switch
    {
        "--" => "The text contains \"--\" (double dash), which IDES rejects as possible SQL injection (failed threat detection). Remove it or use a single dash.",
        "/*" => "The text contains \"/*\" (slash asterisk), which IDES rejects as possible SQL injection (failed threat detection). Remove it.",
        _ => "The text contains \"&#\" (a character reference such as &#233;), which IDES rejects (failed threat detection). Write the character itself; the file is UTF-8.",
    };

    /// <summary>IDES FAQ C6: the recommended maximum is 200 MB compressed; larger reports should be split.</summary>
    private static void CheckCompressedSize(Stream xml, long maxCompressedBytes, IssueCollector issues)
    {
        long length = xml.Length - xml.Position;
        if (length <= maxCompressedBytes)
        {
            return;   // XML never grows when compressed, so it cannot be over the limit
        }

        var counter = new CountingStream();
        using (var deflate = new DeflateStream(counter, CompressionLevel.Optimal, leaveOpen: true))
        {
            xml.CopyTo(deflate);
        }

        if (counter.Length > maxCompressedBytes)
        {
            issues.Add(Issue(ValidationSeverity.Warning, PreflightCodes.Size,
                $"The file compresses to about {Megabytes(counter.Length)} MB; the IRS recommends at most {Megabytes(maxCompressedBytes)} MB compressed per transmission. Split the records over several files.", 0, 0));
        }
    }

    /// <summary>Validates the IDES sender metadata file and checks that it matches the report.</summary>
    private static void CheckMetadata(string metadataPath, XPathNavigator? document, IssueCollector issues)
    {
        ValidationResult metadata = new ValidatorEngine().Validate(metadataPath);
        if (metadata.Status == ValidationStatus.NotValidated)
        {
            string reason = metadata.Issues.Count > 0 ? metadata.Issues[0].Message : "unknown reason";
            issues.Add(Issue(ValidationSeverity.Error, PreflightCodes.MetadataUnreadable, $"The sender metadata file was not checked: {reason}", 0, 0));
            return;
        }

        if (metadata.Schema != BundledSchemas.FatcaSenderMetadataV1_3.Name)
        {
            issues.Add(Issue(ValidationSeverity.Error, PreflightCodes.MetadataUnreadable, $"'{metadataPath}' is not an IDES sender metadata file (FATCAIDESSenderFileMetadata, v1.3).", 0, 0));
            return;
        }

        foreach (ValidationIssue issue in metadata.Issues)
        {
            string where = issue.LineNumber > 0 ? $", line {issue.LineNumber}" : "";
            issues.Add(Issue(issue.Severity, PreflightCodes.MetadataSchema, $"Sender metadata file{where}: {issue.Message}", 0, 0));
        }

        if (!metadata.IsValid || document is null)
        {
            return;   // the cross-checks need both files to pass their schemas
        }

        Dictionary<string, string> values = ReadMetadataValues(metadataPath);
        var ns = new XmlNamespaceManager(new NameTable());
        ns.AddNamespace("ftc", FatcaNamespace);
        ns.AddNamespace("sfa", StfNamespace);
        XPathNavigator? sender = document.SelectSingleNode("/ftc:FATCA_OECD/ftc:MessageSpec/sfa:SendingCompanyIN", ns);
        XPathNavigator? period = document.SelectSingleNode("/ftc:FATCA_OECD/ftc:MessageSpec/sfa:ReportingPeriod", ns);

        string metaSender = values.GetValueOrDefault("FATCAEntitySenderId", "");
        if (sender is not null && sender.Value.Trim() != metaSender)
        {
            issues.Add(Issue(ValidationSeverity.Warning, PreflightCodes.MetadataSender,
                $"The metadata's FATCAEntitySenderId '{metaSender}' differs from MessageSpec/SendingCompanyIN '{sender.Value.Trim()}'; both normally carry the GIIN that sends the file.", sender));
        }

        string receiver = values.GetValueOrDefault("FATCAEntityReceiverId", "");
        if (receiver != IrsIdesId)
        {
            issues.Add(Issue(ValidationSeverity.Error, PreflightCodes.MetadataReceiver,
                $"The metadata's FATCAEntityReceiverId is '{receiver}'; FATCA reports go to the IRS, whose IDES id is {IrsIdesId}.", 0, 0));
        }

        string type = values.GetValueOrDefault("FATCAEntCommunicationTypeCd", "");
        if (type != "RPT")
        {
            issues.Add(Issue(ValidationSeverity.Error, PreflightCodes.MetadataCommunicationType,
                $"The metadata's FATCAEntCommunicationTypeCd is '{type}'; a FATCA report is RPT.", 0, 0));
        }

        if (values.TryGetValue("FileFormatCd", out string? format) && format != "XML")
        {
            issues.Add(Issue(ValidationSeverity.Error, PreflightCodes.MetadataFileFormat,
                $"The metadata's FileFormatCd is '{format}'; a FATCA XML report is XML.", 0, 0));
        }

        if (values.TryGetValue("BinaryEncodingSchemeCd", out string? encoding) && encoding != "NONE")
        {
            issues.Add(Issue(ValidationSeverity.Error, PreflightCodes.MetadataBinaryEncoding,
                $"The metadata's BinaryEncodingSchemeCd is '{encoding}'; an XML report is sent as is (NONE), not Base64-encoded.", 0, 0));
        }

        string taxYear = values.GetValueOrDefault("TaxYear", "");
        string reportYear = period is not null && period.Value.Trim().Length >= 4 ? period.Value.Trim()[..4] : "";
        if (reportYear.Length == 4 && taxYear != reportYear)
        {
            issues.Add(Issue(ValidationSeverity.Error, PreflightCodes.MetadataTaxYear,
                $"The metadata's TaxYear is '{taxYear}' but MessageSpec/ReportingPeriod is {period!.Value.Trim()}.", period));
        }
    }

    private static Dictionary<string, string> ReadMetadataValues(string metadataPath)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
        using XmlReader reader = XmlReader.Create(metadataPath, settings);
        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1 && reader.NamespaceURI == MetadataNamespace)
            {
                string name = reader.LocalName;
                values[name] = reader.ReadElementContentAsString().Trim();   // leaves the reader on the next node
            }
            else
            {
                reader.Read();
            }
        }

        return values;
    }

    private static ValidationIssue Issue(ValidationSeverity severity, string code, string message, XPathNavigator? at)
    {
        var position = at as IXmlLineInfo;
        return Issue(severity, code, message, position?.LineNumber ?? 0, position?.LinePosition ?? 0);
    }

    private static ValidationIssue Issue(ValidationSeverity severity, string code, string message, int line, int position) =>
        new(severity, message, line, position, code, IssueSource.Preflight);

    private static bool EndsWith(char[] tail, string value)
    {
        int offset = tail.Length - value.Length;
        for (int i = 0; i < value.Length; i++)
        {
            if (tail[offset + i] != value[i])
            {
                return false;
            }
        }

        return true;
    }

    private static string Megabytes(long bytes) => (bytes / 1_000_000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private enum ScanState { Content, Comment, Instruction, CData }

    /// <summary>A write-only stream that only counts bytes.</summary>
    private sealed class CountingStream : Stream
    {
        private long _length;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _length;

        public override long Position { get => _length; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => _length += count;

        public override void Write(ReadOnlySpan<byte> buffer) => _length += buffer.Length;
    }
}
