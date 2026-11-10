// The IRS FFI list reader, copied from rante-corporation/FatcaFfiList at commit 47da8f5 (MIT, Rante Corporation):
// Giin.cs, FfiListXml.cs and the parts of FfiListFile.cs that open a list file. Kept internal and unchanged in logic;
// the row hashing, file inspection and archive code are left out. Fix bugs there first, then copy them here.
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Xml;

namespace FatcaValidator.Core.Ffi;

/// <summary>
/// The Global Intermediary Identification Number the IRS assigns at FATCA registration: <c>XXXXXX.XXXXX.XX.XXX</c>,
/// 19 characters. The two letters are the category (LE, SL, ME, BR, SP, TA and others); the last three digits are the
/// ISO 3166-1 numeric code of the country or jurisdiction.
/// </summary>
internal static class Giin
{
    public const int Length = 19;

    /// <summary>Whether the value is a well-formed GIIN (upper case). It does not say whether the IRS issued it.</summary>
    public static bool IsValid([NotNullWhen(true)] string? value)
    {
        if (value is null || value.Length != Length)
        {
            return false;
        }

        for (int i = 0; i < Length; i++)
        {
            char c = value[i];
            bool ok = i switch
            {
                6 or 12 or 15 => c == '.',
                13 or 14 => c is >= 'A' and <= 'Z',
                16 or 17 or 18 => c is >= '0' and <= '9',
                _ => c is >= 'A' and <= 'Z' or >= '0' and <= '9',
            };
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Trims and upper-cases the value; returns it when it is then a well-formed GIIN, else null.</summary>
    public static string? Normalize(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string candidate = value.Trim().ToUpperInvariant();
        return IsValid(candidate) ? candidate : null;
    }

    /// <summary>The two-letter category of a well-formed GIIN (for example LE, SL, BR, TA).</summary>
    public static string Category(string giin) => IsValid(giin) ? giin.Substring(13, 2) : throw new ArgumentException("Not a GIIN.", nameof(giin));
}

/// <summary>A list file that is not the FFI list's XML structure (or not well-formed XML).</summary>
internal sealed class FfiListFormatException : Exception
{
    public FfiListFormatException(string message, int lineNumber = 0, int linePosition = 0, Exception? inner = null)
        : base(lineNumber > 0 ? $"{message} (line {lineNumber}, position {linePosition})" : message, inner)
    {
    }
}

/// <summary>One row: a GIIN with the institution's name and country, as published (both ends trimmed).</summary>
internal sealed record FfiRow(string Giin, string Name, string Country);

/// <summary>What the reader saw: every row, and the rows it left out.</summary>
internal sealed class FfiReadStatistics
{
    public long Rows { get; set; }

    public long ReturnedRows { get; set; }

    public long LeftOutRows => Rows - ReturnedRows;
}

/// <summary>
/// Reads the FFI list's XML: <c>IRSFFIList</c> with one <c>FinancialInstitution</c> per row, each holding <c>GIIN</c>,
/// <c>FINm</c> and <c>CountryNm</c> in that order. The IRS schema declares the namespace
/// <c>urn:us:gov:treasury:irs:fatcaffilist</c>, but the published files use none; both are accepted. Rows stream one at
/// a time. Rows with a malformed GIIN, or an empty or overlong name or country, are left out and counted.
/// </summary>
internal static class FfiListXml
{
    public const string RootElement = "IRSFFIList";
    public const string RowElement = "FinancialInstitution";
    public const string IrsNamespace = "urn:us:gov:treasury:irs:fatcaffilist";
    public const int MaxNameLength = 300;
    public const int MaxCountryLength = 100;

    public static IEnumerable<FfiRow> Read(Stream xml, FfiReadStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = false,
        };

        using XmlReader reader = XmlReader.Create(xml, settings);
        string ns = OpenRoot(reader);
        while (true)
        {
            RawRow? row = NextRow(reader, ns);
            if (row is null)
            {
                break;
            }

            statistics.Rows++;
            FfiRow? entry = Check(row.Value);
            if (entry is not null)
            {
                statistics.ReturnedRows++;
                yield return entry;
            }
        }
    }

    private readonly record struct RawRow(string Giin, string Name, string Country);

    private static string OpenRoot(XmlReader reader)
    {
        try
        {
            if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != RootElement
                || reader.NamespaceURI is not ("" or IrsNamespace))
            {
                throw Problem(reader, $"The root element is not {RootElement}.");
            }

            string ns = reader.NamespaceURI;
            reader.Read();
            return ns;
        }
        catch (XmlException ex)
        {
            throw new FfiListFormatException("The file is not well-formed XML: " + ex.Message, ex.LineNumber, ex.LinePosition, ex);
        }
    }

