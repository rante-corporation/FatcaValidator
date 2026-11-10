using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using System.Xml.XPath;

namespace FatcaValidator.Core;

/// <summary>Who sent a notification or status message.</summary>
public enum FeedbackSource
{
    /// <summary>The IRS: a FATCA notification (FATCA Notification XML v2.5).</summary>
    Irs,

    /// <summary>A tax administration: an OECD status message (CRS, CbC, ETR or generic, v2.0).</summary>
    Oecd,
}

/// <summary>A field that a notification or status message points at.</summary>
/// <param name="Path">The field's path in the report, as the authority wrote it.</param>
/// <param name="Text">The authority's description of the problem (the IRS's FieldErrorTxt), if any.</param>
/// <param name="Code">The authority's code for it (the IRS's FieldErrorCd), if any.</param>
public sealed record FeedbackField(string Path, string? Text, string? Code);

/// <summary>One error that a notification or status message reports, about the whole file or about records.</summary>
public sealed record FeedbackError
{
    /// <summary>The authority's code: an IRS notification or record-level error code, or an OECD status message code.</summary>
    public required string Code { get; init; }

    /// <summary>What the code means or what went wrong, in the authority's words when the file gives them.</summary>
    public string? Description { get; init; }

    /// <summary>The DocRefIds of the records in error.</summary>
    public IReadOnlyList<string> DocRefIds { get; init; } = [];

    /// <summary>The fields in error.</summary>
    public IReadOnlyList<FeedbackField> Fields { get; init; } = [];

    /// <summary>What the IRS asks you to do (ActionRequestedTxt).</summary>
    public string? Action { get; init; }

    /// <summary>By when (the IRS's ActionRequestedDueDateTxt).</summary>
    public string? DueDate { get; init; }

    /// <summary>The kind of record (the IRS's FATCAReportTypeCd, such as ACCOUNT_REPORT).</summary>
    public string? ReportType { get; init; }

    /// <summary>The record's DocTypeIndic, as the IRS repeats it.</summary>
    public string? DocTypeIndic { get; init; }

    /// <summary>The record's CorrDocRefId, as the IRS repeats it.</summary>
    public string? CorrDocRefId { get; init; }

    /// <summary>The record's CorrMessageRefId, as the IRS repeats it.</summary>
    public string? CorrMessageRefId { get; init; }

    /// <summary>The financial institution the IRS files the error under: its GIIN and name.</summary>
    public string? FinancialInstitution { get; init; }

    /// <summary>The line of the error in the notification or status message, or 0.</summary>
    public int LineNumber { get; init; }
}

/// <summary>
/// An IRS FATCA notification or an OECD status message read for what it says about a message you sent: whether the
/// file was accepted, and the file and record errors with the authority's own codes, texts and requested actions.
/// </summary>
public sealed partial class FeedbackDocument
{
    private const string NotificationBase = "urn:fatca:fatcanotificationbase";

    private static readonly SchemaPackage[] IrsPackages =
        [BundledSchemas.FatcaValidFileNotificationV2_5, BundledSchemas.FatcaFileErrorNotificationV2_5];

    private static readonly SchemaPackage[] OecdPackages =
    [
        BundledSchemas.CrsStatusMessageV2_0, BundledSchemas.CbcStatusMessageV2_0,
        BundledSchemas.EtrStatusMessageV2_0, BundledSchemas.GenericStatusMessageV2_0,
    ];

    private static readonly Lazy<IReadOnlyDictionary<string, string>> NotificationCodes = new(LoadNotificationCodes);

    private FeedbackDocument(SchemaPackage package, FeedbackSource source)
    {
        Package = package;
        Source = source;
    }

    /// <summary>The bundled schema package of the document's kind.</summary>
    public SchemaPackage Package { get; }

    /// <summary>Who sent it.</summary>
    public FeedbackSource Source { get; }

    /// <summary>The IRS notification code (FATCANotificationCd), such as NVF or NSV; <see langword="null"/> for status messages.</summary>
    public string? Code { get; private init; }

