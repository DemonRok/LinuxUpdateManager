using LinuxUpdateManager.Core.Updates;

namespace LinuxUpdateManager.Tests;

public sealed class PackageUpgradeCommandTests
{
    [Fact]
    public void AptCommandRunsOnlyConfirmedNoninteractivePackageUpgrade()
    {
        Assert.Contains("apt-get -y", PackageUpgradeCommands.Apt, StringComparison.Ordinal);
        Assert.EndsWith("upgrade", PackageUpgradeCommands.Apt, StringComparison.Ordinal);
        Assert.DoesNotContain("full-upgrade", PackageUpgradeCommands.Apt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dist-upgrade", PackageUpgradeCommands.Apt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reboot", PackageUpgradeCommands.Apt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DnfCommandRunsOnlyNoninteractivePackageUpgrade()
    {
        Assert.Contains("dnf -y upgrade", PackageUpgradeCommands.Dnf, StringComparison.Ordinal);
        Assert.DoesNotContain("system-upgrade", PackageUpgradeCommands.Dnf, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reboot", PackageUpgradeCommands.Dnf, StringComparison.OrdinalIgnoreCase);
    }
}
