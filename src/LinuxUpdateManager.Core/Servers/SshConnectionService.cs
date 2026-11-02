using LinuxUpdateManager.Core.Security;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Security;
using System.Text;

namespace LinuxUpdateManager.Core.Servers;

public sealed record HostKeyChallenge(string Host, int Port, string KeyType, string Sha256Fingerprint);

public sealed class HostKeyMismatchException(string host, string expected, string received)
    : SshException($"The SSH host key for {host} does not match the trusted fingerprint.")
{
    public string Host { get; } = host;
    public string ExpectedFingerprint { get; } = expected;
    public string ReceivedFingerprint { get; } = received;
}

public sealed class SshConnectionService(CredentialService credentialService)
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(15);

    public async Task<SshConnectionSession> ConnectAsync(
        ServerProfile server,
        Func<HostKeyChallenge, bool> confirmUnknownHostKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(confirmUnknownHostKey);
        ServerInventoryService.ValidateServer(server);

        PrivateKeyFile? privateKey = null;
        try
        {
            AuthenticationMethod authentication;
            if (server.AuthenticationMethod == SshAuthenticationMethod.Password)
            {
                var password = credentialService.Retrieve(server.Id, CredentialKind.SshPassword);
                if (string.IsNullOrEmpty(password))
                {
                    throw new InvalidOperationException("The SSH password is not stored.");
                }

                authentication = new PasswordAuthenticationMethod(server.Username, password);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(server.PrivateKeyPath) || !File.Exists(server.PrivateKeyPath))
                {
                    throw new InvalidOperationException("The private key file could not be found.");
                }

                var passphrase = credentialService.Retrieve(server.Id, CredentialKind.PrivateKeyPassphrase);
                privateKey = string.IsNullOrEmpty(passphrase)
                    ? new PrivateKeyFile(server.PrivateKeyPath)
                    : new PrivateKeyFile(server.PrivateKeyPath, passphrase);
                authentication = new PrivateKeyAuthenticationMethod(server.Username, privateKey);
            }

            var connectionInfo = new ConnectionInfo(server.Host, server.SshPort, server.Username, authentication)
            {
                Timeout = ConnectionTimeout
            };
            var client = new SshClient(connectionInfo);
            string? newlyTrustedFingerprint = null;
            string? changedFingerprint = null;
            var rejectedUnknownKey = false;
            client.HostKeyReceived += (_, eventArgs) =>
            {
                var fingerprint = eventArgs.FingerPrintSHA256;
                var trustedFingerprint = NormalizeFingerprint(server.TrustedHostKeyFingerprint);
                if (trustedFingerprint.Length > 0)
                {
                    eventArgs.CanTrust = string.Equals(
                        trustedFingerprint,
                        NormalizeFingerprint(fingerprint),
                        StringComparison.Ordinal);
                    if (!eventArgs.CanTrust)
                    {
                        changedFingerprint = fingerprint;
                    }

                    return;
                }

                var challenge = new HostKeyChallenge(server.Host, server.SshPort, eventArgs.HostKeyName, fingerprint);
                eventArgs.CanTrust = confirmUnknownHostKey(challenge);
                if (eventArgs.CanTrust)
                {
                    newlyTrustedFingerprint = fingerprint;
                }
                else
                {
                    rejectedUnknownKey = true;
                }
            };

            try
            {
                await client.ConnectAsync(cancellationToken);
                return new SshConnectionSession(client, newlyTrustedFingerprint);
            }
            catch
            {
                client.Dispose();
                if (changedFingerprint is not null)
                {
                    throw new HostKeyMismatchException(server.Host, server.TrustedHostKeyFingerprint, changedFingerprint);
                }

                if (rejectedUnknownKey)
                {
                    throw new HostKeyRejectedException(server.Host);
                }

                throw;
            }
        }
        finally
        {
            privateKey?.Dispose();
        }
    }

    internal static string NormalizeFingerprint(string? fingerprint) =>
        (fingerprint ?? string.Empty).Trim().Replace("SHA256:", string.Empty, StringComparison.OrdinalIgnoreCase).TrimEnd('=');
}

public sealed class HostKeyRejectedException(string host)
    : SshException($"The SSH host key for {host} was not accepted.")
{
    public string Host { get; } = host;
}

public sealed class SshConnectionSession(SshClient client, string? newlyTrustedFingerprint) : IDisposable
{
    public string? NewlyTrustedFingerprint { get; } = newlyTrustedFingerprint;

    public ShellStream CreateShellStream()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return client.CreateShellStream("xterm", 80, 24, 800, 600, 2048);
    }

    public async Task<SshCommandResult> ExecuteAsync(
        string commandText,
        string? standardInput = null,
        CancellationToken cancellationToken = default,
        TimeSpan? commandTimeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var command = client.CreateCommand(commandText);
        command.CommandTimeout = commandTimeout ?? TimeSpan.FromSeconds(15);

        if (standardInput is null)
        {
            await command.ExecuteAsync(cancellationToken);
            return new SshCommandResult(command.Result, command.Error, command.ExitStatus ?? -1);
        }

        var executeTask = command.ExecuteAsync(cancellationToken);
        using (var input = command.CreateInputStream())
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(standardInput);
            try
            {
                await input.WriteAsync(bytes, cancellationToken);
                await input.FlushAsync(cancellationToken);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
            }
        }

        await executeTask;
        return new SshCommandResult(command.Result, command.Error, command.ExitStatus ?? -1);
    }

    public async Task<SshCommandResult> ExecuteStreamingAsync(
        string commandText,
        string? standardInput,
        Action<string>? outputReceived,
        CancellationToken cancellationToken = default,
        TimeSpan? commandTimeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var command = client.CreateCommand(commandText);
        command.CommandTimeout = commandTimeout ?? TimeSpan.FromSeconds(30);

        var output = new StringBuilder();
        var error = new StringBuilder();
        var executeTask = command.ExecuteAsync(cancellationToken);
        var outputTask = ReadOutputAsync(command.OutputStream, output, outputReceived, cancellationToken);
        var errorTask = ReadOutputAsync(command.ExtendedOutputStream, error, outputReceived, cancellationToken);

        try
        {
            if (standardInput is not null)
            {
                using var input = command.CreateInputStream();
                var bytes = Encoding.UTF8.GetBytes(standardInput);
                try
                {
                    await input.WriteAsync(bytes, cancellationToken);
                    await input.FlushAsync(cancellationToken);
                }
                finally
                {
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
                }
            }

            await executeTask;
            await Task.WhenAll(outputTask, errorTask);
            return new SshCommandResult(output.ToString(), error.ToString(), command.ExitStatus ?? -1);
        }
        catch
        {
            try
            {
                command.CancelAsync(forceKill: true, millisecondsTimeout: 1000);
            }
            catch (Exception)
            {
                // The remote channel may already be closed.
            }

            try
            {
                await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
                // The original execution error is more useful than a stream-drain error.
            }

            throw;
        }
    }

    private static async Task ReadOutputAsync(
        Stream stream,
        StringBuilder destination,
        Action<string>? outputReceived,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 4096, leaveOpen: true);
        var buffer = new char[2048];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0)
            {
                return;
            }

            var chunk = new string(buffer, 0, count);
            destination.Append(chunk);
            try
            {
                outputReceived?.Invoke(chunk);
            }
            catch (Exception)
            {
                // A UI callback must not interrupt a remote package operation.
            }
        }
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        client.Dispose();
    }
}

public sealed record SshCommandResult(string Output, string Error, int ExitStatus);
