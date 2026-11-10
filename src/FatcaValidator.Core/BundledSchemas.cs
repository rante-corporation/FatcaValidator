using System.Xml;
using System.Xml.Schema;

namespace FatcaValidator.Core;

/// <summary>An official schema package compiled into this library.</summary>
public sealed class SchemaPackage
{
    internal SchemaPackage(string name, string folder, string mainSchemaFile, XmlQualifiedName rootElement)
    {
        Name = name;
        Folder = folder;
        MainSchemaFile = mainSchemaFile;
        RootElement = rootElement;
    }

    /// <summary>The display name, for example "FATCA XML Schema v2.0.1 (IRS)".</summary>
    public string Name { get; }

    /// <summary>The package's folder under Schemas/, for example "Fatca/v2.0.1".</summary>
    public string Folder { get; }

    /// <summary>The file name of the main schema, for example "FatcaXML_v2.0.1.xsd".</summary>
    public string MainSchemaFile { get; }

    /// <summary>The root element of the documents this package describes.</summary>
    public XmlQualifiedName RootElement { get; }

    /// <summary>Reads the package's schemas from this assembly and compiles them into a new schema set.</summary>
    /// <exception cref="XmlSchemaException">The bundled schemas do not compile, which means the package is broken.</exception>
    public XmlSchemaSet CreateSchemaSet() =>
        SchemaSetLoader.Load(
            new EmbeddedSchemaResolver(typeof(SchemaPackage).Assembly),
            EmbeddedSchemaResolver.UriFor($"Schemas/{Folder}/{MainSchemaFile}"));

    /// <summary>Returns <see cref="Name"/>.</summary>
    public override string ToString() => Name;
}

/// <summary>The official schema packages compiled into this library.</summary>
public static class BundledSchemas
{
    /// <summary>
    /// The IRS FATCA XML Schema v2.0.1 (published 2025-10-06) for Form 8966 reports, root element
    /// <c>FATCA_OECD</c> in <c>urn:oecd:ties:fatca:v2</c>. It also accepts FATCA XML v2.0 reports.
    /// </summary>
    public static SchemaPackage FatcaV2_0_1 { get; } = new(
        "FATCA XML Schema v2.0.1 (IRS)",
        "Fatca/v2.0.1",
        "FatcaXML_v2.0.1.xsd",
        new XmlQualifiedName("FATCA_OECD", "urn:oecd:ties:fatca:v2"));

    /// <summary>
    /// The OECD CRS XML Schema v2.0 (2019), the CRS format in use before the amended CRS, root element
    /// <c>CRS_OECD</c> in <c>urn:oecd:ties:crs:v2</c>.
    /// </summary>
    public static SchemaPackage CrsV2_0 { get; } = new(
        "CRS XML Schema v2.0 (OECD)",
        "Crs/v2.0",
        "CrsXML_v2.0.xsd",
        new XmlQualifiedName("CRS_OECD", "urn:oecd:ties:crs:v2"));

    /// <summary>
    /// The OECD CRS XML Schema v3.0 for the amended Common Reporting Standard, root element
    /// <c>CRS_OECD</c> in <c>urn:oecd:ties:crs:v3</c>.
    /// </summary>
    public static SchemaPackage CrsV3_0 { get; } = new(
        "CRS XML Schema v3.0 (OECD)",
        "Crs/v3.0",
        "CrsXML_v3.0.xsd",
        new XmlQualifiedName("CRS_OECD", "urn:oecd:ties:crs:v3"));

    /// <summary>
    /// The IRS FATCA Notification XML Schema v2.5 for the notifications the IRS sends after processing a file that
    /// passed its format checks, root element <c>FATCAValidFileNotification</c> in <c>urn:fatca:fatcavalidfilenotification</c>.
    /// </summary>
    public static SchemaPackage FatcaValidFileNotificationV2_5 { get; } = new(
        "FATCA Valid File Notification v2.5 (IRS)",
        "FatcaNotification/v2.5",
        "FATCA-VALID-FILE-NOTIFICATION-2.5.xsd",
        new XmlQualifiedName("FATCAValidFileNotification", "urn:fatca:fatcavalidfilenotification"));

