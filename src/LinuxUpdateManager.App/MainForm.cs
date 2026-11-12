using System.Collections.Concurrent;
using System.ComponentModel;
using LinuxUpdateManager.App.Localization;
using LinuxUpdateManager.Core.Backups;
using LinuxUpdateManager.Core.Configuration;
using LinuxUpdateManager.Core.History;
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
    private readonly HistoryService _historyService;
    private readonly SshConnectionService _sshConnectionService;
    private readonly PrivilegeElevationService _privilegeElevationService;
    private readonly UpdateDetectionService _updateDetectionService;
    private readonly UpdateExecutionService _updateExecutionService;
    private readonly ApplicationUpdateService _applicationUpdateService = new();
    private readonly ApplicationUpdateInstaller _applicationUpdateInstaller;
    private readonly ServerInventoryDocument _inventory;
    private readonly ConcurrentDictionary<Guid, UpdateCheckResult> _latestUpdateResults = new();
    private readonly ConcurrentDictionary<Guid, byte> _staleUpdateResults = new();
    private readonly ConcurrentDictionary<Guid, byte> _rebootScheduledServers = new();
    private readonly SemaphoreSlim _inventorySaveGate = new(1, 1);
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
    private readonly Button _updateNow = new();
    private readonly Button _reboot = new();
    private readonly Button _openHistory = new();
    private readonly Button _openBackupManager = new();
    private readonly Button _supportProject = new();
    private readonly Button _checkApplicationUpdates = new();
    private string _sortColumnName = "Name";
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;
    private bool _operationInProgress;
    private bool _applicationUpdateCheckInProgress;
    private bool _applicationUpdateInProgress;

    public MainForm(
        AppConfiguration configuration,
        ConfigurationService configurationService,
        ServerInventoryService inventoryService,
        CredentialService credentialService,
        HistoryService historyService,
        ServerInventoryDocument inventory)
    {
        _configuration = configuration;
        _configurationService = configurationService;
        _inventoryService = inventoryService;
        _credentialService = credentialService;
        _historyService = historyService;
        _sshConnectionService = new SshConnectionService(credentialService);
        _privilegeElevationService = new PrivilegeElevationService(credentialService);
        _updateDetectionService = new UpdateDetectionService(_privilegeElevationService);
        _updateExecutionService = new UpdateExecutionService(_privilegeElevationService);
        _applicationUpdateInstaller = new ApplicationUpdateInstaller(_applicationUpdateService);
        _inventory = inventory;

        Text = WindowTitle();
        Icon = AppIcon.Create();
        MinimumSize = new Size(800, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        BuildLayout();
        ApplyLanguage();
        AttachEvents();
        Load += async (_, _) =>
        {
            ResizeWindowForLocalizedContent();
            if (_configuration.CheckForUpdatesOnStartup)
            {
                await CheckApplicationUpdatesAsync(showNoUpdateMessage: false);
            }
        };
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
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
        _toolbarActions.WrapContents = true;
        _toolbarActions.AutoScroll = false;
        _toolbarActions.AutoSize = true;
        _toolbarActions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _toolbarActions.Dock = DockStyle.Top;
        _toolbarActions.Margin = Padding.Empty;

        ConfigureButton(_addServer, "AddServer");
        ConfigureButton(_editServer, "EditServer");
        ConfigureButton(_removeServer, "RemoveServer");
        ConfigureButton(_manageCredentials, "ManageCredentials");
        ConfigureButton(_testConnection, "TestConnection");
        ConfigureButton(_testPrivileges, "TestPrivileges");
        ConfigureButton(_checkUpdates, "CheckForUpdates");
        ConfigureButton(_updateNow, "UpdateNow");
        ConfigureButton(_reboot, "RebootServer");
        ConfigureButton(_openHistory, "OperationHistory");
        ConfigureButton(_openBackupManager, "BackupRestore");
        ConfigureButton(_checkApplicationUpdates, "CheckApplicationUpdates");
        ConfigureButton(_supportProject, "SupportProject");
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
        _toolbarActions.Controls.Add(_updateNow);
        _toolbarActions.Controls.Add(_reboot);
        _toolbarActions.Controls.Add(_openHistory);
        _toolbarActions.Controls.Add(_openBackupManager);
        _toolbarActions.Controls.Add(_checkApplicationUpdates);
        _toolbarActions.Controls.Add(_supportProject);
        _toolbarActions.Controls.Add(_search);
        _toolbarActions.Controls.Add(_groupFilter);

        _servers.Dock = DockStyle.Fill;
        _servers.ReadOnly = true;
        _servers.AllowUserToAddRows = false;
        _servers.AllowUserToDeleteRows = false;
        _servers.AllowUserToResizeRows = false;
        _servers.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        _servers.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _servers.MultiSelect = true;
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
        _updateNow.Click += UpdateNowClicked;
        _reboot.Click += RebootClicked;
        _openHistory.Click += OpenHistoryClicked;
        _openBackupManager.Click += OpenBackupManagerClicked;
        _checkApplicationUpdates.Click += CheckApplicationUpdatesClicked;
        _supportProject.Click += SupportProjectClicked;
        _servers.CellDoubleClick += ServerCellDoubleClicked;
        _servers.ColumnHeaderMouseClick += ServerColumnHeaderMouseClicked;
        _servers.SortCompare += ServerSortCompare;
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
        ConfigureButton(_updateNow, "UpdateNow");
        ConfigureButton(_reboot, "RebootServer");
        ConfigureButton(_openHistory, "OperationHistory");
        ConfigureButton(_openBackupManager, "BackupRestore");
        ConfigureButton(_checkApplicationUpdates, "CheckApplicationUpdates");
        ConfigureButton(_supportProject, "SupportProject");

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
            SortMode = name == "Id" ? DataGridViewColumnSortMode.NotSortable : DataGridViewColumnSortMode.Programmatic
        });

    private void RefreshRows(Guid? selectServerId = null)
    {
        _servers.Rows.Clear();
        foreach (var server in _inventory.Servers.OrderBy(server => server.Name, StringComparer.CurrentCultureIgnoreCase))
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
                result is null
                    ? "—"
                    : AppLocalizer.Get(_rebootScheduledServers.ContainsKey(server.Id)
                        ? "RebootScheduled"
                        : result.RebootRequired ? "Yes" : "No"),
                result?.CheckedAt.ToLocalTime().ToString("g", AppLocalizer.CurrentCulture)
                    ?? AppLocalizer.Get("StatusNotChecked"),
                result is null
                    ? AppLocalizer.Get("StatusIdle")
                    : _staleUpdateResults.ContainsKey(server.Id)
                        ? AppLocalizer.Get("UpdateResultStale")
                        : FormatUpdateSummary(result));
            _servers.Rows[rowIndex].Tag = server.Id;
            _servers.Rows[rowIndex].Cells["Updates"].Tag = result?.Packages.Count;
            _servers.Rows[rowIndex].Cells["Security"].Tag = result?.SecurityUpdateCount;
            _servers.Rows[rowIndex].Cells["Checked"].Tag = result?.CheckedAt;
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

        ApplyCurrentSort();

        _status.Text = _inventory.Servers.Count == 0
            ? AppLocalizer.Get("NoServers")
            : AppLocalizer.Get("StatusInventoryReady");
    }

    private void ServerColumnHeaderMouseClicked(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0)
        {
            return;
        }

        var columnName = _servers.Columns[e.ColumnIndex].Name;
        if (columnName == "Id")
        {
            return;
        }

        if (_sortColumnName == columnName)
        {
            _sortDirection = _sortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            _sortColumnName = columnName;
            _sortDirection = ListSortDirection.Ascending;
        }

        ApplyCurrentSort();
    }

    private void ServerSortCompare(object? sender, DataGridViewSortCompareEventArgs e)
    {
        var firstRow = _servers.Rows[e.RowIndex1];
        var secondRow = _servers.Rows[e.RowIndex2];
        if (e.Column.Name is "Updates" or "Security")
        {
            e.SortResult = CompareNullableNumbers(
                firstRow.Cells[e.Column.Index].Tag,
                secondRow.Cells[e.Column.Index].Tag);
        }
        else if (e.Column.Name == "Checked")
        {
            e.SortResult = CompareNullableDates(
                firstRow.Cells[e.Column.Index].Tag,
                secondRow.Cells[e.Column.Index].Tag);
        }
        else
        {
            e.SortResult = StringComparer.CurrentCultureIgnoreCase.Compare(
                Convert.ToString(e.CellValue1, AppLocalizer.CurrentCulture),
                Convert.ToString(e.CellValue2, AppLocalizer.CurrentCulture));
        }

        e.Handled = true;
    }

    private void ApplyCurrentSort()
    {
        if (!_servers.Columns.Contains(_sortColumnName))
        {
            return;
        }

        foreach (DataGridViewColumn column in _servers.Columns)
        {
            column.HeaderCell.SortGlyphDirection = SortOrder.None;
        }

        var sortColumn = _servers.Columns[_sortColumnName]!;
        _servers.Sort(sortColumn, _sortDirection);
        sortColumn.HeaderCell.SortGlyphDirection = _sortDirection == ListSortDirection.Ascending
            ? SortOrder.Ascending
            : SortOrder.Descending;
    }

    private static int CompareNullableNumbers(object? first, object? second)
    {
        if (first is int firstValue)
        {
            return second is int secondValue ? firstValue.CompareTo(secondValue) : -1;
        }

        return second is int ? 1 : 0;
    }

    private static int CompareNullableDates(object? first, object? second)
    {
        if (first is DateTimeOffset firstValue)
        {
            return second is DateTimeOffset secondValue ? firstValue.CompareTo(secondValue) : -1;
        }

        return second is DateTimeOffset ? 1 : 0;
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

    private List<ServerProfile> SelectedServers()
    {
        var selectedIds = _servers.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<Guid>()
            .ToHashSet();
        return _inventory.Servers.Where(server => selectedIds.Contains(server.Id)).ToList();
    }

    private ServerProfile? SelectedServer() => SelectedServers().FirstOrDefault();

    private void UpdateCommandButtons()
    {
        var selected = SelectedServers();
        var busy = _operationInProgress || _applicationUpdateInProgress;
        var hasOneSelection = !busy && selected.Count == 1;
        var hasSelection = !busy && selected.Count > 0;

        _addServer.Enabled = !busy;
        _editServer.Enabled = hasOneSelection;
        _removeServer.Enabled = hasOneSelection;
        _manageCredentials.Enabled = hasOneSelection;
        _testConnection.Enabled = hasOneSelection;
        _testPrivileges.Enabled = hasOneSelection;
        _checkUpdates.Enabled = hasSelection;
        _updateNow.Enabled = hasSelection && selected.Any(server =>
            _latestUpdateResults.TryGetValue(server.Id, out var result) &&
            result.Packages.Count > 0 &&
            !_staleUpdateResults.ContainsKey(server.Id));
        _reboot.Enabled = hasSelection && selected.Any(server =>
            _latestUpdateResults.TryGetValue(server.Id, out var result) &&
            result.RebootRequired &&
            !_rebootScheduledServers.ContainsKey(server.Id));
        _openHistory.Enabled = !busy;
        _openBackupManager.Enabled = !busy;
        _checkApplicationUpdates.Enabled = !busy && !_applicationUpdateCheckInProgress;
        _supportProject.Enabled = !busy;
        _servers.Enabled = !busy;
    }

    private void OpenHistoryClicked(object? sender, EventArgs e)
    {
        using var history = new OperationHistoryForm(_historyService, _configuration, _configurationService);
        history.ShowDialog(this);
    }

    private void OpenBackupManagerClicked(object? sender, EventArgs e)
    {
        if (_operationInProgress)
        {
            return;
        }

        using var backup = new BackupManagerForm(
            _configuration,
            _inventory,
            _configurationService,
            _inventoryService,
            _credentialService,
            ApplyRestoredBackupAsync);
        backup.ShowDialog(this);
    }

    private void SupportProjectClicked(object? sender, EventArgs e)
    {
        using var contribution = new ContributionForm();
        contribution.ShowDialog(this);
    }

    private void CheckApplicationUpdatesClicked(object? sender, EventArgs e) =>
        _ = CheckApplicationUpdatesAsync(showNoUpdateMessage: true);

    private async Task CheckApplicationUpdatesAsync(bool showNoUpdateMessage)
    {
        if (_applicationUpdateCheckInProgress || _applicationUpdateInProgress)
        {
            return;
        }

        _applicationUpdateCheckInProgress = true;
        UpdateCommandButtons();
        _status.Text = AppLocalizer.Get("CheckingApplicationUpdates");
        var updaterStarted = false;
        try
        {
            var currentVersion = GetType().Assembly.GetName().Version ?? new Version(0, 0, 0);
            var release = await _applicationUpdateService.CheckForUpdateAsync(currentVersion);
            if (release is null)
            {
                _status.Text = AppLocalizer.Get("StatusInventoryReady");
                if (showNoUpdateMessage)
                {
                    MessageBox.Show(this, AppLocalizer.Get("NoApplicationUpdates"),
                        AppLocalizer.Get("ApplicationUpdateTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return;
            }

            using var prompt = new ApplicationUpdateForm(release.Version, release.ReleaseNotes);
            if (prompt.ShowDialog(this) != DialogResult.OK)
            {
                _status.Text = AppLocalizer.Get("StatusInventoryReady");
                return;
            }

            if (_operationInProgress)
            {
                MessageBox.Show(this, AppLocalizer.Get("ApplicationUpdateBusy"),
                    AppLocalizer.Get("ApplicationUpdateTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _applicationUpdateInProgress = true;
            UpdateCommandButtons();
            _status.Text = AppLocalizer.Get("DownloadingApplicationUpdate");
            await _applicationUpdateInstaller.StartAsync(release);
            updaterStarted = true;
            Application.Exit();
        }
        catch (Exception exception)
        {
            _status.Text = AppLocalizer.Get("StatusInventoryReady");
            if (showNoUpdateMessage || exception is not HttpRequestException)
            {
                MessageBox.Show(this,
                    string.Format(AppLocalizer.Get("ApplicationUpdateFailed"), exception.Message),
                    AppLocalizer.Get("ApplicationUpdateTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            _applicationUpdateCheckInProgress = false;
            if (!updaterStarted)
            {
                _applicationUpdateInProgress = false;
            }

            if (!IsDisposed)
            {
                UpdateCommandButtons();
            }
        }
    }

    private Task ApplyRestoredBackupAsync(BackupRestoreResult result)
    {
        _latestUpdateResults.Clear();
        _staleUpdateResults.Clear();
        _rebootScheduledServers.Clear();
        _inventory.Servers = result.Inventory.Servers;
        _configuration.SchemaVersion = result.Configuration.SchemaVersion;
        _configuration.Language = result.Configuration.Language;
        _configuration.SshConnectionTimeoutSeconds = result.Configuration.SshConnectionTimeoutSeconds;
        _configuration.CommandTimeoutSeconds = result.Configuration.CommandTimeoutSeconds;
        _configuration.MaximumConcurrentOperations = result.Configuration.MaximumConcurrentOperations;
        _configuration.CheckForUpdatesOnStartup = result.Configuration.CheckForUpdatesOnStartup;
        _configuration.EnableFileLogging = result.Configuration.EnableFileLogging;
        _configuration.HistoryRetentionDays = result.Configuration.HistoryRetentionDays;
        AppLocalizer.SetLanguage(_configuration.Language);
        ApplyLanguage();
        RefreshGroupFilter();
        ApplyFilters();
        ResizeWindowForLocalizedContent();
        UpdateCommandButtons();
        return Task.CompletedTask;
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
        var selected = SelectedServers();
        if (selected.Count == 0)
        {
            ShowSelectServerMessage();
            return;
        }

        if (selected.Count == 1)
        {
            await RunUpdateCheckAsync(selected[0]);
            return;
        }

        await RunMultipleUpdateChecksAsync(selected);
    }

    private async void UpdateNowClicked(object? sender, EventArgs e)
    {
        if (_operationInProgress)
        {
            return;
        }

        var selected = SelectedServers();
        var targets = selected.Where(server =>
                _latestUpdateResults.TryGetValue(server.Id, out var result) &&
                result.Packages.Count > 0 &&
                !_staleUpdateResults.ContainsKey(server.Id))
            .ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, AppLocalizer.Get("NoUpdatesToInstall"), AppLocalizer.Get("AppTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var packageCount = targets.Sum(server => _latestUpdateResults[server.Id].Packages.Count);
        var confirmation = string.Format(AppLocalizer.Get("ConfirmUpdate"), packageCount, targets.Count);
        if (MessageBox.Show(this, confirmation, AppLocalizer.Get("AppTitle"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        await RunPackageUpdatesAsync(targets);
    }

    private async void RebootClicked(object? sender, EventArgs e)
    {
        if (_operationInProgress)
        {
            return;
        }

        var targets = SelectedServers()
            .Where(server =>
                _latestUpdateResults.TryGetValue(server.Id, out var result) &&
                result.RebootRequired &&
                !_rebootScheduledServers.ContainsKey(server.Id))
            .ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, AppLocalizer.Get("NoRebootRequired"), AppLocalizer.Get("AppTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await OfferRebootAsync(targets, this);
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
        var startedAt = DateTimeOffset.UtcNow;
        SetOperationInProgress(true);
        _staleUpdateResults.TryAdd(server.Id, 0);
        SetServerStatus(server.Id, AppLocalizer.Get("StatusConnecting"), AppLocalizer.Get("StatusChecking"));
        _status.Text = AppLocalizer.Get("UpdateCheckInProgress");
        var connected = false;
        UpdateCheckResult? historyResult = null;
        var historyStatus = HistoryOperationResults.Failed;
        string? historyError = null;
        var historyOutput = string.Empty;

        try
        {
            using var session = await _sshConnectionService.ConnectAsync(server, ConfirmUnknownHostKey);
            connected = true;
            if (session.NewlyTrustedFingerprint is { Length: > 0 } fingerprint &&
                !await SaveTrustedFingerprintAsync(server, fingerprint))
            {
                SetServerStatus(server.Id, AppLocalizer.Get("StatusNotConnected"), AppLocalizer.Get("StatusFailed"));
                historyError = "The server host key could not be saved.";
                return;
            }

            var result = await _updateDetectionService.CheckAsync(session, server);
            historyResult = result;
            historyStatus = HistoryOperationResults.Succeeded;
            historyOutput = FormatHistoryUpdateResult(result);
            _latestUpdateResults[server.Id] = result;
            if (!result.RebootRequired)
            {
                _rebootScheduledServers.TryRemove(server.Id, out _);
            }
            _staleUpdateResults.TryRemove(server.Id, out _);
            RefreshRows(server.Id);
            RefreshGroupFilter();
            ApplyFilters();
            ResizeWindowForLocalizedContent();
            _status.Text = string.Format(AppLocalizer.Get("UpdateCheckSucceeded"), result.Packages.Count);
        }
        catch (Exception exception)
        {
            historyError = exception.Message;
            SetServerStatus(server.Id,
                AppLocalizer.Get(connected ? "StatusConnected" : "StatusNotConnected"),
                AppLocalizer.Get("StatusFailed"));
            _status.Text = AppLocalizer.Get("UpdateCheckFailed");
            MessageBox.Show(this, exception.Message, AppLocalizer.Get("AppTitle"), MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            await RecordHistoryBestEffortAsync(
                server,
                HistoryOperationTypes.UpdateCheck,
                startedAt,
                historyStatus,
                exitStatus: null,
                historyOutput,
                historyError,
                historyResult?.Packages.Count,
                historyResult?.SecurityUpdateCount);
            SetOperationInProgress(false);
        }

        if (historyStatus == HistoryOperationResults.Succeeded && historyResult?.RebootRequired == true)
        {
            await OfferRebootAsync([server], this);
        }
    }

    private async Task RunMultipleUpdateChecksAsync(IReadOnlyList<ServerProfile> servers)
    {
        SetOperationInProgress(true);
        using var progress = new UpdateExecutionForm();
        progress.SetStatus(AppLocalizer.Get("UpdateCheckInProgress"));
        var connectedServers = new ConcurrentDictionary<Guid, byte>();
        IReadOnlyList<ServerProfile> rebootTargets = [];

        progress.Shown += async (_, _) =>
        {
            try
            {
                var outcomes = await RunServerOperationsAsync(
                    servers,
                    (server, cancellationToken) => RunUpdateCheckForServerAsync(
                        server, cancellationToken, progress, connectedServers),
                    progress.CancellationToken,
                    outcome => AppendOperationOutcome(progress, outcome));

                var summary = FormatOperationSummary(outcomes);
                rebootTargets = outcomes
                    .Where(outcome => outcome.Result == ServerOperationResult.Completed &&
                                      _latestUpdateResults.TryGetValue(outcome.Server.Id, out var result) &&
                                      result.RebootRequired)
                    .Select(outcome => outcome.Server)
                    .ToArray();
                progress.Complete(summary);
                _status.Text = summary;
                ResizeWindowForLocalizedContent();
                foreach (var outcome in outcomes)
                {
                    var connectionStatus = connectedServers.ContainsKey(outcome.Server.Id)
                        ? AppLocalizer.Get("StatusConnected")
                        : AppLocalizer.Get("StatusNotConnected");
                    var operationStatus = outcome.Result == ServerOperationResult.Completed &&
                                          _latestUpdateResults.TryGetValue(outcome.Server.Id, out var result)
                        ? FormatUpdateSummary(result)
                        : OperationStatus(outcome);
                    SetServerStatusFromAnyThread(
                        outcome.Server.Id, connectionStatus, operationStatus);
                }
            }
            catch (Exception exception)
            {
                progress.AppendOutput(exception.Message + Environment.NewLine);
                progress.Complete(AppLocalizer.Get("UpdateCheckFailed"));
                _status.Text = AppLocalizer.Get("UpdateCheckFailed");
            }
        };

        progress.ShowDialog(this);
        SetOperationInProgress(false);
        if (rebootTargets.Count > 0)
        {
            await OfferRebootAsync(rebootTargets, this);
        }
    }

    private async Task RunUpdateCheckForServerAsync(
        ServerProfile server,
        CancellationToken cancellationToken,
        UpdateExecutionForm progress,
        ConcurrentDictionary<Guid, byte> connectedServers)
    {
        var startedAt = DateTimeOffset.UtcNow;
        UpdateCheckResult? result = null;
        var status = HistoryOperationResults.Failed;
        string? error = null;
        var output = string.Empty;
        try
        {
            progress.SetStatus(AppLocalizer.Get("UpdateCheckInProgress"));
            progress.AppendOutput(string.Format(
                AppLocalizer.Get("UpdateServerConnecting"), server.Name, server.Host) + Environment.NewLine);
            _staleUpdateResults.TryAdd(server.Id, 0);
            SetServerStatusFromAnyThread(server.Id, AppLocalizer.Get("StatusConnecting"), AppLocalizer.Get("StatusChecking"));

            using var session = await _sshConnectionService.ConnectAsync(
                server, ConfirmUnknownHostKey, cancellationToken);
            connectedServers.TryAdd(server.Id, 0);
            if (session.NewlyTrustedFingerprint is { Length: > 0 } fingerprint &&
                !await InvokeOnUiAsync(() => SaveTrustedFingerprintAsync(server, fingerprint)))
            {
                throw new IOException("The trusted host key could not be saved.");
            }

            result = await _updateDetectionService.CheckAsync(session, server, cancellationToken);
            _latestUpdateResults[server.Id] = result;
            if (!result.RebootRequired)
            {
                _rebootScheduledServers.TryRemove(server.Id, out _);
            }
            _staleUpdateResults.TryRemove(server.Id, out _);
            await InvokeOnUiAsync(() => RefreshServerUpdateResult(server.Id, result));
            progress.AppendOutput(string.Format(
                AppLocalizer.Get("UpdateCheckSucceeded"), result.Packages.Count) +
                $" [{server.Name}]{Environment.NewLine}");
            output = FormatHistoryUpdateResult(result);
            status = HistoryOperationResults.Succeeded;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            status = exception is OperationCanceledException || cancellationToken.IsCancellationRequested
                ? HistoryOperationResults.Cancelled
                : HistoryOperationResults.Failed;
            throw;
        }
        finally
        {
            await RecordHistoryBestEffortAsync(
                server,
                HistoryOperationTypes.UpdateCheck,
                startedAt,
                status,
                null,
                output,
                error,
                result?.Packages.Count,
                result?.SecurityUpdateCount);
        }
    }

    private async Task RunPackageUpdatesAsync(IReadOnlyList<ServerProfile> servers)
    {
        SetOperationInProgress(true);
        using var progress = new UpdateExecutionForm();
        progress.SetStatus(AppLocalizer.Get("UpdateRunning"));
        var connectedServers = new ConcurrentDictionary<Guid, byte>();
        var attemptedServers = new ConcurrentDictionary<Guid, byte>();
        var phasedPackages = new ConcurrentDictionary<Guid, IReadOnlyList<string>>();
        var rebootTargets = new List<ServerProfile>();

        progress.Shown += async (_, _) =>
        {
            try
            {
                var outcomes = await RunServerOperationsAsync(
                    servers,
                    (server, cancellationToken) => RunPackageUpgradeForServerAsync(
                        server, cancellationToken, progress, connectedServers, attemptedServers, phasedPackages),
                    progress.CancellationToken,
                    outcome => AppendOperationOutcome(progress, outcome));

                foreach (var serverId in attemptedServers.Keys)
                {
                    _staleUpdateResults.TryAdd(serverId, 0);
                }

                foreach (var outcome in outcomes.Where(outcome => outcome.Result == ServerOperationResult.Completed))
                {
                    if (progress.CancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (phasedPackages.TryGetValue(outcome.Server.Id, out var deferredPackages))
                    {
                        await OfferPhasedUpdatesAsync(outcome.Server, deferredPackages, progress,
                            progress.CancellationToken);
                    }
                }

                foreach (var outcome in outcomes.Where(outcome => outcome.Result == ServerOperationResult.Completed))
                {
                    if (progress.CancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await OfferAutoremoveAsync(outcome.Server, progress, progress.CancellationToken);
                }

                foreach (var outcome in outcomes.Where(outcome => outcome.Result == ServerOperationResult.Completed))
                {
                    try
                    {
                        using var session = await _sshConnectionService.ConnectAsync(
                            outcome.Server, ConfirmUnknownHostKey, CancellationToken.None);
                        if (session.NewlyTrustedFingerprint is { Length: > 0 } fingerprint &&
                            !await InvokeOnUiAsync(() => SaveTrustedFingerprintAsync(outcome.Server, fingerprint)))
                        {
                            throw new IOException("The trusted host key could not be saved.");
                        }

                        var rebootRequired = await _updateExecutionService.CheckRebootRequiredAsync(
                            session, outcome.Server, CancellationToken.None);
                        if (_latestUpdateResults.TryGetValue(outcome.Server.Id, out var result))
                        {
                            var refreshedResult = result with { RebootRequired = rebootRequired };
                            _latestUpdateResults[outcome.Server.Id] = refreshedResult;
                            if (!rebootRequired)
                            {
                                _rebootScheduledServers.TryRemove(outcome.Server.Id, out _);
                            }

                            RefreshServerUpdateResult(outcome.Server.Id, refreshedResult);
                        }

                        if (rebootRequired)
                        {
                            rebootTargets.Add(outcome.Server);
                        }
                    }
                    catch (Exception exception)
                    {
                        progress.AppendOutput(string.Format(
                            AppLocalizer.Get("RebootCheckFailed"), outcome.Server.Name, exception.Message) +
                            Environment.NewLine);
                    }
                }

                var summary = FormatOperationSummary(outcomes);
                progress.Complete(summary);
                _status.Text = summary;
                foreach (var outcome in outcomes)
                {
                    var connectionStatus = connectedServers.ContainsKey(outcome.Server.Id)
                        ? AppLocalizer.Get("StatusConnected")
                        : AppLocalizer.Get("StatusNotConnected");
                    SetServerStatusFromAnyThread(
                        outcome.Server.Id, connectionStatus, OperationStatus(outcome));
                }
            }
            catch (Exception exception)
            {
                progress.AppendOutput(exception.Message + Environment.NewLine);
                progress.Complete(AppLocalizer.Get("UpdateFailed"));
                _status.Text = AppLocalizer.Get("UpdateFailed");
            }
        };

        progress.ShowDialog(this);
        SetOperationInProgress(false);
        if (rebootTargets.Count > 0)
        {
            await OfferRebootAsync(rebootTargets, this);
        }
    }

    private async Task RunPackageUpgradeForServerAsync(
        ServerProfile server,
        CancellationToken cancellationToken,
        UpdateExecutionForm progress,
        ConcurrentDictionary<Guid, byte> connectedServers,
        ConcurrentDictionary<Guid, byte> attemptedServers,
        ConcurrentDictionary<Guid, IReadOnlyList<string>> phasedPackages)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var updateCount = _latestUpdateResults.TryGetValue(server.Id, out var updateResult)
            ? updateResult.Packages.Count
            : 0;
        var output = new OperationOutputBuffer();
        var status = HistoryOperationResults.Failed;
        string? error = null;
        int? exitStatus = null;
        try
        {
            progress.SetStatus(string.Format(AppLocalizer.Get("UpdateServerRunning"), server.Name, updateCount));
            var connecting = string.Format(
                AppLocalizer.Get("UpdateServerConnecting"), server.Name, server.Host) + Environment.NewLine;
            progress.AppendOutput(connecting);
            output.Append(connecting);
            SetServerStatusFromAnyThread(server.Id, AppLocalizer.Get("StatusConnecting"), AppLocalizer.Get("StatusChecking"));

            using var session = await _sshConnectionService.ConnectAsync(
                server, ConfirmUnknownHostKey, cancellationToken);
            connectedServers.TryAdd(server.Id, 0);
            if (session.NewlyTrustedFingerprint is { Length: > 0 } fingerprint &&
                !await InvokeOnUiAsync(() => SaveTrustedFingerprintAsync(server, fingerprint)))
            {
                throw new IOException("The trusted host key could not be saved.");
            }

            if (updateCount == 0)
            {
                throw new InvalidOperationException("No package updates are available for this server.");
            }

            attemptedServers.TryAdd(server.Id, 0);
            SetServerStatusFromAnyThread(
                server.Id, AppLocalizer.Get("StatusConnected"), AppLocalizer.Get("UpdateRunning"));
            var running = string.Format(AppLocalizer.Get("UpdateServerRunning"), server.Name, updateCount) + Environment.NewLine;
            progress.AppendOutput(running);
            output.Append(running);
            var formattedOutput = new PrefixedOperationOutput(server.Name, text =>
            {
                output.Append(text);
                progress.AppendOutput(text);
            });
            SshCommandResult commandResult;
            try
            {
                commandResult = await _updateExecutionService.UpgradeAsync(
                    session, server, formattedOutput.Append, cancellationToken);
            }
            finally
            {
                formattedOutput.Flush();
            }

            var deferredPackages = _updateExecutionService.ParsePhasedUpdatePackages(commandResult);
            if (deferredPackages.Count > 0)
            {
                phasedPackages[server.Id] = deferredPackages;
            }

            exitStatus = commandResult.ExitStatus;
            status = HistoryOperationResults.Succeeded;
            var completed = string.Format(AppLocalizer.Get("UpdateServerCompleted"), server.Name) + Environment.NewLine;
            progress.AppendOutput(completed);
            output.Append(completed);
        }
        catch (Exception exception)
        {
            if (exception is PackageUpgradeFailedException commandFailure)
            {
                exitStatus = commandFailure.ExitStatus;
            }

            error = exception.Message;
            status = exception is OperationCanceledException || cancellationToken.IsCancellationRequested
                ? HistoryOperationResults.Cancelled
                : HistoryOperationResults.Failed;
            throw;
        }
        finally
        {
            await RecordHistoryBestEffortAsync(
                server,
                HistoryOperationTypes.PackageUpgrade,
                startedAt,
                status,
                exitStatus,
                output.ToString(),
                error,
                updateCount,
                updateResult?.SecurityUpdateCount);
        }
    }

    private async Task OfferPhasedUpdatesAsync(
        ServerProfile server,
        IReadOnlyList<string> packages,
        UpdateExecutionForm progress,
        CancellationToken cancellationToken)
    {
        var packageList = string.Join(Environment.NewLine, packages.Select(package => $"• {package}"));
        var message = string.Format(AppLocalizer.Get("PhasedUpdatesPrompt"), server.Name, packageList);
        if (MessageBox.Show(progress, message, AppLocalizer.Get("AppTitle"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            progress.AppendOutput(string.Format(
                AppLocalizer.Get("PhasedUpdatesDeclined"), server.Name, string.Join(", ", packages)) +
                Environment.NewLine);
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var output = new OperationOutputBuffer();
        var status = HistoryOperationResults.Failed;
        int? exitStatus = null;
        string? error = null;
        try
        {
            using var session = await _sshConnectionService.ConnectAsync(
                server, ConfirmUnknownHostKey, cancellationToken);
            if (session.NewlyTrustedFingerprint is { Length: > 0 } fingerprint &&
                !await InvokeOnUiAsync(() => SaveTrustedFingerprintAsync(server, fingerprint)))
            {
                throw new IOException("The trusted host key could not be saved.");
            }

            var formattedOutput = new PrefixedOperationOutput(server.Name, text =>
            {
                output.Append(text);
                progress.AppendOutput(text);
            });
            SshCommandResult commandResult;
            try
            {
                commandResult = await _updateExecutionService.UpgradeAsync(
                    session, server, formattedOutput.Append, cancellationToken, includePhasedUpdates: true);
            }
            finally
            {
                formattedOutput.Flush();
            }

            exitStatus = commandResult.ExitStatus;
            status = HistoryOperationResults.Succeeded;
            progress.AppendOutput(string.Format(
                AppLocalizer.Get("PhasedUpdatesApplied"), server.Name) + Environment.NewLine);
        }
        catch (Exception exception)
        {
            if (exception is PackageUpgradeFailedException commandFailure)
            {
                exitStatus = commandFailure.ExitStatus;
            }

            error = exception.Message;
            status = exception is OperationCanceledException || cancellationToken.IsCancellationRequested
                ? HistoryOperationResults.Cancelled
                : HistoryOperationResults.Failed;
            progress.AppendOutput(string.Format(
                AppLocalizer.Get("PhasedUpdatesFailed"), server.Name, exception.Message) +
                Environment.NewLine);
        }
        finally
        {
            await RecordHistoryBestEffortAsync(
                server,
                HistoryOperationTypes.PackageUpgrade,
                startedAt,
                status,
                exitStatus,
                output.ToString(),
                error,
                packages.Count);
        }
    }

    private async Task OfferAutoremoveAsync(
        ServerProfile server,
        UpdateExecutionForm progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> packages;
        try
        {
            using var session = await _sshConnectionService.ConnectAsync(
                server, ConfirmUnknownHostKey, cancellationToken);
            if (session.NewlyTrustedFingerprint is { Length: > 0 } fingerprint &&
                !await InvokeOnUiAsync(() => SaveTrustedFingerprintAsync(server, fingerprint)))
            {
                throw new IOException("The trusted host key could not be saved.");
            }

            packages = await _updateExecutionService.PreviewAutoremoveAsync(session, server, cancellationToken);
        }
        catch (NotSupportedException)
        {
            return;
        }
        catch (Exception exception)
        {
            progress.AppendOutput(string.Format(
                AppLocalizer.Get("AutoremovePreviewFailed"), server.Name, exception.Message) +
                Environment.NewLine);
            return;
        }

        if (packages.Count == 0)
        {
            return;
        }

        var packageList = string.Join(Environment.NewLine, packages.Select(package => $"• {package}"));
        var message = string.Format(AppLocalizer.Get("AutoremovePrompt"), server.Name, packageList);
        if (MessageBox.Show(progress, message, AppLocalizer.Get("AppTitle"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            progress.AppendOutput(string.Format(
                AppLocalizer.Get("AutoremoveDeclined"), server.Name) + Environment.NewLine);
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var output = new OperationOutputBuffer();
        var status = HistoryOperationResults.Failed;
        int? exitStatus = null;
        string? error = null;
        try
        {
            using var session = await _sshConnectionService.ConnectAsync(
                server, ConfirmUnknownHostKey, cancellationToken);
            if (session.NewlyTrustedFingerprint is { Length: > 0 } fingerprint &&
                !await InvokeOnUiAsync(() => SaveTrustedFingerprintAsync(server, fingerprint)))
            {
                throw new IOException("The trusted host key could not be saved.");
            }

            var formattedOutput = new PrefixedOperationOutput(server.Name, text =>
            {
                output.Append(text);
                progress.AppendOutput(text);
            });
            SshCommandResult commandResult;
            try
            {
                commandResult = await _updateExecutionService.AutoremoveAsync(
                    session, server, formattedOutput.Append, cancellationToken);
            }
            finally
            {
                formattedOutput.Flush();
            }

            exitStatus = commandResult.ExitStatus;
            status = HistoryOperationResults.Succeeded;
            progress.AppendOutput(string.Format(
                AppLocalizer.Get("AutoremoveCompleted"), server.Name) + Environment.NewLine);
        }
        catch (Exception exception)
        {
            if (exception is PackageUpgradeFailedException commandFailure)
            {
                exitStatus = commandFailure.ExitStatus;
            }

            error = exception.Message;
            status = exception is OperationCanceledException || cancellationToken.IsCancellationRequested
                ? HistoryOperationResults.Cancelled
                : HistoryOperationResults.Failed;
            progress.AppendOutput(string.Format(
                AppLocalizer.Get("AutoremoveFailed"), server.Name, exception.Message) +
                Environment.NewLine);
        }
        finally
        {
            await RecordHistoryBestEffortAsync(
                server,
                HistoryOperationTypes.Autoremove,
                startedAt,
                status,
                exitStatus,
                output.ToString(),
                error,
                packages.Count);
        }
    }

    private async Task OfferRebootAsync(IReadOnlyList<ServerProfile> servers, IWin32Window owner)
    {
        var targets = servers
            .Where(server => !_rebootScheduledServers.ContainsKey(server.Id))
            .DistinctBy(server => server.Id)
            .ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        var message = string.Format(
            AppLocalizer.Get("ConfirmReboot"),
            string.Join(Environment.NewLine, targets.Select(server => $"• {server.Name} ({server.Host})")));
        if (MessageBox.Show(owner, message, AppLocalizer.Get("AppTitle"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        await RunServerRebootsAsync(targets);
    }

    private async Task RunServerRebootsAsync(IReadOnlyList<ServerProfile> servers)
    {
        SetOperationInProgress(true);
        using var progress = new UpdateExecutionForm();
        progress.SetStatus(AppLocalizer.Get("RebootInProgress"));
        var connectedServers = new ConcurrentDictionary<Guid, byte>();
        progress.Shown += async (_, _) =>
        {
            try
            {
                var outcomes = await RunServerOperationsAsync(
                    servers,
                    async (server, cancellationToken) =>
                    {
                        var startedAt = DateTimeOffset.UtcNow;
                        var output = new OperationOutputBuffer();
                        var status = HistoryOperationResults.Failed;
                        string? error = null;
                        try
                        {
                            var connecting = string.Format(
                                AppLocalizer.Get("UpdateServerConnecting"), server.Name, server.Host) +
                                Environment.NewLine;
                            progress.AppendOutput(connecting);
                            output.Append(connecting);
                            using var session = await _sshConnectionService.ConnectAsync(
                                server, ConfirmUnknownHostKey, cancellationToken);
                            connectedServers.TryAdd(server.Id, 0);
                            if (session.NewlyTrustedFingerprint is { Length: > 0 } fingerprint &&
                                !await InvokeOnUiAsync(() => SaveTrustedFingerprintAsync(server, fingerprint)))
                            {
                                throw new IOException("The trusted host key could not be saved.");
                            }

                            var formattedOutput = new PrefixedOperationOutput(server.Name, text =>
                            {
                                output.Append(text);
                                progress.AppendOutput(text);
                            });
                            try
                            {
                                var commandResult = await _updateExecutionService.RebootAsync(
                                    session, server, cancellationToken);
                                formattedOutput.Append(commandResult.Output);
                                formattedOutput.Append(commandResult.Error);
                            }
                            finally
                            {
                                formattedOutput.Flush();
                            }

                            status = HistoryOperationResults.Succeeded;
                            _rebootScheduledServers.TryAdd(server.Id, 0);
                            if (_latestUpdateResults.TryGetValue(server.Id, out var latestResult))
                            {
                                await InvokeOnUiAsync(() => RefreshServerUpdateResult(server.Id, latestResult));
                            }

                            var scheduled = string.Format(
                                AppLocalizer.Get("RebootScheduledForServer"), server.Name) +
                                Environment.NewLine;
                            progress.AppendOutput(scheduled);
                            output.Append(scheduled);
                        }
                        catch (Exception exception)
                        {
                            error = exception.Message;
                            status = exception is OperationCanceledException || cancellationToken.IsCancellationRequested
                                ? HistoryOperationResults.Cancelled
                                : HistoryOperationResults.Failed;
                            throw;
                        }
                        finally
                        {
                            await RecordHistoryBestEffortAsync(
                                server,
                                HistoryOperationTypes.Reboot,
                                startedAt,
                                status,
                                null,
                                output.ToString(),
                                error);
                        }
                    },
                    progress.CancellationToken,
                    outcome => AppendOperationOutcome(progress, outcome));
                var summary = FormatOperationSummary(outcomes);
                progress.Complete(summary);
                _status.Text = summary;
            }
            catch (Exception exception)
            {
                progress.AppendOutput(exception.Message + Environment.NewLine);
                progress.Complete(AppLocalizer.Get("RebootFailed"));
                _status.Text = AppLocalizer.Get("RebootFailed");
            }
        };

        progress.ShowDialog(this);
        SetOperationInProgress(false);
    }

    private async Task<IReadOnlyList<ServerOperationOutcome>> RunServerOperationsAsync(
        IReadOnlyList<ServerProfile> servers,
        Func<ServerProfile, CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        Action<ServerOperationOutcome>? outcomeReceived = null)
    {
        var concurrency = Math.Clamp(_configuration.MaximumConcurrentOperations, 1, 32);
        using var gate = new SemaphoreSlim(concurrency, concurrency);
        var outcomes = new ConcurrentDictionary<Guid, ServerOperationOutcome>();
        var tasks = servers.Select(async server =>
        {
            var enteredGate = false;
            try
            {
                await gate.WaitAsync(cancellationToken);
                enteredGate = true;
                cancellationToken.ThrowIfCancellationRequested();
                await operation(server, cancellationToken);
                outcomes[server.Id] = new ServerOperationOutcome(server, ServerOperationResult.Completed, null);
            }
            catch (OperationCanceledException)
            {
                outcomes[server.Id] = new ServerOperationOutcome(server, ServerOperationResult.Cancelled, null);
            }
            catch (Exception exception)
            {
                outcomes[server.Id] = new ServerOperationOutcome(server, ServerOperationResult.Failed, exception.Message);
            }
            finally
            {
                if (enteredGate)
                {
                    gate.Release();
                }

                if (outcomes.TryGetValue(server.Id, out var outcome))
                {
                    try
                    {
                        outcomeReceived?.Invoke(outcome);
                    }
                    catch (Exception)
                    {
                        // Progress reporting must not stop operations on other servers.
                    }
                }
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        return servers.Select(server => outcomes[server.Id]).ToArray();
    }

    private static void AppendOperationOutcome(UpdateExecutionForm progress, ServerOperationOutcome outcome)
    {
        if (outcome.Result == ServerOperationResult.Completed)
        {
            return;
        }

        var message = outcome.Result == ServerOperationResult.Cancelled
            ? string.Format(AppLocalizer.Get("UpdateServerCancelled"), outcome.Server.Name)
            : string.Format(AppLocalizer.Get("UpdateServerFailed"), outcome.Server.Name, outcome.Error ?? string.Empty);
        progress.AppendOutput(message + Environment.NewLine);
    }

    private static string FormatOperationSummary(IReadOnlyList<ServerOperationOutcome> outcomes) =>
        string.Format(
            AppLocalizer.Get("OperationSummary"),
            outcomes.Count(outcome => outcome.Result == ServerOperationResult.Completed),
            outcomes.Count(outcome => outcome.Result == ServerOperationResult.Failed),
            outcomes.Count(outcome => outcome.Result == ServerOperationResult.Cancelled));

    private static string OperationStatus(ServerOperationOutcome outcome) =>
        outcome.Result switch
        {
            ServerOperationResult.Completed => AppLocalizer.Get("UpdateResultStale"),
            ServerOperationResult.Cancelled => AppLocalizer.Get("UpdateCancelled"),
            _ => AppLocalizer.Get("StatusFailed")
        };

    private enum ServerOperationResult
    {
        Completed,
        Failed,
        Cancelled
    }

    private sealed record ServerOperationOutcome(
        ServerProfile Server,
        ServerOperationResult Result,
        string? Error);

    private async Task RunSshCheckAsync(ServerProfile server, bool testPrivileges)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var operationType = testPrivileges
            ? HistoryOperationTypes.PrivilegeCheck
            : HistoryOperationTypes.SshConnectionCheck;
        var historyStatus = HistoryOperationResults.Failed;
        string? historyError = null;
        var historyOutput = string.Empty;
        SetOperationInProgress(true);
        SetServerStatus(server.Id, AppLocalizer.Get("StatusConnecting"), AppLocalizer.Get("StatusChecking"));
        _status.Text = AppLocalizer.Get(testPrivileges ? "PrivilegeCheckInProgress" : "ConnectionCheckInProgress");

        try
        {
            using var session = await _sshConnectionService.ConnectAsync(server, ConfirmUnknownHostKey);
            if (session.NewlyTrustedFingerprint is { Length: > 0 } fingerprint &&
                !await SaveTrustedFingerprintAsync(server, fingerprint))
            {
                SetServerStatus(server.Id, AppLocalizer.Get("StatusNotConnected"), AppLocalizer.Get("StatusFailed"));
                historyError = "The server host key could not be saved.";
                return;
            }

            if (testPrivileges)
            {
                var hasRootAccess = await _privilegeElevationService.VerifyRootAccessAsync(session, server);
                historyStatus = hasRootAccess ? HistoryOperationResults.Succeeded : HistoryOperationResults.Failed;
                historyOutput = hasRootAccess ? "Privilege elevation verified." : "Privilege elevation was denied.";
                historyError = hasRootAccess ? null : "Privilege elevation was denied.";
                SetServerStatus(server.Id, AppLocalizer.Get("StatusConnected"), AppLocalizer.Get(
                    hasRootAccess ? "StatusPrivilegesVerified" : "StatusPrivilegesDenied"));
                _status.Text = AppLocalizer.Get(hasRootAccess ? "PrivilegeCheckSucceeded" : "PrivilegeCheckFailed");
            }
            else
            {
                historyStatus = HistoryOperationResults.Succeeded;
                historyOutput = "SSH connection verified.";
                SetServerStatus(server.Id, AppLocalizer.Get("StatusConnected"), AppLocalizer.Get("StatusConnectionVerified"));
                _status.Text = AppLocalizer.Get("ConnectionCheckSucceeded");
            }
        }
        catch (HostKeyMismatchException exception)
        {
            historyError = exception.Message;
            var message = string.Format(AppLocalizer.Get("HostKeyMismatch"), exception.Host,
                FormatFingerprint(exception.ExpectedFingerprint), FormatFingerprint(exception.ReceivedFingerprint));
            SetServerStatus(server.Id, AppLocalizer.Get("StatusHostKeyChanged"), AppLocalizer.Get("StatusFailed"));
            _status.Text = AppLocalizer.Get("StatusFailed");
            MessageBox.Show(this, message, AppLocalizer.Get("AppTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (HostKeyRejectedException)
        {
            historyStatus = HistoryOperationResults.Cancelled;
            historyError = "The SSH host key was not trusted.";
            SetServerStatus(server.Id, AppLocalizer.Get("StatusNotConnected"), AppLocalizer.Get("StatusFailed"));
            _status.Text = AppLocalizer.Get("StatusFailed");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                           System.ComponentModel.Win32Exception or Renci.SshNet.Common.SshException or
                                           UnauthorizedAccessException or OperationCanceledException)
        {
            historyError = exception.Message;
            historyStatus = exception is OperationCanceledException
                ? HistoryOperationResults.Cancelled
                : HistoryOperationResults.Failed;
            SetServerStatus(server.Id, AppLocalizer.Get("StatusNotConnected"), AppLocalizer.Get("StatusFailed"));
            _status.Text = AppLocalizer.Get("StatusFailed");
            MessageBox.Show(this, exception.Message, AppLocalizer.Get("AppTitle"), MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            await RecordHistoryBestEffortAsync(
                server,
                operationType,
                startedAt,
                historyStatus,
                null,
                historyOutput,
                historyError);
            SetOperationInProgress(false);
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
        await _inventorySaveGate.WaitAsync();
        try
        {
            var currentServer = _inventory.Servers.FirstOrDefault(item => item.Id == server.Id);
            if (currentServer is null)
            {
                return false;
            }

            var updatedServer = CopyServer(currentServer);
            updatedServer.TrustedHostKeyFingerprint = fingerprint;
            var updatedServers = _inventory.Servers
                .Select(item => item.Id == server.Id ? updatedServer : item)
                .ToList();
            if (!await SaveInventoryCoreAsync(updatedServers, server.Id))
            {
                return false;
            }

            _status.Text = AppLocalizer.Get("HostKeySaved");
            return true;
        }
        finally
        {
            _inventorySaveGate.Release();
        }
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

    private async Task RecordHistoryBestEffortAsync(
        ServerProfile server,
        string operationType,
        DateTimeOffset startedAt,
        string result,
        int? exitStatus,
        string output,
        string? errorSummary,
        int? packageCount = null,
        int? securityUpdateCount = null)
    {
        try
        {
            await _historyService.RecordAsync(new OperationHistoryRecord(
                server.Id,
                server.Name,
                server.Host,
                operationType,
                startedAt,
                DateTimeOffset.UtcNow,
                result,
                exitStatus,
                RedactHistoryText(server, output),
                errorSummary is null ? null : RedactHistoryText(server, errorSummary),
                packageCount,
                securityUpdateCount));
        }
        catch (Exception)
        {
            SetStatusFromAnyThread(AppLocalizer.Get("HistorySaveFailed"));
        }
    }

    private string RedactHistoryText(ServerProfile server, string text)
    {
        foreach (var kind in Enum.GetValues<CredentialKind>())
        {
            try
            {
                var secret = _credentialService.Retrieve(server.Id, kind);
                if (!string.IsNullOrEmpty(secret))
                {
                    text = text.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
                }
            }
            catch (Exception)
            {
                // Do not let unavailable credential storage stop an operation from being recorded.
            }
        }

        return text;
    }

    private void SetStatusFromAnyThread(string message)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => _status.Text = message));
            }
            catch (InvalidOperationException)
            {
                // The dashboard may be closing after the operation has finished.
            }

            return;
        }

        _status.Text = message;
    }

    private static string FormatHistoryUpdateResult(UpdateCheckResult result) =>
        $"Distribution: {result.Distribution}; packages: {result.Packages.Count}; " +
        $"security updates: {result.SecurityUpdateCount?.ToString() ?? "unknown"}; " +
        $"reboot required: {result.RebootRequired}.";

    private void SetOperationInProgress(bool inProgress)
    {
        _operationInProgress = inProgress;
        UpdateCommandButtons();
    }

    private void SetServerStatusFromAnyThread(Guid serverId, string connectionStatus, string operationStatus)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => SetServerStatus(serverId, connectionStatus, operationStatus)));
            }
            catch (InvalidOperationException)
            {
                // The form may be closing while a remote operation completes.
            }

            return;
        }

        SetServerStatus(serverId, connectionStatus, operationStatus);
    }

    private Task<T> InvokeOnUiAsync<T>(Func<Task<T>> action)
    {
        if (!InvokeRequired)
        {
            return action();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (IsDisposed || !IsHandleCreated)
        {
            completion.TrySetException(new ObjectDisposedException(nameof(MainForm)));
            return completion.Task;
        }

        try
        {
            BeginInvoke(new Action(async () =>
            {
                try
                {
                    completion.TrySetResult(await action());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }));
        }
        catch (InvalidOperationException exception)
        {
            completion.TrySetException(exception);
        }

        return completion.Task;
    }

    private Task InvokeOnUiAsync(Action action)
    {
        if (!InvokeRequired)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (IsDisposed || !IsHandleCreated)
        {
            completion.TrySetException(new ObjectDisposedException(nameof(MainForm)));
            return completion.Task;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                try
                {
                    action();
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }));
        }
        catch (InvalidOperationException exception)
        {
            completion.TrySetException(exception);
        }

        return completion.Task;
    }

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

    private void RefreshServerUpdateResult(Guid serverId, UpdateCheckResult result)
    {
        var row = _servers.Rows.Cast<DataGridViewRow>()
            .FirstOrDefault(candidate => candidate.Tag is Guid id && id == serverId);
        if (row is null)
        {
            return;
        }

        row.Cells["Distribution"].Value = result.Distribution;
        row.Cells["Version"].Value = result.Version;
        row.Cells["Connection"].Value = AppLocalizer.Get("StatusConnected");
        row.Cells["Updates"].Value = result.Packages.Count.ToString(AppLocalizer.CurrentCulture);
        row.Cells["Security"].Value = FormatSecurityCount(result.SecurityUpdateCount);
        row.Cells["Reboot"].Value = AppLocalizer.Get(_rebootScheduledServers.ContainsKey(serverId)
            ? "RebootScheduled"
            : result.RebootRequired ? "Yes" : "No");
        row.Cells["Checked"].Value = result.CheckedAt.ToLocalTime().ToString("g", AppLocalizer.CurrentCulture);
        row.Cells["Operation"].Value = _staleUpdateResults.ContainsKey(serverId)
            ? AppLocalizer.Get("UpdateResultStale")
            : FormatUpdateSummary(result);
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
            _latestUpdateResults.TryRemove(editor.Profile.Id, out _);
            _staleUpdateResults.TryRemove(editor.Profile.Id, out _);
            _rebootScheduledServers.TryRemove(editor.Profile.Id, out _);
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
            _latestUpdateResults.TryRemove(editor.Profile.Id, out _);
            _staleUpdateResults.TryRemove(editor.Profile.Id, out _);
            _rebootScheduledServers.TryRemove(editor.Profile.Id, out _);
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

        _latestUpdateResults.TryRemove(selected.Id, out _);
        _rebootScheduledServers.TryRemove(selected.Id, out _);
        _staleUpdateResults.TryRemove(selected.Id, out _);

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
        await _inventorySaveGate.WaitAsync();
        try
        {
            return await SaveInventoryCoreAsync(updatedServers, selectServerId);
        }
        finally
        {
            _inventorySaveGate.Release();
        }
    }

    private async Task<bool> SaveInventoryCoreAsync(List<ServerProfile> updatedServers, Guid? selectServerId)
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
