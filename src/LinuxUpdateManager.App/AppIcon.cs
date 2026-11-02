namespace LinuxUpdateManager.App;

internal static class AppIcon
{
    public static Icon Create() =>
        Icon.ExtractAssociatedIcon(Application.ExecutablePath)
        ?? (Icon)SystemIcons.Application.Clone();
}
