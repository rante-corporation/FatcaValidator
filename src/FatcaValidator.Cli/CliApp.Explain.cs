using System.CommandLine;
using System.Xml;
using FatcaValidator.Cli.Reports;
using FatcaValidator.Core;

namespace FatcaValidator.Cli;

/// <summary>A report given with --report, to point each record error at its line.</summary>
internal sealed class ExplainReport(string path, ReportSummary summary)
{
    private Dictionary<string, ReportRecord>? _records;

    public string Path { get; } = path;

    public ReportSummary Summary { get; } = summary;

    /// <summary>The first record with this DocRefId, or <see langword="null"/>.</summary>
    public ReportRecord? Find(string docRefId)
    {
        if (_records is null)
        {
            _records = new Dictionary<string, ReportRecord>(StringComparer.Ordinal);
            foreach (ReportRecord record in Summary.Records)
            {
                _records.TryAdd(record.DocRefId, record);
            }
        }

        return _records.GetValueOrDefault(docRefId);
    }
}

/// <summary>One notification or status message, explained; or why it could not be.</summary>
internal sealed record ExplainedFile(
    string Path,
    FeedbackDocument? Feedback,
    ValidationResult? Validation,
    string? Problem,
    ExplainReport? Report,
    string? ReportNote,
    HistoryMessage? HistoryMessage,
    string? HistoryNote)
{
    public static ExplainedFile Failed(string path, string problem) => new(path, null, null, problem, null, null, null, null);

    /// <summary>0 when the file was accepted with no errors, 1 when there is something to do, 2 when it could not be explained.</summary>
    public int ExitCode =>
        Feedback is null ? ExitCodes.NotValidated
        : Feedback.Accepted && Feedback.FileErrors.Count == 0 && Feedback.RecordErrors.Count == 0 ? ExitCodes.Valid
        : ExitCodes.Invalid;
}

/// <summary>The explain command: IRS notifications and OECD status messages as a to-do list.</summary>
internal sealed partial class CliApp
{
    private Command ExplainCommand()
    {
        var fileOption = new Option<string[]>("--file", "-f")
        {
            Description = "IRS FATCA notifications or OECD status messages (CRS, CbC, ETR or generic): file paths, folders or wildcards.",
            HelpName = "path",
            Required = true,
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
        };

        Option<FileInfo[]> reportOption = new Option<FileInfo[]>("--report")
        {
            Description = "The reports you sent: each record error is shown with its line in the report that has the same MessageRefId.",
            HelpName = "path",
            AllowMultipleArgumentsPerToken = true,
        }.AcceptExistingOnly();

        Option<FileInfo> historyOption = new Option<FileInfo>("--history")
        {
            Description = "Your submission history file: shows when the message was recorded as sent, and from which file.",
            HelpName = "path",
        }.AcceptExistingOnly();

        var formatOption = new Option<string>("--format")
        {
            Description = "The report format: text or json.",
            HelpName = "format",
            DefaultValueFactory = _ => "text",
        };
        formatOption.AcceptOnlyFromAmong("text", "json");

        Option<FileInfo> outputOption = OutputOption();
        var command = new Command("explain", "Explains IRS FATCA notifications and OECD status messages: whether your file was accepted, "
            + "and a to-do list of the file and record errors with the authority's own instructions.")
        {
            fileOption,
            reportOption,
            historyOption,
            formatOption,
            outputOption,
        };
        command.SetAction(parseResult => Explain(
            parseResult.GetRequiredValue(fileOption),
            parseResult.GetValue(reportOption) ?? [],
            parseResult.GetValue(historyOption),
            parseResult.GetValue(formatOption) ?? "text",
            parseResult.GetValue(outputOption)));
        return command;
    }

    private int Explain(string[] inputs, FileInfo[] reportFiles, FileInfo? historyFile, string format, FileInfo? outputFile)
    {
        var inputErrors = new List<string>();
        List<string> files = InputFiles.Expand(inputs, recursive: false, inputErrors);
        if (inputErrors.Count > 0)
        {
            foreach (string message in inputErrors)
            {
                _error.WriteLine($"fatca-val: {message}");
            }

            return ExitCodes.NotValidated;
        }

        var reports = new Dictionary<string, ExplainReport>(StringComparer.Ordinal);
        foreach (FileInfo reportFile in reportFiles)
        {
            try
            {
                ReportSummary summary = ReportSummary.Read(reportFile.FullName);
                if (summary.MessageRefId is null)
                {
                    _error.WriteLine($"fatca-val: --report '{reportFile.FullName}' has no MessageSpec/MessageRefId.");
                    return ExitCodes.NotValidated;
                }

                reports.TryAdd(summary.MessageRefId, new ExplainReport(reportFile.FullName, summary));
            }
            catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
            {
                _error.WriteLine($"fatca-val: --report '{reportFile.FullName}' could not be read: {ex.Message}");
                return ExitCodes.NotValidated;
            }
        }

        SubmissionHistory? history = null;
        if (historyFile is not null)
        {
            try
            {
                history = SubmissionHistory.Load(historyFile.FullName);
            }
            catch (HistoryException ex)
            {
                _error.WriteLine($"fatca-val: {ex.Message}");
                return ExitCodes.NotValidated;
            }
        }

        var engine = new ValidatorEngine();
        var explained = files.Select(file => ExplainFile(file, engine, reports, history)).ToList();
        if (!WriteReport(outputFile, output =>
            {
                if (format == "json")
                {
                    ExplainWriter.WriteJson(explained, output);
                }
                else
                {
                    ExplainWriter.WriteText(explained, output);
                }
            }))
        {
            return ExitCodes.NotValidated;
        }

        if (outputFile is not null)
        {
            _output.WriteLine($"Explained {Summary.Count(explained.Count, "file")}. Report: {outputFile.FullName}");
        }

        return explained.Max(file => file.ExitCode);
    }

    private static ExplainedFile ExplainFile(string path, ValidatorEngine engine, Dictionary<string, ExplainReport> reports, SubmissionHistory? history)
    {
        ValidationResult validation = engine.Validate(path);
        if (validation.Status == ValidationStatus.NotValidated)
        {
            return ExplainedFile.Failed(path, validation.Issues.FirstOrDefault()?.Message ?? "the file could not be validated");
        }

        FeedbackDocument? feedback;
        try
        {
            feedback = FeedbackDocument.Read(path);
        }
        catch (XmlException ex)
        {
            return ExplainedFile.Failed(path, $"the file is not well-formed XML: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ExplainedFile.Failed(path, $"the file could not be read: {ex.Message}");
        }

        if (feedback is null)
        {
            return ExplainedFile.Failed(path, $"it is not an IRS FATCA notification or an OECD status message (it is a {validation.Schema} document)");
        }

        string? messageRefId = feedback.OriginalMessageRefId;
        ExplainReport? report = null;
        string? reportNote = null;
        if (messageRefId is not null)
        {
            if (!reports.TryGetValue(messageRefId, out report) && reports.Count > 0)
            {
                reportNote = $"none of the --report files is message {messageRefId}";
            }
        }
        else if (reports.Count == 1)
        {
            report = reports.Values.First();
            reportNote = "the notification does not name its message, so this is assumed to be the report";
        }

        HistoryMessage? sent = messageRefId is null ? null : history?.FindMessage(messageRefId);
        string? historyNote = history is null || sent is not null ? null
            : messageRefId is null ? "the notification does not name its message"
            : $"message {messageRefId} is not in {history.Describe()}";
        return new ExplainedFile(path, feedback, validation, null, report, reportNote, sent, historyNote);
    }
}
