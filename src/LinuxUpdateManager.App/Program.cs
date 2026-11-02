namespace LinuxUpdateManager.App;

static class Program
{
    [STAThread]
    static async Task Main()
    {
        ApplicationConfiguration.Initialize();
        var configurationService = new LinuxUpdateManager.Core.Configuration.ConfigurationService();
        var configuration = await configurationService.LoadAsync();
        LinuxUpdateManager.App.Localization.AppLocalizer.SetLanguage(configuration.Language);
        Application.Run(new MainForm(configuration, configurationService));
    }
}
