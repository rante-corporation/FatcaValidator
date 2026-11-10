using System.IO.Compression;
using System.Text.Json;
using FatcaValidator.Cli;
using FatcaValidator.Core;

namespace FatcaValidator.Tests;

public sealed class FfiListTests : IDisposable
{
    private const string SampleGiin = "S519K4.99999.SL.392";
    private const string Sample2016 = "2016_2IW3WK.00000.LE.152.xml";
    private const string SponsorGiin = "2P2RBE.00000.SP.152";
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void IrsExampleList_Loads()
    {
        FfiList list = FfiList.Load(TestFiles.IrsFfiListExample);

        Assert.Equal(20, list.Count);
        Assert.Equal(0, list.LeftOutRows);
        Assert.Null(list.FileTimestamp);
        Assert.True(list.TryGetEntry("98Q96B.00000.LE.250", out FfiListEntry? entry));
        Assert.Equal("Test Bank One", entry.Name);
        Assert.Equal("France", entry.Country);
    }

    [Fact]
    public void ZippedList_LoadsWithTheDateOfItsXml()
    {
        string zip = Path.Combine(_temp.Path, "FFIListFullXml.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(TestFiles.Ffi("ffi-list-irs-samples.xml"), "FFIListFull.xml");
        }

        FfiList list = FfiList.Load(zip);

        Assert.Equal(3, list.Count);
        Assert.NotNull(list.FileTimestamp);
        Assert.StartsWith("FFIListFullXml.zip, 3 GIINs, file dated ", list.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void FileThatIsNotAnFfiList_IsRefused()
    {
        FfiListException ex = Assert.Throws<FfiListException>(() => FfiList.Load(TestFiles.Irs("newnilfireport.xml")));

        Assert.Contains("could not be read", ex.Message, StringComparison.Ordinal);
        Assert.Contains("IRSFFIList", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("newnilfireport.xml")]
    [InlineData("amendednilfireport.xml")]
    [InlineData(Sample2016)]
    public void ListedGiins_PassTheCheck(string sample)
    {
        ValidationResult result = SampleListEngine().Validate(TestFiles.Irs(sample));

        Assert.True(result.IsValid, Describe(result));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GiinsNotOnTheList_AreWarnings()
    {
        var engine = new ValidatorEngine { FfiList = FfiList.Load(TestFiles.IrsFfiListExample) };

        ValidationResult result = engine.Validate(TestFiles.Irs("newnilfireport.xml"));

        Assert.True(result.IsValid, Describe(result));
        Assert.Equal(2, result.WarningCount);
        Assert.All(result.Warnings, warning => Assert.Equal(GiinCheckCodes.NotListed, warning.Code));
        Assert.Contains(result.Warnings, warning => warning.LineNumber == 5 && warning.Message.Contains("MessageSpec/SendingCompanyIN", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.LineNumber == 16 && warning.Message.Contains("ReportingFI/TIN", StringComparison.Ordinal));
    }

    [Fact]
    public void NameThatDiffersFromTheList_IsAWarning()
    {
        string path = TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml", ("<sfa:Name >Bank of NN</sfa:Name>", "<sfa:Name >Other Bank</sfa:Name>"));

        ValidationResult result = SampleListEngine().Validate(path);

        ValidationIssue warning = Assert.Single(result.Issues);
        Assert.Equal(GiinCheckCodes.NameDiffers, warning.Code);
        Assert.Equal(IssueSource.FfiList, warning.Source);
        Assert.Equal(17, warning.LineNumber);
        Assert.Contains("'BANK OF NN'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ListedSponsor_PassesTheCheck()
    {
        ValidationResult result = SampleListEngine().Validate(WriteSponsorVariant(SponsorGiin, "Reporting FI Name"));

        Assert.True(result.IsValid, Describe(result));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void SponsorNotOnTheList_IsAWarning()
    {
        ValidationResult result = SampleListEngine().Validate(WriteSponsorVariant("2P2RBE.00099.SP.152", "Reporting FI Name"));

        ValidationIssue warning = Assert.Single(result.Issues);
        Assert.Equal(GiinCheckCodes.NotListed, warning.Code);
        Assert.Equal(54, warning.LineNumber);
        Assert.Contains("GIIN 2P2RBE.00099.SP.152 (Sponsor/TIN)", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SponsorNameThatDiffersFromTheList_IsAWarning()
    {
        ValidationResult result = SampleListEngine().Validate(WriteSponsorVariant(SponsorGiin, "Other Sponsor"));

        ValidationIssue warning = Assert.Single(result.Issues);
        Assert.Equal(GiinCheckCodes.NameDiffers, warning.Code);
        Assert.Equal(55, warning.LineNumber);
        Assert.Contains("but Sponsor/Name is 'Other Sponsor'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IntermediaryGiin_IsLookedUp()
    {
        // The sample's intermediary has a plain TIN, which is not looked up (see ListedGiins_PassTheCheck); a GIIN is.
        string path = TestFiles.WriteIrsVariant(_temp, Sample2016,
            ("<sfa:TIN issuedBy=\"US\">123456789</sfa:TIN>", "<sfa:TIN issuedBy=\"US\">8Z3K7Q.00000.ME.152</sfa:TIN>"));

        ValidationResult result = SampleListEngine().Validate(path);

        ValidationIssue warning = Assert.Single(result.Issues);
        Assert.Equal(GiinCheckCodes.NotListed, warning.Code);
        Assert.Equal(80, warning.LineNumber);
        Assert.Contains("GIIN 8Z3K7Q.00000.ME.152 (Intermediary/TIN)", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("S519K4.99999.SL.39", "is not a well-formed GIIN")]
    [InlineData("s519k4.99999.sl.392", "should be written S519K4.99999.SL.392")]
    public void MalformedGiin_IsAWarning(string tin, string expected)
    {
        string path = TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml", ($"<sfa:TIN>{SampleGiin}</sfa:TIN>", $"<sfa:TIN>{tin}</sfa:TIN>"));

        ValidationResult result = SampleListEngine().Validate(path);

        ValidationIssue warning = Assert.Single(result.Issues);
        Assert.Equal(GiinCheckCodes.Format, warning.Code);
        Assert.Equal(16, warning.LineNumber);
        Assert.Contains(expected, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TaxAuthorityGiins_AreNotLookedUp()
    {
        string path = TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml",
            ($"<sfa:SendingCompanyIN>{SampleGiin}</sfa:SendingCompanyIN>", "<sfa:SendingCompanyIN>000000.00000.TA.392</sfa:SendingCompanyIN>"));

        ValidationResult result = SampleListEngine().Validate(path);

        Assert.True(result.IsValid, Describe(result));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void GiinCheck_SkipsFilesThatBreakTheirSchema()
    {
        string path = TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml", (">JP</sfa:TransmittingCountry>", ">ZZ</sfa:TransmittingCountry>"));

        ValidationResult result = new ValidatorEngine { FfiList = FfiList.Load(TestFiles.IrsFfiListExample) }.Validate(path);

        Assert.Equal(IssueCodes.SchemaError, Assert.Single(result.Errors).Code);
        Assert.Equal(IssueCodes.RulesSkipped, Assert.Single(result.Warnings).Code);
    }

    [Fact]
    public void GiinCheck_LeavesCrsReportsAlone()
    {
        ValidationResult result = new ValidatorEngine { FfiList = FfiList.Load(TestFiles.IrsFfiListExample) }.Validate(TestFiles.Crs(TestFiles.CrsSample));

        Assert.True(result.IsValid, Describe(result));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void FfiListOption_ChecksGiinsAndNamesTheList()
    {
        (int exitCode, string output, _) = Run("--file", TestFiles.Irs("newnilfireport.xml"), "--ffi-list", TestFiles.IrsFfiListExample);

        Assert.Equal(ExitCodes.Valid, exitCode);
        Assert.StartsWith("FFI list: FFIListExample.xml, 20 GIINs", output, StringComparison.Ordinal);
        Assert.Contains($"[FFI-GIIN-NOT-LISTED] GIIN {SampleGiin} (MessageSpec/SendingCompanyIN)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonReport_NamesTheFfiList()
    {
        (int exitCode, string output, _) = Run("--file", TestFiles.Irs("newnilfireport.xml"), "--ffi-list", TestFiles.IrsFfiListExample, "--format", "json");

        Assert.Equal(ExitCodes.Valid, exitCode);
        using JsonDocument json = JsonDocument.Parse(output);
        Assert.Equal(20, json.RootElement.GetProperty("ffiList").GetProperty("giins").GetInt32());
        JsonElement issue = json.RootElement.GetProperty("files")[0].GetProperty("issues")[0];
        Assert.Equal("ffi-list", issue.GetProperty("source").GetString());
        Assert.Equal(GiinCheckCodes.NotListed, issue.GetProperty("code").GetString());
    }

    [Fact]
    public void UnreadableFfiList_ExitsWithTwo()
    {
        (int exitCode, _, string error) = Run("--file", TestFiles.Irs("newnilfireport.xml"), "--ffi-list", TestFiles.Irs("newnilfireport.xml"));

        Assert.Equal(ExitCodes.NotValidated, exitCode);
        Assert.Contains("could not be read", error, StringComparison.Ordinal);
    }

    private static ValidatorEngine SampleListEngine() => new() { FfiList = FfiList.Load(TestFiles.Ffi("ffi-list-irs-samples.xml")) };

    /// <summary>
    /// The 2016 IRS sample with a sponsor. The sample keeps its Sponsor block inside a comment; this one follows it, and
    /// ReportingFI drops its FilerCategory as that comment says. The sponsor's TIN is on line 54, its name on line 55.
    /// </summary>
    private string WriteSponsorVariant(string tin, string name) =>
        TestFiles.WriteIrsVariant(_temp, Sample2016,
            ("<ftc:FilerCategory>FATCA604</ftc:FilerCategory>", ""),
            ("<ftc:ReportingGroup>", $"""
                <ftc:ReportingGroup>
                            <ftc:Sponsor>
                                <sfa:ResCountryCode>CL</sfa:ResCountryCode>
                                <sfa:TIN issuedBy="US">{tin}</sfa:TIN>
                                <sfa:Name>{name}</sfa:Name>
                                <sfa:Address><sfa:CountryCode>CL</sfa:CountryCode><sfa:AddressFree>234 Street Name/City/State/Zip</sfa:AddressFree></sfa:Address>
                                <ftc:FilerCategory>FATCA607</ftc:FilerCategory>
                                <ftc:DocSpec><ftc:DocTypeIndic>FATCA11</ftc:DocTypeIndic><ftc:DocRefId>2IW3WK.00000.LE.152.DR1-2</ftc:DocRefId></ftc:DocSpec>
                            </ftc:Sponsor>
                """));

    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode = CliApp.Run(args, output, error, useColor: false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string Describe(ValidationResult result) =>
        $"{result.Status}: {string.Join(Environment.NewLine, result.Issues)}";
}
