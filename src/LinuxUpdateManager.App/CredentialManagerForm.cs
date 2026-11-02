using LinuxUpdateManager.App.Localization;
using LinuxUpdateManager.Core.Security;

namespace LinuxUpdateManager.App;

internal sealed class CredentialManagerForm : Form
{
    private readonly Guid _serverId;
    private readonly CredentialService _credentialService;
    private readonly Dictionary<CredentialKind, (TextBox Input, Label State, Button Remove)> _rows = [];

    public CredentialManagerForm(Guid serverId, string serverName, CredentialService credentialService)
    {
        _serverId = serverId;
        _credentialService = credentialService;
        Text = AppLocalizer.Get("ManageCredentialsTitle");
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(700, 312);
        MinimumSize = Size;
        BuildLayout(serverName);
        RefreshStates();
    }

    private void BuildLayout(string serverName)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var header = new Label
        {
            Text = $"{AppLocalizer.Get("ServerName")}: {serverName}",
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 5,
            RowCount = 5
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));

        AddCredentialRow(grid, 0, CredentialKind.SshPassword, "SshPassword");
        AddCredentialRow(grid, 1, CredentialKind.SudoPassword, "SudoPassword");
        AddCredentialRow(grid, 2, CredentialKind.RootPassword, "RootPassword");
        AddCredentialRow(grid, 3, CredentialKind.PrivateKeyPassphrase, "PrivateKeyPassphrase");
        AddCredentialRow(grid, 4, CredentialKind.DoasPassword, "DoasPassword");

        var close = new Button
        {
            Text = AppLocalizer.Get("Close"),
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        footer.Controls.Add(close);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(grid, 0, 1);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
        AcceptButton = close;
    }

    private void AddCredentialRow(TableLayoutPanel grid, int row, CredentialKind kind, string labelKey)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var label = new Label
        {
            Text = AppLocalizer.Get(labelKey),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true
        };
        var input = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            UseSystemPasswordChar = true,
            PlaceholderText = AppLocalizer.Get("NewCredentialPlaceholder"),
            Margin = new Padding(3, 0, 3, 0)
        };
        var state = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = true
        };
        var save = new Button
        {
            Text = AppLocalizer.Get("Set"),
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Margin = Padding.Empty,
            Tag = kind
        };
        var remove = new Button
        {
            Text = AppLocalizer.Get("Remove"),
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Margin = Padding.Empty,
            Tag = kind
        };
        save.Click += SaveCredentialClicked;
        remove.Click += RemoveCredentialClicked;

        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(input, 1, row);
        grid.Controls.Add(state, 2, row);
        grid.Controls.Add(save, 3, row);
        grid.Controls.Add(remove, 4, row);
        _rows.Add(kind, (input, state, remove));
    }

    private void SaveCredentialClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { Tag: CredentialKind kind } || !_rows.TryGetValue(kind, out var row))
        {
            return;
        }

        if (string.IsNullOrEmpty(row.Input.Text))
        {
            MessageBox.Show(this, AppLocalizer.Get("CredentialValueRequired"), AppLocalizer.Get("AppTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _credentialService.Save(_serverId, kind, row.Input.Text);
            row.Input.Clear();
            RefreshState(kind);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or PlatformNotSupportedException or ArgumentOutOfRangeException)
        {
            MessageBox.Show(this, AppLocalizer.Get("CredentialOperationFailed"), AppLocalizer.Get("AppTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RemoveCredentialClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { Tag: CredentialKind kind } || !_rows.TryGetValue(kind, out var row))
        {
            return;
        }

        if (MessageBox.Show(this, AppLocalizer.Get("ConfirmRemoveCredential"), AppLocalizer.Get("AppTitle"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            _credentialService.Delete(_serverId, kind);
            RefreshState(kind);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            MessageBox.Show(this, AppLocalizer.Get("CredentialOperationFailed"), AppLocalizer.Get("AppTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshStates()
    {
        foreach (var kind in _rows.Keys)
        {
            RefreshState(kind);
        }
    }

    private void RefreshState(CredentialKind kind)
    {
        if (!_rows.TryGetValue(kind, out var row))
        {
            return;
        }

        try
        {
            var exists = _credentialService.Exists(_serverId, kind);
            row.State.Text = AppLocalizer.Get(exists ? "CredentialStored" : "CredentialMissing");
            row.Remove.Enabled = exists;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            row.State.Text = AppLocalizer.Get("CredentialUnavailable");
            row.Remove.Enabled = false;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        foreach (var row in _rows.Values)
        {
            row.Input.Clear();
        }

        base.OnFormClosed(e);
    }
}
