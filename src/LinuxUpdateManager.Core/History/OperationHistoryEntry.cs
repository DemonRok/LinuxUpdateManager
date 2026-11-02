namespace LinuxUpdateManager.Core.History;

public sealed record OperationHistoryEntry(
    long Id,
    Guid ServerId,
    string ServerName,
    string ServerHost,
    string OperationType,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    string Result,
    int? ExitStatus,
    string SanitizedOutput,
    string? ErrorSummary,
    int? PackageCount,
    int? SecurityUpdateCount);

public sealed record OperationHistoryRecord(
    Guid ServerId,
    string ServerName,
    string ServerHost,
    string OperationType,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    string Result,
    int? ExitStatus = null,
    string SanitizedOutput = "",
    string? ErrorSummary = null,
    int? PackageCount = null,
    int? SecurityUpdateCount = null);

public sealed record OperationHistoryFilter(
    string SearchText = "",
    string OperationType = "",
    string Result = "",
    DateTimeOffset? StartedAfterUtc = null,
    DateTimeOffset? StartedBeforeUtc = null,
    int MaximumEntries = 2000);
