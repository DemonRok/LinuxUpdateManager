using LinuxUpdateManager.Core.Configuration;
using LinuxUpdateManager.Core.Security;
using LinuxUpdateManager.Core.Servers;

namespace LinuxUpdateManager.Core.Backups;

public sealed class BackupPayload
{
    public int SchemaVersion { get; set; } = 1;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IncludesConfiguration { get; set; } = true;

    public bool IncludesCredentials { get; set; }

    public bool IncludesPrivateKeys { get; set; }

    public AppConfiguration? Configuration { get; set; } = new();

    public ServerInventoryDocument? Inventory { get; set; } = new();

    public List<BackupCredential> Credentials { get; set; } = [];

    public List<BackupPrivateKey> PrivateKeys { get; set; } = [];
}

public sealed record BackupCredential(Guid ServerId, CredentialKind Kind, string Secret);

public sealed record BackupPrivateKey(Guid ServerId, string FileName, byte[] Contents);

public sealed record BackupPreview(
    BackupPayload Payload,
    bool IsEncrypted,
    bool IncludesConfiguration,
    bool IncludesCredentials,
    bool IncludesPrivateKeys,
    string FileName);
