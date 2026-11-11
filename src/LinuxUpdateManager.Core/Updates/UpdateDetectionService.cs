using System.Text.RegularExpressions;
using LinuxUpdateManager.Core.Servers;

namespace LinuxUpdateManager.Core.Updates;

public sealed class UpdateDetectionService
{
    private static readonly Regex AptPackageLine = new(
        "^Inst\\s+(?<name>\\S+)(?:\\s+\\[(?<current>[^\\]]*)\\])?\\s+\\((?<candidate>\\S+)(?:\\s+(?<repository>[^)]*))?\\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex AptListPackageLine = new(
        "^(?<name>[^/\\s]+)/(?<repository>\\S+)\\s+(?<candidate>\\S+)\\s+\\S+\\s+\\[upgradable from:\\s+(?<current>[^\\]]+)\\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private readonly IReadOnlyList<IUpdateProvider> _providers;

    public UpdateDetectionService(PrivilegeElevationService privilegeElevationService)
    {
        ArgumentNullException.ThrowIfNull(privilegeElevationService);
        _providers =
        [
            new AptUpdateProvider(privilegeElevationService),
            new DnfUpdateProvider(privilegeElevationService)
        ];
    }

    public async Task<UpdateCheckResult> CheckAsync(
        SshConnectionSession session,
        ServerProfile server,
        CancellationToken cancellationToken = default)
    {
        var osReleaseResult = await session.ExecuteAsync("cat /etc/os-release", cancellationToken: cancellationToken);
        EnsureSuccess(osReleaseResult, "Could not read /etc/os-release.");
        var release = ParseOsRelease(osReleaseResult.Output);
        var provider = _providers.FirstOrDefault(candidate => candidate.CanHandle(release));
        if (provider is null)
        {
            var name = release.GetValueOrDefault("PRETTY_NAME", release.GetValueOrDefault("NAME", "Linux"));
            throw new NotSupportedException($"Update checks are not supported for {name} yet.");
        }

        var check = await provider.CheckAsync(session, server, cancellationToken);
        return new UpdateCheckResult(
            release.GetValueOrDefault("PRETTY_NAME", release.GetValueOrDefault("NAME", "Linux")),
            release.GetValueOrDefault("VERSION_ID", string.Empty),
            check.Packages,
            check.SecurityUpdateCount,
            check.RebootRequired,
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
        foreach (Match match in AptPackageLine.Matches(output))
        {
            var repository = match.Groups["repository"].Value.Trim();
            bool? isSecurity = repository.Length == 0
                ? null
                : repository.Contains("security", StringComparison.OrdinalIgnoreCase) ||
                  repository.Contains("debian-security", StringComparison.OrdinalIgnoreCase);
            packages.Add(new PackageUpdate(
                match.Groups["name"].Value,
                match.Groups["current"].Value.Trim(),
                match.Groups["candidate"].Value,
                isSecurity,
                repository.Length == 0 ? null : repository));
        }

        return packages;
    }

    public static IReadOnlyList<PackageUpdate> ParseAptListPackageUpdates(string output)
    {
        var packages = new List<PackageUpdate>();
        foreach (Match match in AptListPackageLine.Matches(output))
        {
            var repository = match.Groups["repository"].Value.Trim();
            var isSecurity = repository.Contains("security", StringComparison.OrdinalIgnoreCase);
            packages.Add(new PackageUpdate(
                match.Groups["name"].Value,
                match.Groups["current"].Value.Trim(),
                match.Groups["candidate"].Value,
                isSecurity,
                repository));
        }

        return packages;
    }

    internal static bool IsDnfDistribution(IReadOnlyDictionary<string, string> release)
    {
        var ids = release.GetValueOrDefault("ID", string.Empty) + " " +
                  release.GetValueOrDefault("ID_LIKE", string.Empty);
        return ids.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(id => id.Equals("fedora", StringComparison.OrdinalIgnoreCase) ||
                       id.Equals("rhel", StringComparison.OrdinalIgnoreCase) ||
                       id.Equals("centos", StringComparison.OrdinalIgnoreCase) ||
                       id.Equals("rocky", StringComparison.OrdinalIgnoreCase) ||
                       id.Equals("almalinux", StringComparison.OrdinalIgnoreCase) ||
                       id.Equals("ol", StringComparison.OrdinalIgnoreCase) ||
                       id.Equals("amzn", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsAptDistribution(IReadOnlyDictionary<string, string> release)
    {
        var ids = release.GetValueOrDefault("ID", string.Empty) + " " +
                  release.GetValueOrDefault("ID_LIKE", string.Empty);
        return ids.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(id => id.Equals("debian", StringComparison.OrdinalIgnoreCase) ||
                       id.Equals("ubuntu", StringComparison.OrdinalIgnoreCase));
    }

    internal static void EnsureSuccess(SshCommandResult result, string message)
    {
        if (result.ExitStatus != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? message
                : $"{message}{Environment.NewLine}{detail.Trim()}");
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
}
