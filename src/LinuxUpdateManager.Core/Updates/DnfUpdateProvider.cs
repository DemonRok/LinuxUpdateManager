using System.Text.RegularExpressions;
using LinuxUpdateManager.Core.Servers;

namespace LinuxUpdateManager.Core.Updates;

internal sealed class DnfUpdateProvider(PrivilegeElevationService privilegeElevationService) : IUpdateProvider
{
    private static readonly TimeSpan DnfTimeout = TimeSpan.FromMinutes(5);
    private static readonly Regex PackageLine = new(
        "^(?<nevra>\\S+)\\s+(?<candidate>\\S+)\\s+(?<repository>\\S+)\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    public bool CanHandle(IReadOnlyDictionary<string, string> osRelease) =>
        UpdateDetectionService.IsDnfDistribution(osRelease);

    public async Task<UpdateProviderResult> CheckAsync(
        SshConnectionSession session,
        ServerProfile server,
        CancellationToken cancellationToken)
    {
        var refreshResult = await privilegeElevationService.ExecuteAsRootAsync(
            session, server, "LC_ALL=C dnf -q makecache --refresh", cancellationToken, DnfTimeout);
        UpdateDetectionService.EnsureSuccess(refreshResult, "DNF package metadata could not be refreshed.");

        var check = await session.ExecuteAsync(
            "LC_ALL=C dnf -q check-update",
            cancellationToken: cancellationToken,
            commandTimeout: DnfTimeout);
        if (check.ExitStatus is not 0 and not 100)
        {
            UpdateDetectionService.EnsureSuccess(check, "DNF could not determine available package updates.");
        }

        var packages = ParsePackageUpdates(check.Output);
        var rebootCheck = await session.ExecuteAsync(
            "if command -v needs-restarting >/dev/null 2>&1; then needs-restarting -r >/dev/null 2>&1; result=$?; if test $result -eq 1; then printf yes; else printf no; fi; elif test -f /var/run/reboot-required; then printf yes; else printf no; fi",
            cancellationToken: cancellationToken);
        UpdateDetectionService.EnsureSuccess(rebootCheck, "Could not determine whether the server requires a reboot.");
        var rebootRequired = rebootCheck.Output.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
        int? securityCount = packages.Count == 0 ? 0 : null;
        return new UpdateProviderResult(packages, securityCount, rebootRequired);
    }

    public static IReadOnlyList<PackageUpdate> ParsePackageUpdates(string output)
    {
        var packages = new List<PackageUpdate>();
        foreach (Match match in PackageLine.Matches(output))
        {
            var nevra = match.Groups["nevra"].Value;
            var separator = nevra.LastIndexOf('.');
            var name = separator > 0 ? nevra[..separator] : nevra;
            packages.Add(new PackageUpdate(
                name,
                string.Empty,
                match.Groups["candidate"].Value,
                null,
                match.Groups["repository"].Value));
        }

        return packages;
    }
}