    /// <summary>The IRS's name for the notification code, from the bundled notification schema.</summary>
    public string? CodeDescription { get; private init; }

    /// <summary>
    /// Whether the file was accepted: an IRS Valid File Notification (NVF), or a status message whose status is
    /// Accepted. Records of an accepted file can still have errors.
    /// </summary>
    public bool Accepted { get; private init; }

    /// <summary>The sender: the IRS's GIIN, or the tax administration's country.</summary>
    public string? Sender { get; private init; }

    /// <summary>The receiver: your GIIN, or your country.</summary>
    public string? Receiver { get; private init; }

    /// <summary>The document's own reference: the IRS notification id, or the status message's MessageRefId.</summary>
    public string? Reference { get; private init; }

    /// <summary>When the authority created it, as written.</summary>
    public string? CreatedAt { get; private init; }

    /// <summary>The MessageRefId of the message it is about, when it names one.</summary>
    public string? OriginalMessageRefId { get; private init; }

    /// <summary>The transmission it is about: the IDES or CTS transmission id.</summary>
    public string? OriginalTransmissionId { get; private init; }

    /// <summary>When that transmission was sent, as written.</summary>
    public string? OriginalSentAt { get; private init; }

    /// <summary>The name of the transmitted file (the IRS's SenderFileId).</summary>
    public string? OriginalFileName { get; private init; }

    /// <summary>The IRS's explanation of the notification (NotificationContentTxt).</summary>
    public string? Content { get; private init; }

    /// <summary>How many records the IRS counted in the file (RecordCnt), when it says.</summary>
    public int? RecordCount { get; private init; }

    /// <summary>Who validated the message (the status message's ValidatedBy).</summary>
    public IReadOnlyList<string> ValidatedBy { get; private init; } = [];

    /// <summary>Errors about the whole file: an IRS file-level notification, or the status message's FileError elements.</summary>
    public IReadOnlyList<FeedbackError> FileErrors { get; private init; } = [];

    /// <summary>Errors about records: the IRS's record-level errors, or the status message's RecordError elements.</summary>
    public IReadOnlyList<FeedbackError> RecordErrors { get; private init; } = [];

    /// <summary>Whether a root element is an IRS notification or an OECD status message that this class reads.</summary>
    public static bool IsFeedback(XmlQualifiedName rootElement) => FindPackage(rootElement) is not null;

    /// <summary>The IRS's name for a FATCA notification code, from the bundled schema, or <see langword="null"/>.</summary>
    public static string? DescribeNotificationCode(string code) => NotificationCodes.Value.GetValueOrDefault(code);

