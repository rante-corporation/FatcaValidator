using FatcaValidator.Cli;

namespace FatcaValidator.Tests;

public sealed class CliAppTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<string[]> BadArguments => new()
    {
        Array.Empty<string>(),
        new[] { "--file", "does-not-exist.xml" },
        new[] { "--file", TestFiles.Irs("newnilfireport.xml"), "--max-errors", "0" },
        new[] { "--file", TestFiles.Irs("newnilfireport.xml"), "--unknown-option" },
    };

    [Fact]
    public void ValidFile_ExitsWithZero()
    {
        (int exitCode, string output, _) = Run("--file", TestFiles.Irs("newnilfireport.xml"));

        Assert.Equal(ExitCodes.Valid, exitCode);
        Assert.Contains("Schema: FATCA XML Schema v2.0.1 (IRS)", output, StringComparison.Ordinal);
        Assert.Contains("Result: VALID - no schema violations found.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidFile_ListsTheErrorAndExitsWithOne()
    {
        string text = File.ReadAllText(TestFiles.Irs("newnilfireport.xml"))
            .Replace(">JP</sfa:TransmittingCountry>", ">ZZ</sfa:TransmittingCountry>", StringComparison.Ordinal);

        (int exitCode, string output, _) = Run("-f", _temp.Write("bad.xml", text));

        Assert.Equal(ExitCodes.Invalid, exitCode);
        Assert.Contains("Result: INVALID - 1 error, 0 warnings.", output, StringComparison.Ordinal);
        Assert.Contains("  error    Line 6, Position ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorsBeyondTheLimit_AreCountedButNotListed()
    {
        string text = File.ReadAllText(TestFiles.Irs("newnilfireport.xml"))
            .Replace(">JP</sfa:TransmittingCountry>", ">ZZ</sfa:TransmittingCountry>", StringComparison.Ordinal)
            .Replace(">2016-12-31</sfa:ReportingPeriod>", ">2016-13-45</sfa:ReportingPeriod>", StringComparison.Ordinal);

        (int exitCode, string output, _) = Run("-f", _temp.Write("bad.xml", text), "-m", "1");

        Assert.Equal(ExitCodes.Invalid, exitCode);
        Assert.Contains("Result: INVALID - 2 errors, 0 warnings.", output, StringComparison.Ordinal);
        Assert.Contains("... and 1 error more", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaOption_IsUsedInsteadOfTheBundledSchemas()
    {
        (int exitCode, string output, _) = Run("--file", TestFiles.Crs(TestFiles.CrsSample), "--schema", TestFiles.FatcaSchema);

        Assert.Equal(ExitCodes.Invalid, exitCode);
        Assert.Contains("is not declared by the schema", output, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownDocumentType_ExitsWithTwo()
    {
        (int exitCode, string output, _) = Run("--file", _temp.Write("other.xml", """<Report xmlns="urn:example:other"/>"""));

        Assert.Equal(ExitCodes.NotValidated, exitCode);
        Assert.Contains("Result: NOT VALIDATED", output, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(BadArguments))]
    public void BadArguments_ExitWithTwo(string[] args)
    {
        (int exitCode, _, string error) = Run(args);

        Assert.Equal(ExitCodes.NotValidated, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Help_ExitsWithZero()
    {
        (int exitCode, string output, _) = Run("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("--schema", output, StringComparison.Ordinal);
        Assert.Contains("--max-errors", output, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode = CliApp.Run(args, output, error, useColor: false);
        return (exitCode, output.ToString(), error.ToString());
    }
}
