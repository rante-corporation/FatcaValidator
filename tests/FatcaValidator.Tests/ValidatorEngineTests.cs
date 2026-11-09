using System.Xml.Schema;
using FatcaValidator.Core;

namespace FatcaValidator.Tests;

public sealed class ValidatorEngineTests : IDisposable
{
    private readonly ValidatorEngine _engine = new();
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("newnilfireport.xml")]
    [InlineData("amendednilfireport.xml")]
    [InlineData("voidnilfireport.xml")]
    [InlineData("2014_2IW3WK.00000.LE.152.xml")]
    [InlineData("2016_2IW3WK.00000.LE.152.xml")]
    public void IrsSampleReports_AreValidAgainstTheBundledFatcaSchema(string sample)
    {
        ValidationResult result = _engine.Validate(TestFiles.Irs(sample));

        Assert.True(result.IsValid, Describe(result));
        Assert.Equal(BundledSchemas.FatcaV2_0_1.Name, result.Schema);
        Assert.Equal(0, result.WarningCount);
    }

    [Fact]
    public void CrsV3Report_IsValidAgainstTheBundledCrsSchema()
    {
        ValidationResult result = _engine.Validate(TestFiles.Crs(TestFiles.CrsSample));

        Assert.True(result.IsValid, Describe(result));
        Assert.Equal(BundledSchemas.CrsV3_0.Name, result.Schema);
        Assert.Equal(0, result.WarningCount);
    }

    [Fact]
    public void BundledPackages_CompileAndDeclareTheirRootElements()
    {
        foreach (SchemaPackage package in BundledSchemas.All)
        {
            XmlSchemaSet schemas = package.CreateSchemaSet();

            Assert.True(schemas.IsCompiled, package.Name);
            Assert.True(schemas.GlobalElements.Contains(package.RootElement), $"{package.Name} does not declare {package.RootElement}.");
        }
    }

    [Fact]
    public void WrongCountryCode_IsReportedOnItsLine()
    {
        string path = WriteIrsVariant("newnilfireport.xml",
            ("<sfa:TransmittingCountry>JP</sfa:TransmittingCountry>", "<sfa:TransmittingCountry>ZZ</sfa:TransmittingCountry>"));

        ValidationResult result = _engine.Validate(path);

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        ValidationIssue error = Assert.Single(result.Errors);
        Assert.Equal(6, error.LineNumber);
        Assert.Contains("TransmittingCountry", error.Message, StringComparison.Ordinal);
        Assert.Contains("'ZZ'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRequiredElement_IsInvalid()
    {
        string path = WriteIrsVariant("newnilfireport.xml",
            ("<sfa:MessageRefId>DBA6455E-8454-47D9-914B-FEE48E4EF3AA</sfa:MessageRefId>", ""));

        ValidationResult result = _engine.Validate(path);

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        Assert.Contains(result.Errors, error => error.Message.Contains("MessageRefId", StringComparison.Ordinal));
    }

    [Fact]
    public void CrsReport_CheckedAgainstTheFatcaSchema_FailsInsteadOfPassingSilently()
    {
        ValidationResult result = _engine.Validate(TestFiles.Crs(TestFiles.CrsSample), TestFiles.FatcaSchema);

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        ValidationIssue error = Assert.Single(result.Errors);
        Assert.Contains("'CRS_OECD'", error.Message, StringComparison.Ordinal);
        Assert.Contains("not declared", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaFile_IsUsedWithTheSchemasItImports()
    {
        ValidationResult result = _engine.Validate(TestFiles.Irs("newnilfireport.xml"), TestFiles.FatcaSchema);

        Assert.True(result.IsValid, Describe(result));
        Assert.Equal(Path.GetFullPath(TestFiles.FatcaSchema), result.Schema);
    }

    [Fact]
    public void SchemaFile_ImportingFromTheNetwork_IsRefused()
    {
        string schema = _temp.Write("main.xsd", """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:t="urn:example:types"
                       targetNamespace="urn:example:main" elementFormDefault="qualified">
              <xs:import namespace="urn:example:types" schemaLocation="http://127.0.0.1:9/types.xsd"/>
              <xs:element name="Root" type="t:Root_Type"/>
            </xs:schema>
            """);
        string document = _temp.Write("doc.xml", """<Root xmlns="urn:example:main"/>""");

        ValidationResult result = _engine.Validate(document, schema);

        Assert.Equal(ValidationStatus.NotValidated, result.Status);
        Assert.Contains("never over the network", Assert.Single(result.Issues).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaFile_ImportingFromOutsideItsFolder_IsRefused()
    {
        _temp.Write("types.xsd", """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:example:types">
              <xs:complexType name="Root_Type"/>
            </xs:schema>
            """);
        string schema = _temp.Write(Path.Combine("schemas", "main.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:t="urn:example:types"
                       targetNamespace="urn:example:main" elementFormDefault="qualified">
              <xs:import namespace="urn:example:types" schemaLocation="../types.xsd"/>
              <xs:element name="Root" type="t:Root_Type"/>
            </xs:schema>
            """);
        string document = _temp.Write("doc.xml", """<Root xmlns="urn:example:main"/>""");

        ValidationResult result = _engine.Validate(document, schema);

        Assert.Equal(ValidationStatus.NotValidated, result.Status);
        Assert.Contains("must be in the main schema's folder", Assert.Single(result.Issues).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaFile_CopiedWithoutItsImports_NamesTheMissingFile()
    {
        string schema = Path.Combine(_temp.Path, "FatcaXML_v2.0.1.xsd");
        File.Copy(TestFiles.FatcaSchema, schema);

        ValidationResult result = _engine.Validate(TestFiles.Irs("newnilfireport.xml"), schema);

        Assert.Equal(ValidationStatus.NotValidated, result.Status);
        string message = Assert.Single(result.Issues).Message;
        Assert.Contains("isofatcatypes_v1.2.xsd' was not found", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""<Report xmlns="urn:example:other"/>""")]
    [InlineData("""<crs:CRS_OECD xmlns:crs="urn:oecd:ties:crs:v2" version="2.0"/>""")]
    public void DocumentWithoutABundledSchema_IsNotValidated(string content)
    {
        ValidationResult result = _engine.Validate(_temp.Write("doc.xml", content));

        Assert.Equal(ValidationStatus.NotValidated, result.Status);
        Assert.Null(result.Schema);
        Assert.Contains("No bundled schema matches", Assert.Single(result.Issues).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedXml_IsInvalidWithItsPosition()
    {
        string path = _temp.Write("truncated.xml", "<ftc:FATCA_OECD xmlns:ftc=\"urn:oecd:ties:fatca:v2\" version=\"2.0\">\n  <ftc:MessageSpec>\n");

        ValidationResult result = _engine.Validate(path);

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        ValidationIssue error = Assert.Single(result.Errors, e => e.Message.StartsWith("The file is not well-formed XML", StringComparison.Ordinal));
        Assert.True(error.LineNumber > 0);
        Assert.DoesNotContain(", position ", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dtd_IsRefusedAndItsEntitiesAreNotRead()
    {
        string secret = _temp.Write("secret.txt", "TOP-SECRET-MARKER");
        string path = _temp.Write("xxe.xml", $"""
            <?xml version="1.0"?>
            <!DOCTYPE ftc:FATCA_OECD [ <!ENTITY leak SYSTEM "{new Uri(secret).AbsoluteUri}"> ]>
            <ftc:FATCA_OECD xmlns:ftc="urn:oecd:ties:fatca:v2" version="2.0">&leak;</ftc:FATCA_OECD>
            """);

        ValidationResult result = _engine.Validate(path);

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        ValidationIssue error = Assert.Single(result.Issues);
        Assert.Contains("DTD", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("TOP-SECRET-MARKER", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IssueLimit_KeepsCountingBeyondTheLimit()
    {
        string path = WriteIrsVariant("newnilfireport.xml",
            ("<sfa:TransmittingCountry>JP</sfa:TransmittingCountry>", "<sfa:TransmittingCountry>ZZ</sfa:TransmittingCountry>"),
            ("<sfa:ReportingPeriod>2016-12-31</sfa:ReportingPeriod>", "<sfa:ReportingPeriod>2016-13-45</sfa:ReportingPeriod>"));
        var engine = new ValidatorEngine { MaxIssuesPerSeverity = 1 };

        ValidationResult result = engine.Validate(path);

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        Assert.Equal(2, result.ErrorCount);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void MissingFile_IsNotValidated()
    {
        ValidationResult result = _engine.Validate(Path.Combine(_temp.Path, "missing.xml"));

        Assert.Equal(ValidationStatus.NotValidated, result.Status);
        Assert.Contains("could not be read", Assert.Single(result.Issues).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IssueLimit_MustBeAtLeastOne() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ValidatorEngine { MaxIssuesPerSeverity = 0 });

    private string WriteIrsVariant(string sample, params (string Find, string Replace)[] edits)
    {
        string text = File.ReadAllText(TestFiles.Irs(sample));
        foreach ((string find, string replace) in edits)
        {
            Assert.Contains(find, text, StringComparison.Ordinal);
            text = text.Replace(find, replace, StringComparison.Ordinal);
        }

        return _temp.Write("variant-" + sample, text);
    }

    private static string Describe(ValidationResult result) =>
        $"{result.Status}: {string.Join(Environment.NewLine, result.Issues)}";
}
