namespace LinuxUpdateManager.App;

static class Program
{
    [STAThread]
    static async Task Main()
    {
        ApplicationConfiguration.Initialize();
        var configurationService = new LinuxUpdateManager.Core.Configuration.ConfigurationService();
        var configuration = await configurationService.LoadAsync();
        var inventoryService = new LinuxUpdateManager.Core.Servers.ServerInventoryService();
        var inventory = await inventoryService.LoadAsync();
        var credentialService = new LinuxUpdateManager.Core.Security.CredentialService();
        LinuxUpdateManager.App.Localization.AppLocalizer.SetLanguage(configuration.Language);
        Application.Run(new MainForm(configuration, configurationService, inventoryService, credentialService, inventory));
    }
}
