using System.Text.Json;

namespace LinuxUpdateManager.Core.Configuration;

public sealed class ConfigurationService
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _configurationPath;

    public ConfigurationService(string? configurationPath = null)
    {
        _configurationPath = configurationPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinuxUpdateManager",
            "config.json");
    }

    public async Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_configurationPath))
        {
            var defaults = new AppConfiguration();
            await SaveAsync(defaults, cancellationToken);
            return defaults;
        }

        try
        {
            await using var stream = File.OpenRead(_configurationPath);
            var configuration = await JsonSerializer.DeserializeAsync<AppConfiguration>(
                stream, JsonOptions, cancellationToken);

            if (!IsValid(configuration))
            {
                throw new InvalidDataException("The configuration contains unsupported or invalid values.");
            }

            return configuration!;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            PreserveInvalidConfiguration();
            var defaults = new AppConfiguration();
            await SaveAsync(defaults, cancellationToken);
            return defaults;
        }
    }

    public async Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!IsValid(configuration))
        {
            throw new ArgumentException("The configuration contains unsupported or invalid values.", nameof(configuration));
        }

        var directory = Path.GetDirectoryName(_configurationPath)
            ?? throw new InvalidOperationException("The configuration path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{_configurationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(_configurationPath))
            {
                File.Replace(temporaryPath, _configurationPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, _configurationPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsValid(AppConfiguration? configuration) =>
        configuration is not null &&
        configuration.SchemaVersion == CurrentSchemaVersion &&
        configuration.Language is "en-US" or "it-IT" &&
        configuration.SshConnectionTimeoutSeconds is >= 1 and <= 300 &&
        configuration.CommandTimeoutSeconds is >= 1 and <= 86400 &&
        configuration.MaximumConcurrentOperations is >= 1 and <= 32;

    private void PreserveInvalidConfiguration()
    {
        if (!File.Exists(_configurationPath))
        {
            return;
        }

        var preservedPath = $"{_configurationPath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        File.Move(_configurationPath, preservedPath);
    }
}
