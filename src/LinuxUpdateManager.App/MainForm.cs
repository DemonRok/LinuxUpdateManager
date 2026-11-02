using LinuxUpdateManager.App.Localization;
using LinuxUpdateManager.Core.Configuration;
using LinuxUpdateManager.Core.Security;
using LinuxUpdateManager.Core.Servers;

namespace LinuxUpdateManager.App;

internal sealed class MainForm : Form
{
    private readonly AppConfiguration _configuration;
    private readonly ConfigurationService _configurationService;
    private readonly ServerInventoryService _inventoryService;
    private readonly CredentialService _credentialService;
    private readonly ServerInventoryDocument _inventory;
    private readonly ComboBox _languageSelector = new();
    private readonly Label _languageLabel = new();
    private readonly Label _title = new();
    private readonly Label _notice = new();
    private readonly Label _status = new();
    private readonly DataGridView _servers = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _groupFilter = new();
    private readonly Button _addServer = new();
    private readonly Button _editServer = new();
    private readonly Button _removeServer = new();
    private readonly Button _manageCredentials = new();

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
        _inventory = inventory;

        Text = AppLocalizer.Get("AppTitle");
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? System.Drawing.SystemIcons.Application;
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

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true
        };
        ConfigureButton(_addServer, "AddServer");
        ConfigureButton(_editServer, "EditServer");
        ConfigureButton(_removeServer, "RemoveServer");
        ConfigureButton(_manageCredentials, "ManageCredentials");
        _search.Width = 190;
        _search.Margin = new Padding(8, 7, 4, 3);
        _groupFilter.Width = 150;
        _groupFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _groupFilter.Margin = new Padding(4, 6, 3, 3);
        toolbar.Controls.Add(_addServer);
        toolbar.Controls.Add(_editServer);
        toolbar.Controls.Add(_removeServer);
        toolbar.Controls.Add(_manageCredentials);
        toolbar.Controls.Add(_search);
        toolbar.Controls.Add(_groupFilter);

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
        root.Controls.Add(toolbar, 0, 2);
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
    }

    private void ApplyLanguage()
    {
        Text = AppLocalizer.Get("AppTitle");
        _title.Text = AppLocalizer.Get("DashboardTitle");
        _notice.Text = AppLocalizer.Get("DemoNotice");
        _languageLabel.Text = AppLocalizer.Get("LanguageLabel");
        _languageSelector.AccessibleName = AppLocalizer.Get("LanguageLabel");
        _search.PlaceholderText = AppLocalizer.Get("SearchServers");

        ConfigureButton(_addServer, "AddServer");
        ConfigureButton(_editServer, "EditServer");
        ConfigureButton(_removeServer, "RemoveServer");
        ConfigureButton(_manageCredentials, "ManageCredentials");

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
            var rowIndex = _servers.Rows.Add(
                server.Id,
                server.Name,
                server.Host,
                server.Group,
                "—",
                "—",
                AppLocalizer.Get("StatusNotConnected"),
                "—",
                "—",
                "—",
                AppLocalizer.Get("StatusNotChecked"),
                AppLocalizer.Get("StatusIdle"));
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
        var desiredClientWidth = gridWidth + contentPanel.Padding.Horizontal;
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
