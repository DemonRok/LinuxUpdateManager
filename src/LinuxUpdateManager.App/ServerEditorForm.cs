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
    private readonly TextBox _privateKeyPath = new() { ReadOnly = true };
    private readonly Button _browsePrivateKey = new();
    private readonly TableLayoutPanel _privateKeyPicker = new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 2,
        RowCount = 1,
        Padding = Padding.Empty
    };
    private readonly ComboBox _privilege = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _group = new();
    private readonly TextBox _notes = new() { Multiline = true, Height = 72, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _checkForUpdates = new() { AutoSize = true };

    public ServerProfile Profile => _profile;

    public ServerEditorForm(ServerProfile? profile)
    {
        _profile = profile is null ? new ServerProfile() : Copy(profile);
        Text = AppLocalizer.Get("ServerDetails");
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(610, 420);
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
        SetRowStyle(root, 0, new RowStyle(SizeType.AutoSize));
        SetRowStyle(root, 1, new RowStyle(SizeType.AutoSize));

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 10,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 0, 8, 0)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(fields, 0, "ServerName", _name);
        AddField(fields, 1, "HostnameOrIp", _host);
        AddField(fields, 2, "SshPort", _port);
        AddField(fields, 3, "Username", _username);
        AddField(fields, 4, "Authentication", _authentication);
        _privateKeyPicker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _privateKeyPicker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _privateKeyPicker.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _privateKeyPath.Dock = DockStyle.Fill;
        _privateKeyPath.Margin = new Padding(0, 2, 5, 2);
        _browsePrivateKey.Text = AppLocalizer.Get("Browse");
        _browsePrivateKey.AutoSize = true;
        _browsePrivateKey.Margin = Padding.Empty;
        _browsePrivateKey.Click += BrowsePrivateKeyClicked;
        _privateKeyPicker.Controls.Add(_privateKeyPath, 0, 0);
        _privateKeyPicker.Controls.Add(_browsePrivateKey, 1, 0);
        AddField(fields, 5, "PrivateKey", _privateKeyPicker, _privateKeyLabel);
        AddField(fields, 6, "PrivilegeMethod", _privilege);
        AddField(fields, 7, "ServerGroup", _group);
        AddField(fields, 8, "Notes", _notes);
        AddField(fields, 9, "UpdatePreferences", _checkForUpdates);

        _authentication.Items.AddRange([AppLocalizer.Get("Password"), AppLocalizer.Get("PrivateKey")]);
        _privilege.Items.AddRange([
            AppLocalizer.Get("DirectRoot"),
            AppLocalizer.Get("Sudo"),
            AppLocalizer.Get("SudoInteractive"),
            AppLocalizer.Get("Su"),
            AppLocalizer.Get("Auto")
        ]);

        _authentication.SelectedIndexChanged += (_, _) => UpdatePrivateKeyAvailability();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
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
        SetRowStyle(fields, row, new RowStyle(SizeType.AutoSize));
        var label = existingLabel ?? new Label();
        label.Text = AppLocalizer.Get(labelKey);
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.AutoSize = true;
        label.Padding = new Padding(0, 5, 8, 5);
        control.Dock = control is CheckBox ? DockStyle.Left : control is TableLayoutPanel ? DockStyle.Fill : DockStyle.Top;
        control.Margin = new Padding(3, 3, 3, 3);
        fields.Controls.Add(label, 0, row);
        fields.Controls.Add(control, 1, row);
    }

    private static void SetRowStyle(TableLayoutPanel panel, int row, RowStyle style)
    {
        if (panel.RowStyles.Count <= row)
        {
            panel.RowStyles.Add(style);
        }
        else
        {
            panel.RowStyles[row] = style;
        }
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
        _privateKeyPicker.Enabled = enabled;
        _privateKeyLabel.Enabled = enabled;
    }

    private async void BrowsePrivateKeyClicked(object? sender, EventArgs e)
    {
        _browsePrivateKey.Enabled = false;
        try
        {
            var selectedPath = await ShowPrivateKeyDialogAsync(
                _privateKeyPath.Text,
                Handle,
                AppLocalizer.Get("SelectPrivateKey"),
                AppLocalizer.Get("PrivateKeyFileFilter"));
            if (selectedPath is not null)
            {
                _privateKeyPath.Text = selectedPath;
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"{AppLocalizer.Get("PrivateKeyDialogFailed")}{Environment.NewLine}{exception.Message}",
                AppLocalizer.Get("AppTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            UpdatePrivateKeyAvailability();
        }
    }

    private static Task<string?> ShowPrivateKeyDialogAsync(
        string currentPath,
        IntPtr ownerHandle,
        string title,
        string filter)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogThread = new Thread(() =>
        {
            try
            {
                using var dialog = new OpenFileDialog
                {
                    Title = title,
                    Filter = filter,
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = false,
                    AutoUpgradeEnabled = true,
                    RestoreDirectory = true
                };

                if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    dialog.FileName = Path.GetFileName(currentPath);
                }

                var initialDirectory = Path.GetDirectoryName(currentPath);
                if (string.IsNullOrWhiteSpace(initialDirectory))
                {
                    initialDirectory = Application.StartupPath;
                }

                dialog.InitialDirectory = initialDirectory;
                var result = dialog.ShowDialog(new WindowHandleOwner(ownerHandle));
                completion.TrySetResult(result == DialogResult.OK ? dialog.FileName : null);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "PrivateKeyFileDialog"
        };

        dialogThread.SetApartmentState(ApartmentState.STA);
        dialogThread.Start();
        return completion.Task;
    }

    private sealed class WindowHandleOwner(IntPtr handle) : IWin32Window
    {
        public IntPtr Handle { get; } = handle;
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
        TrustedHostKeyFingerprint = source.TrustedHostKeyFingerprint,
        PrivilegeElevationMethod = source.PrivilegeElevationMethod,
        Group = source.Group,
        Notes = source.Notes,
        UpdatePreferences = new ServerUpdatePreferences { CheckForUpdates = source.UpdatePreferences.CheckForUpdates }
    };
}
