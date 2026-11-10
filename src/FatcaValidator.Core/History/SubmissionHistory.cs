using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FatcaValidator.Core;

/// <summary>Where a recorded message stands, from the latest notification or status message recorded for it.</summary>
public enum HistoryMessageStatus
{
    /// <summary>No notification or status message has been recorded for it yet.</summary>
    Sent,

    /// <summary>The latest feedback accepted the file; some records may still have errors.</summary>
    Accepted,

    /// <summary>The latest feedback rejected the file as a whole, so none of its records were processed.</summary>
    Rejected,
}

/// <summary>A report recorded as sent: its MessageSpec, its records and the feedback received for it.</summary>
public sealed class HistoryMessage
{
    /// <summary>MessageSpec/MessageRefId.</summary>
    public string MessageRefId { get; init; } = "";

    /// <summary>MessageSpec/MessageType, for example FATCA or CRS.</summary>
    public string? MessageType { get; init; }

    /// <summary>The schema the file passed when it was recorded.</summary>
    public string? Schema { get; init; }

    /// <summary>MessageSpec/SendingCompanyIN.</summary>
    public string? SendingCompanyIN { get; init; }

    /// <summary>MessageSpec/TransmittingCountry.</summary>
    public string? TransmittingCountry { get; init; }

    /// <summary>MessageSpec/ReceivingCountry.</summary>
    public string? ReceivingCountry { get; init; }

    /// <summary>MessageSpec/ReportingPeriod, as written.</summary>
    public string? ReportingPeriod { get; init; }

    /// <summary>MessageSpec/Timestamp, as written.</summary>
    public string? Timestamp { get; init; }

    /// <summary>Whether the message holds test data. Test and production messages are checked separately.</summary>
    public bool Test { get; init; }

    /// <summary>The report file's name, without its folder.</summary>
    public string File { get; init; } = "";

    /// <summary>The SHA-256 of the report file, in lower-case hexadecimal.</summary>
    public string Sha256 { get; init; } = "";

    /// <summary>When the file was recorded as sent.</summary>
    public DateTimeOffset RecordedAt { get; init; }

    /// <summary>The message's records, in document order.</summary>
    public List<HistoryRecord> Records { get; init; } = [];

    /// <summary>The notifications and status messages recorded for the message, oldest first.</summary>
    public List<HistoryFeedback> Feedback { get; init; } = [];

    /// <summary>The status that the latest recorded feedback gives the message.</summary>
    [JsonIgnore]
    public HistoryMessageStatus Status =>
        Feedback.Count == 0 ? HistoryMessageStatus.Sent
        : Feedback[^1].Accepted ? HistoryMessageStatus.Accepted
        : HistoryMessageStatus.Rejected;
}

/// <summary>One record of a recorded message.</summary>
public sealed class HistoryRecord
{
    /// <summary>The element that holds the DocSpec, for example ReportingFI or AccountReport.</summary>
    public string Kind { get; init; } = "";

    /// <summary>The record's DocRefId.</summary>
    public string DocRefId { get; init; } = "";

    /// <summary>The record's DocTypeIndic.</summary>
    public string DocTypeIndic { get; init; } = "";

    /// <summary>The record it corrects, amends or voids, if any.</summary>
    public string? CorrDocRefId { get; init; }

    /// <summary>The message of that record (FATCA), if given.</summary>
    public string? CorrMessageRefId { get; init; }
}

/// <summary>A notification or status message recorded for a message.</summary>
public sealed class HistoryFeedback
{
    /// <summary>The kind of document: the name of its bundled schema.</summary>
    public string Type { get; init; } = "";

    /// <summary>The IRS notification code, such as NVF or NSV; <see langword="null"/> for OECD status messages.</summary>
    public string? Code { get; init; }

    /// <summary>Whether the file was accepted (records may still have errors).</summary>
    public bool Accepted { get; init; }

    /// <summary>The feedback's own reference: the IRS notification id or the status message's MessageRefId.</summary>
    public string? Reference { get; init; }

    /// <summary>When the authority created it, as written.</summary>
    public string? CreatedAt { get; init; }

    /// <summary>The feedback file's name, without its folder.</summary>
    public string File { get; init; } = "";

    /// <summary>The SHA-256 of the feedback file, in lower-case hexadecimal.</summary>
    public string Sha256 { get; init; } = "";

    /// <summary>When it was recorded.</summary>
    public DateTimeOffset RecordedAt { get; init; }

