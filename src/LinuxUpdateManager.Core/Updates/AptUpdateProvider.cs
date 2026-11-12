using System.Text.RegularExpressions;

using LinuxUpdateManager.Core.Servers;

namespace LinuxUpdateManager.Core.Updates;

internal sealed class AptUpdateProvider(PrivilegeElevationService privilegeElevationService) : IUpdateProvider
{
    private static readonly TimeSpan AptTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan UpgradeTimeout = TimeSpan.FromHours(2);
    private static readonly Regex AutoremovePackageLine = new(
        "^Remv\\s+(?<name>\\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex PhasedUpdateHeader = new(
        "^The following upgrades have been deferred due to phasing:\\s*\\r?\\n(?<packages>(?:[ \\t]+[^\\r\\n]+\\r?\\n?)+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

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

        var availableUpdates = await session.ExecuteAsync(
            "LC_ALL=C apt list --upgradable 2>/dev/null",
            cancellationToken: cancellationToken,
            commandTimeout: AptTimeout);
        UpdateDetectionService.EnsureSuccess(availableUpdates, "APT could not determine available package updates.");
        var heldPackagesResult = await session.ExecuteAsync(
            "LC_ALL=C apt-mark showhold 2>/dev/null",
            cancellationToken: cancellationToken,
            commandTimeout: AptTimeout);
        var heldPackages = heldPackagesResult.ExitStatus == 0
            ? heldPackagesResult.Output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(StripAptArchitectureSuffix)
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var packages = UpdateDetectionService.ParseAptListPackageUpdates(availableUpdates.Output)
            .Where(package => !heldPackages.Contains(StripAptArchitectureSuffix(package.Name)))
            .ToArray();

        var rebootRequired = await CheckRebootRequiredAsync(session, server, cancellationToken);
        int? securityCount = packages.Length == 0
            ? 0
            : packages.All(package => package.IsSecurityUpdate.HasValue)
                ? packages.Count(package => package.IsSecurityUpdate == true)
                : null;

        return new UpdateProviderResult(packages, securityCount, rebootRequired);
    }

    private static string StripAptArchitectureSuffix(string packageName)
    {
        var separator = packageName.LastIndexOf(':');
        if (separator < 0 || separator == packageName.Length - 1)
        {
            return packageName;
        }

        var architecture = packageName[(separator + 1)..];
        return architecture.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
            ? packageName[..separator]
            : packageName;
    }

    public Task<SshCommandResult> UpgradeAsync(
        SshConnectionSession session,
        ServerProfile server,
        bool includePhasedUpdates,
        Action<string>? outputReceived,
        CancellationToken cancellationToken)
    {
        return privilegeElevationService.ExecuteAsRootAsync(
            session,
            server,
            includePhasedUpdates ? PackageUpgradeCommands.AptIncludingPhasedUpdates : PackageUpgradeCommands.Apt,
            cancellationToken,
            UpgradeTimeout,
            outputReceived);
    }

    public async Task<bool> CheckRebootRequiredAsync(
        SshConnectionSession session,
        ServerProfile server,
        CancellationToken cancellationToken)
    {
        var rebootCheck = await session.ExecuteAsync(
            "if test -f /var/run/reboot-required; then printf yes; else printf no; fi",
            cancellationToken: cancellationToken);
        UpdateDetectionService.EnsureSuccess(rebootCheck, "Could not determine whether the server requires a reboot.");
        return rebootCheck.Output.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> ParseAutoremoveCandidates(string output) =>
        AutoremovePackageLine.Matches(output)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> ParsePhasedUpdatePackages(string output)
    {
        var match = PhasedUpdateHeader.Match(output);
        return match.Success
            ? match.Groups["packages"].Value
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : [];
    }
}
