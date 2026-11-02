namespace LinuxUpdateManager.Core.Updates;

public static class PackageUpgradeCommands
{
    public const string Apt =
        "DEBIAN_FRONTEND=noninteractive LC_ALL=C apt-get -y " +
        "-o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold upgrade";

    public const string Dnf = "LC_ALL=C dnf -y upgrade";
}
