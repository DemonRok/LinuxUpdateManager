using LinuxUpdateManager.Core.Servers;

namespace LinuxUpdateManager.Core.Updates;

public sealed class UpdateExecutionService(PrivilegeElevationService privilegeElevationService)
{
    private readonly IReadOnlyList<IUpdateProvider> _providers =
    [
        new AptUpdateProvider(privilegeElevationService),
        new DnfUpdateProvider(privilegeElevationService)
    ];

    public async Task<SshCommandResult> UpgradeAsync(
        SshConnectionSession session,
        ServerProfile server,
        Action<string>? outputReceived = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(server);

        var osReleaseResult = await session.ExecuteAsync(
            "cat /etc/os-release",
            cancellationToken: cancellationToken);
        UpdateDetectionService.EnsureSuccess(osReleaseResult, "Could not read /etc/os-release.");

        var release = UpdateDetectionService.ParseOsRelease(osReleaseResult.Output);
        var provider = _providers.FirstOrDefault(candidate => candidate.CanHandle(release));
        if (provider is null)
        {
            var name = release.GetValueOrDefault("PRETTY_NAME", release.GetValueOrDefault("NAME", "Linux"));
            throw new NotSupportedException($"Package updates are not supported for {name} yet.");
        }

        var result = await provider.UpgradeAsync(session, server, outputReceived, cancellationToken);
        if (result.ExitStatus != 0)
        {
            throw new PackageUpgradeFailedException(result.ExitStatus, result.Output, result.Error);
        }

        return result;
    }
}

public sealed class PackageUpgradeFailedException(int exitStatus, string output, string error)
    : InvalidOperationException($"Package upgrade failed with exit status {exitStatus}.")
{
    public int ExitStatus { get; } = exitStatus;

    public string Output { get; } = output;

    public string Error { get; } = error;
}
