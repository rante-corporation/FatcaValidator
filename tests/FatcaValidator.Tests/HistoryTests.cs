using System.Text.Json;
using FatcaValidator.Cli;
using FatcaValidator.Core;

namespace FatcaValidator.Tests;

/// <summary>
/// The submission history. The IRS samples form a chain: newnilfireport.xml is new data, and amendednilfireport.xml
/// and voidnilfireport.xml each replace its two records (as alternatives: they share a MessageRefId and DocRefIds).
/// </summary>
public sealed class HistoryTests : IDisposable
{
    private const string NewMessage = "DBA6455E-8454-47D9-914B-FEE48E4EF3AA";
    private const string AmendedMessage = "D976F6C6-BA90-4F5A-9556-246EB3A4F3B6";
    private const string NewFiDocRefId = "S519K4.99999.SL.392.50B80D2D-79DA-4AFD-8148-F06480FFDEB5";
    private const string AmendedFiDocRefId = "S519K4.99999.SL.392.0187BF2D-ECE0-4664-A0CF-E73741E0B8EA";
    private const string AmendedNilDocRefId = "S519K4.99999.SL.392.5893E436-89FE-46C1-B350-2E0A10CB2B39";
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string HistoryPath => Path.Combine(_temp.Path, "history.json");

    [Fact]
    public void ReportSummary_ReadsTheMessageSpecAndEveryDocSpec()
    {
        ReportSummary report = ReportSummary.Read(TestFiles.Irs("amendednilfireport.xml"));

        Assert.Equal(AmendedMessage, report.MessageRefId);
        Assert.Equal(9, report.MessageRefIdLine);
        Assert.Equal("FATCA", report.MessageType);
        Assert.Equal("S519K4.99999.SL.392", report.SendingCompanyIN);
        Assert.Equal("2016-12-31", report.ReportingPeriod);
        Assert.False(report.IsTest);
        Assert.Collection(report.Records,
            fi =>
            {
                Assert.Equal("ReportingFI", fi.Kind);
                Assert.Equal(AmendedFiDocRefId, fi.DocRefId);
                Assert.Equal("FATCA4", fi.DocTypeIndic);
                Assert.Equal(NewFiDocRefId, fi.CorrDocRefId);
                Assert.Equal(NewMessage, fi.CorrMessageRefId);
                Assert.Equal(24, fi.LineNumber);
                Assert.Equal(26, fi.CorrLineNumber);
                Assert.True(fi.IsCorrection);
                Assert.False(fi.IsVoid);
            },
            nil =>
            {
                Assert.Equal("NilReport", nil.Kind);
                Assert.Equal(AmendedNilDocRefId, nil.DocRefId);
                Assert.Equal(33, nil.LineNumber);
            });
    }

    [Fact]
    public void ReportSummary_ReadsCrsReports()
    {
        ReportSummary report = ReportSummary.Read(TestFiles.Crs(TestFiles.CrsSample));

        Assert.Equal("NL2026FR-EXAMPLE-0001", report.MessageRefId);
        Assert.Contains(report.Records, record => record is { Kind: "ReportingFI", DocRefId: "NL2026-EXAMPLE-FI-0001", DocTypeIndic: "OECD1", LineNumber: 34 });
        Assert.Contains(report.Records, record => record is { Kind: "AccountReport", DocRefId: "NL2026-EXAMPLE-ACCOUNT-0001", LineNumber: 41 });
    }

