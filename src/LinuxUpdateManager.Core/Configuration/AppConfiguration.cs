namespace LinuxUpdateManager.Core.Configuration;

public sealed class AppConfiguration
{
    public int SchemaVersion { get; set; } = ConfigurationService.CurrentSchemaVersion;

    public string Language { get; set; } = "en-US";

    public int SshConnectionTimeoutSeconds { get; set; } = 15;

    public int CommandTimeoutSeconds { get; set; } = 300;

    public int MaximumConcurrentOperations { get; set; } = 3;

    public bool CheckForUpdatesOnStartup { get; set; } = true;

    public bool EnableFileLogging { get; set; } = true;

    public int HistoryRetentionDays { get; set; } = 90;
}
