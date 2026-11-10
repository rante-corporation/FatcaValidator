namespace FatcaValidator.Core;

/// <summary>How serious a finding is.</summary>
public enum ValidationSeverity
{
    /// <summary>The document still passes, for example when an element has no schema declaration in a lax section.</summary>
    Warning,

    /// <summary>The document fails validation.</summary>
    Error,
}

/// <summary>The outcome of validating one document.</summary>
public enum ValidationStatus
{
    /// <summary>The document was checked against its schema and has no errors.</summary>
    Valid,

    /// <summary>The document is not well-formed XML, or it breaks its schema.</summary>
    Invalid,

    /// <summary>
    /// The document could not be checked: the file could not be read, no bundled schema matches its
    /// root element, or the schema could not be loaded. The reason is the single issue in the result.
    /// </summary>
    NotValidated,
}

/// <summary>Which check produced a finding.</summary>
public enum IssueSource
{
    /// <summary>The XML parser: the file is not well-formed, or it contains a DTD.</summary>
    Xml,

    /// <summary>The XML schema.</summary>
    Schema,

    /// <summary>A business rule, built in or from a rule file.</summary>
    Rule,

    /// <summary>The FATCA upload pre-flight (IDES checks).</summary>
    Preflight,

    /// <summary>The validator itself: the file could not be checked, or a step was skipped.</summary>
    Validator,
}

/// <summary>One error or warning, with its position in the XML file when that is known.</summary>
/// <param name="Severity">Whether the finding fails the document.</param>
/// <param name="Message">The message from the parser, the schema validator, a rule or the pre-flight.</param>
/// <param name="LineNumber">The 1-based line in the XML file, or 0 when it is not known.</param>
/// <param name="LinePosition">The 1-based position in that line, or 0 when it is not known.</param>
/// <param name="Code">A stable identifier: an <see cref="IssueCodes"/> value, or the id of the rule that fired.</param>
/// <param name="Source">Which check produced the finding.</param>
public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Message,
    int LineNumber = 0,
    int LinePosition = 0,
    string? Code = null,
    IssueSource Source = IssueSource.Schema)
{
    /// <summary>
    /// Formats the issue as "Line 12, Position 6: message", or just the message when the position is not known. Rule and
    /// pre-flight findings start with their code in brackets.
    /// </summary>
    public override string ToString()
    {
        string where = LineNumber > 0 ? $"Line {LineNumber}, Position {LinePosition}: " : "";
        string code = Code is not null && Source is IssueSource.Rule or IssueSource.Preflight ? $"[{Code}] " : "";
        return where + code + Message;
    }
}

/// <summary>The codes of the findings the validator itself produces. Rules use their own ids.</summary>
public static class IssueCodes
{
    /// <summary>The file is not well-formed XML.</summary>
    public const string NotWellFormed = "XML-NOT-WELL-FORMED";

    /// <summary>The file contains a DTD, which is refused.</summary>
    public const string DtdRefused = "XML-DTD-REFUSED";

    /// <summary>The file breaks its XML schema.</summary>
    public const string SchemaError = "XSD-ERROR";

    /// <summary>A warning from the schema validator.</summary>
    public const string SchemaWarning = "XSD-WARNING";

    /// <summary>The schema does not declare the file's root element.</summary>
    public const string RootNotDeclared = "XSD-ROOT-NOT-DECLARED";

    /// <summary>The file could not be read.</summary>
    public const string FileUnreadable = "FILE-UNREADABLE";

    /// <summary>No bundled schema matches the file's root element.</summary>
    public const string NoBundledSchema = "NO-BUNDLED-SCHEMA";

    /// <summary>The schema file could not be loaded or compiled.</summary>
    public const string SchemaNotLoaded = "SCHEMA-NOT-LOADED";

    /// <summary>Business rules were not run because the file breaks its schema.</summary>
    public const string RulesSkipped = "RULES-SKIPPED";
}

/// <summary>The result of validating one document.</summary>
public sealed class ValidationResult
{
    internal ValidationResult(ValidationStatus status, string? schema, IReadOnlyList<ValidationIssue> issues, int errorCount, int warningCount)
    {
        Status = status;
        Schema = schema;
        Issues = issues;
        ErrorCount = errorCount;
        WarningCount = warningCount;
    }

    /// <summary>The outcome.</summary>
    public ValidationStatus Status { get; }

    /// <summary><see langword="true"/> when <see cref="Status"/> is <see cref="ValidationStatus.Valid"/>.</summary>
    public bool IsValid => Status == ValidationStatus.Valid;

    /// <summary>
    /// The schema the document was checked against: a bundled package's name or the full path of a
    /// schema file. <see langword="null"/> when no schema was chosen.
    /// </summary>
    public string? Schema { get; }

    /// <summary>
    /// The findings in document order, up to <see cref="ValidatorEngine.MaxIssuesPerSeverity"/> of each severity.
    /// </summary>
    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>The total number of errors, including any beyond the limit.</summary>
    public int ErrorCount { get; }

    /// <summary>The total number of warnings, including any beyond the limit.</summary>
    public int WarningCount { get; }

    /// <summary>The errors that were kept.</summary>
    public IEnumerable<ValidationIssue> Errors => Issues.Where(issue => issue.Severity == ValidationSeverity.Error);

    /// <summary>The warnings that were kept.</summary>
    public IEnumerable<ValidationIssue> Warnings => Issues.Where(issue => issue.Severity == ValidationSeverity.Warning);
}

/// <summary>Collects findings in document order, keeping at most a fixed number of each severity.</summary>
internal sealed class IssueCollector(int maxPerSeverity)
{
    private readonly List<ValidationIssue> _issues = [];
    private int _errorCount;
    private int _warningCount;

    public int ErrorCount => _errorCount;

    public void Add(ValidationIssue issue)
    {
        int seen = issue.Severity == ValidationSeverity.Error ? ++_errorCount : ++_warningCount;
        if (seen <= maxPerSeverity)
        {
            _issues.Add(issue);
        }
    }

    public ValidationResult ToResult(ValidationStatus status, string? schema) =>
        new(status, schema, _issues.ToArray(), _errorCount, _warningCount);
}
