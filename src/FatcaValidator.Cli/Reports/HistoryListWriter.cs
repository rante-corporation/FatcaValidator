using System.Text;
using System.Text.Json;
using FatcaValidator.Core;

namespace FatcaValidator.Cli.Reports;

/// <summary>Writes the messages of a submission history as text or JSON (fatca-val history list).</summary>
internal static class HistoryListWriter
{
    public static void WriteText(SubmissionHistory history, TextWriter output)
    {
        output.WriteLine($"History: {history.Path}");
        output.WriteLine($"{Summary.Count(history.Messages.Count, "message")}, {Summary.Count(history.RecordCount, "record")}.");
        foreach (HistoryMessage message in history.Messages)
        {
            output.WriteLine();
            string details = string.Join(", ", new[]
            {
                message.MessageType,
                message.ReportingPeriod is null ? null : "period " + message.ReportingPeriod,
                message.Test ? "test data" : null,
                Summary.Count(message.Records.Count, "record"),
            }.OfType<string>());
            output.WriteLine($"{message.RecordedAt:yyyy-MM-dd HH:mm}  {message.MessageRefId} ({details})  {message.File}");
            output.WriteLine($"                  {StatusName(message.Status)}");
            foreach (HistoryFeedback feedback in message.Feedback)
            {
                output.WriteLine($"                  {feedback.RecordedAt:yyyy-MM-dd}  {Describe(feedback)}  {feedback.File}");
            }
        }
    }

    public static void WriteJson(SubmissionHistory history, TextWriter output)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteStartObject("tool");
            json.WriteString("name", ToolInfo.Name);
            json.WriteString("version", ToolInfo.Version);
            json.WriteEndObject();
            json.WriteString("history", history.Path);
            json.WriteNumber("messageCount", history.Messages.Count);
            json.WriteNumber("recordCount", history.RecordCount);
            json.WriteStartArray("messages");
            foreach (HistoryMessage message in history.Messages)
            {
                json.WriteStartObject();
                json.WriteString("messageRefId", message.MessageRefId);
                json.WriteString("messageType", message.MessageType);
                json.WriteString("reportingPeriod", message.ReportingPeriod);
                json.WriteBoolean("test", message.Test);
                json.WriteString("file", message.File);
                json.WriteString("sha256", message.Sha256);
                json.WriteString("recordedAt", message.RecordedAt);
                json.WriteNumber("records", message.Records.Count);
                json.WriteString("status", StatusName(message.Status));
                json.WriteStartArray("feedback");
                foreach (HistoryFeedback feedback in message.Feedback)
                {
                    json.WriteStartObject();
                    json.WriteString("type", feedback.Type);
                    json.WriteString("code", feedback.Code);
                    json.WriteBoolean("accepted", feedback.Accepted);
                    json.WriteString("file", feedback.File);
                    json.WriteString("recordedAt", feedback.RecordedAt);
                    json.WriteStartArray("fileErrors");
                    foreach (string code in feedback.FileErrors)
                    {
                        json.WriteStringValue(code);
                    }

                    json.WriteEndArray();
                    json.WriteNumber("recordErrors", feedback.RecordErrors.Count);
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

    private static string StatusName(HistoryMessageStatus status) => status switch
    {
        HistoryMessageStatus.Accepted => "accepted",
        HistoryMessageStatus.Rejected => "rejected",
        _ => "sent",
    };

    private static string Describe(HistoryFeedback feedback)
    {
        string verdict = feedback.Accepted ? "accepted" : "rejected";
        string code = feedback.Code is null ? "" : feedback.Code + " ";
        var errors = new List<string>();
        if (feedback.FileErrors.Count > 0 && feedback.Code is null)
        {
            errors.Add("file errors " + string.Join(", ", feedback.FileErrors));
        }

        if (feedback.RecordErrors.Count > 0)
        {
            errors.Add(Summary.Count(feedback.RecordErrors.Count, "record error"));
        }

        return $"{code}{verdict}{(errors.Count > 0 ? ", " + string.Join(", ", errors) : "")}";
    }
}
