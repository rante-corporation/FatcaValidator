using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;

namespace FatcaValidator.Core;

/// <summary>
/// Validates FATCA and CRS XML reports against the official XML schemas. Files are streamed, so a
/// large report is never loaded into memory whole, and every well-formedness and schema error is
/// reported with its line and position.
/// </summary>
/// <remarks>
/// <para>
/// Without a schema file, the engine picks the bundled schema whose root element matches the
/// document's (see <see cref="BundledSchemas"/>).
/// </para>
/// <para>
/// Safety: DTDs are refused, so there is no external-entity (XXE) access and no entity expansion;
/// <c>xsi:schemaLocation</c> hints in the document are ignored, so a document cannot choose its own
/// schema; and nothing is fetched over the network.
/// </para>
/// <para>
/// An engine compiles each schema the first time it needs it and then reuses it. It is not
/// thread-safe: use one engine per thread.
/// </para>
/// </remarks>
public sealed partial class ValidatorEngine
{
    private readonly Dictionary<SchemaPackage, XmlSchemaSet> _bundledSchemas = [];
    private readonly Dictionary<string, XmlSchemaSet> _fileSchemas = new(StringComparer.Ordinal);
    private readonly int _maxIssuesPerSeverity = 1000;

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
    /// Validates an XML file against the bundled schema that matches its root element, or against
    /// <paramref name="schemaPath"/> when one is given.
    /// </summary>
    /// <param name="xmlPath">The FATCA or CRS XML file.</param>
    /// <param name="schemaPath">
    /// A main .xsd file to use instead of the bundled schemas, or <see langword="null"/>. The schemas it
    /// imports are read from its folder; nothing is fetched over the network.
    /// </param>
    public ValidationResult Validate(string xmlPath, string? schemaPath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(xmlPath);

        FileStream stream;
        try
        {
            stream = new FileStream(xmlPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16, FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return NotValidated(FullPathOrNull(schemaPath), $"The file '{xmlPath}' could not be read: {ex.Message}");
        }

        using (stream)
        {
            return Validate(stream, schemaPath);
        }
    }

    /// <summary>
    /// Validates XML read from a seekable stream, starting at its current position. The stream is left open.
    /// </summary>
    /// <param name="xml">The document. It is read twice: once for the root element, then for validation.</param>
    /// <param name="schemaPath">As for <see cref="Validate(string, string?)"/>.</param>
    /// <exception cref="ArgumentException">The stream does not support seeking.</exception>
    public ValidationResult Validate(Stream xml, string? schemaPath = null)
    {
        ArgumentNullException.ThrowIfNull(xml);
        if (!xml.CanSeek)
        {
            throw new ArgumentException("The stream must support seeking: the root element is read before validation starts.", nameof(xml));
        }

        string? schemaFile = FullPathOrNull(schemaPath);
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
                    return NotValidated(schemaFile, loadError);
                }

                schemaName = schemaFile;
                schemas = fileSchemas;
            }
            else
            {
                SchemaPackage? package = BundledSchemas.FindByRootElement(root);
                if (package is null)
                {
                    return NotValidated(null,
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
                    rootPosition));
                return issues.ToResult(ValidationStatus.Invalid, schemaName);
            }

            // 4. Stream the whole document through the validating reader.
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
            }

            return issues.ToResult(issues.ErrorCount == 0 ? ValidationStatus.Valid : ValidationStatus.Invalid, schemaName);
        }
        catch (IOException ex)
        {
            return NotValidated(schemaFile, $"The document could not be read: {ex.Message}");
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

        settings.ValidationEventHandler += (_, e) => issues.Add(new ValidationIssue(
            e.Severity == XmlSeverityType.Error ? ValidationSeverity.Error : ValidationSeverity.Warning,
            e.Message,
            e.Exception?.LineNumber ?? 0,
            e.Exception?.LinePosition ?? 0));
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

    private static ValidationResult NotValidated(string? schema, string reason)
    {
        var issues = new IssueCollector(1);
        issues.Add(new ValidationIssue(ValidationSeverity.Error, reason));
        return issues.ToResult(ValidationStatus.NotValidated, schema);
    }

    private static ValidationIssue FromXmlException(XmlException ex)
    {
        string message = ex.Message.Contains("DTD is prohibited", StringComparison.Ordinal)
            ? "The file contains a DTD (<!DOCTYPE ...>). DTDs are refused for security reasons and were not processed."
            : "The file is not well-formed XML: " + TrailingPosition().Replace(ex.Message, "");
        return new ValidationIssue(ValidationSeverity.Error, message, ex.LineNumber, ex.LinePosition);
    }

    private static string DescribeBundledRoots() =>
        string.Join(" and ", BundledSchemas.All.Select(p => $"'{p.RootElement.Name}' in '{p.RootElement.Namespace}' ({p.Name})"));

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
