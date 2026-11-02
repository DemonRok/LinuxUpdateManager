using System.Globalization;
using System.Reflection;
using System.Resources;

namespace LinuxUpdateManager.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void DashboardResourcesProvideEnglishDefaultAndItalianTranslation()
    {
        var assembly = Assembly.Load("LinuxUpdateManager.App");
        var resources = new ResourceManager("LinuxUpdateManager.App.Strings", assembly);

        Assert.Equal("Server Dashboard", resources.GetString("DashboardTitle", CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("Dashboard dei server", resources.GetString("DashboardTitle", CultureInfo.GetCultureInfo("it-IT")));
        Assert.Equal("Check for Updates", resources.GetString("CheckForUpdates", CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("Verifica aggiornamenti", resources.GetString("CheckForUpdates", CultureInfo.GetCultureInfo("it-IT")));
    }

    [Fact]
    public void EnglishAndItalianResourcesHaveMatchingKeysAndNonemptyValues()
    {
        var assembly = Assembly.Load("LinuxUpdateManager.App");
        var resources = new ResourceManager("LinuxUpdateManager.App.Strings", assembly);
        var english = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var italian = resources.GetResourceSet(CultureInfo.GetCultureInfo("it-IT"), true, false)!;
        var englishEntries = english.Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        var italianEntries = italian.Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);

        Assert.Equal(englishEntries.Keys.Order(), italianEntries.Keys.Order());
        Assert.All(englishEntries.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.All(italianEntries.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }
}
