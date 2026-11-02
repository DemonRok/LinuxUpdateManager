using System.Globalization;
using System.Resources;

namespace LinuxUpdateManager.App;

internal static class ContributionDestinations
{
    private static readonly ResourceManager Resources = new(
        "LinuxUpdateManager.App.ContributionDestinations",
        typeof(ContributionDestinations).Assembly);

    public static string PayPalUrl => GetRequired("PayPalUrl");

    public static string BitcoinAddress => GetRequired("BitcoinAddress");

    private static string GetRequired(string key) =>
        Resources.GetString(key, CultureInfo.InvariantCulture)
        ?? throw new MissingManifestResourceException($"Contribution destination '{key}' is missing.");
}
