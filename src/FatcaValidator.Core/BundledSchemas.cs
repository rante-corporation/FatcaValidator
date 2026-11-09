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

    /// <summary>All bundled packages.</summary>
    public static IReadOnlyList<SchemaPackage> All { get; } = [FatcaV2_0_1, CrsV2_0, CrsV3_0];

    /// <summary>Returns the package for documents with this root element, or <see langword="null"/> when none is bundled.</summary>
    public static SchemaPackage? FindByRootElement(XmlQualifiedName rootElement)
    {
        ArgumentNullException.ThrowIfNull(rootElement);
        return All.FirstOrDefault(package => package.RootElement == rootElement);
    }
}