    /// <summary>The codes of its file-level errors.</summary>
    public List<string> FileErrors { get; init; } = [];

    /// <summary>Its record-level errors.</summary>
    public List<HistoryRecordError> RecordErrors { get; init; } = [];
}

/// <summary>A record-level error from a notification or status message.</summary>
public sealed class HistoryRecordError
{
    /// <summary>The authority's error code.</summary>
    public string Code { get; init; } = "";

    /// <summary>The DocRefIds of the records in error.</summary>
    public List<string> DocRefIds { get; init; } = [];
}

/// <summary>A history file could not be read or written.</summary>
public sealed class HistoryException : Exception
{
    /// <summary>Creates the exception.</summary>
    public HistoryException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with its cause.</summary>
    public HistoryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A local record of the reports sent and the IRS notifications and OECD status messages received, kept in one JSON
/// file. With it, <see cref="ValidatorEngine.History"/> checks what one file cannot show: a MessageRefId or DocRefId
/// used before, and corrections that name a record that was never sent, was rejected, or was already replaced. It keeps
/// identifiers, dates, file names and hashes, and no account data.
/// </summary>
/// <remarks>Not thread-safe. <see cref="Save"/> replaces the file in one step, so a failed write leaves the old one.</remarks>
public sealed class SubmissionHistory
{
    /// <summary>The value of the file's "format" property.</summary>
    public const string FormatName = "fatca-val-history";

    /// <summary>The version of the file format this library reads and writes.</summary>
    public const int FormatVersion = 1;

    private readonly HistoryDocument _document;
    private readonly Ledger _production = new();
    private readonly Ledger _test = new();
    private readonly Dictionary<string, HistoryMessage> _messagesByFile = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HistoryMessage> _feedbackFiles = new(StringComparer.Ordinal);

    private SubmissionHistory(string path, HistoryDocument document)
    {
        Path = path;
        _document = document;
        foreach (HistoryMessage message in document.Messages)
        {
            Index(message);
        }
    }

    /// <summary>The full path of the history file.</summary>
    public string Path { get; }

    /// <summary>The recorded messages, oldest first.</summary>
    public IReadOnlyList<HistoryMessage> Messages => _document.Messages;

    /// <summary>How many records the recorded messages hold.</summary>
    public int RecordCount => _document.Messages.Sum(message => message.Records.Count);

    /// <summary>A short description for messages, for example "fatca-history.json, 3 messages since 2026-11-10".</summary>
    public string Describe()
    {
        string name = System.IO.Path.GetFileName(Path);
        if (Messages.Count == 0)
        {
            return $"{name}, no messages yet";
        }

        DateTimeOffset first = Messages.Min(message => message.RecordedAt);
        return $"{name}, {Messages.Count} message{(Messages.Count == 1 ? "" : "s")} since {first:yyyy-MM-dd}";
    }

    /// <summary>Loads a history file.</summary>
    /// <exception cref="HistoryException">The file does not exist, cannot be read or is not a history file.</exception>
    public static SubmissionHistory Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string fullPath = System.IO.Path.GetFullPath(path);
        if (!System.IO.File.Exists(fullPath))
        {
            throw new HistoryException($"The history file '{fullPath}' does not exist. 'fatca-val history add' creates it when it records the first file.");
        }

        HistoryDocument? document;
        try
        {
            using FileStream stream = System.IO.File.OpenRead(fullPath);
            document = JsonSerializer.Deserialize(stream, HistoryJsonContext.Default.HistoryDocument);
        }
        catch (JsonException ex)
        {
            throw new HistoryException($"The history file '{fullPath}' is not valid JSON: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new HistoryException($"The history file '{fullPath}' could not be read: {ex.Message}", ex);
        }

        if (document is null || document.Format != FormatName)
        {
            throw new HistoryException($"'{fullPath}' is not a fatca-val history file (its \"format\" is not \"{FormatName}\").");
        }

        if (document.Version != FormatVersion)
        {
            throw new HistoryException($"'{fullPath}' is a version {document.Version} history file; this fatca-val reads version {FormatVersion}.");
        }

        string? problem = FindProblem(document);
        if (problem is not null)
        {
            throw new HistoryException($"The history file '{fullPath}' is damaged: {problem}.");
        }

        return new SubmissionHistory(fullPath, document);
    }

