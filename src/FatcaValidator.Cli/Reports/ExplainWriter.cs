using System.Text;
using System.Text.Json;
using FatcaValidator.Core;

namespace FatcaValidator.Cli.Reports;

/// <summary>Writes explained notifications and status messages as text (a to-do list) or JSON.</summary>
internal static class ExplainWriter
{
    /// <summary>One line on what a notification or status message decided, for example "NVF (Valid File Notification (N5)): accepted, 1 record error".</summary>
    public static string Verdict(FeedbackDocument feedback)
    {
        string code = feedback.Code is null ? "status" : feedback.CodeDescription is null ? feedback.Code : $"{feedback.Code} ({feedback.CodeDescription})";
        string verdict = feedback.Accepted ? "accepted" : "rejected";
        var errors = new List<string>();
        if (feedback.FileErrors.Count > 0 && feedback.Source == FeedbackSource.Oecd)
        {
            errors.Add(Summary.Count(feedback.FileErrors.Count, "file error"));
        }

        if (feedback.RecordErrors.Count > 0)
        {
            errors.Add(Summary.Count(feedback.RecordErrors.Count, "record error"));
        }

        return $"{code}: {verdict}{(errors.Count > 0 ? ", " + string.Join(", ", errors) : "")}";
    }

    public static void WriteText(IReadOnlyList<ExplainedFile> files, TextWriter output)
    {
        for (int i = 0; i < files.Count; i++)
        {
            if (i > 0)
            {
                output.WriteLine();
            }

            WriteFile(files[i], output);
        }
    }

    public static void WriteJson(IReadOnlyList<ExplainedFile> files, TextWriter output)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteStartObject("tool");
            json.WriteString("name", ToolInfo.Name);
            json.WriteString("version", ToolInfo.Version);
            json.WriteEndObject();