    [Fact]
    public void HistoryFile_RoundTrips()
    {
        History("newnilfireport.xml").Save();

        SubmissionHistory loaded = SubmissionHistory.Load(HistoryPath);

        HistoryMessage message = Assert.Single(loaded.Messages);
        Assert.Equal(NewMessage, message.MessageRefId);
        Assert.Equal("newnilfireport.xml", message.File);
        Assert.Equal(SubmissionHistory.ComputeSha256(TestFiles.Irs("newnilfireport.xml")), message.Sha256);
        Assert.Equal(HistoryMessageStatus.Sent, message.Status);
        Assert.Equal(new[] { "ReportingFI", "NilReport" }, message.Records.Select(record => record.Kind));
        Assert.False(File.Exists(HistoryPath + ".tmp"));
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(HistoryPath));
        Assert.Equal(SubmissionHistory.FormatName, json.RootElement.GetProperty("format").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("version").GetInt32());
    }

    [Theory]
    [InlineData("{}", "is not a fatca-val history file")]
    [InlineData("not json", "is not valid JSON")]
    [InlineData("""{"format":"fatca-val-history","version":1,"messages":[{"messageRefId":""}]}""", "is damaged")]
    public void FileThatIsNotAHistory_IsRefused(string content, string expected)
    {
        File.WriteAllText(HistoryPath, content);

        HistoryException ex = Assert.Throws<HistoryException>(() => SubmissionHistory.Load(HistoryPath));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SameFileAgain_IsOneError()
    {
        ValidationResult result = Validate(History("newnilfireport.xml"), TestFiles.Irs("newnilfireport.xml"));

        ValidationIssue error = Assert.Single(result.Issues);
        Assert.Equal(HistoryCheckCodes.MessageRefIdReused, error.Code);
        Assert.Equal(IssueSource.History, error.Source);
        Assert.Equal(9, error.LineNumber);
        Assert.Contains("already recorded as sent", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AmendmentOfASentReport_Passes()
    {
        ValidationResult result = Validate(History("newnilfireport.xml"), TestFiles.Irs("amendednilfireport.xml"));

        Assert.True(result.IsValid, Describe(result));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ReusedMessageRefIdAndDocRefIds_AreErrors()
    {
        ValidationResult result = Validate(History("newnilfireport.xml", "amendednilfireport.xml"), TestFiles.Irs("voidnilfireport.xml"));

        Assert.Equal(3, result.ErrorCount);
        Assert.Single(result.Errors, error => error.Code == HistoryCheckCodes.MessageRefIdReused);
        Assert.Equal(2, result.Errors.Count(error => error.Code == HistoryCheckCodes.DocRefIdReused));
        Assert.Contains(result.Errors, error => error.LineNumber == 24 && error.Message.Contains(AmendedFiDocRefId, StringComparison.Ordinal));
    }

    [Fact]
    public void CorrectionOfAReplacedRecord_IsAnError()
    {
        string path = TestFiles.WriteIrsVariant(_temp, "voidnilfireport.xml",
            ($"<sfa:MessageRefId>{AmendedMessage}</sfa:MessageRefId>", "<sfa:MessageRefId>VOID-EXAMPLE-0001</sfa:MessageRefId>"),
            ($"<ftc:DocRefId>{AmendedFiDocRefId}</ftc:DocRefId>", "<ftc:DocRefId>S519K4.99999.SL.392.VOID-EXAMPLE-FI</ftc:DocRefId>"),
            ($"<ftc:DocRefId>{AmendedNilDocRefId}</ftc:DocRefId>", "<ftc:DocRefId>S519K4.99999.SL.392.VOID-EXAMPLE-NIL</ftc:DocRefId>"));

        ValidationResult result = Validate(History("newnilfireport.xml", "amendednilfireport.xml"), path);

        Assert.Equal(2, result.ErrorCount);
        Assert.All(result.Errors, error => Assert.Equal(HistoryCheckCodes.CorrectionTargetReplaced, error.Code));
        ValidationIssue first = result.Errors.First();
        Assert.Equal(26, first.LineNumber);
        Assert.Contains($"CorrDocRefId {AmendedFiDocRefId} with CorrMessageRefId {AmendedMessage}", first.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CorrectionWithTheWrongCorrMessageRefId_IsAnError()
    {
        string path = TestFiles.WriteIrsVariant(_temp, "amendednilfireport.xml",
            ($"<ftc:CorrMessageRefId>{NewMessage}</ftc:CorrMessageRefId>", "<ftc:CorrMessageRefId>SOME-OTHER-MESSAGE</ftc:CorrMessageRefId>"));

        ValidationResult result = Validate(History("newnilfireport.xml"), path);

        Assert.Equal(2, result.ErrorCount);
        Assert.All(result.Errors, error => Assert.Equal(HistoryCheckCodes.CorrMessageRefIdMismatch, error.Code));
        Assert.Contains($"CorrMessageRefId must be {NewMessage}", result.Errors.First().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CorrectionOfARecordNotInTheHistory_IsAWarning()
    {
        ValidationResult result = Validate(History(), TestFiles.Irs("amendednilfireport.xml"));

        Assert.True(result.IsValid, Describe(result));
        Assert.Equal(2, result.WarningCount);
        Assert.All(result.Warnings, warning => Assert.Equal(HistoryCheckCodes.CorrectionTargetUnknown, warning.Code));
    }

    [Fact]
    public void RecordsOfARejectedMessage_WereNeverProcessed()
    {
        SubmissionHistory history = History("newnilfireport.xml");
        string notification = TestFiles.IrsNotification("FileErrorNotificationSample_2.5.xml");
        history.AddFeedback(history.Messages[0], FeedbackDocument.Read(notification)!, notification, SubmissionHistory.ComputeSha256(notification), DateTimeOffset.UtcNow);
        Assert.Equal(HistoryMessageStatus.Rejected, history.Messages[0].Status);

        // Correcting one of its records is an error...
        ValidationResult amended = Validate(history, TestFiles.Irs("amendednilfireport.xml"));
        Assert.Equal(2, amended.ErrorCount);
        Assert.All(amended.Errors, error => Assert.Equal(HistoryCheckCodes.CorrectionTargetRejected, error.Code));
        Assert.Contains("NCF in FileErrorNotificationSample_2.5.xml", amended.Errors.First().Message, StringComparison.Ordinal);

        // ...and sending its records again in a new message is only a warning.
        string resent = TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml",
            ($"<sfa:MessageRefId>{NewMessage}</sfa:MessageRefId>", "<sfa:MessageRefId>RESENT-EXAMPLE-0001</sfa:MessageRefId>"));
        ValidationResult again = Validate(history, resent);
        Assert.True(again.IsValid, Describe(again));
        Assert.Equal(2, again.WarningCount);
        Assert.All(again.Warnings, warning => Assert.Equal(HistoryCheckCodes.DocRefIdReused, warning.Code));
    }

    [Fact]
    public void TestData_IsCheckedApartFromProduction()
    {
        string test = TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml", ("<ftc:DocTypeIndic>FATCA1</ftc:DocTypeIndic>", "<ftc:DocTypeIndic>FATCA11</ftc:DocTypeIndic>"));

        ValidationResult result = Validate(History("newnilfireport.xml"), test);

        Assert.True(result.IsValid, Describe(result));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void FileThatBreaksItsSchema_IsNotCheckedAgainstTheHistory()
    {
        string path = TestFiles.WriteIrsVariant(_temp, "newnilfireport.xml", (">JP</sfa:TransmittingCountry>", ">ZZ</sfa:TransmittingCountry>"));

        ValidationResult result = Validate(History("newnilfireport.xml"), path);

        Assert.Equal(IssueCodes.SchemaError, Assert.Single(result.Errors).Code);
        Assert.Equal(IssueCodes.RulesSkipped, Assert.Single(result.Warnings).Code);
    }

    [Fact]
    public void HistoryAdd_RecordsReportsOnceAndRefusesConflicts()
    {
        (int exitCode, string output, _) = Run("history", "add", "--history", HistoryPath,
            "--file", TestFiles.Irs("newnilfireport.xml"), TestFiles.Irs("amendednilfireport.xml"));

        Assert.Equal(ExitCodes.Valid, exitCode);
        Assert.Contains($"Recorded: newnilfireport.xml as message {NewMessage} (FATCA, 2 records).", output, StringComparison.Ordinal);
        Assert.Contains($"Recorded: amendednilfireport.xml as message {AmendedMessage}", output, StringComparison.Ordinal);
        Assert.Contains("Saved: history.json, 2 messages since ", output, StringComparison.Ordinal);

        (exitCode, output, _) = Run("history", "add", "--history", HistoryPath,
            "--file", TestFiles.Irs("newnilfireport.xml"), TestFiles.Irs("voidnilfireport.xml"));

        Assert.Equal(ExitCodes.Invalid, exitCode);
        Assert.Contains("Already recorded: newnilfireport.xml", output, StringComparison.Ordinal);
        Assert.Contains("Not recorded: voidnilfireport.xml has 3 errors", output, StringComparison.Ordinal);
        Assert.Contains("[HIST-MESSAGEREFID-REUSED]", output, StringComparison.Ordinal);
        Assert.Contains("Nothing new was recorded", output, StringComparison.Ordinal);
        Assert.Equal(2, SubmissionHistory.Load(HistoryPath).Messages.Count);
    }

    [Fact]
    public void HistoryAdd_RecordsANotificationForItsMessage()
    {
        Run("history", "add", "--history", HistoryPath, "--file", TestFiles.Irs("newnilfireport.xml"));
        string notification = TestFiles.IrsNotification("FileErrorNotificationSample_2.5.xml");

        (int exitCode, string output, _) = Run("history", "add", "--history", HistoryPath, "--file", notification);

        Assert.Equal(ExitCodes.NotValidated, exitCode);
        Assert.Contains("give its MessageRefId with --message", output, StringComparison.Ordinal);

        (exitCode, output, _) = Run("history", "add", "--history", HistoryPath, "--file", notification, "--message", NewMessage);

        Assert.Equal(ExitCodes.Valid, exitCode);
        Assert.Contains($"Recorded: FileErrorNotificationSample_2.5.xml for message {NewMessage}: NCF (Certificate Failure Notification", output, StringComparison.Ordinal);
        Assert.Contains("): rejected.", output, StringComparison.Ordinal);

        (exitCode, output, _) = Run("history", "list", "--history", HistoryPath);

        Assert.Equal(ExitCodes.Valid, exitCode);
        Assert.Contains($"{NewMessage} (FATCA, period 2016-12-31, 2 records)  newnilfireport.xml", output, StringComparison.Ordinal);
        Assert.Contains("NCF rejected  FileErrorNotificationSample_2.5.xml", output, StringComparison.Ordinal);

        (exitCode, output, _) = Run("history", "list", "--history", HistoryPath, "--format", "json");

        Assert.Equal(ExitCodes.Valid, exitCode);
        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement message = json.RootElement.GetProperty("messages")[0];
        Assert.Equal("rejected", message.GetProperty("status").GetString());
        Assert.Equal("NCF", message.GetProperty("feedback")[0].GetProperty("code").GetString());
    }

    [Fact]
    public void HistoryAdd_RecordsAStatusMessageForACrsReport()
    {
        (int exitCode, string output, _) = Run("history", "add", "--history", HistoryPath,
            "--file", TestFiles.Crs("crs-status-message-rejected.xml"), TestFiles.Crs(TestFiles.CrsSample));

        Assert.Equal(ExitCodes.Valid, exitCode);
        Assert.Contains("Recorded: crs-v3-individual-account.xml as message NL2026FR-EXAMPLE-0001 (CRS", output, StringComparison.Ordinal);
        Assert.Contains("Recorded: crs-status-message-rejected.xml for message NL2026FR-EXAMPLE-0001: status: rejected, 1 file error.", output, StringComparison.Ordinal);
        Assert.Equal(HistoryMessageStatus.Rejected, Assert.Single(SubmissionHistory.Load(HistoryPath).Messages).Status);
    }

    [Fact]
    public void ValidateWithHistory_NamesTheHistoryAndReportsConflicts()
    {
        Run("history", "add", "--history", HistoryPath, "--file", TestFiles.Irs("newnilfireport.xml"), TestFiles.Irs("amendednilfireport.xml"));

        (int exitCode, string output, _) = Run("--file", TestFiles.Irs("voidnilfireport.xml"), "--history", HistoryPath);

        Assert.Equal(ExitCodes.Invalid, exitCode);
        Assert.StartsWith("History:  history.json, 2 messages since ", output, StringComparison.Ordinal);
        Assert.Contains("[HIST-DOCREFID-REUSED]", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateWithAMissingHistoryFile_ExitsWithTwo()
    {
        (int exitCode, _, string error) = Run("--file", TestFiles.Irs("newnilfireport.xml"), "--history", Path.Combine(_temp.Path, "missing.json"));

        Assert.Equal(ExitCodes.NotValidated, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    private SubmissionHistory History(params string[] sentSamples)
    {
        SubmissionHistory history = SubmissionHistory.LoadOrCreate(HistoryPath, out _);
        foreach (string sample in sentSamples)
        {
            string path = TestFiles.Irs(sample);
            history.AddMessage(ReportSummary.Read(path), path, SubmissionHistory.ComputeSha256(path), "test", DateTimeOffset.UtcNow);
        }

        return history;
    }

    private static ValidationResult Validate(SubmissionHistory history, string path) => new ValidatorEngine { History = history }.Validate(path);

    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode = CliApp.Run(args, output, error, useColor: false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string Describe(ValidationResult result) =>
        $"{result.Status}: {string.Join(Environment.NewLine, result.Issues)}";
}
