using System.CommandLine;
using System.Xml;
using FatcaValidator.Cli.Reports;
using FatcaValidator.Core;

namespace FatcaValidator.Cli;

/// <summary>The history commands: add (record sent reports and the feedback received) and list.</summary>
internal sealed partial class CliApp
{
    private Command HistoryCommand()
    {
        var addHistoryOption = new Option<FileInfo>("--history")
        {
            Description = "The history file. It is created when it does not exist yet.",
            HelpName = "path",
            Required = true,
        };

        var addFileOption = new Option<string[]>("--file", "-f")
        {
            Description = "Reports you sent, and the IRS notifications and OECD status messages you received for them: file paths, folders "
                + "or wildcards. Reports are recorded first, so a report and its notification can be added together.",
            HelpName = "path",
            Required = true,
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
        };

        var messageOption = new Option<string>("--message")
        {
            Description = "The MessageRefId that a notification is about, for notifications that do not name it (IRS file error notifications often cannot).",
            HelpName = "MessageRefId",
        };

        var add = new Command("add", "Records reports you sent, and the notifications and status messages you received for them. "
            + "A report is recorded only when it passes its schema and the history checks; recording the same file again changes nothing.")
        {
            addHistoryOption,
            addFileOption,
            messageOption,
        };
        add.SetAction(parseResult => AddToHistory(
            parseResult.GetRequiredValue(addHistoryOption),
            parseResult.GetRequiredValue(addFileOption),
            parseResult.GetValue(messageOption)));

        Option<FileInfo> listHistoryOption = new Option<FileInfo>("--history")
        {
            Description = "The history file.",
            HelpName = "path",
            Required = true,
        }.AcceptExistingOnly();

        var listFormatOption = new Option<string>("--format")
        {
            Description = "The report format: text or json.",
            HelpName = "format",
            DefaultValueFactory = _ => "text",
        };
        listFormatOption.AcceptOnlyFromAmong("text", "json");

        Option<FileInfo> listOutputOption = OutputOption();
        var list = new Command("list", "Lists the messages in a history file, with the feedback recorded for each.")
        {
            listHistoryOption,
            listFormatOption,
            listOutputOption,
        };
        list.SetAction(parseResult => ListHistory(
            parseResult.GetRequiredValue(listHistoryOption),
            parseResult.GetValue(listFormatOption) ?? "text",
            parseResult.GetValue(listOutputOption)));

        return new Command("history", "Keeps a record of the reports you sent and the feedback you received, so later reports can be checked against them (--history).")
        {
            add,
            list,
        };
    }

    private int AddToHistory(FileInfo historyFile, string[] inputs, string? messageRefId)
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

        SubmissionHistory history;
        bool created;
        try
        {
            history = SubmissionHistory.LoadOrCreate(historyFile.FullName, out created);
        }
        catch (HistoryException ex)
        {
            _error.WriteLine($"fatca-val: {ex.Message}");
            return ExitCodes.NotValidated;
        }

        _output.WriteLine($"History: {history.Path}{(created ? " (new file)" : "")}");

        // Reports first (OrderBy is stable), so a notification finds the report recorded in the same run.
        var items = files.Select(file => (Path: file, Root: TryReadRoot(file, out string? problem), Problem: problem))
            .OrderBy(item => item.Root is not null && FeedbackDocument.IsFeedback(item.Root))
            .ToList();

        var engine = new ValidatorEngine { History = history };
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int exitCode = ExitCodes.Valid;
        bool changed = false;
        foreach ((string path, XmlQualifiedName? root, string? problem) in items)
        {
            (int ExitCode, bool Changed) outcome;
            if (root is null)
            {
                _output.WriteLine($"Not recorded: {Path.GetFileName(path)}: {problem}");
                outcome = (ExitCodes.NotValidated, false);
            }
            else if (FeedbackDocument.IsFeedback(root))
            {
                outcome = AddFeedback(history, engine, path, messageRefId, now);
            }
            else
            {
                outcome = AddReport(history, engine, path, now);
            }

            exitCode = Math.Max(exitCode, outcome.ExitCode);
            changed |= outcome.Changed;
        }

        if (!changed)
        {
            _output.WriteLine("Nothing new was recorded; the history file is unchanged.");
            return exitCode;
        }

        try
        {
            history.Save();
        }
        catch (HistoryException ex)
        {
            _error.WriteLine($"fatca-val: {ex.Message}");
            return ExitCodes.NotValidated;
        }