            json.WriteStartArray("files");
            foreach (ExplainedFile file in files)
            {
                WriteJsonFile(file, json);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        output.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private static void WriteFile(ExplainedFile file, TextWriter output)
    {
        output.WriteLine($"File:    {file.Path}");
        if (file.Feedback is not { } feedback)
        {
            output.WriteLine($"Result:  NOT EXPLAINED - {file.Problem}.");
            return;
        }

        output.WriteLine($"Type:    {feedback.Package.Name}");
        if (feedback.Code is not null)
        {
            output.WriteLine($"Code:    {feedback.Code}{(feedback.CodeDescription is null ? "" : " - " + feedback.CodeDescription)}");
        }

        output.WriteLine($"Result:  {Result(feedback)}");
        output.WriteLine($"From:    {feedback.Sender ?? "(not given)"} to {feedback.Receiver ?? "(not given)"}"
            + $"{Optional(", reference ", feedback.Reference)}{Optional(", created ", feedback.CreatedAt)}");
        output.WriteLine($"About:   {About(feedback)}");
        if (file.Report is not null)
        {
            output.WriteLine($"Report:  {file.Report.Path}{(file.ReportNote is null ? "" : $" ({file.ReportNote})")}");
        }
        else if (file.ReportNote is not null)
        {
            output.WriteLine($"Report:  {file.ReportNote}");
        }

        if (file.HistoryMessage is { } sent)
        {
            output.WriteLine($"History: recorded as sent on {sent.RecordedAt:yyyy-MM-dd} from {sent.File}");
        }
        else if (file.HistoryNote is not null)
        {
            output.WriteLine($"History: {file.HistoryNote}");
        }

        if (file.Validation is { Status: ValidationStatus.Invalid } validation)
        {
            output.WriteLine($"Note:    the file breaks its schema ({Summary.Count(validation.ErrorCount, "error")}), so details may be missing.");
        }

        if (feedback.Content is not null)
        {
            output.WriteLine();
            output.WriteLine($"{(feedback.Source == FeedbackSource.Irs ? "The IRS" : "The tax administration")} says: {feedback.Content}");
        }

        output.WriteLine();
        int count = feedback.FileErrors.Count + feedback.RecordErrors.Count;
        if (count == 0)
        {
            output.WriteLine(feedback.Accepted ? "Nothing to do: the file was accepted without errors." : "The file was rejected, but no errors are listed.");
            return;
        }

        output.WriteLine($"To do ({count}):");
        int number = 0;
        foreach (FeedbackError error in feedback.FileErrors)
        {
            WriteError(++number, error, isFileError: true, file.Report, output);
        }

        foreach (FeedbackError error in feedback.RecordErrors)
        {
            WriteError(++number, error, isFileError: false, file.Report, output);
        }
    }

    private static void WriteError(int number, FeedbackError error, bool isFileError, ExplainReport? report, TextWriter output)
    {
        string head = $"{number,3}. {(isFileError ? "File error" : "Record error")} {Code(error)}";
        output.WriteLine(isFileError && error.Description is not null ? $"{head} - {error.Description}" : head);
        const string Indent = "       ";
        if (!isFileError && error.Description is not null)
        {
            output.WriteLine($"{Indent}Details: {error.Description}");
        }

        foreach (string docRefId in error.DocRefIds)
        {
            string kind = error.ReportType is null ? "" : error.ReportType + " ";
            string type = error.DocTypeIndic is null ? "" : $" ({error.DocTypeIndic})";
            output.WriteLine($"{Indent}Record:  {kind}{docRefId}{type}{Location(docRefId, report)}");
        }

        if (error.CorrDocRefId is not null)
        {
            output.WriteLine($"{Indent}Corrects: {error.CorrDocRefId}{Optional(" in message ", error.CorrMessageRefId)}");
        }

        if (error.FinancialInstitution is not null)
        {
            output.WriteLine($"{Indent}Institution: {error.FinancialInstitution}");
        }

        foreach (FeedbackField field in error.Fields)
        {
            output.WriteLine($"{Indent}Field:   {field.Path}{Optional(" - ", field.Text)}{Optional(" (code ", field.Code, ")")}");
        }

        if (error.Action is not null)
        {
            output.WriteLine($"{Indent}Action:  {error.Action}");
        }

        if (error.DueDate is not null)
        {
            output.WriteLine($"{Indent}Due:     {error.DueDate}");
        }
    }

    private static void WriteJsonFile(ExplainedFile file, Utf8JsonWriter json)
    {
        json.WriteStartObject();
        json.WriteString("path", file.Path);
        if (file.Feedback is not { } feedback)
        {
            json.WriteString("status", "notExplained");
            json.WriteString("problem", file.Problem);
            json.WriteEndObject();
            return;
        }

        json.WriteString("status", feedback.Accepted ? "accepted" : "rejected");
        json.WriteString("type", feedback.Package.Name);
        json.WriteString("source", feedback.Source == FeedbackSource.Irs ? "irs" : "oecd");
        WriteNullable(json, "code", feedback.Code);
        WriteNullable(json, "codeDescription", feedback.CodeDescription);
        WriteNullable(json, "sender", feedback.Sender);
        WriteNullable(json, "receiver", feedback.Receiver);
        WriteNullable(json, "reference", feedback.Reference);
        WriteNullable(json, "createdAt", feedback.CreatedAt);
        json.WriteStartObject("originalMessage");
        WriteNullable(json, "messageRefId", feedback.OriginalMessageRefId);
        WriteNullable(json, "transmissionId", feedback.OriginalTransmissionId);
        WriteNullable(json, "sentAt", feedback.OriginalSentAt);
        WriteNullable(json, "fileName", feedback.OriginalFileName);
        json.WriteEndObject();
        WriteNullable(json, "content", feedback.Content);
        if (feedback.RecordCount is { } recordCount)
        {
            json.WriteNumber("recordCount", recordCount);
        }
        else
        {
            json.WriteNull("recordCount");
        }

        json.WriteStartArray("validatedBy");
        foreach (string validator in feedback.ValidatedBy)
        {
            json.WriteStringValue(validator);
        }

        json.WriteEndArray();
        json.WriteBoolean("schemaValid", file.Validation?.Status == ValidationStatus.Valid);
        WriteNullable(json, "report", file.Report?.Path);
        WriteNullable(json, "reportNote", file.ReportNote);
        if (file.HistoryMessage is { } sent)
        {
            json.WriteStartObject("history");
            json.WriteString("file", sent.File);
            json.WriteString("recordedAt", sent.RecordedAt);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("history");
        }

        WriteNullable(json, "historyNote", file.HistoryNote);
        json.WriteStartArray("fileErrors");
        foreach (FeedbackError error in feedback.FileErrors)
        {
            WriteJsonError(error, file.Report, json);
        }

        json.WriteEndArray();
        json.WriteStartArray("recordErrors");
        foreach (FeedbackError error in feedback.RecordErrors)
        {
            WriteJsonError(error, file.Report, json);
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteJsonError(FeedbackError error, ExplainReport? report, Utf8JsonWriter json)
    {
        json.WriteStartObject();
        json.WriteString("code", error.Code);
        WriteNullable(json, "description", error.Description);
        json.WriteStartArray("records");
        foreach (string docRefId in error.DocRefIds)
        {
            json.WriteStartObject();
            json.WriteString("docRefId", docRefId);
            if (report?.Find(docRefId) is { } record)
            {
                json.WriteString("kind", record.Kind);
                json.WriteNumber("line", record.LineNumber);
            }
            else
            {
                json.WriteNull("kind");
                json.WriteNull("line");
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        WriteNullable(json, "reportType", error.ReportType);
        WriteNullable(json, "docTypeIndic", error.DocTypeIndic);
        WriteNullable(json, "corrDocRefId", error.CorrDocRefId);
        WriteNullable(json, "corrMessageRefId", error.CorrMessageRefId);
        WriteNullable(json, "financialInstitution", error.FinancialInstitution);
        json.WriteStartArray("fields");
        foreach (FeedbackField field in error.Fields)
        {
            json.WriteStartObject();
            json.WriteString("path", field.Path);
            WriteNullable(json, "text", field.Text);
            WriteNullable(json, "code", field.Code);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        WriteNullable(json, "action", error.Action);
        WriteNullable(json, "dueDate", error.DueDate);
        json.WriteEndObject();
    }

    private static string Result(FeedbackDocument feedback)
    {
        string authority = feedback.Source == FeedbackSource.Irs ? "the IRS" : "the tax administration";
        string validatedBy = feedback.ValidatedBy.Count == 0 ? "" : $" (validated by {string.Join(", ", feedback.ValidatedBy)})";
        if (!feedback.Accepted)
        {
            return $"REJECTED - {authority} did not process the file{validatedBy}.";
        }

        string errors = feedback.RecordErrors.Count == 0
            ? "no record errors"
            : $"{Summary.Count(feedback.RecordErrors.Count, "record error")} to correct";
        string records = feedback.RecordCount is { } count ? $" (the file had {Summary.Count(count, "record")})" : "";
        return $"ACCEPTED - {authority} processed the file{validatedBy}; {errors}{records}.";
    }

    private static string About(FeedbackDocument feedback)
    {
        var parts = new List<string>
        {
            feedback.OriginalMessageRefId is { } id ? $"message {id}" : "the message is not named",
        };
        if (feedback.OriginalTransmissionId is not null)
        {
            parts.Add($"transmission {feedback.OriginalTransmissionId}{Optional(" sent ", feedback.OriginalSentAt)}");
        }

        if (feedback.OriginalFileName is not null)
        {
            parts.Add($"file {feedback.OriginalFileName}");
        }

        return string.Join(", ", parts);
    }

    private static string Code(FeedbackError error) => error.Code.Length == 0 ? "(no code)" : error.Code;

    private static string Location(string docRefId, ExplainReport? report)
    {
        if (report is null)
        {
            return "";
        }

        string name = Path.GetFileName(report.Path);
        return report.Find(docRefId) is { } record
            ? $" - line {record.LineNumber} of {name} ({record.Kind})"
            : $" - not in {name}";
    }

    private static string Optional(string prefix, string? value, string suffix = "") => value is null ? "" : prefix + value + suffix;

    private static void WriteNullable(Utf8JsonWriter json, string name, string? value)
    {
        if (value is null)
        {
            json.WriteNull(name);
        }
        else
        {
            json.WriteString(name, value);
        }
    }
}
