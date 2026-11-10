namespace FatcaValidator.Core;

/// <summary>Codes of the checks against a submission history (<see cref="ValidatorEngine.History"/>).</summary>
public static class HistoryCheckCodes
{
    /// <summary>The MessageRefId was used by a message already sent (error).</summary>
    public const string MessageRefIdReused = "HIST-MESSAGEREFID-REUSED";

    /// <summary>A DocRefId was used by a record already sent (error; a warning when that message was rejected as a whole).</summary>
    public const string DocRefIdReused = "HIST-DOCREFID-REUSED";

    /// <summary>A correction names a record the history does not hold (warning: it may predate the history).</summary>
    public const string CorrectionTargetUnknown = "HIST-CORR-UNKNOWN";

    /// <summary>A correction's CorrMessageRefId is not the message that holds the corrected record (error).</summary>
    public const string CorrMessageRefIdMismatch = "HIST-CORR-MESSAGE-MISMATCH";

    /// <summary>A correction names a record that was already corrected, amended or voided (error).</summary>
    public const string CorrectionTargetReplaced = "HIST-CORR-REPLACED";

    /// <summary>A correction names a record of a message that was rejected as a whole, so it was never processed (error).</summary>
    public const string CorrectionTargetRejected = "HIST-CORR-REJECTED";
}

/// <summary>
/// Checks a report against the messages already sent: MessageRefIds and DocRefIds are used once, and corrections name
/// the latest version of a record that was sent and processed. Test and production messages are checked separately.
/// </summary>
internal static class HistoryChecks
{
    /// <summary>Checks a report; <paramref name="sha256"/> is its file's hash, to recognize a file that was recorded itself.</summary>
    public static void Run(ReportSummary report, string sha256, SubmissionHistory history, IssueCollector issues)
    {
        string? messageRefId = report.MessageRefId;
        if (messageRefId is null || report.Records.Count == 0)
        {
            return;     // not a report with a MessageSpec and DocSpecs, such as a notification
        }

        if (history.FindMessageByFile(sha256) is { } same)
        {
            Add(issues, ValidationSeverity.Error, report.MessageRefIdLine, report.MessageRefIdPosition, HistoryCheckCodes.MessageRefIdReused,
                $"This file was already recorded as sent: {Describe(same)}. Sending it again would repeat its MessageRefId and DocRefIds.");
            return;
        }

        bool test = report.IsTest;
        if (history.FindMessage(messageRefId, test) is { } sent)
        {
            Add(issues, ValidationSeverity.Error, report.MessageRefIdLine, report.MessageRefIdPosition, HistoryCheckCodes.MessageRefIdReused,
                $"MessageRefId {messageRefId} was already used by {Describe(sent)}. Every message needs a new MessageRefId.");
        }

        foreach (ReportRecord record in report.Records)
        {
            CheckDocRefId(record, test, history, issues);
            if (record.IsCorrection && record.CorrDocRefId is not null)
            {
                CheckCorrection(record, record.CorrDocRefId, test, history, issues);
            }
        }
    }

    private static void CheckDocRefId(ReportRecord record, bool test, SubmissionHistory history, IssueCollector issues)
    {
        // Resent data (OECD0) repeats a record unchanged.
        if (record.IsResent || !history.TryFindRecord(record.DocRefId, test, out HistoryMessage? earlier, out _))
        {
            return;
        }

        if (earlier.Status == HistoryMessageStatus.Rejected)
        {
            Add(issues, ValidationSeverity.Warning, record.LineNumber, record.LinePosition, HistoryCheckCodes.DocRefIdReused,
                $"DocRefId {record.DocRefId} was also in {Describe(earlier)}, which was rejected as a whole ({Verdict(earlier)}), so that record was never processed.");
        }
        else
        {
            Add(issues, ValidationSeverity.Error, record.LineNumber, record.LinePosition, HistoryCheckCodes.DocRefIdReused,
                $"DocRefId {record.DocRefId} was already used in {Describe(earlier)}. A DocRefId must be unique for all time: give this record a new one.");
        }
    }

    private static void CheckCorrection(ReportRecord record, string target, bool test, SubmissionHistory history, IssueCollector issues)
    {
        int line = record.CorrLineNumber > 0 ? record.CorrLineNumber : record.LineNumber;
        int position = record.CorrLineNumber > 0 ? record.CorrLinePosition : record.LinePosition;

        if (!history.TryFindRecord(target, test, out HistoryMessage? original, out _))
        {
            Add(issues, ValidationSeverity.Warning, line, position, HistoryCheckCodes.CorrectionTargetUnknown,
                $"CorrDocRefId {target} is not in the history ({history.Describe()}). If that record was sent before the history was started, ignore this warning.");
            return;
        }

        if (original.Status == HistoryMessageStatus.Rejected)
        {
            Add(issues, ValidationSeverity.Error, line, position, HistoryCheckCodes.CorrectionTargetRejected,
                $"Record {target} was in {Describe(original)}, which was rejected as a whole ({Verdict(original)}), so it was never processed and cannot be corrected, amended or voided. Send it as new data instead.");
            return;
        }

        if (record.CorrMessageRefId is { } corrMessageRefId && corrMessageRefId != original.MessageRefId)
        {
            Add(issues, ValidationSeverity.Error, line, position, HistoryCheckCodes.CorrMessageRefIdMismatch,
                $"CorrMessageRefId {corrMessageRefId} is not the message that holds record {target}: that is {Describe(original)}, so CorrMessageRefId must be {original.MessageRefId}.");
        }

        (HistoryMessage Message, HistoryRecord Record)? latest = null;
        foreach ((HistoryMessage message, HistoryRecord replacement) in history.FindReplacements(target, test))
        {
            // A rejected message replaced nothing; a record with this record's own DocRefId is an earlier copy of it.
            if (message.Status != HistoryMessageStatus.Rejected && replacement.DocRefId != record.DocRefId)
            {
                latest = (message, replacement);
            }
        }

        if (latest is { } replaced)
        {
            string message = DocType.Parse(replaced.Record.DocTypeIndic).Voids
                ? $"Record {target} was already voided by record {replaced.Record.DocRefId} in {Describe(replaced.Message)}; a voided record cannot be changed again."
                : $"Record {target} was already replaced by record {replaced.Record.DocRefId} ({replaced.Record.DocTypeIndic}) in {Describe(replaced.Message)}. "
                    + $"A correction names the latest version of a record: CorrDocRefId {replaced.Record.DocRefId}"
                    + (record.CorrMessageRefId is null ? "." : $" with CorrMessageRefId {replaced.Message.MessageRefId}.");
            Add(issues, ValidationSeverity.Error, line, position, HistoryCheckCodes.CorrectionTargetReplaced, message);
        }
    }

    private static string Describe(HistoryMessage message) =>
        $"message {message.MessageRefId} ({message.File}, recorded {message.RecordedAt:yyyy-MM-dd})";

    private static string Verdict(HistoryMessage message)
    {
        HistoryFeedback feedback = message.Feedback[^1];
        string code = feedback.Code ?? (feedback.FileErrors.Count > 0 ? string.Join(", ", feedback.FileErrors) : "rejected");
        return $"{code} in {feedback.File}";
    }

    private static void Add(IssueCollector issues, ValidationSeverity severity, int line, int position, string code, string message) =>
        issues.Add(new ValidationIssue(severity, message, line, position, code, IssueSource.History));
}
