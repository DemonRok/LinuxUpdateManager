using LinuxUpdateManager.Core.Security;

namespace LinuxUpdateManager.Core.Servers;

public enum SshAuthenticationMethod
{
    Password,
    PrivateKey
}

public enum PrivilegeElevationMethod
{
    DirectRoot,
    Sudo,
    SudoInteractive,
    Su
}

public sealed class ServerUpdatePreferences
{
    public bool CheckForUpdates { get; set; } = true;
}

public sealed class ServerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int SshPort { get; set; } = 22;

    public string Username { get; set; } = string.Empty;

    public SshAuthenticationMethod AuthenticationMethod { get; set; } = SshAuthenticationMethod.Password;

    public string PrivateKeyPath { get; set; } = string.Empty;

    public string TrustedHostKeyFingerprint { get; set; } = string.Empty;

    public PrivilegeElevationMethod PrivilegeElevationMethod { get; set; } = PrivilegeElevationMethod.Sudo;

    public string Group { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public ServerUpdatePreferences UpdatePreferences { get; set; } = new();

    public IReadOnlyDictionary<string, string> CredentialReferences =>
        Enum.GetValues<CredentialKind>().ToDictionary(
            kind => kind.ToString(),
            kind => CredentialService.GetTargetName(Id, kind));
}