    /// <summary>
    /// The IRS FATCA Notification XML Schema v2.5 for file-level error notifications, root element
    /// <c>FATCAFileErrorNotification</c> in <c>urn:fatca:fatcafileerrornotification</c>.
    /// </summary>
    public static SchemaPackage FatcaFileErrorNotificationV2_5 { get; } = new(
        "FATCA File Error Notification v2.5 (IRS)",
        "FatcaNotification/v2.5",
        "FATCA-FILE-ERROR-NOTIFICATION-2.5.xsd",
        new XmlQualifiedName("FATCAFileErrorNotification", "urn:fatca:fatcafileerrornotification"));

    /// <summary>
    /// The IRS IDES Sender File Metadata XML Schema v1.3 (effective 07-2020) for the metadata file of an IDES data packet,
    /// root element <c>FATCAIDESSenderFileMetadata</c> in <c>urn:fatca:idessenderfilemetadata</c>.
    /// </summary>
    public static SchemaPackage FatcaSenderMetadataV1_3 { get; } = new(
        "FATCA IDES Sender Metadata v1.3 (IRS)",
        "FatcaMetadata/v1.3",
        "FATCA-IDES-SenderFileMetadata-1.3.xsd",
        new XmlQualifiedName("FATCAIDESSenderFileMetadata", "urn:fatca:idessenderfilemetadata"));

    /// <summary>The OECD Crypto-Asset Reporting Framework (CARF) XML Schema v1.5, root <c>CARF_OECD</c> in <c>urn:oecd:ties:carf:v1</c>.</summary>
    public static SchemaPackage CarfV1_5 { get; } = Oecd("CARF XML Schema v1.5", "Carf/v1.5", "CARFXML_v1.5.xsd", "CARF_OECD", "urn:oecd:ties:carf:v1");

    /// <summary>The OECD Country-by-Country Reporting XML Schema v2.0, root <c>CBC_OECD</c> in <c>urn:oecd:ties:cbc:v2</c>.</summary>
    public static SchemaPackage CbcV2_0 { get; } = Oecd("CbC XML Schema v2.0", "Cbc/v2.0", "CbcXML_v2.0.xsd", "CBC_OECD", "urn:oecd:ties:cbc:v2");

    /// <summary>The OECD CRS Status Message XML Schema v2.0, root <c>CRSStatusMessage_OECD</c> in <c>urn:oecd:ties:csm:v2</c>.</summary>
    public static SchemaPackage CrsStatusMessageV2_0 { get; } = Oecd("CRS Status Message XML Schema v2.0", "CrsStatusMessage/v2.0", "CrsStatusMessageXML_v2.0.xsd", "CRSStatusMessage_OECD", "urn:oecd:ties:csm:v2");

    /// <summary>The OECD CbC Status Message XML Schema v2.0, root <c>CbCStatusMessage_OECD</c> in <c>urn:oecd:ties:cbcstm:v2</c>.</summary>
    public static SchemaPackage CbcStatusMessageV2_0 { get; } = Oecd("CbC Status Message XML Schema v2.0", "CbcStatusMessage/v2.0", "CbcStatusMessageXML_v2.0.xsd", "CbCStatusMessage_OECD", "urn:oecd:ties:cbcstm:v2");

    /// <summary>The OECD Generic Status Message XML Schema v2.0, root <c>GenericStatusMessage_OECD</c> in <c>urn:oecd:ties:gsm:v1</c>.</summary>
    public static SchemaPackage GenericStatusMessageV2_0 { get; } = Oecd("Generic Status Message XML Schema v2.0", "GenericStatusMessage/v2.0", "GenericStatusMessageXML_v2.0.xsd", "GenericStatusMessage_OECD", "urn:oecd:ties:gsm:v1");

