using System.Text.RegularExpressions;
using System.Text;
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
        TimeSpan? commandTimeout = null,
        Action<string>? outputReceived = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        if (server.PrivilegeElevationMethod == PrivilegeElevationMethod.Auto)
        {
            return await ExecuteAutomaticallyAsync(session, server, command, cancellationToken, commandTimeout, outputReceived);
        }

        switch (server.PrivilegeElevationMethod)
        {
            case PrivilegeElevationMethod.DirectRoot:
                return await ExecuteCommandAsync(session, command, null, cancellationToken, commandTimeout, outputReceived);

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
                return await ExecuteCommandAsync(
                    session,
                    elevatedCommand,
                    password + "\n",
                    cancellationToken,
                    commandTimeout,
                    outputReceived);
            }

            case PrivilegeElevationMethod.Su:
            {
                var rootPassword = credentialService.Retrieve(server.Id, CredentialKind.RootPassword);
                if (string.IsNullOrEmpty(rootPassword))
                {
                    throw new InvalidOperationException("The root password is not stored.");
                }

                return await ExecuteWithSuAsync(session, command, rootPassword, cancellationToken, commandTimeout, outputReceived);
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
        TimeSpan? commandTimeout,
        Action<string>? outputReceived)
    {
        var identity = await session.ExecuteAsync("id -u", cancellationToken: cancellationToken);
        if (identity.ExitStatus != 0)
        {
            throw new InvalidOperationException("The remote user identity could not be determined.");
        }

        if (identity.Output.Trim() == "0")
        {
            return await ExecuteCommandAsync(session, command, null, cancellationToken, commandTimeout, outputReceived);
        }

        var hasSudo = await HasCommandAsync(session, "sudo", cancellationToken);
        if (hasSudo)
        {
            var passwordless = await session.ExecuteAsync("sudo -n true", cancellationToken: cancellationToken);
            if (passwordless.ExitStatus == 0)
            {
                return await ExecuteCommandAsync(
                    session,
                    $"sudo -n -- sh -c {QuoteShellArgument(command)}",
                    null,
                    cancellationToken,
                    commandTimeout,
                    outputReceived);
            }

            var sudoPassword = credentialService.Retrieve(server.Id, CredentialKind.SudoPassword);
            if (!string.IsNullOrEmpty(sudoPassword))
            {
                return await ExecuteSudoWithPasswordAsync(
                    session, command, sudoPassword, interactive: false, cancellationToken, commandTimeout, outputReceived);
            }
        }

        var hasDoas = await HasCommandAsync(session, "doas", cancellationToken);
        if (hasDoas)
        {
            var passwordless = await session.ExecuteAsync("doas -n true", cancellationToken: cancellationToken);
            if (passwordless.ExitStatus == 0)
            {
                return await ExecuteCommandAsync(
                    session,
                    $"doas -n sh -c {QuoteShellArgument(command)}",
                    null,
                    cancellationToken,
                    commandTimeout,
                    outputReceived);
            }

            var doasPassword = credentialService.Retrieve(server.Id, CredentialKind.DoasPassword);
            if (!string.IsNullOrEmpty(doasPassword))
            {
                return await ExecuteWithDoasAsync(
                    session, command, doasPassword, cancellationToken, commandTimeout, outputReceived);
            }
        }

        var hasSu = await HasCommandAsync(session, "su", cancellationToken);
        if (hasSu)
        {
            var rootPassword = credentialService.Retrieve(server.Id, CredentialKind.RootPassword);
            if (!string.IsNullOrEmpty(rootPassword))
            {
                return await ExecuteWithSuAsync(session, command, rootPassword, cancellationToken, commandTimeout, outputReceived);
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
        TimeSpan? commandTimeout,
        Action<string>? outputReceived)
    {
        var elevatedCommand = interactive
            ? $"sudo -S -p '' -i -- sh -c {QuoteShellArgument(command)}"
            : $"sudo -S -p '' -- sh -c {QuoteShellArgument(command)}";
        return await ExecuteCommandAsync(
            session,
            elevatedCommand,
            password + "\n",
            cancellationToken,
            commandTimeout,
            outputReceived);
    }

    private static async Task<SshCommandResult> ExecuteWithDoasAsync(
        SshConnectionSession session,
        string command,
        string password,
        CancellationToken cancellationToken,
        TimeSpan? commandTimeout,
        Action<string>? outputReceived)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timeout = commandTimeout ?? TimeSpan.FromSeconds(30);
        return await Task.Run(() =>
        {
            using var shell = session.CreateShellStream();
            var redactor = new ShellOutputRedactor(password);
            EventHandler<Renci.SshNet.Common.ShellDataEventArgs> dataReceived = (_, args) =>
                PublishOutput(outputReceived, redactor.Push(args.Data));
            shell.DataReceived += dataReceived;
            using var cancellationRegistration = cancellationToken.Register(
                static state => ((ShellStream)state!).Dispose(), shell);

            try
            {
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

            PublishOutput(outputReceived, redactor.Push([], flush: true));
            var output = exitPattern.Replace(response, string.Empty).Replace(password, "[REDACTED]", StringComparison.Ordinal).Trim();
            return new SshCommandResult(output, string.Empty, exitStatus);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            finally
            {
                shell.DataReceived -= dataReceived;
            }
        }, CancellationToken.None);
    }

    private static async Task<SshCommandResult> ExecuteWithSuAsync(
        SshConnectionSession session,
        string command,
        string rootPassword,
        CancellationToken cancellationToken,
        TimeSpan? commandTimeout,
        Action<string>? outputReceived)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timeout = commandTimeout ?? TimeSpan.FromSeconds(30);
        return await Task.Run(() =>
        {
            using var shell = session.CreateShellStream();
            var redactor = new ShellOutputRedactor(rootPassword);
            EventHandler<Renci.SshNet.Common.ShellDataEventArgs> dataReceived = (_, args) =>
                PublishOutput(outputReceived, redactor.Push(args.Data));
            shell.DataReceived += dataReceived;
            using var cancellationRegistration = cancellationToken.Register(
                static state => ((ShellStream)state!).Dispose(), shell);

            try
            {
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

            PublishOutput(outputReceived, redactor.Push([], flush: true));
            var output = exitPattern.Replace(response, string.Empty).Replace(rootPassword, "[REDACTED]", StringComparison.Ordinal).Trim();
            return new SshCommandResult(output, string.Empty, exitStatus);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            finally
            {
                shell.DataReceived -= dataReceived;
            }
        }, CancellationToken.None);
    }

    private static Task<SshCommandResult> ExecuteCommandAsync(
        SshConnectionSession session,
        string command,
        string? standardInput,
        CancellationToken cancellationToken,
        TimeSpan? commandTimeout,
        Action<string>? outputReceived) =>
        outputReceived is null
            ? session.ExecuteAsync(command, standardInput, cancellationToken, commandTimeout)
            : session.ExecuteStreamingAsync(command, standardInput, outputReceived, cancellationToken, commandTimeout);

    private static void PublishOutput(Action<string>? outputReceived, string output)
    {
        if (output.Length == 0)
        {
            return;
        }

        try
        {
            outputReceived?.Invoke(output);
        }
        catch (Exception)
        {
            // A UI callback must not interrupt a remote package operation.
        }
    }

    private sealed class ShellOutputRedactor(string secret)
    {
        private readonly object _gate = new();
        private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
        private readonly StringBuilder _pending = new();

        public string Push(byte[] bytes, bool flush = false)
        {
            lock (_gate)
            {
                var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
                var count = _decoder.GetChars(bytes, 0, bytes.Length, chars, 0, flush);
                _pending.Append(chars, 0, count);

                var raw = _pending.ToString();
                var safeLength = raw.Length;
                if (!flush && secret.Length > 1)
                {
                    var maximumPartialLength = Math.Min(secret.Length - 1, raw.Length);
                    for (var partialLength = maximumPartialLength; partialLength > 0; partialLength--)
                    {
                        if (raw.EndsWith(secret[..partialLength], StringComparison.Ordinal))
                        {
                            safeLength -= partialLength;
                            break;
                        }
                    }
                }

                var output = raw[..safeLength].Replace(secret, "[REDACTED]", StringComparison.Ordinal);
                _pending.Clear();
                _pending.Append(raw[safeLength..]);
                if (flush)
                {
                    output += _pending.ToString().Replace(secret, "[REDACTED]", StringComparison.Ordinal);
                    _pending.Clear();
                }

                return output;
            }
        }
    }

    private static string QuoteShellArgument(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
}
