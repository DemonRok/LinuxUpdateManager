using System.Text.RegularExpressions;
using LinuxUpdateManager.Core.Security;
using Renci.SshNet;

namespace LinuxUpdateManager.Core.Servers;

public sealed class PrivilegeElevationService(CredentialService credentialService)
{
    public async Task<bool> VerifyRootAccessAsync(
        SshConnectionSession session,
        ServerProfile server,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsRootAsync(session, server, "id -u", cancellationToken);
        return result.ExitStatus == 0 && result.Output.Trim() == "0";
    }

    public async Task<SshCommandResult> ExecuteAsRootAsync(
        SshConnectionSession session,
        ServerProfile server,
        string command,
        CancellationToken cancellationToken = default,
        TimeSpan? commandTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        switch (server.PrivilegeElevationMethod)
        {
            case PrivilegeElevationMethod.DirectRoot:
                return await session.ExecuteAsync(command, cancellationToken: cancellationToken, commandTimeout: commandTimeout);

            case PrivilegeElevationMethod.Sudo:
            case PrivilegeElevationMethod.SudoInteractive:
            {
                var password = credentialService.Retrieve(server.Id, CredentialKind.SudoPassword);
                if (string.IsNullOrEmpty(password))
                {
                    throw new InvalidOperationException("The sudo password is not stored.");
                }

                var elevatedCommand = server.PrivilegeElevationMethod == PrivilegeElevationMethod.Sudo
                    ? $"sudo -S -p '' -- sh -c {QuoteShellArgument(command)}"
                    : $"sudo -S -p '' -i -- sh -c {QuoteShellArgument(command)}";
                return await session.ExecuteAsync(
                    elevatedCommand,
                    password + "\n",
                    cancellationToken,
                    commandTimeout);
            }

            case PrivilegeElevationMethod.Su:
            {
                var rootPassword = credentialService.Retrieve(server.Id, CredentialKind.RootPassword);
                if (string.IsNullOrEmpty(rootPassword))
                {
                    throw new InvalidOperationException("The root password is not stored.");
                }

                return await ExecuteWithSuAsync(session, command, rootPassword, cancellationToken, commandTimeout);
            }

            default:
                throw new InvalidDataException("The server has an unsupported privilege elevation method.");
        }
    }

    private static async Task<SshCommandResult> ExecuteWithSuAsync(
        SshConnectionSession session,
        string command,
        string rootPassword,
        CancellationToken cancellationToken,
        TimeSpan? commandTimeout)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timeout = commandTimeout ?? TimeSpan.FromSeconds(30);
        return await Task.Run(() =>
        {
            using var shell = session.CreateShellStream();
            shell.WriteLine($"su - -c {QuoteShellArgument(command)}; printf '\\nLUM_EXIT:%s\\n' \"$?\"");

            var exitPattern = new Regex("LUM_EXIT:(?<code>[0-9]+)", RegexOptions.CultureInvariant);
            var response = shell.Expect(
                new Regex("(?i)password\\s*:|LUM_EXIT:[0-9]+", RegexOptions.CultureInvariant),
                timeout);
            if (response is null)
            {
                throw new TimeoutException("The su command did not complete before the timeout.");
            }

            if (response.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                shell.WriteLine(rootPassword);
                var remainder = shell.Expect(exitPattern, timeout);
                if (remainder is null)
                {
                    throw new TimeoutException("The su command did not complete before the timeout.");
                }

                response += remainder;
            }

            var match = exitPattern.Match(response);
            if (!match.Success || !int.TryParse(match.Groups["code"].Value, out var exitStatus))
            {
                throw new InvalidOperationException("The su command did not return an exit status.");
            }

            var output = exitPattern.Replace(response, string.Empty).Trim();
            return new SshCommandResult(output, string.Empty, exitStatus);
        }, cancellationToken);
    }

    private static string QuoteShellArgument(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
}
