using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml;
using FatcaValidator.Core;

namespace FatcaValidator.Cli.Reports;

/// <summary>Writes the results of a run in one format.</summary>
internal interface IReportWriter
{
    void Write(IReadOnlyList<FileReport> reports, RunInfo run, TextWriter output);
}

/// <summary>What a run used besides the files: the FFI list for the GIIN checks and the submission history, if any.</summary>
internal sealed record RunInfo(FfiList? FfiList, SubmissionHistory? History = null);

/// <summary>The tool's name, version and home page, as reports show them.</summary>
internal static class ToolInfo
{
    public const string Name = "fatca-val";
    public const string InformationUri = "https://github.com/rante-corporation/FatcaValidator";

    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        string? version = typeof(ToolInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(version))
        {
            return "0.0.0";
        }

        int plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? version[..plus] : version;
    }
}

/// <summary>Counts over all files of a run.</summary>
internal static class Summary
{
    public static string Describe(IReadOnlyList<FileReport> reports)
    {
        int valid = reports.Count(r => r.Result.Status == ValidationStatus.Valid);
        int invalid = reports.Count(r => r.Result.Status == ValidationStatus.Invalid);
        int notValidated = reports.Count(r => r.Result.Status == ValidationStatus.NotValidated);
        int errors = reports.Sum(r => r.Result.ErrorCount);
        int warnings = reports.Sum(r => r.Result.WarningCount);
        return $"Checked {Count(reports.Count, "file")}: {valid} valid, {invalid} invalid, {notValidated} not validated; {Count(errors, "error")}, {Count(warnings, "warning")}.";
    }

    public static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    public static string StatusName(ValidationStatus status) => status switch
    {
        ValidationStatus.Valid => "valid",
        ValidationStatus.Invalid => "invalid",
        _ => "notValidated",
    };

    public static string SourceName(IssueSource source) => source switch
    {
        IssueSource.Xml => "xml",
        IssueSource.Schema => "schema",
        IssueSource.Rule => "rule",
        IssueSource.Preflight => "preflight",
        IssueSource.FfiList => "ffi-list",
        IssueSource.History => "history",
        _ => "validator",
    };
}

/// <summary>The human-readable report: one block per file, then a summary line when there are several.</summary>
internal sealed class TextReportWriter(bool useColor) : IReportWriter
{
    public void Write(IReadOnlyList<FileReport> reports, RunInfo run, TextWriter output)
    {
        if (run.FfiList is not null)
        {
            output.WriteLine($"FFI list: {run.FfiList.Describe()}");
        }

        if (run.History is not null)
        {
            output.WriteLine($"History:  {run.History.Describe()}");
        }

        if (run.FfiList is not null || run.History is not null)
        {
            output.WriteLine();
        }

        for (int i = 0; i < reports.Count; i++)
        {
            if (i > 0)
            {
                output.WriteLine();
            }

            WriteFile(reports[i], output);
        }

        if (reports.Count > 1)
        {
            output.WriteLine();
            output.WriteLine(Summary.Describe(reports));
        }
    }

    private void WriteFile(FileReport report, TextWriter output)
    {
        ValidationResult result = report.Result;
        output.WriteLine($"File:   {report.Path}");
        output.WriteLine($"Schema: {result.Schema ?? "(none)"}");
        output.Write("Result: ");
        switch (result.Status)
        {
            case ValidationStatus.Valid:
                WriteColored(output, "VALID", ConsoleColor.Green);
                output.WriteLine(result.WarningCount == 0 ? " - no errors found." : $" - no errors, {Summary.Count(result.WarningCount, "warning")}.");
                break;

            case ValidationStatus.Invalid:
                WriteColored(output, "INVALID", ConsoleColor.Red);
                output.WriteLine($" - {Summary.Count(result.ErrorCount, "error")}, {Summary.Count(result.WarningCount, "warning")}.");
                break;

            default:
                WriteColored(output, "NOT VALIDATED", ConsoleColor.Yellow);
                output.WriteLine();
                break;
        }

        foreach (ValidationIssue issue in result.Issues)
        {
            string label = result.Status == ValidationStatus.NotValidated ? ""
                : issue.Severity == ValidationSeverity.Error ? "error    "
                : "warning  ";
            output.WriteLine($"  {label}{issue}");
        }

        WriteOmitted(output, result.ErrorCount - result.Errors.Count(), "error");
        WriteOmitted(output, result.WarningCount - result.Warnings.Count(), "warning");
    }

