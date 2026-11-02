using System.Globalization;
using System.Resources;

namespace LinuxUpdateManager.App.Localization;

internal static class AppLocalizer
{
    private static readonly ResourceManager Resources = new(
        "LinuxUpdateManager.App.Strings", typeof(AppLocalizer).Assembly);

    public static CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    public static void SetLanguage(string language)
    {
        CurrentCulture = language == "it-IT"
            ? CultureInfo.GetCultureInfo("it-IT")
            : CultureInfo.GetCultureInfo("en-US");
    }

    public static string Get(string key) => Resources.GetString(key, CurrentCulture) ?? key;
}
