namespace LinuxUpdateManager.Core.Updates;

public sealed record PackageUpdate(
    string Name,
    string CurrentVersion,
    string CandidateVersion,
    bool? IsSecurityUpdate,
    string? Repository);

public sealed record UpdateCheckResult(
    string Distribution,
    string Version,
    IReadOnlyList<PackageUpdate> Packages,
    int? SecurityUpdateCount,
    bool RebootRequired,
    DateTimeOffset CheckedAt);