    private static RawRow? NextRow(XmlReader reader, string ns)
    {
        try
        {
            XmlNodeType node = reader.MoveToContent();
            if (node == XmlNodeType.None)
            {
                return null;
            }

            if (node == XmlNodeType.EndElement && reader.LocalName == RootElement)
            {
                reader.Read();
                if (reader.MoveToContent() != XmlNodeType.None)
                {
                    throw Problem(reader, "Content follows the end of the list.");
                }

                return null;
            }

            if (node != XmlNodeType.Element || reader.LocalName != RowElement || reader.NamespaceURI != ns)
            {
                throw Problem(reader, $"Expected {RowElement}, found {node} '{reader.LocalName}'.");
            }

            if (reader.IsEmptyElement)
            {
                throw Problem(reader, $"An empty {RowElement} element.");
            }

            reader.ReadStartElement(RowElement, ns);
            string giin = ReadField(reader, "GIIN", ns);
            string name = ReadField(reader, "FINm", ns);
            string country = ReadField(reader, "CountryNm", ns);
            if (reader.MoveToContent() != XmlNodeType.EndElement || reader.LocalName != RowElement)
            {
                throw Problem(reader, $"Unexpected '{reader.LocalName}' in {RowElement}.");
            }

            reader.ReadEndElement();
            return new RawRow(giin, name, country);
        }
        catch (XmlException ex)
        {
            throw new FfiListFormatException("The file is not the FFI list structure: " + ex.Message, ex.LineNumber, ex.LinePosition, ex);
        }
    }

    private static string ReadField(XmlReader reader, string name, string ns)
    {
        if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != name || reader.NamespaceURI != ns)
        {
            throw Problem(reader, $"Expected {name}, found '{reader.LocalName}'.");
        }

        return reader.ReadElementContentAsString(name, ns);
    }

    private static FfiRow? Check(RawRow row)
    {
        string? giin = Giin.IsValid(row.Giin) ? row.Giin : Giin.Normalize(row.Giin);
        string name = row.Name.Trim();
        string country = row.Country.Trim();
        if (giin is null || name.Length == 0 || country.Length == 0 || name.Length > MaxNameLength || country.Length > MaxCountryLength)
        {
            return null;
        }

        return new FfiRow(giin, name, country);
    }

    private static FfiListFormatException Problem(XmlReader reader, string message)
    {
        var info = reader as IXmlLineInfo;
        return new FfiListFormatException(message, info?.LineNumber ?? 0, info?.LinePosition ?? 0);
    }
}

/// <summary>
/// One FFI list file as the IRS publishes it: a zip holding one XML file (FFIListFullXml.zip and the prior-month
/// archives) or the plain XML (FFIListFull.xml). Nothing is extracted to disk.
/// </summary>
internal sealed class FfiListFile
{
    /// <summary>The largest XML accepted inside a zip (a guard against zip bombs; the list is about 100 MB).</summary>
    public const long DefaultMaxXmlBytes = 2L * 1024 * 1024 * 1024;

    private FfiListFile(string path, bool isZip, string xmlEntryName, DateTime? timestamp)
    {
        Path = path;
        IsZip = isZip;
        XmlEntryName = xmlEntryName;
        XmlEntryTimestamp = timestamp;
    }

    public string Path { get; }

    public bool IsZip { get; }

    public string XmlEntryName { get; }

    /// <summary>The zip entry's last-write time: the only date an IRS list file carries.</summary>
    public DateTime? XmlEntryTimestamp { get; }

    public static FfiListFile Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Span<byte> head = stackalloc byte[4];
        int read;
        using (FileStream probe = File.OpenRead(path))
        {
            read = probe.ReadAtLeast(head, 4, throwOnEndOfStream: false);
        }

        bool isZip = read == 4 && head[0] == (byte)'P' && head[1] == (byte)'K' && head[2] == 3 && head[3] == 4;
        if (!isZip)
        {
            return new FfiListFile(path, false, System.IO.Path.GetFileName(path), null);
        }

        using ZipArchive zip = ZipFile.OpenRead(path);
        ZipArchiveEntry[] xmlEntries = zip.Entries
            .Where(e => e.Name.Length > 0 && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (xmlEntries.Length != 1)
        {
            throw new FfiListFormatException($"The zip holds {xmlEntries.Length} XML files; an FFI list zip holds exactly one.");
        }

        ZipArchiveEntry entry = xmlEntries[0];
        if (entry.Length > DefaultMaxXmlBytes)
        {
            throw new FfiListFormatException($"The XML inside the zip is {entry.Length} bytes, over the {DefaultMaxXmlBytes}-byte limit.");
        }

        return new FfiListFile(path, true, entry.FullName, entry.LastWriteTime.DateTime);
    }

    public IEnumerable<FfiRow> ReadRows(FfiReadStatistics statistics)
    {
        if (!IsZip)
        {
            using FileStream xml = File.OpenRead(Path);
            foreach (FfiRow row in FfiListXml.Read(xml, statistics))
            {
                yield return row;
            }

            yield break;
        }

        using ZipArchive zip = ZipFile.OpenRead(Path);
        ZipArchiveEntry entry = zip.GetEntry(XmlEntryName) ?? throw new FfiListFormatException("The XML entry disappeared from the zip.");
        using var limited = new LimitedReadStream(entry.Open(), DefaultMaxXmlBytes);
        foreach (FfiRow row in FfiListXml.Read(limited, statistics))
        {
            yield return row;
        }
    }

    /// <summary>A read-only stream that stops with <see cref="FfiListFormatException"/> past a byte limit.</summary>
    private sealed class LimitedReadStream(Stream inner, long limit) : Stream
    {
        private long _bytesRead;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _bytesRead;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int n = inner.Read(buffer);
            _bytesRead += n;
            if (_bytesRead > limit)
            {
                throw new FfiListFormatException($"The XML is over the {limit}-byte limit.");
            }

            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
