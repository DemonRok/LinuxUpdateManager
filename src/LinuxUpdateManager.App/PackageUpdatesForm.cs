using LinuxUpdateManager.App.Localization;
using LinuxUpdateManager.Core.Updates;

namespace LinuxUpdateManager.App;

internal sealed class PackageUpdatesForm : Form
{
    public PackageUpdatesForm(string serverName, UpdateCheckResult result)
    {
        Text = string.Format(AppLocalizer.Get("PackageDetailsTitle"), serverName);
        Icon = System.Drawing.SystemIcons.Application;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(650, 360);
        ClientSize = new Size(850, 500);

        var content = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 9F),
            Text = FormatPackages(result.Packages)
        };
        var close = new Button
        {
            Text = AppLocalizer.Get("Close"),
            AutoSize = true,
            DialogResult = DialogResult.OK,
            Anchor = AnchorStyles.Right
        };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        footer.Controls.Add(close);

        Controls.Add(content);
        Controls.Add(footer);
        AcceptButton = close;
    }

    private static string FormatPackages(IReadOnlyList<PackageUpdate> packages)
    {
        if (packages.Count == 0)
        {
            return AppLocalizer.Get("NoPackageUpdates");
        }

        var lines = new List<string>
        {
            $"{AppLocalizer.Get("PackageColumnName"),-34} {AppLocalizer.Get("PackageColumnCurrent"),-24} {AppLocalizer.Get("PackageColumnCandidate"),-24} {AppLocalizer.Get("PackageColumnSecurity"),-10} {AppLocalizer.Get("PackageColumnRepository")}",
            new string('-', 130)
        };
        lines.AddRange(packages.Select(package =>
            $"{Truncate(package.Name, 34),-34} {Truncate(package.CurrentVersion, 24),-24} " +
            $"{Truncate(package.CandidateVersion, 24),-24} " +
            $"{AppLocalizer.Get(package.IsSecurityUpdate switch { true => "Yes", false => "No", null => "Unknown" }),-10} " +
            $"{package.Repository ?? "—"}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
