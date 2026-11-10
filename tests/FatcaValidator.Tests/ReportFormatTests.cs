using System.Text.Json;
using System.Xml.Linq;
using FatcaValidator.Cli;

namespace FatcaValidator.Tests;

public sealed class ReportFormatTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void JsonReport_DescribesEveryFile()
    {
        (int exitCode, string output, _) = Run("--file", TestFiles.Irs("newnilfireport.xml"), InvalidReport(), "--format", "json");

        Assert.Equal(ExitCodes.Invalid, exitCode);
        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement root = json.RootElement;
        Assert.Equal("fatca-val", root.GetProperty("tool").GetProperty("name").GetString());
        JsonElement summary = root.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("files").GetInt32());
        Assert.Equal(1, summary.GetProperty("valid").GetInt32());
        Assert.Equal(1, summary.GetProperty("invalid").GetInt32());
        JsonElement invalid = root.GetProperty("files")[1];
        Assert.Equal("invalid", invalid.GetProperty("status").GetString());
        JsonElement issue = invalid.GetProperty("issues")[0];
        Assert.Equal("XSD-ERROR", issue.GetProperty("code").GetString());
        Assert.Equal("schema", issue.GetProperty("source").GetString());
        Assert.Equal(6, issue.GetProperty("line").GetInt32());
    }

    [Fact]
    public void SarifReport_ListsRulesAndRegions()
    {
        (int exitCode, string output, _) = Run("--file", InvalidReport(), "--format", "sarif");

        Assert.Equal(ExitCodes.Invalid, exitCode);
        using JsonDocument sarif = JsonDocument.Parse(output);
        Assert.Equal("2.1.0", sarif.RootElement.GetProperty("version").GetString());
        JsonElement run = sarif.RootElement.GetProperty("runs")[0];
        JsonElement driver = run.GetProperty("tool").GetProperty("driver");
        Assert.Equal("fatca-val", driver.GetProperty("name").GetString());
        Assert.Equal("XSD-ERROR", driver.GetProperty("rules")[0].GetProperty("id").GetString());
        JsonElement result = run.GetProperty("results")[0];
        Assert.Equal("XSD-ERROR", result.GetProperty("ruleId").GetString());
        Assert.Equal("error", result.GetProperty("level").GetString());
        JsonElement location = result.GetProperty("locations")[0].GetProperty("physicalLocation");
        Assert.Equal(6, location.GetProperty("region").GetProperty("startLine").GetInt32());
        Assert.EndsWith("variant-newnilfireport.xml", location.GetProperty("artifactLocation").GetProperty("uri").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void JUnitReport_FailsTheInvalidFile()
    {
        (int exitCode, string output, _) = Run("--file", TestFiles.Irs("newnilfireport.xml"), InvalidReport(), "--format", "junit");

        Assert.Equal(ExitCodes.Invalid, exitCode);
        XElement suites = XDocument.Parse(output).Root!;
        Assert.Equal("testsuites", suites.Name.LocalName);
        Assert.Equal("2", (string?)suites.Attribute("tests"));
        Assert.Equal("1", (string?)suites.Attribute("failures"));
        Assert.Equal("0", (string?)suites.Attribute("errors"));
        Assert.Single(suites.Descendants("failure"));
    }

    [Fact]
    public void FolderInput_ValidatesItsXmlFiles()
    {
        string folder = WriteReportsFolder();

        (int exitCode, string output, _) = Run("--file", folder);

        Assert.Equal(ExitCodes.Invalid, exitCode);
        Assert.Contains("Checked 2 files: 1 valid, 1 invalid, 0 not validated", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WildcardInput_MatchesFileNames()
    {
        string folder = WriteReportsFolder();

        (int exitCode, string output, _) = Run("--file", Path.Combine(folder, "*.xml"));

        Assert.Equal(ExitCodes.Invalid, exitCode);
        Assert.Contains("Checked 2 files: 1 valid, 1 invalid, 0 not validated", output, StringComparison.Ordinal);
    }

    [Fact]
    public void OutputOption_WritesTheReportToAFile()
    {
        string report = Path.Combine(_temp.Path, "report.json");

        (int exitCode, string output, _) = Run("--file", TestFiles.Irs("newnilfireport.xml"), "--format", "json", "--output", report);

        Assert.Equal(ExitCodes.Valid, exitCode);
        Assert.Contains("Report: " + report, output, StringComparison.Ordinal);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(report));
        Assert.Equal(1, json.RootElement.GetProperty("summary").GetProperty("valid").GetInt32());
    }

    [Fact]
    public void RulesOption_RunsYourOwnRules()
    {
        string rules = _temp.Write("rules.json", """
            {
              "appliesTo": "urn:oecd:ties:fatca:v2",
              "namespaces": { "sfa": "urn:oecd:ties:stffatcatypes:v2" },
              "rules": [ { "id": "MY-RULE", "context": "//sfa:TransmittingCountry", "assert": ". = 'US'", "message": "Sent from {.}." } ]
            }
            """);

        (int exitCode, string output, _) = Run("--file", TestFiles.Irs("newnilfireport.xml"), "--rules", rules);

        Assert.Equal(ExitCodes.Invalid, exitCode);
        Assert.Contains("error    Line 6, Position", output, StringComparison.Ordinal);
        Assert.Contains("[MY-RULE] Sent from JP.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void BadRuleFile_ExitsWithTwo()
    {
        string rules = _temp.Write("rules.json", "not json");

        (int exitCode, _, string error) = Run("--file", TestFiles.Irs("newnilfireport.xml"), "--rules", rules);

        Assert.Equal(ExitCodes.NotValidated, exitCode);
        Assert.Contains("not valid JSON", error, StringComparison.Ordinal);
    }

    [Fact]
    public void PreflightOption_ReportsProhibitedCharacters()
    {
        string path = TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml", ("Bank of NN", "Bank -- of NN"));

        (int exitCode, string output, _) = Run("--file", path, "--preflight");

        Assert.Equal(ExitCodes.Invalid, exitCode);
        Assert.Contains("[IDES-PROHIBITED-CHARACTERS]", output, StringComparison.Ordinal);
    }

    [Fact]
    public void MetadataOption_ChecksTheMetadataFile()
    {
        (int exitCode, string output, _) = Run("--file", TestFiles.Irs("newnilfireport.xml"), "--metadata", TestFiles.Fatca("newnilfireport-metadata.xml"));

        Assert.Equal(ExitCodes.Valid, exitCode);
        Assert.Contains("Result: VALID - no errors found.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void MetadataOption_NeedsExactlyOneFile()
    {
        (int exitCode, _, string error) = Run("--file", TestFiles.Irs("newnilfireport.xml"), TestFiles.Irs("voidnilfireport.xml"),
            "--metadata", TestFiles.Fatca("newnilfireport-metadata.xml"));

        Assert.Equal(ExitCodes.NotValidated, exitCode);
        Assert.Contains("--metadata describes one report", error, StringComparison.Ordinal);
    }

    private string InvalidReport() =>
        TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml", (">JP</sfa:TransmittingCountry>", ">ZZ</sfa:TransmittingCountry>"));

    private string WriteReportsFolder()
    {
        string folder = Path.Combine(_temp.Path, "reports");
        Directory.CreateDirectory(folder);
        File.Copy(TestFiles.Irs("newnilfireport.xml"), Path.Combine(folder, "a.xml"));
        File.Copy(InvalidReport(), Path.Combine(folder, "b.xml"));
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "not xml");
        return folder;
    }

    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode = CliApp.Run(args, output, error, useColor: false);
        return (exitCode, output.ToString(), error.ToString());
    }
}
