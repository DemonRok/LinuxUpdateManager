using LinuxUpdateManager.App.Localization;

namespace LinuxUpdateManager.App;

internal sealed class ApplicationUpdateForm : Form
{
    public ApplicationUpdateForm(Version version, string releaseNotes)
    {
        Text = AppLocalizer.Get("ApplicationUpdateTitle");
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(540, 360);
        ClientSize = new Size(680, 500);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var header = new Label
        {
            Dock = DockStyle.Fill,
            Text = string.Format(AppLocalizer.Get("ApplicationUpdateAvailable"), version.ToString(3)),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold)
        };
        layout.Controls.Add(header, 0, 0);

        var notesLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        notesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        notesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        notesLayout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = AppLocalizer.Get("ApplicationReleaseNotes"),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        notesLayout.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.IsNullOrWhiteSpace(releaseNotes) ? "—" : releaseNotes,
            WordWrap = true
        }, 0, 1);
        layout.Controls.Add(notesLayout, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty
        };
        var later = new Button
        {
            Text = AppLocalizer.Get("ApplicationUpdateLater"),
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };
        var update = new Button
        {
            Text = AppLocalizer.Get("ApplicationUpdateNow"),
            AutoSize = true,
            DialogResult = DialogResult.OK
        };
        buttons.Controls.Add(later);
        buttons.Controls.Add(update);
        layout.Controls.Add(buttons, 0, 2);

        Controls.Add(layout);
        AcceptButton = update;
        CancelButton = later;
    }
}
