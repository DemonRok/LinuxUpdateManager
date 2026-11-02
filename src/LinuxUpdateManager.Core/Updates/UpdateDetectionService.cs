using System.Text.RegularExpressions;
using LinuxUpdateManager.Core.Servers;

namespace LinuxUpdateManager.Core.Updates;

public sealed class UpdateDetectionService(PrivilegeElevationService privilegeElevationService)
{
    private static readonly TimeSpan AptTimeout = TimeSpan.FromMinutes(5);
    private static readonly Regex PackageLine = new(
        "^Inst\\s+(?<name>\\S+)(?:\\s+\\[(?<current>[^\\]]*)\\])?\\s+\\((?<candidate>\\S+)(?:\\s+(?<repository>[^)]*))?\\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    public async Task<UpdateCheckResult> CheckAsync(
        SshConnectionSession session,
        ServerProfile server,
        CancellationToken cancellationToken = default)
    {
        var osReleaseResult = await session.ExecuteAsync("cat /etc/os-release", cancellationToken: cancellationToken);
        EnsureSuccess(osReleaseResult, "Could not read /etc/os-release.");
        var release = ParseOsRelease(osReleaseResult.Output);
        EnsureSupportedDistribution(release);

        var refreshResult = await privilegeElevationService.ExecuteAsRootAsync(
            session, server, "LC_ALL=C apt-get update -qq", cancellationToken, AptTimeout);
        EnsureSuccess(refreshResult, "APT package indexes could not be refreshed.");

        var simulation = await session.ExecuteAsync(
            "LC_ALL=C apt-get -s -o Debug::NoLocking=1 upgrade",
            cancellationToken: cancellationToken,
            commandTimeout: AptTimeout);
        EnsureSuccess(simulation, "APT could not determine available package updates.");
        var packages = ParsePackageUpdates(simulation.Output);

        var rebootCheck = await session.ExecuteAsync(
            "if test -f /var/run/reboot-required; then printf yes; else printf no; fi",
            cancellationToken: cancellationToken);
        EnsureSuccess(rebootCheck, "Could not determine whether the server requires a reboot.");
        var rebootRequired = rebootCheck.Output.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
        int? securityCount = packages.Count == 0
            ? 0
            : packages.All(package => package.IsSecurityUpdate.HasValue)
                ? packages.Count(package => package.IsSecurityUpdate == true)
                : null;

        return new UpdateCheckResult(
            release.GetValueOrDefault("PRETTY_NAME", release.GetValueOrDefault("NAME", "Linux")),
            release.GetValueOrDefault("VERSION_ID", string.Empty),
            packages,
            securityCount,
            rebootRequired,
            DateTimeOffset.Now);
    }

    public static IReadOnlyDictionary<string, string> ParseOsRelease(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            if (!key.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
            {
                continue;
            }

            values[key] = Unquote(line[(separator + 1)..].Trim());
        }

        return values;
    }

    public static IReadOnlyList<PackageUpdate> ParsePackageUpdates(string output)
    {
        var packages = new List<PackageUpdate>();
        foreach (Match match in PackageLine.Matches(output))
        {
            var repository = match.Groups["repository"].Success
                ? match.Groups["repository"].Value.Trim()
                : string.Empty;
            bool? isSecurity = repository.Length == 0
                ? null
                : repository.Contains("security", StringComparison.OrdinalIgnoreCase) ||
                  repository.Contains("debian-security", StringComparison.OrdinalIgnoreCase);
            packages.Add(new PackageUpdate(
                match.Groups["name"].Value,
                match.Groups["current"].Success ? match.Groups["current"].Value : string.Empty,
                match.Groups["candidate"].Value,
                isSecurity,
                repository.Length == 0 ? null : repository));
        }

        return packages;
    }

    private static void EnsureSupportedDistribution(IReadOnlyDictionary<string, string> release)
    {
        var ids = release.GetValueOrDefault("ID", string.Empty) + " " +
                  release.GetValueOrDefault("ID_LIKE", string.Empty);
        if (!ids.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Any(id => id.Equals("debian", StringComparison.OrdinalIgnoreCase) ||
                           id.Equals("ubuntu", StringComparison.OrdinalIgnoreCase)))
        {
            throw new NotSupportedException("Only Debian and Ubuntu Linux servers are supported for APT update checks.");
        }
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == value[^1] && value[0] is '"' or '\'')
        {
            value = value[1..^1];
        }

        return value.Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal)
            .Replace("\\$", "$", StringComparison.Ordinal)
            .Replace("\\`", "`", StringComparison.Ordinal);
    }

    private static void EnsureSuccess(SshCommandResult result, string message)
    {
        if (result.ExitStatus != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? message
                : $"{message}{Environment.NewLine}{detail.Trim()}");
        }
    }
}