    private static void WriteOmitted(TextWriter output, int omitted, string noun)
    {
        if (omitted > 0)
        {
            output.WriteLine($"  ... and {Summary.Count(omitted, noun)} more (raise --max-errors to list them).");
        }
    }

    private void WriteColored(TextWriter output, string text, ConsoleColor color)
    {
        if (!useColor)
        {
            output.Write(text);
            return;
        }

        Console.ForegroundColor = color;
        output.Write(text);
        Console.ResetColor();
    }
}

/// <summary>The JSON report: a summary and, per file, its status, counts and issues.</summary>
internal sealed class JsonReportWriter : IReportWriter
{
    public void Write(IReadOnlyList<FileReport> reports, RunInfo run, TextWriter output)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteStartObject("tool");
            json.WriteString("name", ToolInfo.Name);
            json.WriteString("version", ToolInfo.Version);
            json.WriteEndObject();

            if (run.FfiList is null)
            {
                json.WriteNull("ffiList");
            }
            else
            {
                json.WriteStartObject("ffiList");
                json.WriteString("file", run.FfiList.Source);
                json.WriteNumber("giins", run.FfiList.Count);
                if (run.FfiList.FileTimestamp is { } dated)
                {
                    json.WriteString("fileDated", dated.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    json.WriteNull("fileDated");
                }

                json.WriteEndObject();
            }

            if (run.History is null)
            {
                json.WriteNull("history");
            }
            else
            {
                json.WriteStartObject("history");
                json.WriteString("file", run.History.Path);
                json.WriteNumber("messages", run.History.Messages.Count);
                json.WriteNumber("records", run.History.RecordCount);
                json.WriteEndObject();
            }

            json.WriteStartObject("summary");
            json.WriteNumber("files", reports.Count);
            json.WriteNumber("valid", reports.Count(r => r.Result.Status == ValidationStatus.Valid));
            json.WriteNumber("invalid", reports.Count(r => r.Result.Status == ValidationStatus.Invalid));
            json.WriteNumber("notValidated", reports.Count(r => r.Result.Status == ValidationStatus.NotValidated));
            json.WriteNumber("errors", reports.Sum(r => r.Result.ErrorCount));
            json.WriteNumber("warnings", reports.Sum(r => r.Result.WarningCount));
            json.WriteEndObject();

            json.WriteStartArray("files");
            foreach (FileReport report in reports)
            {
                ValidationResult result = report.Result;
                json.WriteStartObject();
                json.WriteString("path", report.Path);
                json.WriteString("status", Summary.StatusName(result.Status));
                if (result.Schema is null) { json.WriteNull("schema"); } else { json.WriteString("schema", result.Schema); }
                json.WriteNumber("errorCount", result.ErrorCount);
                json.WriteNumber("warningCount", result.WarningCount);
                json.WriteBoolean("issuesTruncated", result.ErrorCount + result.WarningCount > result.Issues.Count);
                json.WriteStartArray("issues");
                foreach (ValidationIssue issue in result.Issues)
                {
                    json.WriteStartObject();
                    json.WriteString("severity", issue.Severity == ValidationSeverity.Error ? "error" : "warning");
                    if (issue.Code is null) { json.WriteNull("code"); } else { json.WriteString("code", issue.Code); }
                    json.WriteString("source", Summary.SourceName(issue.Source));
                    if (issue.LineNumber > 0)
                    {
                        json.WriteNumber("line", issue.LineNumber);
                        json.WriteNumber("position", issue.LinePosition);
                    }
                    else
                    {
                        json.WriteNull("line");
                        json.WriteNull("position");
                    }

                    json.WriteString("message", issue.Message);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        output.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }
}

/// <summary>SARIF 2.1.0, for code-scanning tools: one result per issue, with the file region when it is known.</summary>
internal sealed class SarifReportWriter : IReportWriter
{
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        [IssueCodes.NotWellFormed] = "The file is not well-formed XML.",
        [IssueCodes.DtdRefused] = "The file contains a DTD, which is refused for security reasons.",
        [IssueCodes.SchemaError] = "The file breaks its XML schema.",
        [IssueCodes.SchemaWarning] = "The XML schema validator reported a warning.",
        [IssueCodes.RootNotDeclared] = "The schema does not declare the file's root element.",
        [IssueCodes.FileUnreadable] = "The file could not be read.",
        [IssueCodes.NoBundledSchema] = "No bundled schema matches the file's root element.",
        [IssueCodes.SchemaNotLoaded] = "The schema file could not be loaded.",
        [IssueCodes.RulesSkipped] = "Business rules were not run because the file breaks its schema.",
        [PreflightCodes.ProhibitedCharacters] = "The text contains a character pattern IDES rejects (--, /* or &#).",
        [PreflightCodes.Encoding] = "The file is not UTF-8.",
        [PreflightCodes.Size] = "The file is larger than the IRS's recommended maximum compressed size.",
        [PreflightCodes.MetadataUnreadable] = "The IDES sender metadata file could not be checked.",
        [PreflightCodes.MetadataSchema] = "The IDES sender metadata file breaks its schema.",
        [PreflightCodes.MetadataSender] = "The metadata sender differs from MessageSpec/SendingCompanyIN.",
        [PreflightCodes.MetadataReceiver] = "The metadata receiver is not the IRS.",
        [PreflightCodes.MetadataCommunicationType] = "The metadata communication type is not RPT.",
        [PreflightCodes.MetadataFileFormat] = "The metadata file format is not XML.",
        [PreflightCodes.MetadataBinaryEncoding] = "The metadata binary encoding is not NONE.",
        [PreflightCodes.MetadataTaxYear] = "The metadata tax year differs from the reporting period.",
        [GiinCheckCodes.Format] = "A field that should hold a GIIN does not hold a well-formed one.",
        [GiinCheckCodes.NotListed] = "The GIIN is not on the IRS FFI list.",
        [GiinCheckCodes.NameDiffers] = "The name in the report differs from the name on the IRS FFI list.",
        [HistoryCheckCodes.MessageRefIdReused] = "The MessageRefId was used by a message already sent.",
        [HistoryCheckCodes.DocRefIdReused] = "The DocRefId was used by a record already sent.",
        [HistoryCheckCodes.CorrectionTargetUnknown] = "The corrected record is not in the submission history.",
        [HistoryCheckCodes.CorrMessageRefIdMismatch] = "CorrMessageRefId is not the message that holds the corrected record.",
        [HistoryCheckCodes.CorrectionTargetReplaced] = "The corrected record was already corrected, amended or voided.",
        [HistoryCheckCodes.CorrectionTargetRejected] = "The corrected record was in a message that was rejected as a whole.",
    };

    public void Write(IReadOnlyList<FileReport> reports, RunInfo run, TextWriter output)
    {
        var issues = reports.SelectMany(report => report.Result.Issues.Select(issue => (report.Path, Issue: issue))).ToList();
        var ruleIds = issues.Select(item => item.Issue.Code ?? "FATCA-VAL").Distinct(StringComparer.Ordinal).ToList();
        var ruleIndex = ruleIds.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("$schema", "https://json.schemastore.org/sarif-2.1.0.json");
            json.WriteString("version", "2.1.0");
            json.WriteStartArray("runs");
            json.WriteStartObject();

            json.WriteStartObject("tool");
            json.WriteStartObject("driver");
            json.WriteString("name", ToolInfo.Name);
            json.WriteString("version", ToolInfo.Version);
            json.WriteString("informationUri", ToolInfo.InformationUri);
            json.WriteStartArray("rules");
            foreach (string id in ruleIds)
            {
                json.WriteStartObject();
                json.WriteString("id", id);
                json.WriteStartObject("shortDescription");
                json.WriteString("text", Descriptions.GetValueOrDefault(id, $"Business rule {id}."));
                json.WriteEndObject();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndObject();

            json.WriteStartArray("results");
            foreach ((string path, ValidationIssue issue) in issues)
            {
                string id = issue.Code ?? "FATCA-VAL";
                json.WriteStartObject();
                json.WriteString("ruleId", id);
                json.WriteNumber("ruleIndex", ruleIndex[id]);
                json.WriteString("level", issue.Severity == ValidationSeverity.Error ? "error" : "warning");
                json.WriteStartObject("message");
                json.WriteString("text", issue.Message);
                json.WriteEndObject();
                json.WriteStartArray("locations");
                json.WriteStartObject();
                json.WriteStartObject("physicalLocation");
                json.WriteStartObject("artifactLocation");
                json.WriteString("uri", ArtifactUri(path));
                json.WriteEndObject();
                if (issue.LineNumber > 0)
                {
                    json.WriteStartObject("region");
                    json.WriteNumber("startLine", issue.LineNumber);
                    json.WriteNumber("startColumn", Math.Max(1, issue.LinePosition));
                    json.WriteEndObject();
                }

                json.WriteEndObject();
                json.WriteEndObject();
                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
        }

        output.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    /// <summary>A path relative to the current folder (as code-scanning tools expect), or an absolute file URI.</summary>
    private static string ArtifactUri(string path)
    {
        string relative = Path.GetRelativePath(Environment.CurrentDirectory, path);
        bool outside = Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        return outside ? new Uri(path).AbsoluteUri : relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}

/// <summary>JUnit XML, for CI test reports: one test case per file; invalid files fail, unvalidated files are errors.</summary>
internal sealed class JUnitReportWriter : IReportWriter
{
    public void Write(IReadOnlyList<FileReport> reports, RunInfo run, TextWriter output)
    {
        int failures = reports.Count(r => r.Result.Status == ValidationStatus.Invalid);
        int errors = reports.Count(r => r.Result.Status == ValidationStatus.NotValidated);

        using var buffer = new MemoryStream();
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) };
        using (XmlWriter xml = XmlWriter.Create(buffer, settings))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("testsuites");
            xml.WriteAttributeString("name", ToolInfo.Name);
            xml.WriteAttributeString("tests", Count(reports.Count));
            xml.WriteAttributeString("failures", Count(failures));
            xml.WriteAttributeString("errors", Count(errors));

            xml.WriteStartElement("testsuite");
            xml.WriteAttributeString("name", ToolInfo.Name);
            xml.WriteAttributeString("tests", Count(reports.Count));
            xml.WriteAttributeString("failures", Count(failures));
            xml.WriteAttributeString("errors", Count(errors));
            xml.WriteAttributeString("skipped", "0");

            foreach (FileReport report in reports)
            {
                ValidationResult result = report.Result;
                xml.WriteStartElement("testcase");
                xml.WriteAttributeString("classname", ToolInfo.Name);
                xml.WriteAttributeString("name", report.Path);
                string details = string.Join(Environment.NewLine, result.Issues.Select(issue =>
                    $"{(issue.Severity == ValidationSeverity.Error ? "error" : "warning")}: {issue}"));

                switch (result.Status)
                {
                    case ValidationStatus.Invalid:
                        xml.WriteStartElement("failure");
                        xml.WriteAttributeString("type", "INVALID");
                        xml.WriteAttributeString("message", $"{Summary.Count(result.ErrorCount, "error")}, {Summary.Count(result.WarningCount, "warning")}");
                        xml.WriteString(details);
                        xml.WriteEndElement();
                        break;

                    case ValidationStatus.NotValidated:
                        xml.WriteStartElement("error");
                        xml.WriteAttributeString("type", "NOT_VALIDATED");
                        xml.WriteAttributeString("message", result.Issues.Count > 0 ? result.Issues[0].Message : "not validated");
                        xml.WriteString(details);
                        xml.WriteEndElement();
                        break;

                    default:
                        if (result.WarningCount > 0)
                        {
                            xml.WriteElementString("system-out", details);
                        }

                        break;
                }

                xml.WriteEndElement();
            }

            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        output.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private static string Count(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
