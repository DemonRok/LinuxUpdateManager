using LinuxUpdateManager.App.Localization;
using LinuxUpdateManager.Core.History;

namespace LinuxUpdateManager.App;

internal sealed class OperationHistoryDetailsForm : Form
{
    public OperationHistoryDetailsForm(OperationHistoryEntry entry)
    {
        Text = AppLocalizer.Get("HistoryDetailsTitle");
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 520);
        MinimumSize = new Size(620, 400);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var metadata = new Label
        {
            Text = string.Format(AppLocalizer.Get("HistoryDetailsSummary"),
                entry.ServerName,
                entry.ServerHost,
                AppLocalizer.Get($"HistoryOperation{entry.OperationType}"),
                AppLocalizer.Get($"HistoryResult{entry.Result}"),
                entry.StartedAtUtc.ToLocalTime().ToString("g", AppLocalizer.CurrentCulture),
                entry.EndedAtUtc.ToLocalTime().ToString("g", AppLocalizer.CurrentCulture),
                entry.ExitStatus?.ToString(AppLocalizer.CurrentCulture) ?? "—",
                entry.PackageCount?.ToString(AppLocalizer.CurrentCulture) ?? "—",
                entry.SecurityUpdateCount?.ToString(AppLocalizer.CurrentCulture) ?? "—",
                entry.ErrorSummary ?? "—"),
            Dock = DockStyle.Fill,
            AutoEllipsis = true
        };
        layout.Controls.Add(metadata, 0, 0);

        var output = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Text = entry.SanitizedOutput
        };
        layout.Controls.Add(output, 0, 1);

        var close = new Button
        {
            Text = AppLocalizer.Get("Close"),
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };
        layout.Controls.Add(close, 0, 2);
        Controls.Add(layout);
        AcceptButton = close;
    }
}