    /// <summary>Loads a history file, or starts an empty one (not yet saved) when the file does not exist.</summary>
    /// <exception cref="HistoryException">The file exists but cannot be read or is not a history file.</exception>
    public static SubmissionHistory LoadOrCreate(string path, out bool created)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string fullPath = System.IO.Path.GetFullPath(path);
        created = !System.IO.File.Exists(fullPath);
        return created
            ? new SubmissionHistory(fullPath, new HistoryDocument { Format = FormatName, Version = FormatVersion })
            : Load(fullPath);
    }

    /// <summary>Writes the history to <see cref="Path"/>, replacing the file in one step.</summary>
    /// <exception cref="HistoryException">The file could not be written.</exception>
    public void Save()
    {
        string temporary = Path + ".tmp";
        try
        {
            string? folder = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, _document, HistoryJsonContext.Default.HistoryDocument);
            }

            System.IO.File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                System.IO.File.Delete(temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Leave it; the next save overwrites it.
            }

            throw new HistoryException($"The history file '{Path}' could not be written: {ex.Message}", ex);
        }
    }

    /// <summary>The recorded message with this MessageRefId among test or production messages, or <see langword="null"/>.</summary>
    public HistoryMessage? FindMessage(string messageRefId, bool test) => LedgerFor(test).Messages.GetValueOrDefault(messageRefId);

    /// <summary>The recorded message with this MessageRefId, production first, or <see langword="null"/>.</summary>
    public HistoryMessage? FindMessage(string messageRefId) => FindMessage(messageRefId, test: false) ?? FindMessage(messageRefId, test: true);

    /// <summary>The message recorded from a file with this SHA-256, or <see langword="null"/>.</summary>
    public HistoryMessage? FindMessageByFile(string sha256) => _messagesByFile.GetValueOrDefault(sha256);

    /// <summary>The message a feedback file with this SHA-256 was recorded for, or <see langword="null"/>.</summary>
    public HistoryMessage? FindFeedbackFile(string sha256) => _feedbackFiles.GetValueOrDefault(sha256);

    /// <summary>Finds the first recorded record with this DocRefId among test or production messages.</summary>
    public bool TryFindRecord(
        string docRefId,
        bool test,
        [NotNullWhen(true)] out HistoryMessage? message,
        [NotNullWhen(true)] out HistoryRecord? record)
    {
        if (LedgerFor(test).Records.TryGetValue(docRefId, out (HistoryMessage Message, HistoryRecord Record) found))
        {
            (message, record) = found;
            return true;
        }

        message = null;
        record = null;
        return false;
    }

    /// <summary>The recorded records that correct, amend or void the record with this DocRefId, oldest first.</summary>
    public IReadOnlyList<(HistoryMessage Message, HistoryRecord Record)> FindReplacements(string docRefId, bool test) =>
        LedgerFor(test).Replacements.TryGetValue(docRefId, out List<(HistoryMessage, HistoryRecord)>? found) ? found : [];

    /// <summary>Adds a sent report. The caller checks it first: see <see cref="ValidatorEngine.History"/>.</summary>
    /// <param name="report">The report's MessageSpec and records.</param>
    /// <param name="file">The report file's name or path; only the name is kept.</param>
    /// <param name="sha256">The report file's SHA-256 (<see cref="ComputeSha256"/>).</param>
    /// <param name="schema">The schema it passed.</param>
    /// <param name="recordedAt">When it is recorded.</param>
    /// <exception cref="ArgumentException">The report has no MessageRefId.</exception>
    public HistoryMessage AddMessage(ReportSummary report, string file, string sha256, string? schema, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrEmpty(file);
        ArgumentException.ThrowIfNullOrEmpty(sha256);
        if (report.MessageRefId is null)
        {
            throw new ArgumentException("The report has no MessageSpec/MessageRefId.", nameof(report));
        }

        var message = new HistoryMessage
        {
            MessageRefId = report.MessageRefId,
            MessageType = report.MessageType,
            Schema = schema,
            SendingCompanyIN = report.SendingCompanyIN,
            TransmittingCountry = report.TransmittingCountry,
            ReceivingCountry = report.ReceivingCountry,
            ReportingPeriod = report.ReportingPeriod,
            Timestamp = report.Timestamp,
            Test = report.IsTest,
            File = System.IO.Path.GetFileName(file),
            Sha256 = sha256,
            RecordedAt = recordedAt,
            Records = report.Records.Select(record => new HistoryRecord
            {
                Kind = record.Kind,
                DocRefId = record.DocRefId,
                DocTypeIndic = record.DocTypeIndic,
                CorrDocRefId = record.CorrDocRefId,
                CorrMessageRefId = record.CorrMessageRefId,
            }).ToList(),
        };

        _document.Messages.Add(message);
        Index(message);
        return message;
    }

    /// <summary>Adds a notification or status message to the message it is about.</summary>
    /// <param name="message">The recorded message, from <see cref="FindMessage(string)"/>.</param>
    /// <param name="feedback">The notification or status message.</param>
    /// <param name="file">Its file's name or path; only the name is kept.</param>
    /// <param name="sha256">Its file's SHA-256.</param>
    /// <param name="recordedAt">When it is recorded.</param>
    public HistoryFeedback AddFeedback(HistoryMessage message, FeedbackDocument feedback, string file, string sha256, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentException.ThrowIfNullOrEmpty(file);
        ArgumentException.ThrowIfNullOrEmpty(sha256);

        var entry = new HistoryFeedback
        {
            Type = feedback.Package.Name,
            Code = feedback.Code,
            Accepted = feedback.Accepted,
            Reference = feedback.Reference,
            CreatedAt = feedback.CreatedAt,
            File = System.IO.Path.GetFileName(file),
            Sha256 = sha256,
            RecordedAt = recordedAt,
            FileErrors = feedback.FileErrors.Select(error => error.Code).ToList(),
            RecordErrors = feedback.RecordErrors.Select(error => new HistoryRecordError { Code = error.Code, DocRefIds = [.. error.DocRefIds] }).ToList(),
        };

        message.Feedback.Add(entry);
        _feedbackFiles.TryAdd(sha256, message);
        return entry;
    }

    /// <summary>The SHA-256 of a file, in lower-case hexadecimal.</summary>
    public static string ComputeSha256(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16, FileOptions.SequentialScan);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string? FindProblem(HistoryDocument document)
    {
        if (document.Messages is null)
        {
            return "\"messages\" is missing";
        }

        foreach (HistoryMessage? message in document.Messages)
        {
            if (message is null || string.IsNullOrEmpty(message.MessageRefId) || string.IsNullOrEmpty(message.Sha256))
            {
                return "a message has no messageRefId or sha256";
            }

            if (message.Records is null || message.Feedback is null || message.Records.Any(record => record is null || string.IsNullOrEmpty(record.DocRefId)))
            {
                return $"message {message.MessageRefId} has a record without a docRefId";
            }

            if (message.Feedback.Any(feedback => feedback is null || feedback.FileErrors is null || feedback.RecordErrors is null || string.IsNullOrEmpty(feedback.Sha256)))
            {
                return $"message {message.MessageRefId} has incomplete feedback";
            }
        }

        return null;
    }

    private void Index(HistoryMessage message)
    {
        _messagesByFile.TryAdd(message.Sha256, message);
        foreach (HistoryFeedback feedback in message.Feedback)
        {
            _feedbackFiles.TryAdd(feedback.Sha256, message);
        }

        Ledger ledger = LedgerFor(message.Test);
        ledger.Messages.TryAdd(message.MessageRefId, message);
        foreach (HistoryRecord record in message.Records)
        {
            ledger.Records.TryAdd(record.DocRefId, (message, record));
            if (record.CorrDocRefId is { } target && DocType.Parse(record.DocTypeIndic).Replaces)
            {
                if (!ledger.Replacements.TryGetValue(target, out List<(HistoryMessage, HistoryRecord)>? replacements))
                {
                    replacements = [];
                    ledger.Replacements.Add(target, replacements);
                }

                replacements.Add((message, record));
            }
        }
    }

    private Ledger LedgerFor(bool test) => test ? _test : _production;

    /// <summary>The indexes of one environment (production or test).</summary>
    private sealed class Ledger
    {
        public Dictionary<string, HistoryMessage> Messages { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, (HistoryMessage Message, HistoryRecord Record)> Records { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<(HistoryMessage, HistoryRecord)>> Replacements { get; } = new(StringComparer.Ordinal);
    }
}

/// <summary>The JSON document of a history file.</summary>
internal sealed class HistoryDocument
{
    public string? Format { get; set; }

    public int Version { get; set; }

    public List<HistoryMessage> Messages { get; set; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HistoryDocument))]
internal sealed partial class HistoryJsonContext : JsonSerializerContext
{
}
