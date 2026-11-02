using LinuxUpdateManager.App.Localization;
using LinuxUpdateManager.Core.Configuration;
using LinuxUpdateManager.Core.Security;
using LinuxUpdateManager.Core.Servers;
using LinuxUpdateManager.Core.Updates;

namespace LinuxUpdateManager.App;

internal sealed class MainForm : Form
{
    private readonly AppConfiguration _configuration;
    private readonly ConfigurationService _configurationService;
    private readonly ServerInventoryService _inventoryService;
    private readonly CredentialService _credentialService;
    private readonly SshConnectionService _sshConnectionService;
    private readonly PrivilegeElevationService _privilegeElevationService;
    private readonly UpdateDetectionService _updateDetectionService;
    private readonly ServerInventoryDocument _inventory;
    private readonly Dictionary<Guid, UpdateCheckResult> _latestUpdateResults = [];
    private readonly ComboBox _languageSelector = new();
    private readonly Label _languageLabel = new();
    private readonly Label _title = new();
    private readonly Label _notice = new();
    private readonly Label _status = new();
    private readonly DataGridView _servers = new();
    private readonly FlowLayoutPanel _toolbarActions = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _groupFilter = new();
    private readonly Button _addServer = new();
    private readonly Button _editServer = new();
    private readonly Button _removeServer = new();
    private readonly Button _manageCredentials = new();
    private readonly Button _testConnection = new();
    private readonly Button _testPrivileges = new();
    private readonly Button _checkUpdates = new();

