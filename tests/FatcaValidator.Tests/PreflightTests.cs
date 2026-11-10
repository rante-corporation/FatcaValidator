using System.Text;
using FatcaValidator.Core;

namespace FatcaValidator.Tests;

public sealed class PreflightTests : IDisposable
{
    private const string FirstDocRefId = "S519K4.99999.SL.392.50B80D2D-79DA-4AFD-8148-F06480FFDEB5";
    private const string SecondDocRefId = "S519K4.99999.SL.392.CE54CA78-7C31-4EC2-B73C-E387C314F426";

    private readonly ValidatorEngine _engine = new() { FatcaPreflight = true };
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("newnilfireport.xml")]
    [InlineData("amendednilfireport.xml")]
    [InlineData("voidnilfireport.xml")]
    public void IrsSampleReports_PassThePreflight(string sample)
    {
        ValidationResult result = _engine.Validate(TestFiles.Irs(sample));

        Assert.True(result.IsValid, Describe(result));
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("2014_2IW3WK.00000.LE.152.xml")]
    [InlineData("2016_2IW3WK.00000.LE.152.xml")]
    public void IrsTestDataSamples_PassWithOneTestDataWarning(string sample)
    {
        ValidationResult result = _engine.Validate(TestFiles.Irs(sample));

        Assert.True(result.IsValid, Describe(result));
        ValidationIssue warning = Assert.Single(result.Issues);
        Assert.Equal("IRS-TEST-DATA", warning.Code);
        Assert.Equal(IssueSource.Preflight, warning.Source);
    }

    [Theory]
    [InlineData("Bank of NN", "Bank -- of NN", 17, "\"--\"")]
    [InlineData("123 Main Street", "123 Main /* Street", 20, "\"/*\"")]
    [InlineData("Bank of NN", "Bank of N&#78;", 17, "\"&#\"")]
    public void ProhibitedPatternsInTheText_AreErrors(string find, string replace, int line, string named)
    {
        string path = TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml", (find, replace));

        ValidationResult result = _engine.Validate(path);

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        ValidationIssue issue = Assert.Single(result.Errors);
        Assert.Equal(PreflightCodes.ProhibitedCharacters, issue.Code);
        Assert.Equal(line, issue.LineNumber);
        Assert.Contains(named, issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredEncodingOtherThanUtf8_IsAnError()
    {
        string path = TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml", ("encoding=\"UTF-8\"", "encoding=\"ISO-8859-1\""));

        ValidationResult result = _engine.Validate(path);

        ValidationIssue issue = Assert.Single(result.Errors);
        Assert.Equal(PreflightCodes.Encoding, issue.Code);
        Assert.Equal(1, issue.LineNumber);
    }

    [Fact]
    public void Utf16File_IsAnError()
    {
        string text = File.ReadAllText(TestFiles.Irs("newnilfireport.xml")).Replace("encoding=\"UTF-8\"", "encoding=\"UTF-16\"", StringComparison.Ordinal);
        string path = Path.Combine(_temp.Path, "utf16.xml");
        File.WriteAllText(path, text, Encoding.Unicode);

        ValidationResult result = _engine.Validate(path);

        Assert.Equal(PreflightCodes.Encoding, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void FileOverTheCompressedSizeLimit_GetsAWarning()
    {
        var engine = new ValidatorEngine { FatcaPreflight = true, PreflightMaxCompressedBytes = 300 };

        ValidationResult result = engine.Validate(TestFiles.Irs("newnilfireport.xml"));

        Assert.True(result.IsValid, Describe(result));
        Assert.Equal(PreflightCodes.Size, Assert.Single(result.Warnings).Code);
    }

    [Theory]
    [InlineData("S519K4.99999.SL.392.", "IRS-DOCREFID-LENGTH")]
    [InlineData("S519K4.99999.SL.392.ABC_123", "IRS-DOCREFID-CHARACTERS")]
    public void DocRefIdFormatProblems_AreErrors(string docRefId, string code)
    {
        string path = TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml", (FirstDocRefId, docRefId));

        ValidationResult result = _engine.Validate(path);

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        ValidationIssue issue = Assert.Single(result.Errors);
        Assert.Equal(code, issue.Code);
        Assert.Equal(24, issue.LineNumber);
    }

    [Fact]
    public void DocRefIdWithoutTheReportingGiin_IsAWarning()
    {
        string path = TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml", (FirstDocRefId, "AAAAAA.99999.SL.392.50B80D2D-79DA-4AFD-8148-F06480FFDEB5"));

        ValidationResult result = _engine.Validate(path);

        Assert.True(result.IsValid, Describe(result));
        ValidationIssue warning = Assert.Single(result.Warnings);
        Assert.Equal("IRS-DOCREFID-GIIN", warning.Code);
        Assert.Equal(24, warning.LineNumber);
    }

    [Fact]
    public void RepeatedDocRefId_IsAnError()
    {
        string path = TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml", (SecondDocRefId, FirstDocRefId));

        ValidationResult result = _engine.Validate(path);

        ValidationIssue issue = Assert.Single(result.Errors);
        Assert.Equal("IRS-DOCREFID-UNIQUE", issue.Code);
        Assert.Equal(31, issue.LineNumber);
    }

    [Fact]
    public void CorrectionWithoutCorrDocRefId_IsAnError()
    {
        string path = TestFiles.WriteIrsVariant(_temp, "amendednilfireport.xml",
            ($"<ftc:CorrDocRefId>{FirstDocRefId}</ftc:CorrDocRefId>", ""));

        ValidationResult result = _engine.Validate(path);

        ValidationIssue issue = Assert.Single(result.Errors);
        Assert.Equal("IRS-CORRDOCREFID-REQUIRED", issue.Code);
        Assert.Contains("FATCA4", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchingMetadata_PassesThePreflight()
    {
        ValidationResult result = _engine.Validate(TestFiles.Irs("newnilfireport.xml"),
            new ValidationOptions { MetadataPath = TestFiles.Fatca("newnilfireport-metadata.xml") });

        Assert.True(result.IsValid, Describe(result));
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData(">000000.00000.TA.840<", ">000000.00000.TA.124<", PreflightCodes.MetadataReceiver, ValidationSeverity.Error)]
    [InlineData(">RPT<", ">NTF<", PreflightCodes.MetadataCommunicationType, ValidationSeverity.Error)]
    [InlineData("<TaxYear>2016<", "<TaxYear>2015<", PreflightCodes.MetadataTaxYear, ValidationSeverity.Error)]
    [InlineData(">XML<", ">PDF<", PreflightCodes.MetadataFileFormat, ValidationSeverity.Error)]
    [InlineData(">NONE<", ">BASE64<", PreflightCodes.MetadataBinaryEncoding, ValidationSeverity.Error)]
    [InlineData("<FATCAEntitySenderId>S519K4.99999.SL.392<", "<FATCAEntitySenderId>S519K4.99999.SL.123<", PreflightCodes.MetadataSender, ValidationSeverity.Warning)]
    public void MetadataThatDoesNotMatchTheReport_IsReported(string find, string replace, string code, ValidationSeverity severity)
    {
        string metadata = WriteMetadataVariant(find, replace);

        ValidationResult result = _engine.Validate(TestFiles.Irs("newnilfireport.xml"), new ValidationOptions { MetadataPath = metadata });

        ValidationIssue issue = Assert.Single(result.Issues);
        Assert.Equal(code, issue.Code);
        Assert.Equal(severity, issue.Severity);
    }

    [Fact]
    public void MetadataThatBreaksItsSchema_IsReported()
    {
        string metadata = WriteMetadataVariant("<TaxYear>2016<", "<TaxYear>20X6<");

        ValidationResult result = _engine.Validate(TestFiles.Irs("newnilfireport.xml"), new ValidationOptions { MetadataPath = metadata });

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        Assert.All(result.Errors, error => Assert.Equal(PreflightCodes.MetadataSchema, error.Code));
        Assert.StartsWith("Sender metadata file, line ", result.Errors.First().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FileThatIsNotSenderMetadata_IsReported()
    {
        ValidationResult result = _engine.Validate(TestFiles.Irs("newnilfireport.xml"),
            new ValidationOptions { MetadataPath = TestFiles.Irs("voidnilfireport.xml") });

        Assert.Equal(PreflightCodes.MetadataUnreadable, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Preflight_LeavesCrsReportsAlone()
    {
        ValidationResult result = _engine.Validate(TestFiles.Crs(TestFiles.CrsSample));

        Assert.True(result.IsValid, Describe(result));
        Assert.Empty(result.Issues);
    }

    private string WriteMetadataVariant(string find, string replace)
    {
        string text = File.ReadAllText(TestFiles.Fatca("newnilfireport-metadata.xml"));
        Assert.Contains(find, text, StringComparison.Ordinal);
        return _temp.Write("metadata.xml", text.Replace(find, replace, StringComparison.Ordinal));
    }

    private static string Describe(ValidationResult result) =>
        $"{result.Status}: {string.Join(Environment.NewLine, result.Issues)}";
}