    /// <summary>Reads a notification or status message file; <see langword="null"/> when the file is neither.</summary>
    /// <exception cref="XmlException">The file is not well-formed XML.</exception>
    public static FeedbackDocument? Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(stream);
    }

    /// <summary>Reads a notification or status message; <see langword="null"/> when the document is neither.</summary>
    /// <exception cref="XmlException">The document is not well-formed XML.</exception>
    public static FeedbackDocument? Read(Stream xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CloseInput = false,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        };

        XPathNavigator root;
        using (XmlReader reader = XmlReader.Create(xml, settings))
        {
            root = new XPathDocument(reader).CreateNavigator();
        }

        if (!root.MoveToChild(XPathNodeType.Element))
        {
            return null;
        }

        SchemaPackage? package = FindPackage(new XmlQualifiedName(root.LocalName, root.NamespaceURI));
        return package is null ? null
            : IrsPackages.Contains(package) ? ReadIrs(root, package)
            : ReadOecd(root, package);
    }

    private static SchemaPackage? FindPackage(XmlQualifiedName root) =>
        IrsPackages.Concat(OecdPackages).FirstOrDefault(package => package.RootElement == root);

    private static FeedbackDocument ReadIrs(XPathNavigator root, SchemaPackage package)
    {
        XPathNavigator? header = Child(root, "FATCANotificationHeaderGrp");
        XPathNavigator? metadata = Child(root, "OriginalFileMetadataGrp");
        XPathNavigator? action = Child(root, "ActionRequestedGrp");
        string? code = Text(header, "FATCANotificationCd");
        bool accepted = code == "NVF";
        string? description = code is null ? null : DescribeNotificationCode(code);

        var fileErrors = new List<FeedbackError>();
        if (!accepted && code is not null)
        {
            fileErrors.Add(new FeedbackError
            {
                Code = code,
                Description = description,
                Action = Text(action, "ActionRequestedTxt"),
                DueDate = Text(action, "ActionRequestedDueDateTxt"),
                LineNumber = Line(Child(header, "FATCANotificationCd")),
            });
        }

        var recordErrors = new List<FeedbackError>();
        foreach (XPathNavigator institution in Descendants(root, "FATCARecordErrorFIGrp"))
        {
            string? name = Join(Text(institution, "ReportingFIGIIN"), Text(institution, "ReportingFINm"))
                ?? Join(Text(institution, "SponsorGIIN"), Text(institution, "SponsorNm"));
            foreach (XPathNavigator detail in Children(institution, "FATCARecordErrorDetailGrp"))
            {
                XPathNavigator? detailAction = Child(detail, "ActionRequestedGrp");
                recordErrors.Add(new FeedbackError
                {
                    Code = Text(detail, "RecordLevelErrorCd") ?? "",
                    DocRefIds = Text(detail, "DocRefId") is { } docRefId ? [docRefId] : [],
                    Fields = Children(detail, "FieldErrorGrp")
                        .Select(field => new FeedbackField(Text(field, "FieldNm") ?? "", Text(field, "FieldErrorTxt"), Text(field, "FieldErrorCd")))
                        .ToList(),
                    Action = Text(detailAction, "ActionRequestedTxt"),
                    DueDate = Text(detailAction, "ActionRequestedDueDateTxt"),
                    ReportType = Text(detail, "FATCAReportTypeCd"),
                    DocTypeIndic = Text(detail, "DocTypeIndicCd"),
                    CorrDocRefId = Text(detail, "CorrDocRefId"),
                    CorrMessageRefId = Text(detail, "CorrMessageRefId"),
                    FinancialInstitution = name,
                    LineNumber = Line(detail),
                });
            }
        }

        return new FeedbackDocument(package, FeedbackSource.Irs)
        {
            Code = code,
            CodeDescription = description,
            Accepted = accepted,
            Sender = Text(header, "FATCAEntitySenderId"),
            Receiver = Text(header, "FATCAEntityReceiverId"),
            Reference = Text(header, "FATCANotificationRefId"),
            CreatedAt = Text(header, "FATCANotificationCreateTs"),
            OriginalMessageRefId = Text(Child(root, "OriginalFileMessageSpecGrp"), "MessageRefId"),
            OriginalTransmissionId = Text(metadata, "IDESTransmissionId"),
            OriginalSentAt = Text(metadata, "IDESSendingTs"),
            OriginalFileName = Text(metadata, "SenderFileId"),
            Content = Text(root, "NotificationContentTxt"),
            RecordCount = int.TryParse(Text(Child(root, "OriginalFileProcessingDataGrp"), "RecordCnt"), out int count) ? count : null,
            FileErrors = fileErrors,
            RecordErrors = recordErrors,
        };
    }

    private static FeedbackDocument ReadOecd(XPathNavigator root, SchemaPackage package)
    {
        XPathNavigator? spec = Child(root, "MessageSpec");
        XPathNavigator? original = Descendants(root, "OriginalMessage").FirstOrDefault();
        XPathNavigator? metadata = Child(original, "FileMetaData");
        XPathNavigator? errors = Descendants(root, "ValidationErrors").FirstOrDefault();
        XPathNavigator? result = Descendants(root, "ValidationResult").FirstOrDefault();

        return new FeedbackDocument(package, FeedbackSource.Oecd)
        {
            Accepted = Text(result, "Status") == "Accepted",
            Sender = Text(spec, "TransmittingCountry"),
            Receiver = Text(spec, "ReceivingCountry"),
            Reference = Text(spec, "MessageRefId"),
            CreatedAt = Text(spec, "Timestamp"),
            OriginalMessageRefId = Text(original, "OriginalMessageRefID"),
            OriginalTransmissionId = Text(metadata, "CTSTransmissionID"),
            OriginalSentAt = Text(metadata, "CTSSendingTimeStamp"),
            ValidatedBy = Children(result, "ValidatedBy").Select(Value).OfType<string>().ToList(),
            FileErrors = Children(errors, "FileError").Select(error => new FeedbackError
            {
                Code = Text(error, "Code") ?? "",
                Description = Text(error, "Details"),
                LineNumber = Line(error),
            }).ToList(),
            RecordErrors = Children(errors, "RecordError").Select(error => new FeedbackError
            {
                Code = Text(error, "Code") ?? "",
                Description = Text(error, "Details"),
                DocRefIds = Children(error, "DocRefIDInError").Select(Value).OfType<string>().ToList(),
                Fields = Children(error, "FieldsInError").Select(field => Text(field, "FieldPath")).OfType<string>()
                    .Select(path => new FeedbackField(path, null, null)).ToList(),
                LineNumber = Line(error),
            }).ToList(),
        };
    }

    /// <summary>The descriptions of FATCANotificationCdType's values in the bundled IRS notification schema.</summary>
    private static IReadOnlyDictionary<string, string> LoadNotificationCodes()
    {
        var codes = new Dictionary<string, string>(StringComparer.Ordinal);
        XmlSchemaSet schemas = BundledSchemas.FatcaFileErrorNotificationV2_5.CreateSchemaSet();
        if (schemas.GlobalTypes[new XmlQualifiedName("FATCANotificationCdType", NotificationBase)] is XmlSchemaSimpleType
            {
                Content: XmlSchemaSimpleTypeRestriction restriction,
            })
        {
            foreach (XmlSchemaEnumerationFacet facet in restriction.Facets.OfType<XmlSchemaEnumerationFacet>())
            {
                string? text = facet.Annotation?.Items.OfType<XmlSchemaDocumentation>()
                    .SelectMany(documentation => documentation.Markup ?? [])
                    .Select(node => node?.InnerText)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (facet.Value is { } value && text is not null)
                {
                    codes[value] = Spaces().Replace(text.Trim(), " ");
                }
            }
        }

        return codes;
    }

    private static XPathNavigator? Child(XPathNavigator? parent, string localName) => Children(parent, localName).FirstOrDefault();

    private static IEnumerable<XPathNavigator> Children(XPathNavigator? parent, string localName)
    {
        if (parent is null)
        {
            yield break;
        }

        XPathNavigator child = parent.Clone();
        if (!child.MoveToFirstChild())
        {
            yield break;
        }

        do
        {
            if (child.NodeType == XPathNodeType.Element && child.LocalName == localName)
            {
                yield return child.Clone();
            }
        }
        while (child.MoveToNext());
    }

    private static IEnumerable<XPathNavigator> Descendants(XPathNavigator root, string localName)
    {
        XPathNodeIterator iterator = root.SelectDescendants(XPathNodeType.Element, matchSelf: false);
        while (iterator.MoveNext())
        {
            if (iterator.Current is { } node && node.LocalName == localName)
            {
                yield return node.Clone();
            }
        }
    }

    private static string? Text(XPathNavigator? parent, string localName) => Value(Child(parent, localName));

    private static string? Value(XPathNavigator? node)
    {
        string? value = node?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : Spaces().Replace(value, " ");
    }

    private static string? Join(string? id, string? name) =>
        id is null ? name : name is null ? id : $"{id} ({name})";

    private static int Line(XPathNavigator? node) => node is IXmlLineInfo info ? info.LineNumber : 0;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
