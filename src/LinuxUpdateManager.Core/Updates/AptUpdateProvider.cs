using LinuxUpdateManager.Core.Servers;

namespace LinuxUpdateManager.Core.Updates;

internal sealed class AptUpdateProvider(PrivilegeElevationService privilegeElevationService) : IUpdateProvider
{
    private static readonly TimeSpan AptTimeout = TimeSpan.FromMinutes(5);

    public bool CanHandle(IReadOnlyDictionary<string, string> osRelease) =>
        UpdateDetectionService.IsAptDistribution(osRelease);

    public async Task<UpdateProviderResult> CheckAsync(
        SshConnectionSession session,
        ServerProfile server,
        CancellationToken cancellationToken)
    {
        var refreshResult = await privilegeElevationService.ExecuteAsRootAsync(
            session, server, "LC_ALL=C apt-get update -qq", cancellationToken, AptTimeout);
        UpdateDetectionService.EnsureSuccess(refreshResult, "APT package indexes could not be refreshed.");

        var simulation = await session.ExecuteAsync(
            "LC_ALL=C apt-get -s -o Debug::NoLocking=1 upgrade",
            cancellationToken: cancellationToken,
            commandTimeout: AptTimeout);
        UpdateDetectionService.EnsureSuccess(simulation, "APT could not determine available package updates.");
        var packages = UpdateDetectionService.ParsePackageUpdates(simulation.Output);

        var rebootCheck = await session.ExecuteAsync(
            "if test -f /var/run/reboot-required; then printf yes; else printf no; fi",
            cancellationToken: cancellationToken);
        UpdateDetectionService.EnsureSuccess(rebootCheck, "Could not determine whether the server requires a reboot.");
        var rebootRequired = rebootCheck.Output.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
        int? securityCount = packages.Count == 0
            ? 0
            : packages.All(package => package.IsSecurityUpdate.HasValue)
                ? packages.Count(package => package.IsSecurityUpdate == true)
                : null;

        return new UpdateProviderResult(packages, securityCount, rebootRequired);
    }
}
