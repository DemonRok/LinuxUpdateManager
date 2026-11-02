namespace LinuxUpdateManager.Core.Servers;

public sealed class ServerInventoryDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<ServerProfile> Servers { get; set; } = [];
}