        _output.WriteLine($"Saved: {history.Describe()}.");
        return exitCode;
    }

    private (int ExitCode, bool Changed) AddReport(SubmissionHistory history, ValidatorEngine engine, string path, DateTimeOffset now)
    {
        string name = Path.GetFileName(path);
        if (!TryHash(path, out string? sha256))
        {
            return (ExitCodes.NotValidated, false);
        }

        if (history.FindMessageByFile(sha256) is { } recorded)
        {
            _output.WriteLine($"Already recorded: {name} as message {recorded.MessageRefId} on {recorded.RecordedAt:yyyy-MM-dd}.");
            return (ExitCodes.Valid, false);
        }

        ValidationResult result = engine.Validate(path);
        if (result.Status != ValidationStatus.Valid)
        {
            _output.WriteLine(result.Status == ValidationStatus.Invalid
                ? $"Not recorded: {name} has {Summary.Count(result.ErrorCount, "error")}; only files that pass every check are recorded."
                : $"Not recorded: {name} could not be validated.");
            WriteIssues(result);
            return (result.Status == ValidationStatus.Invalid ? ExitCodes.Invalid : ExitCodes.NotValidated, false);
        }

        ReportSummary report;
        try
        {
            report = ReportSummary.Read(path);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            _output.WriteLine($"Not recorded: {name} could not be read: {ex.Message}");
            return (ExitCodes.NotValidated, false);
        }

        if (report.MessageRefId is null || report.Records.Count == 0)
        {
            _output.WriteLine($"Not recorded: {name} is not a report with a MessageSpec and DocSpecs ({result.Schema}).");
            return (ExitCodes.NotValidated, false);
        }

        HistoryMessage message = history.AddMessage(report, path, sha256, result.Schema, now);
        _output.WriteLine($"Recorded: {name} as message {message.MessageRefId} ({message.MessageType ?? "unknown type"}"
            + $"{(message.Test ? ", test data" : "")}, {Summary.Count(message.Records.Count, "record")}).");
        WriteIssues(result);
        return (ExitCodes.Valid, true);
    }

    private (int ExitCode, bool Changed) AddFeedback(SubmissionHistory history, ValidatorEngine engine, string path, string? messageRefId, DateTimeOffset now)
    {
        string name = Path.GetFileName(path);
        if (!TryHash(path, out string? sha256))
        {
            return (ExitCodes.NotValidated, false);
        }

        if (history.FindFeedbackFile(sha256) is { } recordedFor)
        {
            _output.WriteLine($"Already recorded: {name} for message {recordedFor.MessageRefId}.");
            return (ExitCodes.Valid, false);
        }

        ValidationResult result = engine.Validate(path);
        if (result.Status != ValidationStatus.Valid)
        {
            _output.WriteLine($"Not recorded: {name} breaks its schema or could not be validated.");
            WriteIssues(result);
            return (result.Status == ValidationStatus.Invalid ? ExitCodes.Invalid : ExitCodes.NotValidated, false);
        }

        FeedbackDocument? feedback;
        try
        {
            feedback = FeedbackDocument.Read(path);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            _output.WriteLine($"Not recorded: {name} could not be read: {ex.Message}");
            return (ExitCodes.NotValidated, false);
        }

        if (feedback is null)
        {
            _output.WriteLine($"Not recorded: {name} is not an IRS notification or an OECD status message.");
            return (ExitCodes.NotValidated, false);
        }

        string? about = feedback.OriginalMessageRefId;
        if (about is not null && messageRefId is not null && about != messageRefId)
        {
            _output.WriteLine($"Not recorded: {name} is about message {about}, not {messageRefId} (--message).");
            return (ExitCodes.NotValidated, false);
        }

        about ??= messageRefId;
        if (about is null)
        {
            _output.WriteLine($"Not recorded: {name} does not name the message it is about; give its MessageRefId with --message.");
            return (ExitCodes.NotValidated, false);
        }

        if (history.FindMessage(about) is not { } message)
        {
            _output.WriteLine($"Not recorded: {name} is about message {about}, which is not in the history; record that report first.");
            return (ExitCodes.NotValidated, false);
        }

        history.AddFeedback(message, feedback, path, sha256, now);
        _output.WriteLine($"Recorded: {name} for message {about}: {ExplainWriter.Verdict(feedback)}.");
        return (ExitCodes.Valid, true);
    }

    private int ListHistory(FileInfo historyFile, string format, FileInfo? outputFile)
    {
        SubmissionHistory history;
        try
        {
            history = SubmissionHistory.Load(historyFile.FullName);
        }
        catch (HistoryException ex)
        {
            _error.WriteLine($"fatca-val: {ex.Message}");
            return ExitCodes.NotValidated;
        }

        bool written = WriteReport(outputFile, output =>
        {
            if (format == "json")
            {
                HistoryListWriter.WriteJson(history, output);
            }
            else
            {
                HistoryListWriter.WriteText(history, output);
            }
        });
        return written ? ExitCodes.Valid : ExitCodes.NotValidated;
    }

    private void WriteIssues(ValidationResult result)
    {
        foreach (ValidationIssue issue in result.Issues)
        {
            string label = result.Status == ValidationStatus.NotValidated ? "" : issue.Severity == ValidationSeverity.Error ? "error    " : "warning  ";
            _output.WriteLine($"  {label}{issue}");
        }
    }

    private bool TryHash(string path, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? sha256)
    {
        try
        {
            sha256 = SubmissionHistory.ComputeSha256(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _output.WriteLine($"Not recorded: {Path.GetFileName(path)} could not be read: {ex.Message}");
            sha256 = null;
            return false;
        }
    }

    /// <summary>The document's root element, without validating; null (with the reason) when the file cannot be read as XML.</summary>
    private static XmlQualifiedName? TryReadRoot(string path, out string? problem)
    {
        problem = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
            };
            using XmlReader reader = XmlReader.Create(stream, settings);
            reader.MoveToContent();
            return new XmlQualifiedName(reader.LocalName, reader.NamespaceURI);
        }
        catch (XmlException ex)
        {
            problem = $"it is not well-formed XML: {ex.Message}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"it could not be read: {ex.Message}";
        }

        return null;
    }
}
