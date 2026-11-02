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
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(server);

        if (server.PrivilegeElevationMethod == PrivilegeElevationMethod.DirectRoot)
        {
            var directResult = await session.ExecuteAsync("id -u", cancellationToken: cancellationToken);
            return directResult.ExitStatus == 0 && directResult.Output.Trim() == "0";
        }

        if (server.PrivilegeElevationMethod is PrivilegeElevationMethod.Sudo or PrivilegeElevationMethod.SudoInteractive)
        {
            var password = credentialService.Retrieve(server.Id, CredentialKind.SudoPassword);
            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException("The sudo password is not stored.");
            }

            var commandText = server.PrivilegeElevationMethod == PrivilegeElevationMethod.Sudo
                ? "sudo -S -p '' -- id -u"
                : "sudo -S -p '' -i -- id -u";
            var result = await session.ExecuteAsync(commandText, password + "\n", cancellationToken);
            return result.ExitStatus == 0 && result.Output.Trim() == "0";
        }

        var rootPassword = credentialService.Retrieve(server.Id, CredentialKind.RootPassword);
        if (string.IsNullOrEmpty(rootPassword))
        {
            throw new InvalidOperationException("The root password is not stored.");
        }

        return await VerifySuAsync(session, rootPassword, cancellationToken);
    }

    private static async Task<bool> VerifySuAsync(
        SshConnectionSession session,
        string rootPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(() =>
        {
            using var shell = session.CreateShellStream();
            shell.WriteLine("su -c 'id -u; echo LUM_PRIV_CHECK_DONE'");
            var response = shell.Expect(
                new Regex("(?i)(password\\s*:|LUM_PRIV_CHECK_DONE)", RegexOptions.CultureInvariant),
                TimeSpan.FromSeconds(15));
            if (response is null)
            {
                return false;
            }

            if (response.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                shell.WriteLine(rootPassword);
                response += shell.Expect(
                    new Regex("LUM_PRIV_CHECK_DONE", RegexOptions.CultureInvariant),
                    TimeSpan.FromSeconds(15));
            }

            return response.Contains("LUM_PRIV_CHECK_DONE", StringComparison.Ordinal) &&
                   Regex.IsMatch(response, "(?:^|\\r?\\n)0(?:\\r?\\n|$)", RegexOptions.CultureInvariant);
        }, cancellationToken);
    }
}
