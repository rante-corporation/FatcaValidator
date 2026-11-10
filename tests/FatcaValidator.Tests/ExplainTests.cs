using System.Text.Json;
using FatcaValidator.Cli;
using FatcaValidator.Core;

namespace FatcaValidator.Tests;

/// <summary>Reading IRS notifications and OECD status messages, and the explain command.</summary>
public sealed class ExplainTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void NotificationCodes_ComeFromTheIrsSchema()
    {
        Assert.Equal("Valid File Notification (N5)", FeedbackDocument.DescribeNotificationCode("NVF"));
        Assert.Equal("Failed Schema Notification (N4)", FeedbackDocument.DescribeNotificationCode("NSV"));
        Assert.Null(FeedbackDocument.DescribeNotificationCode("XYZ"));
    }

    [Fact]
    public void IrsNotification_IsRead()
    {
        FeedbackDocument? feedback = FeedbackDocument.Read(TestFiles.IrsNotification("FieldLevelErrorNotification_2.5.xml"));

        Assert.NotNull(feedback);
        Assert.Equal(FeedbackSource.Irs, feedback.Source);
        Assert.Equal("NVF", feedback.Code);
        Assert.True(feedback.Accepted);
        Assert.Equal("1PLDzAM1zzWDRLJuly13", feedback.OriginalMessageRefId);
        Assert.Equal("20180122T004725848Z_ABEBBA.99999.SL.756.zip", feedback.OriginalFileName);
        Assert.Equal(5, feedback.RecordCount);
        Assert.Empty(feedback.FileErrors);
        FeedbackError error = Assert.Single(feedback.RecordErrors);
        Assert.Equal("8007", error.Code);
        Assert.Equal("ABEBBA.99999.SL.756.1PLDAM3WsRDLDsocJuly1301", Assert.Single(error.DocRefIds));
        Assert.Equal("ABEBBA.99999.SL.756 (Union Asset Management Incorporated)", error.FinancialInstitution);
        Assert.Equal("Name of Account Holder or Recipient Not Provided", Assert.Single(error.Fields).Text);
        Assert.StartsWith("Your file contains a record with one or more errors", error.Action, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusMessage_IsRead()
    {
        FeedbackDocument? feedback = FeedbackDocument.Read(TestFiles.Crs("crs-status-message-record-error.xml"));

        Assert.NotNull(feedback);
        Assert.Equal(FeedbackSource.Oecd, feedback.Source);
        Assert.Same(BundledSchemas.CrsStatusMessageV2_0, feedback.Package);
        Assert.True(feedback.Accepted);
        Assert.Equal("NL2026FR-EXAMPLE-0001", feedback.OriginalMessageRefId);
        Assert.Equal("EXAMPLE-CTS-TRANSMISSION-0001", feedback.OriginalTransmissionId);
        Assert.Equal(new[] { "Example tax administration" }, feedback.ValidatedBy);
        FeedbackError error = Assert.Single(feedback.RecordErrors);
        Assert.Equal("EXAMPLE1", error.Code);
        Assert.Equal("NL2026-EXAMPLE-ACCOUNT-0001", Assert.Single(error.DocRefIds));
        Assert.Equal("CRS_OECD/CrsBody/ReportingGroup/AccountReport/AccountHolder/Individual/TIN", Assert.Single(error.Fields).Path);
    }

    [Fact]
    public void AReport_IsNotFeedback()
    {
        Assert.Null(FeedbackDocument.Read(TestFiles.Irs("newnilfireport.xml")));
    }

    [Fact]
    public void Explain_RecordErrorsBecomeAToDoList()
    {
        (int exitCode, string output, _) = Run("explain", "--file", TestFiles.IrsNotification("RecordLevelErrorNotification_2.5.xml"));

        Assert.Equal(ExitCodes.Invalid, exitCode);
        Assert.Contains("Code:    NVF - Valid File Notification (N5)", output, StringComparison.Ordinal);
        Assert.Contains("Result:  ACCEPTED - the IRS processed the file; 1 record error to correct (the file had 1 record).", output, StringComparison.Ordinal);
        Assert.Contains("About:   message EK9AMD70722-1, transmission a32be902284a41c593096a80633f0b94", output, StringComparison.Ordinal);
        Assert.Contains("To do (1):", output, StringComparison.Ordinal);
        Assert.Contains("1. Record error 8021", output, StringComparison.Ordinal);
        Assert.Contains("Record:  ACCOUNT_REPORT 528L2G.00000.BR.152.DocRefIdEK9AMD70722-6 (FATCA1)", output, StringComparison.Ordinal);
        Assert.Contains("Corrects: 528L2G.00000.BR.152.DocRefIdEK70722-6 in message EK70722-1", output, StringComparison.Ordinal);
        Assert.Contains("Due:     Correct the errors within 120 days of the date of this notice.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Explain_FieldErrorsNameTheField()
    {
        (int exitCode, string output, _) = Run("explain", "--file", TestFiles.IrsNotification("FieldLevelErrorNotification_2.5.xml"));

        Assert.Equal(ExitCodes.Invalid, exitCode);
        Assert.Contains("Field:   ReportingGroup/AccountReport/AccountHolder/Organisation/Name; ReportingGroup/AccountReport/AccountHolder/Individual/Name - Name of Account Holder or Recipient Not Provided", output, StringComparison.Ordinal);
        Assert.Contains("Institution: ABEBBA.99999.SL.756 (Union Asset Management Incorporated)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Explain_FileErrorNotificationIsARejection()
    {
        (int exitCode, string output, _) = Run("explain", "--file", TestFiles.IrsNotification("FileErrorNotificationSample_2.5.xml"));

        Assert.Equal(ExitCodes.Invalid, exitCode);
        Assert.Contains("Result:  REJECTED - the IRS did not process the file.", output, StringComparison.Ordinal);
        Assert.Contains("About:   the message is not named, transmission a7c6363de36f4c2192856b4d3283747c sent 2018-01-10T00:00:00Z, file 20180110T000000000Z_000000.00000.TA.124.zip", output, StringComparison.Ordinal);
        Assert.Contains("1. File error NCF - Certificate Failure Notification", output, StringComparison.Ordinal);
        Assert.Contains("Action:  Please post an updated certificate and resubmit the file to IDES.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Explain_AcceptedWithoutErrors_ExitsWithZero()
    {
        (int exitCode, string output, _) = Run("explain", "--file", TestFiles.IrsNotification("ValidFileNotification_2.5.xml"));

        Assert.Equal(ExitCodes.Valid, exitCode);
        Assert.Contains("Nothing to do: the file was accepted without errors.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Explain_PointsRecordErrorsAtTheReport()
    {
        (int exitCode, string output, _) = Run("explain", "--file", TestFiles.Crs("crs-status-message-record-error.xml"), "--report", TestFiles.Crs(TestFiles.CrsSample));

        Assert.Equal(ExitCodes.Invalid, exitCode);
        Assert.Contains("Type:    CRS Status Message XML Schema v2.0 (OECD)", output, StringComparison.Ordinal);
        Assert.Contains("Result:  ACCEPTED - the tax administration processed the file (validated by Example tax administration); 1 record error to correct.", output, StringComparison.Ordinal);
        Assert.Contains("Details: Example record error", output, StringComparison.Ordinal);
        Assert.Contains("Record:  NL2026-EXAMPLE-ACCOUNT-0001 - line 41 of crs-v3-individual-account.xml (AccountReport)", output, StringComparison.Ordinal);
        Assert.Contains("Field:   CRS_OECD/CrsBody/ReportingGroup/AccountReport/AccountHolder/Individual/TIN", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Explain_RejectedStatusMessage()
    {
        (int exitCode, string output, _) = Run("explain", "--file", TestFiles.Crs("crs-status-message-rejected.xml"));

        Assert.Equal(ExitCodes.Invalid, exitCode);
        Assert.Contains("Result:  REJECTED - the tax administration did not process the file (validated by Example tax administration).", output, StringComparison.Ordinal);
        Assert.Contains("1. File error EXAMPLE2 - Example file error: the message could not be processed.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Explain_Json()
    {
        (int exitCode, string output, _) = Run("explain", "--file", TestFiles.Crs("crs-status-message-record-error.xml"),
            "--report", TestFiles.Crs(TestFiles.CrsSample), "--format", "json");

        Assert.Equal(ExitCodes.Invalid, exitCode);
        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement file = json.RootElement.GetProperty("files")[0];
        Assert.Equal("accepted", file.GetProperty("status").GetString());
        Assert.Equal("oecd", file.GetProperty("source").GetString());
        Assert.Equal("NL2026FR-EXAMPLE-0001", file.GetProperty("originalMessage").GetProperty("messageRefId").GetString());
        JsonElement error = file.GetProperty("recordErrors")[0];
        Assert.Equal("EXAMPLE1", error.GetProperty("code").GetString());
        JsonElement record = error.GetProperty("records")[0];
        Assert.Equal("AccountReport", record.GetProperty("kind").GetString());
        Assert.Equal(41, record.GetProperty("line").GetInt32());
    }

    [Fact]
    public void Explain_WithHistory_SaysWhenTheMessageWasSent()
    {
        string history = Path.Combine(_temp.Path, "history.json");
        Run("history", "add", "--history", history, "--file", TestFiles.Crs(TestFiles.CrsSample));

        (_, string output, _) = Run("explain", "--file", TestFiles.Crs("crs-status-message-rejected.xml"), "--history", history);

        Assert.Contains("History: recorded as sent on ", output, StringComparison.Ordinal);
        Assert.Contains(" from crs-v3-individual-account.xml", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Explain_AReportIsNotExplained()
    {
        (int exitCode, string output, _) = Run("explain", "--file", TestFiles.Irs("newnilfireport.xml"));

        Assert.Equal(ExitCodes.NotValidated, exitCode);
        Assert.Contains("Result:  NOT EXPLAINED - it is not an IRS FATCA notification or an OECD status message", output, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode = CliApp.Run(args, output, error, useColor: false);
        return (exitCode, output.ToString(), error.ToString());
    }
}
