namespace LinuxUpdateManager.Core.History;

public static class HistoryOperationTypes
{
    public const string SshConnectionCheck = "SshConnectionCheck";
    public const string PrivilegeCheck = "PrivilegeCheck";
    public const string UpdateCheck = "UpdateCheck";
    public const string PackageUpgrade = "PackageUpgrade";
}

public static class HistoryOperationResults
{
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}
