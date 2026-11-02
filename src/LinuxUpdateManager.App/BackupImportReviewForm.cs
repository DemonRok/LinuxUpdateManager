using LinuxUpdateManager.App.Localization;
using LinuxUpdateManager.Core.Backups;
using LinuxUpdateManager.Core.Servers;

namespace LinuxUpdateManager.App;

internal sealed class BackupImportReviewForm : Form
{
    private readonly BackupPreview _preview;
    private readonly RadioButton _merge = new() { AutoSize = true };
    private readonly RadioButton _replace = new() { AutoSize = true };
    private readonly DataGridView _items = new();

    public BackupRestoreMode RestoreMode => _replace.Checked ? BackupRestoreMode.Replace : BackupRestoreMode.Merge;

    public BackupImportReviewForm(BackupPreview preview, ServerInventoryDocument currentInventory)
    {
        _preview = preview;
        Text = AppLocalizer.Get("BackupPreviewTitle");
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(760, 510);
        MinimumSize = new Size(640, 420);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var summary = new Label
        {
            Text = FormatSummary(preview),
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(summary, 0, 0);

        _items.Dock = DockStyle.Fill;
        _items.ReadOnly = true;
        _items.AllowUserToAddRows = false;
        _items.AllowUserToDeleteRows = false;
        _items.AllowUserToResizeRows = false;
        _items.RowHeadersVisible = false;
        _items.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _items.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _items.Columns.Add("Name", AppLocalizer.Get("BackupPreviewServer"));
        _items.Columns.Add("Host", AppLocalizer.Get("ColumnHost"));
        _items.Columns.Add("Status", AppLocalizer.Get("BackupPreviewStatus"));
        PopulateItems(preview, currentInventory);
        layout.Controls.Add(_items, 0, 1);

        var modes = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _merge.Text = AppLocalizer.Get("BackupMergeMode");
        _replace.Text = AppLocalizer.Get("BackupReplaceMode");
        _merge.Checked = true;
        _replace.Enabled = preview.IncludesConfiguration;
        modes.Controls.Add(_merge);
        modes.Controls.Add(_replace);
        layout.Controls.Add(modes, 0, 2);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var import = new Button { Text = AppLocalizer.Get("Import"), AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = AppLocalizer.Get("Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        footer.Controls.Add(import);
        footer.Controls.Add(cancel);
        layout.Controls.Add(footer, 0, 3);
        Controls.Add(layout);
        AcceptButton = import;
        CancelButton = cancel;
    }

    private string FormatSummary(BackupPreview preview)
    {
        var configuration = preview.IncludesConfiguration
            ? AppLocalizer.Get("BackupIncluded")
            : AppLocalizer.Get("BackupOmitted");
        var credentials = preview.IncludesCredentials
            ? string.Format(AppLocalizer.Get("BackupCredentialCount"), preview.Payload.Credentials.Count)
            : AppLocalizer.Get("BackupOmitted");
        var keys = preview.IncludesPrivateKeys
            ? string.Format(AppLocalizer.Get("BackupPrivateKeyCount"), preview.Payload.PrivateKeys.Count)
            : AppLocalizer.Get("BackupOmitted");
        var encryption = AppLocalizer.Get(preview.IsEncrypted ? "BackupEncrypted" : "BackupUnencrypted");
        return string.Format(AppLocalizer.Get("BackupPreviewSummary"), preview.FileName, encryption,
            configuration, credentials, keys);
    }

    private void PopulateItems(BackupPreview preview, ServerInventoryDocument currentInventory)
    {
        if (preview.Payload.Inventory is { } inventory)
        {
            var currentById = currentInventory.Servers.ToDictionary(server => server.Id);
            var currentEndpoints = currentInventory.Servers
                .Select(EndpointKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var server in inventory.Servers.OrderBy(server => server.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var duplicate = currentById.ContainsKey(server.Id) || currentEndpoints.Contains(EndpointKey(server));
                _items.Rows.Add(server.Name, server.Host,
                    AppLocalizer.Get(duplicate ? "BackupPreviewDuplicate" : "BackupPreviewNew"));
            }

            return;
        }

        var currentServers = currentInventory.Servers.ToDictionary(server => server.Id);
        foreach (var serverId in preview.Payload.Credentials.Select(credential => credential.ServerId).Distinct())
        {
            if (currentServers.TryGetValue(serverId, out var server))
            {
                _items.Rows.Add(server.Name, server.Host, AppLocalizer.Get("BackupPreviewMatched"));
            }
            else
            {
                _items.Rows.Add(serverId.ToString("D"), "", AppLocalizer.Get("BackupPreviewMissing"));
            }
        }
    }

    private static string EndpointKey(ServerProfile server) =>
        $"{server.Username}@{server.Host}:{server.SshPort}";
}