    public MainForm(
        AppConfiguration configuration,
        ConfigurationService configurationService,
        ServerInventoryService inventoryService,
        CredentialService credentialService,
        ServerInventoryDocument inventory)
    {
        _configuration = configuration;
        _configurationService = configurationService;
        _inventoryService = inventoryService;
        _credentialService = credentialService;
        _sshConnectionService = new SshConnectionService(credentialService);
        _privilegeElevationService = new PrivilegeElevationService(credentialService);
        _updateDetectionService = new UpdateDetectionService(_privilegeElevationService);
        _inventory = inventory;

        Text = WindowTitle();
        Icon = AppIcon.Create();
        MinimumSize = new Size(800, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        BuildLayout();
        ApplyLanguage();
        AttachEvents();
        Load += (_, _) => ResizeWindowForLocalizedContent();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _title.Font = new Font(_title.Font.FontFamily, 16F, FontStyle.Bold);
        _title.Dock = DockStyle.Fill;
        _title.TextAlign = ContentAlignment.MiddleLeft;
        _languageSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageSelector.Width = 120;
        _languageSelector.Items.AddRange(["English", "Italiano"]);
        _languageLabel.AutoSize = true;
        _languageLabel.TextAlign = ContentAlignment.MiddleLeft;
        _languageLabel.Margin = new Padding(0, 7, 8, 0);
        var languagePicker = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };
        languagePicker.Controls.Add(_languageLabel);
        languagePicker.Controls.Add(_languageSelector);
        header.Controls.Add(_title, 0, 0);
        header.Controls.Add(languagePicker, 1, 0);

        _notice.Dock = DockStyle.Fill;
        _notice.ForeColor = Color.DimGray;
        _notice.TextAlign = ContentAlignment.MiddleLeft;

        _toolbarActions.Dock = DockStyle.Fill;
        _toolbarActions.FlowDirection = FlowDirection.LeftToRight;
        _toolbarActions.WrapContents = false;
        _toolbarActions.AutoScroll = false;
        _toolbarActions.Margin = Padding.Empty;

        ConfigureButton(_addServer, "AddServer");
        ConfigureButton(_editServer, "EditServer");
        ConfigureButton(_removeServer, "RemoveServer");
        ConfigureButton(_manageCredentials, "ManageCredentials");
        ConfigureButton(_testConnection, "TestConnection");
        ConfigureButton(_testPrivileges, "TestPrivileges");
        ConfigureButton(_checkUpdates, "CheckForUpdates");
        _search.Width = 190;
        _search.Margin = new Padding(0, 6, 4, 3);
        _groupFilter.Width = 150;
        _groupFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _groupFilter.Margin = new Padding(0, 5, 3, 3);
        _toolbarActions.Controls.Add(_addServer);
        _toolbarActions.Controls.Add(_editServer);
        _toolbarActions.Controls.Add(_removeServer);
        _toolbarActions.Controls.Add(_manageCredentials);
        _toolbarActions.Controls.Add(_testConnection);
        _toolbarActions.Controls.Add(_testPrivileges);
        _toolbarActions.Controls.Add(_checkUpdates);
        _toolbarActions.Controls.Add(_search);
        _toolbarActions.Controls.Add(_groupFilter);

        _servers.Dock = DockStyle.Fill;
        _servers.ReadOnly = true;
        _servers.AllowUserToAddRows = false;
        _servers.AllowUserToDeleteRows = false;
        _servers.AllowUserToResizeRows = false;
        _servers.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        _servers.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _servers.MultiSelect = false;
        _servers.RowHeadersVisible = false;
        _servers.BackgroundColor = SystemColors.Window;

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Color.DimGray;

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_notice, 0, 1);
        root.Controls.Add(_toolbarActions, 0, 2);
        root.Controls.Add(_servers, 0, 3);
        root.Controls.Add(_status, 0, 4);
        Controls.Add(root);
    }

    private static void ConfigureButton(Button button, string resourceKey)
    {
        button.Text = AppLocalizer.Get(resourceKey);
        button.AutoSize = true;
        button.Margin = new Padding(0, 5, 5, 3);
    }

    private void AttachEvents()
    {
        _languageSelector.SelectedIndexChanged += LanguageSelectorChanged;
        _search.TextChanged += (_, _) => ApplyFilters();
        _groupFilter.SelectedIndexChanged += (_, _) => ApplyFilters();
        _servers.SelectionChanged += (_, _) => UpdateCommandButtons();
        _addServer.Click += AddServerClicked;
        _editServer.Click += EditServerClicked;
        _removeServer.Click += RemoveServerClicked;
        _manageCredentials.Click += ManageCredentialsClicked;
        _testConnection.Click += TestConnectionClicked;
        _testPrivileges.Click += TestPrivilegesClicked;
        _checkUpdates.Click += CheckUpdatesClicked;
        _servers.CellDoubleClick += ServerCellDoubleClicked;
    }

    private void ApplyLanguage()
    {
        Text = WindowTitle();
        _title.Text = AppLocalizer.Get("DashboardTitle");
        _notice.Text = AppLocalizer.Get("DemoNotice");
        _languageLabel.Text = AppLocalizer.Get("LanguageLabel");
        _languageSelector.AccessibleName = AppLocalizer.Get("LanguageLabel");
        _search.PlaceholderText = AppLocalizer.Get("SearchServers");

        ConfigureButton(_addServer, "AddServer");
        ConfigureButton(_editServer, "EditServer");
        ConfigureButton(_removeServer, "RemoveServer");
        ConfigureButton(_manageCredentials, "ManageCredentials");
        ConfigureButton(_testConnection, "TestConnection");
        ConfigureButton(_testPrivileges, "TestPrivileges");
        ConfigureButton(_checkUpdates, "CheckForUpdates");

        var preferredIndex = _configuration.Language == "it-IT" ? 1 : 0;
        if (_languageSelector.SelectedIndex != preferredIndex)
        {
            _languageSelector.SelectedIndex = preferredIndex;
        }

        _servers.Columns.Clear();
        AddColumn("Id", "Id");
        AddColumn("Name", "ColumnName");
        AddColumn("Host", "ColumnHost");
        AddColumn("Group", "ColumnGroup");
        AddColumn("Distribution", "ColumnDistribution");
        AddColumn("Version", "ColumnVersion");
        AddColumn("Connection", "ColumnConnection");
        AddColumn("Updates", "ColumnUpdates");
        AddColumn("Security", "ColumnSecurity");
        AddColumn("Reboot", "ColumnReboot");
        AddColumn("Checked", "ColumnChecked");
        AddColumn("Operation", "ColumnOperation");
        _servers.Columns["Id"]!.Visible = false;
        RefreshRows();
        RefreshGroupFilter();
        ApplyFilters();
        UpdateCommandButtons();
    }

    private static string WindowTitle()
    {
        var version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return $"{AppLocalizer.Get("AppTitle")} {version}";
    }

    private void AddColumn(string name, string resourceKey) =>
        _servers.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = name == "Id" ? name : AppLocalizer.Get(resourceKey),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

    private void RefreshRows(Guid? selectServerId = null)
    {
        _servers.Rows.Clear();
        foreach (var server in _inventory.Servers)
        {
            _latestUpdateResults.TryGetValue(server.Id, out var result);
            var rowIndex = _servers.Rows.Add(
                server.Id,
                server.Name,
                server.Host,
                server.Group,
                result?.Distribution ?? "—",
                result?.Version ?? "—",
                AppLocalizer.Get(result is null ? "StatusNotConnected" : "StatusConnected"),
                result?.Packages.Count.ToString(AppLocalizer.CurrentCulture) ?? "—",
                FormatSecurityCount(result?.SecurityUpdateCount),
                result is null ? "—" : AppLocalizer.Get(result.RebootRequired ? "Yes" : "No"),
                result?.CheckedAt.ToLocalTime().ToString("g", AppLocalizer.CurrentCulture)
                    ?? AppLocalizer.Get("StatusNotChecked"),
                result is null ? AppLocalizer.Get("StatusIdle") : FormatUpdateSummary(result));
            _servers.Rows[rowIndex].Tag = server.Id;
        }

        if (selectServerId is Guid selectedId)
        {
            var row = _servers.Rows.Cast<DataGridViewRow>().FirstOrDefault(candidate =>
                candidate.Tag is Guid id && id == selectedId);
            if (row is not null)
            {
                row.Selected = true;
                _servers.CurrentCell = row.Cells["Name"];
            }
        }

        _status.Text = _inventory.Servers.Count == 0
            ? AppLocalizer.Get("NoServers")
            : AppLocalizer.Get("StatusInventoryReady");
    }

    private void RefreshGroupFilter()
    {
        var previous = _groupFilter.SelectedItem as string;
        _groupFilter.BeginUpdate();
        _groupFilter.Items.Clear();
        _groupFilter.Items.Add(AppLocalizer.Get("AllGroups"));
        foreach (var group in _inventory.Servers.Select(server => server.Group)
                     .Where(group => !string.IsNullOrWhiteSpace(group))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group, StringComparer.CurrentCultureIgnoreCase))
        {
            _groupFilter.Items.Add(group);
        }

        var previousIndex = previous is null ? -1 : _groupFilter.Items.IndexOf(previous);
        _groupFilter.SelectedIndex = previousIndex > 0 ? previousIndex : 0;
        _groupFilter.EndUpdate();
    }

    private static string FormatSecurityCount(int? count) =>
        count?.ToString(AppLocalizer.CurrentCulture) ?? AppLocalizer.Get("Unknown");

    private static string FormatUpdateSummary(UpdateCheckResult result) =>
        result.Packages.Count == 0
            ? AppLocalizer.Get("StatusUpToDate")
            : string.Format(AppLocalizer.Get("StatusUpdatesFound"), result.Packages.Count);

    private void ApplyFilters()
    {
        var search = _search.Text.Trim();
        var selectedGroup = _groupFilter.SelectedIndex > 0 ? _groupFilter.SelectedItem as string : null;
        foreach (DataGridViewRow row in _servers.Rows)
        {
            if (row.Tag is not Guid id)
            {
                continue;
            }

            var server = _inventory.Servers.FirstOrDefault(item => item.Id == id);
            if (server is null)
            {
                row.Visible = false;
                continue;
            }

            var matchesText = search.Length == 0 ||
                server.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                server.Host.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                server.Username.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                server.Group.Contains(search, StringComparison.CurrentCultureIgnoreCase);
            var matchesGroup = selectedGroup is null ||
                string.Equals(server.Group, selectedGroup, StringComparison.OrdinalIgnoreCase);
            row.Visible = matchesText && matchesGroup;
        }
    }

    private void ResizeWindowForLocalizedContent()
    {
        if (_servers.Columns.Count == 0)
        {
            return;
        }

        _servers.Columns["Operation"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.NotSet;
        _servers.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
        var gridWidth = _servers.Columns.Cast<DataGridViewColumn>()
            .Where(column => column.Visible)
            .Sum(column => column.Width);
        var contentPanel = (TableLayoutPanel)_servers.Parent!;
        var toolbarWidth = _toolbarActions.Controls.Cast<Control>()
            .Sum(control => (control is Button && control.AutoSize ? control.PreferredSize.Width : control.Width) +
                            control.Margin.Horizontal) + _toolbarActions.Padding.Horizontal;
        var desiredClientWidth = Math.Max(gridWidth, toolbarWidth) + contentPanel.Padding.Horizontal;
        var workingArea = Screen.FromControl(this).WorkingArea;
        var maximumOuterWidth = Math.Max(400, workingArea.Width - 32);
        var maximumOuterHeight = Math.Max(360, workingArea.Height - 32);
        var horizontalChrome = Width - ClientSize.Width;
        var minimumOuterWidth = Math.Min(800, maximumOuterWidth);
        var minimumOuterHeight = Math.Min(520, maximumOuterHeight);
        var maximumClientWidth = Math.Max(300, maximumOuterWidth - horizontalChrome);
        var minimumClientWidth = Math.Max(300, minimumOuterWidth - horizontalChrome);

        MinimumSize = new Size(minimumOuterWidth, minimumOuterHeight);
        _servers.Columns["Operation"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        if (WindowState != FormWindowState.Normal)
        {
            return;
        }

        var previousCenter = Left + Width / 2;
        var targetClientWidth = Math.Clamp(desiredClientWidth, minimumClientWidth, maximumClientWidth);
        ClientSize = new Size(targetClientWidth, ClientSize.Height);

        if (Visible)
        {
            Left = Math.Clamp(previousCenter - Width / 2, workingArea.Left, workingArea.Right - Width);
        }
    }

    private ServerProfile? SelectedServer()
    {
        if (_servers.SelectedRows.Count == 0 || _servers.SelectedRows[0].Tag is not Guid id)
        {
            return null;
        }

        return _inventory.Servers.FirstOrDefault(server => server.Id == id);
    }

    private void UpdateCommandButtons()
    {
        var hasSelection = SelectedServer() is not null;
        _editServer.Enabled = hasSelection;
        _removeServer.Enabled = hasSelection;
        _manageCredentials.Enabled = hasSelection;
        _testConnection.Enabled = hasSelection;
        _testPrivileges.Enabled = hasSelection;
        _checkUpdates.Enabled = hasSelection;
    }

    private async void TestConnectionClicked(object? sender, EventArgs e)
    {
        var selected = SelectedServer();
        if (selected is null)
        {
            ShowSelectServerMessage();
            return;
        }

        await RunSshCheckAsync(selected, testPrivileges: false);
    }

    private async void TestPrivilegesClicked(object? sender, EventArgs e)
    {
        var selected = SelectedServer();
        if (selected is null)
        {
            ShowSelectServerMessage();
            return;
        }

        await RunSshCheckAsync(selected, testPrivileges: true);
    }

    private async void CheckUpdatesClicked(object? sender, EventArgs e)
    {
        var selected = SelectedServer();
        if (selected is null)
        {
            ShowSelectServerMessage();
            return;
        }

        await RunUpdateCheckAsync(selected);
    }

    private void ServerCellDoubleClicked(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _servers.Rows[e.RowIndex].Tag is not Guid serverId ||
            !_latestUpdateResults.TryGetValue(serverId, out var result))
        {
            return;
        }

        var server = _inventory.Servers.FirstOrDefault(item => item.Id == serverId);
        if (server is null)
        {
            return;
        }

        using var details = new PackageUpdatesForm(server.Name, result);
        details.ShowDialog(this);
    }

    private async Task RunUpdateCheckAsync(ServerProfile server)
    {
        _testConnection.Enabled = false;
        _testPrivileges.Enabled = false;
        _checkUpdates.Enabled = false;
        SetServerStatus(server.Id, AppLocalizer.Get("StatusConnecting"), AppLocalizer.Get("StatusChecking"));
        _status.Text = AppLocalizer.Get("UpdateCheckInProgress");
        var connected = false;

        try
        {
            using var session = await _sshConnectionService.ConnectAsync(server, ConfirmUnknownHostKey);
            connected = true;
            if (session.NewlyTrustedFingerprint is { Length: > 0 } fingerprint &&
                !await SaveTrustedFingerprintAsync(server, fingerprint))
            {
                SetServerStatus(server.Id, AppLocalizer.Get("StatusNotConnected"), AppLocalizer.Get("StatusFailed"));
                return;
            }

            var result = await _updateDetectionService.CheckAsync(session, server);
            _latestUpdateResults[server.Id] = result;
            RefreshRows(server.Id);
            RefreshGroupFilter();
            ApplyFilters();
            ResizeWindowForLocalizedContent();
            _status.Text = string.Format(AppLocalizer.Get("UpdateCheckSucceeded"), result.Packages.Count);
        }
        catch (Exception exception)
        {
            SetServerStatus(server.Id,
                AppLocalizer.Get(connected ? "StatusConnected" : "StatusNotConnected"),
                AppLocalizer.Get("StatusFailed"));
            _status.Text = AppLocalizer.Get("UpdateCheckFailed");
            MessageBox.Show(this, exception.Message, AppLocalizer.Get("AppTitle"), MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            UpdateCommandButtons();
        }
    }

    private async Task RunSshCheckAsync(ServerProfile server, bool testPrivileges)
    {
        _testConnection.Enabled = false;
        _testPrivileges.Enabled = false;
        SetServerStatus(server.Id, AppLocalizer.Get("StatusConnecting"), AppLocalizer.Get("StatusChecking"));
        _status.Text = AppLocalizer.Get(testPrivileges ? "PrivilegeCheckInProgress" : "ConnectionCheckInProgress");

        try
        {
            using var session = await _sshConnectionService.ConnectAsync(server, ConfirmUnknownHostKey);
            if (session.NewlyTrustedFingerprint is { Length: > 0 } fingerprint &&
                !await SaveTrustedFingerprintAsync(server, fingerprint))
            {
                SetServerStatus(server.Id, AppLocalizer.Get("StatusNotConnected"), AppLocalizer.Get("StatusFailed"));
                return;
            }

            if (testPrivileges)
            {
                var hasRootAccess = await _privilegeElevationService.VerifyRootAccessAsync(session, server);
                SetServerStatus(server.Id, AppLocalizer.Get("StatusConnected"), AppLocalizer.Get(
                    hasRootAccess ? "StatusPrivilegesVerified" : "StatusPrivilegesDenied"));
                _status.Text = AppLocalizer.Get(hasRootAccess ? "PrivilegeCheckSucceeded" : "PrivilegeCheckFailed");
            }
            else
            {
                SetServerStatus(server.Id, AppLocalizer.Get("StatusConnected"), AppLocalizer.Get("StatusConnectionVerified"));
                _status.Text = AppLocalizer.Get("ConnectionCheckSucceeded");
            }
        }
        catch (HostKeyMismatchException exception)
        {
            var message = string.Format(AppLocalizer.Get("HostKeyMismatch"), exception.Host,
                FormatFingerprint(exception.ExpectedFingerprint), FormatFingerprint(exception.ReceivedFingerprint));
            SetServerStatus(server.Id, AppLocalizer.Get("StatusHostKeyChanged"), AppLocalizer.Get("StatusFailed"));
            _status.Text = AppLocalizer.Get("StatusFailed");
            MessageBox.Show(this, message, AppLocalizer.Get("AppTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (HostKeyRejectedException)
        {
            SetServerStatus(server.Id, AppLocalizer.Get("StatusNotConnected"), AppLocalizer.Get("StatusFailed"));
            _status.Text = AppLocalizer.Get("StatusFailed");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                           System.ComponentModel.Win32Exception or Renci.SshNet.Common.SshException or
                                           UnauthorizedAccessException or OperationCanceledException)
        {
            SetServerStatus(server.Id, AppLocalizer.Get("StatusNotConnected"), AppLocalizer.Get("StatusFailed"));
            _status.Text = AppLocalizer.Get("StatusFailed");
            MessageBox.Show(this, exception.Message, AppLocalizer.Get("AppTitle"), MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            UpdateCommandButtons();
        }
    }

    private bool ConfirmUnknownHostKey(HostKeyChallenge challenge)
    {
        if (InvokeRequired)
        {
            return (bool)Invoke(new Func<bool>(() => ConfirmUnknownHostKey(challenge)));
        }

        var message = string.Format(AppLocalizer.Get("UnknownHostKey"), challenge.Host, challenge.Port,
            challenge.KeyType, FormatFingerprint(challenge.Sha256Fingerprint));
        return MessageBox.Show(this, message, AppLocalizer.Get("AppTitle"), MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private async Task<bool> SaveTrustedFingerprintAsync(ServerProfile server, string fingerprint)
    {
        var updatedServer = CopyServer(server);
        updatedServer.TrustedHostKeyFingerprint = fingerprint;
        var updatedServers = _inventory.Servers.Select(item => item.Id == server.Id ? updatedServer : item).ToList();
        if (!await SaveInventoryAsync(updatedServers, server.Id))
        {
            return false;
        }

        _status.Text = AppLocalizer.Get("HostKeySaved");
        return true;
    }

    private static ServerProfile CopyServer(ServerProfile source) => new()
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

    private static string FormatFingerprint(string fingerprint) => $"SHA256:{fingerprint}";

    private void SetServerStatus(Guid serverId, string connectionStatus, string operationStatus)
    {
        var row = _servers.Rows.Cast<DataGridViewRow>().FirstOrDefault(candidate =>
            candidate.Tag is Guid id && id == serverId);
        if (row is null)
        {
            return;
        }

        row.Cells["Connection"].Value = connectionStatus;
        row.Cells["Operation"].Value = operationStatus;
    }

    private async void AddServerClicked(object? sender, EventArgs e)
    {
        using var editor = new ServerEditorForm(null);
        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var updatedServers = _inventory.Servers.ToList();
        updatedServers.Add(editor.Profile);
        if (await SaveInventoryAsync(updatedServers, editor.Profile.Id))
        {
            _latestUpdateResults.Remove(editor.Profile.Id);
            RefreshRows(editor.Profile.Id);
            ApplyFilters();
            _status.Text = AppLocalizer.Get("ServerSaved");
        }
    }

    private async void EditServerClicked(object? sender, EventArgs e)
    {
        var selected = SelectedServer();
        if (selected is null)
        {
            ShowSelectServerMessage();
            return;
        }

        using var editor = new ServerEditorForm(selected);
        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var updatedServers = _inventory.Servers.ToList();
        var index = updatedServers.FindIndex(server => server.Id == selected.Id);
        updatedServers[index] = editor.Profile;
        if (await SaveInventoryAsync(updatedServers, editor.Profile.Id))
        {
            _latestUpdateResults.Remove(editor.Profile.Id);
            RefreshRows(editor.Profile.Id);
            ApplyFilters();
            _status.Text = AppLocalizer.Get("ServerSaved");
        }
    }

    private async void RemoveServerClicked(object? sender, EventArgs e)
    {
        var selected = SelectedServer();
        if (selected is null)
        {
            ShowSelectServerMessage();
            return;
        }

        if (MessageBox.Show(this, AppLocalizer.Get("ConfirmRemoveServer"), AppLocalizer.Get("AppTitle"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var updatedServers = _inventory.Servers.Where(server => server.Id != selected.Id).ToList();
        if (!await SaveInventoryAsync(updatedServers))
        {
            return;
        }

        _latestUpdateResults.Remove(selected.Id);

        var cleanupFailed = false;
        foreach (var kind in Enum.GetValues<CredentialKind>())
        {
            try
            {
                _credentialService.Delete(selected.Id, kind);
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or PlatformNotSupportedException)
            {
                cleanupFailed = true;
            }
        }

        _status.Text = cleanupFailed
            ? AppLocalizer.Get("CredentialCleanupFailed")
            : AppLocalizer.Get("ServerRemoved");
    }

    private void ManageCredentialsClicked(object? sender, EventArgs e)
    {
        var selected = SelectedServer();
        if (selected is null)
        {
            ShowSelectServerMessage();
            return;
        }

        using var manager = new CredentialManagerForm(selected.Id, selected.Name, _credentialService);
        manager.ShowDialog(this);
    }

    private async Task<bool> SaveInventoryAsync(List<ServerProfile> updatedServers, Guid? selectServerId = null)
    {
        var updatedInventory = new ServerInventoryDocument
        {
            SchemaVersion = ServerInventoryDocument.CurrentSchemaVersion,
            Servers = updatedServers
        };

        try
        {
            await _inventoryService.SaveAsync(updatedInventory);
            _inventory.Servers = updatedInventory.Servers;
            RefreshRows(selectServerId);
            RefreshGroupFilter();
            ApplyFilters();
            ResizeWindowForLocalizedContent();
            UpdateCommandButtons();
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, AppLocalizer.Get("InventorySaveFailed"), AppLocalizer.Get("AppTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void ShowSelectServerMessage() =>
        MessageBox.Show(this, AppLocalizer.Get("SelectServer"), AppLocalizer.Get("AppTitle"),
            MessageBoxButtons.OK, MessageBoxIcon.Information);

    private async void LanguageSelectorChanged(object? sender, EventArgs e)
    {
        if (_languageSelector.SelectedIndex < 0)
        {
            return;
        }

        _configuration.Language = _languageSelector.SelectedIndex == 1 ? "it-IT" : "en-US";
        AppLocalizer.SetLanguage(_configuration.Language);
        ApplyLanguage();
        ResizeWindowForLocalizedContent();
        try
        {
            await _configurationService.SaveAsync(_configuration);
        }
        catch (IOException)
        {
            _status.Text = $"{AppLocalizer.Get("StatusInventoryReady")} · {AppLocalizer.Get("ConfigSaveFailed")}";
        }
        catch (UnauthorizedAccessException)
        {
            _status.Text = $"{AppLocalizer.Get("StatusInventoryReady")} · {AppLocalizer.Get("ConfigSaveFailed")}";
        }
    }
}
