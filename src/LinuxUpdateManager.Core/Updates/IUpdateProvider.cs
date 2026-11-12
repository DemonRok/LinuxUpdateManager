using LinuxUpdateManager.Core.Servers;

namespace LinuxUpdateManager.Core.Updates;

internal interface IUpdateProvider
{
    bool CanHandle(IReadOnlyDictionary<string, string> osRelease);

    Task<UpdateProviderResult> CheckAsync(
        SshConnectionSession session,
        ServerProfile server,
        CancellationToken cancellationToken);

    Task<SshCommandResult> UpgradeAsync(
        SshConnectionSession session,
        ServerProfile server,
        bool includePhasedUpdates,
        Action<string>? outputReceived,
        CancellationToken cancellationToken);

    Task<bool> CheckRebootRequiredAsync(
        SshConnectionSession session,
        ServerProfile server,
        CancellationToken cancellationToken);
}

internal sealed record UpdateProviderResult(
    IReadOnlyList<PackageUpdate> Packages,
    int? SecurityUpdateCount,
    bool RebootRequired);
