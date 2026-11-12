using LinuxUpdateManager.Core.Servers;

namespace LinuxUpdateManager.Core.Updates;

public sealed class UpdateExecutionService(PrivilegeElevationService privilegeElevationService)
{
    private static readonly TimeSpan AutoremoveTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RebootTimeout = TimeSpan.FromSeconds(30);
    private readonly IReadOnlyList<IUpdateProvider> _providers =
    [
        new AptUpdateProvider(privilegeElevationService),
        new DnfUpdateProvider(privilegeElevationService)
    ];

    public async Task<SshCommandResult> UpgradeAsync(
        SshConnectionSession session,
        ServerProfile server,
        Action<string>? outputReceived = null,
        CancellationToken cancellationToken = default,
        bool includePhasedUpdates = false)
    {
        var provider = await GetProviderAsync(session, cancellationToken);
        var result = await provider.UpgradeAsync(session, server, includePhasedUpdates, outputReceived, cancellationToken);
        if (result.ExitStatus != 0)
        {
            throw new PackageUpgradeFailedException(result.ExitStatus, result.Output, result.Error);
        }

        return result;
    }

    public async Task<bool> CheckRebootRequiredAsync(
        SshConnectionSession session,
        ServerProfile server,
        CancellationToken cancellationToken = default)
    {
        var provider = await GetProviderAsync(session, cancellationToken);
        return await provider.CheckRebootRequiredAsync(session, server, cancellationToken);
    }

    public IReadOnlyList<string> ParsePhasedUpdatePackages(SshCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return AptUpdateProvider.ParsePhasedUpdatePackages(result.Output + Environment.NewLine + result.Error);
    }

    public async Task<IReadOnlyList<string>> PreviewAutoremoveAsync(
        SshConnectionSession session,
        ServerProfile server,
        CancellationToken cancellationToken = default)
    {
        var provider = await GetProviderAsync(session, cancellationToken);
        if (provider is not AptUpdateProvider)
        {
            return [];
        }

        var result = await privilegeElevationService.ExecuteAsRootAsync(
            session,
            server,
            "DEBIAN_FRONTEND=noninteractive LC_ALL=C apt-get -s autoremove",
            cancellationToken,
            AutoremoveTimeout);
        UpdateDetectionService.EnsureSuccess(result, "APT could not determine which packages can be removed.");
        return AptUpdateProvider.ParseAutoremoveCandidates(result.Output);
    }

    public async Task<SshCommandResult> AutoremoveAsync(
        SshConnectionSession session,
        ServerProfile server,
        Action<string>? outputReceived = null,
        CancellationToken cancellationToken = default)
    {
        var provider = await GetProviderAsync(session, cancellationToken);
        if (provider is not AptUpdateProvider)
        {
            throw new NotSupportedException("Automatic dependency cleanup is currently supported for APT servers only.");
        }

        var result = await privilegeElevationService.ExecuteAsRootAsync(
            session,
            server,
            "DEBIAN_FRONTEND=noninteractive LC_ALL=C apt-get -y autoremove",
            cancellationToken,
            AutoremoveTimeout,
            outputReceived);
        if (result.ExitStatus != 0)
        {
            throw new PackageUpgradeFailedException(result.ExitStatus, result.Output, result.Error, "APT autoremove");
        }

        return result;
    }

    public async Task<SshCommandResult> RebootAsync(
        SshConnectionSession session,
        ServerProfile server,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(server);
        var result = await privilegeElevationService.ExecuteAsRootAsync(
            session,
            server,
            "shutdown -r +1 \"Requested by Linux Update Manager\"",
            cancellationToken,
            RebootTimeout);
        if (result.ExitStatus != 0)
        {
            throw new InvalidOperationException($"The server could not schedule a reboot (exit status {result.ExitStatus}).");
        }

        return result;
    }

    private async Task<IUpdateProvider> GetProviderAsync(
        SshConnectionSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var osReleaseResult = await session.ExecuteAsync(
            "cat /etc/os-release",
            cancellationToken: cancellationToken);
        UpdateDetectionService.EnsureSuccess(osReleaseResult, "Could not read /etc/os-release.");

        var release = UpdateDetectionService.ParseOsRelease(osReleaseResult.Output);
        var provider = _providers.FirstOrDefault(candidate => candidate.CanHandle(release));
        if (provider is not null)
        {
            return provider;
        }

        var name = release.GetValueOrDefault("PRETTY_NAME", release.GetValueOrDefault("NAME", "Linux"));
        throw new NotSupportedException($"Package updates are not supported for {name} yet.");
    }
}

public sealed class PackageUpgradeFailedException(
    int exitStatus,
    string output,
    string error,
    string operation = "Package upgrade")
    : InvalidOperationException($"{operation} failed with exit status {exitStatus}.")
{
    public int ExitStatus { get; } = exitStatus;

    public string Output { get; } = output;

    public string Error { get; } = error;
}