    /// <summary>The OECD Exchange on Tax Rulings XML Schema v2.0, root <c>ETR_OECD</c> in <c>urn:oecd:ties:etr:v2</c>.</summary>
    public static SchemaPackage EtrV2_0 { get; } = Oecd("ETR XML Schema v2.0", "Etr/v2.0", "EtrXML_v2.0.xsd", "ETR_OECD", "urn:oecd:ties:etr:v2");

    /// <summary>The OECD Exchange on Tax Rulings XML Schema v3.0, root <c>ETR_OECD</c> in <c>urn:oecd:ties:etr:v3</c>.</summary>
    public static SchemaPackage EtrV3_0 { get; } = Oecd("ETR XML Schema v3.0", "Etr/v3.0", "EtrXML_v3.0.xsd", "ETR_OECD", "urn:oecd:ties:etr:v3");

    /// <summary>The OECD ETR Status Message XML Schema v2.0, root <c>ETRStatusMessage_OECD</c> in <c>urn:oecd:ties:etrstm:v2</c>.</summary>
    public static SchemaPackage EtrStatusMessageV2_0 { get; } = Oecd("ETR Status Message XML Schema v2.0", "EtrStatusMessage/v2.0", "EtrStatusMessageXML_v2.0.xsd", "ETRStatusMessage_OECD", "urn:oecd:ties:etrstm:v2");

    /// <summary>The OECD Mandatory Disclosure Rules XML Schema v1.0, root <c>MDR_OECD</c> in <c>urn:oecd:ties:mdr:v1</c>.</summary>
    public static SchemaPackage MdrV1_0 { get; } = Oecd("MDR XML Schema v1.0", "Mdr/v1.0", "MdrXML_v1.0.xsd", "MDR_OECD", "urn:oecd:ties:mdr:v1");

    /// <summary>The OECD No or only Nominal Tax Jurisdictions XML Schema v1.0, root <c>NTJ_OECD</c> in <c>urn:oecd:ties:ntj:v1</c>.</summary>
    public static SchemaPackage NtjV1_0 { get; } = Oecd("NTJ XML Schema v1.0", "Ntj/v1.0", "NtjXML_v1.0.xsd", "NTJ_OECD", "urn:oecd:ties:ntj:v1");

    /// <summary>The OECD Standard Transmission Format XML Schema v2.2, root <c>STF_OECD</c> in <c>urn:oecd:ties:stf:v2.2</c>.</summary>
    public static SchemaPackage StfV2_2 { get; } = Oecd("STF XML Schema v2.2", "Stf/v2.2", "stfdirect-2.2.xsd", "STF_OECD", "urn:oecd:ties:stf:v2.2");

    /// <summary>All bundled packages.</summary>
    public static IReadOnlyList<SchemaPackage> All { get; } =
    [
        FatcaV2_0_1, CrsV2_0, CrsV3_0,
        FatcaValidFileNotificationV2_5, FatcaFileErrorNotificationV2_5, FatcaSenderMetadataV1_3,
        CarfV1_5, CbcV2_0, CrsStatusMessageV2_0, CbcStatusMessageV2_0, GenericStatusMessageV2_0,
        EtrV2_0, EtrV3_0, EtrStatusMessageV2_0, MdrV1_0, NtjV1_0, StfV2_2,
    ];

    private static SchemaPackage Oecd(string name, string folder, string mainSchemaFile, string root, string ns) =>
        new($"{name} (OECD)", folder, mainSchemaFile, new XmlQualifiedName(root, ns));

    /// <summary>Returns the package for documents with this root element, or <see langword="null"/> when none is bundled.</summary>
    public static SchemaPackage? FindByRootElement(XmlQualifiedName rootElement)
    {
        ArgumentNullException.ThrowIfNull(rootElement);
        return All.FirstOrDefault(package => package.RootElement == rootElement);
    }
}
