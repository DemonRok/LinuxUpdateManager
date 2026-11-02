using LinuxUpdateManager.Core.Configuration;

namespace LinuxUpdateManager.Tests;

public sealed class ConfigurationServiceTests
{
    [Fact]
    public async Task MissingConfigurationUsesEnglishDefaultsAndCreatesFile()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "config.json");
        var service = new ConfigurationService(path);

        var configuration = await service.LoadAsync();

        Assert.Equal("en-US", configuration.Language);
        Assert.Equal(ConfigurationService.CurrentSchemaVersion, configuration.SchemaVersion);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task SaveAndLoadPreserveLanguagePreference()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "config.json");
        var service = new ConfigurationService(path);

        await service.SaveAsync(new AppConfiguration { Language = "it-IT" });
        var loaded = await service.LoadAsync();

        Assert.Equal("it-IT", loaded.Language);
        Assert.DoesNotContain(".tmp", string.Join(Environment.NewLine, Directory.GetFiles(directory.Path)));
    }

    [Fact]
    public async Task MalformedConfigurationIsPreservedAndDefaultsAreRestored()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "config.json");
        await File.WriteAllTextAsync(path, "{ invalid json }");
        var service = new ConfigurationService(path);

        var loaded = await service.LoadAsync();

        Assert.Equal("en-US", loaded.Language);
        Assert.Single(Directory.GetFiles(directory.Path, "config.json.corrupt-*"));
        Assert.Contains("\"Language\": \"en-US\"", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task SaveRejectsUnsupportedLanguage()
    {
        using var directory = new TemporaryDirectory();
        var service = new ConfigurationService(Path.Combine(directory.Path, "config.json"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveAsync(new AppConfiguration { Language = "fr-FR" }));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"lum-tests-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
