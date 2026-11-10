using FatcaValidator.Core;

namespace FatcaValidator.Tests;

public sealed class RuleSetTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<string, string> InvalidRuleFiles => new()
    {
        { "not json", "not valid JSON" },
        { """{ "rules": [] }""", "\"rules\" must be a non-empty array" },
        { """{ "rules": [ { "id": "A", "context": "/*", "asert": "true()", "message": "m" } ] }""", "unknown property \"asert\"" },
        { """{ "rules": [ { "id": "A", "context": "//[", "assert": "true()", "message": "m" } ] }""", "is not a valid XPath 1.0 expression" },
        { """{ "rules": [ { "id": "A", "context": "//x:Y", "assert": "true()", "message": "m" } ] }""", "'//x:Y'" },
        { """{ "rules": [ { "id": "A", "context": "/*", "assert": "fv:nope(.)", "message": "m" } ] }""", "Unknown function 'fv:nope()'" },
        { """{ "rules": [ { "id": "A", "context": "1 + 1", "assert": "true()", "message": "m" } ] }""", "must select nodes" },
        { """{ "rules": [ { "id": "A", "context": "/*", "assert": "true()", "message": "m" }, { "id": "A", "context": "/*", "assert": "true()", "message": "m" } ] }""", "used twice" },
        { """{ "rules": [ { "id": "A", "unique": "/*", "context": "/*", "assert": "true()", "message": "m" } ] }""", "not both" },
        { """{ "rules": [ { "id": "A", "severity": "fatal", "context": "/*", "assert": "true()", "message": "m" } ] }""", "\"severity\" must be" },
        { """{ "rules": [ { "id": "A", "context": "/*", "assert": "true()", "message": "Value {." } ] }""", "without a closing" },
        { """{ "rules": [ { "id": "bad id!", "context": "/*", "assert": "true()", "message": "m" } ] }""", "may only contain" },
    };

    [Fact]
    public void AssertRule_ReportsTheFailingNodeWithItsIdAndLine()
    {
        RuleSet rules = FatcaRules("""
            { "id": "T-COUNTRY", "context": "//sfa:TransmittingCountry", "assert": ". = 'US'", "message": "Sent from {.}, not US." }
            """);

        ValidationResult result = Run(rules, TestFiles.Irs("newnilfireport.xml"));

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        ValidationIssue issue = Assert.Single(result.Issues);
        Assert.Equal("T-COUNTRY", issue.Code);
        Assert.Equal(IssueSource.Rule, issue.Source);
        Assert.Equal("Sent from JP, not US.", issue.Message);
        Assert.Equal(6, issue.LineNumber);
    }

    [Fact]
    public void WarningRule_KeepsTheFileValid()
    {
        RuleSet rules = FatcaRules("""
            { "id": "T-COUNTRY", "severity": "warning", "context": "//sfa:TransmittingCountry", "assert": ". = 'US'", "message": "Sent from {.}." }
            """);

        ValidationResult result = Run(rules, TestFiles.Irs("newnilfireport.xml"));

        Assert.Equal(ValidationStatus.Valid, result.Status);
        Assert.Equal("T-COUNTRY", Assert.Single(result.Warnings).Code);
    }

    [Fact]
    public void MatchesFunction_TestsRegularExpressions()
    {
        RuleSet rules = FatcaRules("""
            { "id": "T-GIIN", "context": "//ftc:DocSpec/ftc:DocRefId", "assert": "fv:matches(., '^S519K4[.]')", "message": "{.}" },
            { "id": "T-IGNORE-CASE", "context": "//ftc:DocSpec/ftc:DocRefId", "assert": "fv:matches(., '^s519k4', 'i')", "message": "{.}" },
            { "id": "T-UUID", "context": "//ftc:DocSpec/ftc:DocRefId", "assert": "fv:matches(., '[0-9A-F]{8}-[0-9A-F]{4}$')", "message": "No short tail in {.}" }
            """);

        ValidationResult result = Run(rules, TestFiles.Irs("newnilfireport.xml"));

        Assert.Equal(2, result.ErrorCount);
        Assert.All(result.Errors, error => Assert.Equal("T-UUID", error.Code));
    }

    [Fact]
    public void StringFunctions_Work()
    {
        RuleSet rules = FatcaRules("""
            { "id": "T-STRINGS", "context": "//sfa:MessageType", "assert": "fv:ends-with(., 'CA') and fv:lower-case(.) = 'fatca' and fv:upper-case('fatca') = .", "message": "{.}" }
            """);

        ValidationResult result = Run(rules, TestFiles.Irs("newnilfireport.xml"));

        Assert.True(result.IsValid, Describe(result));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void UniqueRule_ReportsRepeatsAndPointsBackToTheFirst()
    {
        string path = TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml",
            ("S519K4.99999.SL.392.CE54CA78-7C31-4EC2-B73C-E387C314F426", "S519K4.99999.SL.392.50B80D2D-79DA-4AFD-8148-F06480FFDEB5"));
        RuleSet rules = FatcaRules("""
            { "id": "T-UNIQUE", "unique": "//ftc:DocSpec/ftc:DocRefId", "message": "{.} is repeated" }
            """);

        ValidationResult result = Run(rules, path);

        ValidationIssue issue = Assert.Single(result.Errors);
        Assert.Equal(31, issue.LineNumber);
        Assert.EndsWith("is repeated (first used on line 24)", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MessagePlaceholders_QuoteValuesAndEscapeBraces()
    {
        RuleSet rules = FatcaRules("""
            { "id": "T-MESSAGE", "context": "//sfa:SendingCompanyIN", "assert": "false()", "message": "{{x}} {.} has {string-length(.)} characters" }
            """);

        ValidationResult result = Run(rules, TestFiles.Irs("newnilfireport.xml"));

        Assert.Equal("{x} S519K4.99999.SL.392 has 19 characters", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void Rules_DoNotRunOnFilesThatBreakTheirSchema()
    {
        string path = TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml", (">JP</sfa:TransmittingCountry>", ">ZZ</sfa:TransmittingCountry>"));
        RuleSet rules = FatcaRules("""
            { "id": "T-COUNTRY", "context": "//sfa:TransmittingCountry", "assert": ". = 'US'", "message": "Sent from {.}." }
            """);

        ValidationResult result = Run(rules, path);

        Assert.Equal(ValidationStatus.Invalid, result.Status);
        Assert.Equal(IssueCodes.SchemaError, Assert.Single(result.Errors).Code);
        Assert.Equal(IssueCodes.RulesSkipped, Assert.Single(result.Warnings).Code);
    }

    [Fact]
    public void Rules_OnlyRunOnTheDocumentsTheyApplyTo()
    {
        RuleSet rules = FatcaRules("""
            { "id": "T-ALWAYS", "context": "/*", "assert": "false()", "message": "fires everywhere" }
            """);

        ValidationResult result = Run(rules, TestFiles.Crs(TestFiles.CrsSample));

        Assert.True(result.IsValid, Describe(result));
        Assert.Empty(result.Issues);
    }

    [Theory]
    [MemberData(nameof(InvalidRuleFiles))]
    public void InvalidRuleFiles_AreRefusedWithAClearMessage(string json, string expected)
    {
        RuleSetException ex = Assert.Throws<RuleSetException>(() => RuleSet.Parse(json, "bad.rules.json"));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExampleRuleFile_RunsOnTheIrsSampleReports()
    {
        RuleSet rules = RuleSet.Load(TestFiles.ExampleRules("fatca-example.rules.json"));

        Assert.Equal(new[] { "EX-US-TIN-FORMAT", "EX-BALANCE-NOT-NEGATIVE", "EX-ACCOUNT-ONCE", "EX-USD-ONLY" }, rules.RuleIds);
        foreach (string sample in new[] { "newnilfireport.xml", "2016_2IW3WK.00000.LE.152.xml" })
        {
            ValidationResult result = Run(rules, TestFiles.Irs(sample));
            Assert.NotEqual(ValidationStatus.NotValidated, result.Status);
            Assert.DoesNotContain(result.Issues, issue => issue.Message.StartsWith("The rule could not be evaluated", StringComparison.Ordinal));
        }
    }

    private static RuleSet FatcaRules(string rules) => RuleSet.Parse($$"""
        {
          "name": "test rules",
          "appliesTo": "urn:oecd:ties:fatca:v2",
          "namespaces": { "ftc": "urn:oecd:ties:fatca:v2", "sfa": "urn:oecd:ties:stffatcatypes:v2" },
          "rules": [ {{rules}} ]
        }
        """, "test.rules.json");

    private static ValidationResult Run(RuleSet rules, string path) => new ValidatorEngine { RuleSets = [rules] }.Validate(path);

    private static string Describe(ValidationResult result) =>
        $"{result.Status}: {string.Join(Environment.NewLine, result.Issues)}";
}
