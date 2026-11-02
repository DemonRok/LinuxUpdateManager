namespace LinuxUpdateManager.App;

static class Program
{
    [STAThread]
    static async Task<int> Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (ApplicationUpdateInstaller.IsUpdateCommand(args))
        {
            return ApplicationUpdateInstaller.ApplyUpdate(args);
        }

        var configurationService = new LinuxUpdateManager.Core.Configuration.ConfigurationService();
        var configuration = await configurationService.LoadAsync();
        var inventoryService = new LinuxUpdateManager.Core.Servers.ServerInventoryService();
        var inventory = await inventoryService.LoadAsync();
        var credentialService = new LinuxUpdateManager.Core.Security.CredentialService();
        LinuxUpdateManager.App.Localization.AppLocalizer.SetLanguage(configuration.Language);
        var historyService = new LinuxUpdateManager.Core.History.HistoryService();
        try
        {
            await historyService.InitializeAsync();
            if (configuration.HistoryRetentionDays > 0)
            {
                await historyService.DeleteOlderThanAsync(
                    DateTimeOffset.UtcNow.AddDays(-configuration.HistoryRetentionDays));
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                string.Format(
                    LinuxUpdateManager.App.Localization.AppLocalizer.Get("HistoryUnavailableAtStartup"),
                    exception.Message),
                LinuxUpdateManager.App.Localization.AppLocalizer.Get("HistoryTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        Application.Run(new MainForm(
            configuration, configurationService, inventoryService, credentialService, historyService, inventory));
        return 0;
    }
}
