using LinuxUpdateManager.App.Localization;
using LinuxUpdateManager.Core.Servers;

namespace LinuxUpdateManager.App;

internal sealed class ServerEditorForm : Form
{
    private readonly ServerProfile _profile;
    private readonly TextBox _name = new();
    private readonly TextBox _host = new();
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 22 };
    private readonly TextBox _username = new();
    private readonly ComboBox _authentication = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _privateKeyLabel = new();
    private readonly TextBox _privateKeyPath = new();
    private readonly ComboBox _privilege = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _group = new();
    private readonly TextBox _notes = new() { Multiline = true, Height = 72, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _checkForUpdates = new() { AutoSize = true };

    public ServerProfile Profile => _profile;

    public ServerEditorForm(ServerProfile? profile)
    {
        _profile = profile is null ? new ServerProfile() : Copy(profile);
        Text = AppLocalizer.Get("ServerDetails");
        Icon = System.Drawing.SystemIcons.Application;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(610, 570);
        MinimumSize = Size;
        BuildLayout();
        LoadProfile();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(16)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            AutoScroll = true,
            Padding = new Padding(0, 0, 8, 0)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(fields, 0, "ServerName", _name);
        AddField(fields, 1, "HostnameOrIp", _host);
        AddField(fields, 2, "SshPort", _port);
        AddField(fields, 3, "Username", _username);
        AddField(fields, 4, "Authentication", _authentication);
        AddField(fields, 5, "PrivateKeyPath", _privateKeyPath, _privateKeyLabel);
        AddField(fields, 6, "PrivilegeMethod", _privilege);
        AddField(fields, 7, "ServerGroup", _group);
        AddField(fields, 8, "Notes", _notes);
        AddField(fields, 9, "UpdatePreferences", _checkForUpdates);

        _authentication.Items.AddRange([AppLocalizer.Get("Password"), AppLocalizer.Get("PrivateKey")]);
        _privilege.Items.AddRange([
            AppLocalizer.Get("DirectRoot"),
            AppLocalizer.Get("Sudo"),
            AppLocalizer.Get("SudoInteractive"),
            AppLocalizer.Get("Su")
        ]);

        _authentication.SelectedIndexChanged += (_, _) => UpdatePrivateKeyAvailability();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var save = new Button { Text = AppLocalizer.Get("Save"), AutoSize = true };
        var cancel = new Button { Text = AppLocalizer.Get("Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += SaveClicked;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        root.Controls.Add(fields, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static void AddField(TableLayoutPanel fields, int row, string labelKey, Control control, Label? existingLabel = null)
    {
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = existingLabel ?? new Label();
        label.Text = AppLocalizer.Get(labelKey);
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.AutoSize = true;
        label.Padding = new Padding(0, 5, 8, 5);
        control.Dock = control is CheckBox ? DockStyle.Left : DockStyle.Top;
        control.Margin = new Padding(3, 5, 3, 5);
        fields.Controls.Add(label, 0, row);
        fields.Controls.Add(control, 1, row);
    }

    private void LoadProfile()
    {
        _name.Text = _profile.Name;
        _host.Text = _profile.Host;
        _port.Value = _profile.SshPort;
        _username.Text = _profile.Username;
        _authentication.SelectedIndex = (int)_profile.AuthenticationMethod;
        _privateKeyPath.Text = _profile.PrivateKeyPath;
        _privilege.SelectedIndex = (int)_profile.PrivilegeElevationMethod;
        _group.Text = _profile.Group;
        _notes.Text = _profile.Notes;
        _checkForUpdates.Text = AppLocalizer.Get("CheckForUpdatesPreference");
        _checkForUpdates.Checked = _profile.UpdatePreferences.CheckForUpdates;
        UpdatePrivateKeyAvailability();
    }

    private void UpdatePrivateKeyAvailability()
    {
        var enabled = _authentication.SelectedIndex == (int)SshAuthenticationMethod.PrivateKey;
        _privateKeyPath.Enabled = enabled;
        _privateKeyLabel.Enabled = enabled;
    }

    private void SaveClicked(object? sender, EventArgs e)
    {
        _profile.Name = _name.Text.Trim();
        _profile.Host = _host.Text.Trim();
        _profile.SshPort = (int)_port.Value;
        _profile.Username = _username.Text.Trim();
        _profile.AuthenticationMethod = (SshAuthenticationMethod)_authentication.SelectedIndex;
        _profile.PrivateKeyPath = _privateKeyPath.Text.Trim();
        _profile.PrivilegeElevationMethod = (PrivilegeElevationMethod)_privilege.SelectedIndex;
        _profile.Group = _group.Text.Trim();
        _profile.Notes = _notes.Text.Trim();
        _profile.UpdatePreferences.CheckForUpdates = _checkForUpdates.Checked;

        try
        {
            ServerInventoryService.ValidateServer(_profile);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (InvalidDataException)
        {
            MessageBox.Show(this, AppLocalizer.Get("InvalidServerDetails"), AppLocalizer.Get("AppTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static ServerProfile Copy(ServerProfile source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Host = source.Host,
        SshPort = source.SshPort,
        Username = source.Username,
        AuthenticationMethod = source.AuthenticationMethod,
        PrivateKeyPath = source.PrivateKeyPath,
        PrivilegeElevationMethod = source.PrivilegeElevationMethod,
        Group = source.Group,
        Notes = source.Notes,
        UpdatePreferences = new ServerUpdatePreferences { CheckForUpdates = source.UpdatePreferences.CheckForUpdates }
    };
}
