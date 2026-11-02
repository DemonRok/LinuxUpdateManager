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

        if (server.PrivilegeElevationMethod == PrivilegeElevationMethod.Auto)
        {
            return await ExecuteAutomaticallyAsync(session, server, command, cancellationToken, commandTimeout);
        }

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

    private async Task<SshCommandResult> ExecuteAutomaticallyAsync(
        SshConnectionSession session,
        ServerProfile server,
        string command,
        CancellationToken cancellationToken,
        TimeSpan? commandTimeout)
    {
        var identity = await session.ExecuteAsync("id -u", cancellationToken: cancellationToken);
        if (identity.ExitStatus != 0)
        {
            throw new InvalidOperationException("The remote user identity could not be determined.");
        }

        if (identity.Output.Trim() == "0")
        {
            return await session.ExecuteAsync(command, cancellationToken: cancellationToken, commandTimeout: commandTimeout);
        }

        var hasSudo = await HasCommandAsync(session, "sudo", cancellationToken);
        if (hasSudo)
        {
            var passwordless = await session.ExecuteAsync("sudo -n true", cancellationToken: cancellationToken);
            if (passwordless.ExitStatus == 0)
            {
                return await session.ExecuteAsync(
                    $"sudo -n -- sh -c {QuoteShellArgument(command)}",
                    cancellationToken: cancellationToken,
                    commandTimeout: commandTimeout);
            }

            var sudoPassword = credentialService.Retrieve(server.Id, CredentialKind.SudoPassword);
            if (!string.IsNullOrEmpty(sudoPassword))
            {
                return await ExecuteSudoWithPasswordAsync(
                    session, command, sudoPassword, interactive: false, cancellationToken, commandTimeout);
            }
        }

        var hasDoas = await HasCommandAsync(session, "doas", cancellationToken);
        if (hasDoas)
        {
            var passwordless = await session.ExecuteAsync("doas -n true", cancellationToken: cancellationToken);
            if (passwordless.ExitStatus == 0)
            {
                return await session.ExecuteAsync(
                    $"doas -n sh -c {QuoteShellArgument(command)}",
                    cancellationToken: cancellationToken,
                    commandTimeout: commandTimeout);
            }

            var doasPassword = credentialService.Retrieve(server.Id, CredentialKind.DoasPassword);
            if (!string.IsNullOrEmpty(doasPassword))
            {
                return await ExecuteWithDoasAsync(session, command, doasPassword, cancellationToken, commandTimeout);
            }
        }

        var hasSu = await HasCommandAsync(session, "su", cancellationToken);
        if (hasSu)
        {
            var rootPassword = credentialService.Retrieve(server.Id, CredentialKind.RootPassword);
            if (!string.IsNullOrEmpty(rootPassword))
            {
                return await ExecuteWithSuAsync(session, command, rootPassword, cancellationToken, commandTimeout);
            }
        }

        if (hasSudo)
        {
            throw new InvalidOperationException("sudo is available, but it requires a saved sudo password or passwordless sudo access.");
        }

        if (hasDoas)
        {
            throw new InvalidOperationException("doas requires passwordless access or a saved doas password.");
        }

        if (hasSu)
        {
            throw new InvalidOperationException("su is available, but no root password is stored.");
        }

        throw new NotSupportedException("No supported privilege elevation command (sudo, doas, or su) is available.");
    }

    private static async Task<bool> HasCommandAsync(
        SshConnectionSession session,
        string command,
        CancellationToken cancellationToken)
    {
        var result = await session.ExecuteAsync(
            $"command -v {command} >/dev/null 2>&1",
            cancellationToken: cancellationToken);
        return result.ExitStatus == 0;
    }

    private async Task<SshCommandResult> ExecuteSudoWithPasswordAsync(
        SshConnectionSession session,
        string command,
        string password,
        bool interactive,
        CancellationToken cancellationToken,
        TimeSpan? commandTimeout)
    {
        var elevatedCommand = interactive
            ? $"sudo -S -p '' -i -- sh -c {QuoteShellArgument(command)}"
            : $"sudo -S -p '' -- sh -c {QuoteShellArgument(command)}";
        return await session.ExecuteAsync(
            elevatedCommand,
            password + "\n",
            cancellationToken,
            commandTimeout);
    }

    private static async Task<SshCommandResult> ExecuteWithDoasAsync(
        SshConnectionSession session,
        string command,
        string password,
        CancellationToken cancellationToken,
        TimeSpan? commandTimeout)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timeout = commandTimeout ?? TimeSpan.FromSeconds(30);
        return await Task.Run(() =>
        {
            using var shell = session.CreateShellStream();
            shell.WriteLine($"doas -u root sh -c {QuoteShellArgument(command)}; printf '\\nLUM_EXIT:%s\\n' \"$?\"");

            var exitPattern = new Regex("LUM_EXIT:(?<code>[0-9]+)", RegexOptions.CultureInvariant);
            var response = shell.Expect(
                new Regex("(?i)password\\s*:|LUM_EXIT:[0-9]+", RegexOptions.CultureInvariant),
                timeout);
            if (response is null)
            {
                throw new TimeoutException("The doas command did not complete before the timeout.");
            }

            if (response.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                shell.WriteLine(password);
                var remainder = shell.Expect(exitPattern, timeout);
                if (remainder is null)
                {
                    throw new TimeoutException("The doas command did not complete before the timeout.");
                }

                response += remainder;
            }

            var match = exitPattern.Match(response);
            if (!match.Success || !int.TryParse(match.Groups["code"].Value, out var exitStatus))
            {
                throw new InvalidOperationException("The doas command did not return an exit status.");
            }

            var output = exitPattern.Replace(response, string.Empty).Trim();
            return new SshCommandResult(output, string.Empty, exitStatus);
        }, cancellationToken);
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
