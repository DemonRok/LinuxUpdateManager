using System.Globalization;
using System.Text;
using LinuxUpdateManager.App.Localization;
using LinuxUpdateManager.Core.Configuration;
using LinuxUpdateManager.Core.History;

namespace LinuxUpdateManager.App;

internal sealed class OperationHistoryForm : Form
{
    private readonly HistoryService _historyService;
    private readonly AppConfiguration _configuration;
    private readonly ConfigurationService _configurationService;
    private readonly TextBox _search = new() { Width = 190 };
    private readonly ComboBox _operationFilter = new() { Width = 165, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _resultFilter = new() { Width = 135, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView _grid = new();
    private readonly NumericUpDown _retentionDays = new() { Minimum = 0, Maximum = 3650, Width = 78 };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Label _entryCount = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly System.Windows.Forms.Timer _filterTimer = new() { Interval = 300 };
    private IReadOnlyList<OperationHistoryEntry> _entries = [];

    public OperationHistoryForm(
        HistoryService historyService,
        AppConfiguration configuration,
        ConfigurationService configurationService)
    {
        _historyService = historyService;
        _configuration = configuration;
        _configurationService = configurationService;

        Text = AppLocalizer.Get("HistoryTitle");
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1050, 560);
        MinimumSize = new Size(820, 500);

        BuildLayout();
        Shown += (_, _) => FitToWorkingArea();
        Load += async (_, _) => await RefreshEntriesAsync();
        _filterTimer.Tick += async (_, _) =>
        {
            _filterTimer.Stop();
            await RefreshEntriesAsync();
        };
        _search.TextChanged += (_, _) => RestartFilterTimer();
        _operationFilter.SelectedIndexChanged += (_, _) => RestartFilterTimer();
        _resultFilter.SelectedIndexChanged += (_, _) => RestartFilterTimer();
        FormClosed += (_, _) => _filterTimer.Dispose();
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].Tag is long entryId)
            {
                ShowDetails(entryId);
            }
        };
    }

    private void FitToWorkingArea()
    {
        var workingArea = Screen.FromControl(this).WorkingArea;
        const int screenMargin = 12;
        var maxWindowWidth = Math.Max(420, workingArea.Width - screenMargin);
        var maxWindowHeight = Math.Max(320, workingArea.Height - screenMargin);
        var nonClientWidth = Width - ClientSize.Width;
        var nonClientHeight = Height - ClientSize.Height;

        MinimumSize = new Size(
            Math.Min(MinimumSize.Width, maxWindowWidth),
            Math.Min(MinimumSize.Height, maxWindowHeight));

        var clientWidth = Math.Min(ClientSize.Width, maxWindowWidth - nonClientWidth);
        var clientHeight = Math.Min(ClientSize.Height, maxWindowHeight - nonClientHeight);
        ClientSize = new Size(clientWidth, clientHeight);
        Location = new Point(
            workingArea.Left + (workingArea.Width - Width) / 2,
            workingArea.Top + (workingArea.Height - Height) / 2);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var title = new Label
        {
            Text = AppLocalizer.Get("HistoryTitle"),
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(title, 0, 0);

        var filters = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnCount = 4,
            RowCount = 1
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 198));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 171));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 141));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _search.PlaceholderText = AppLocalizer.Get("HistorySearchPlaceholder");
        _search.Margin = Padding.Empty;
        _search.Anchor = AnchorStyles.Left;
        _operationFilter.Items.Add(new HistoryFilterItem("", AppLocalizer.Get("HistoryAllOperations")));
        AddOperationFilter(HistoryOperationTypes.SshConnectionCheck, "HistoryOperationSshConnectionCheck");
        AddOperationFilter(HistoryOperationTypes.PrivilegeCheck, "HistoryOperationPrivilegeCheck");
        AddOperationFilter(HistoryOperationTypes.UpdateCheck, "HistoryOperationUpdateCheck");
        AddOperationFilter(HistoryOperationTypes.PackageUpgrade, "HistoryOperationPackageUpgrade");
        AddOperationFilter(HistoryOperationTypes.Autoremove, "HistoryOperationAutoremove");
        AddOperationFilter(HistoryOperationTypes.Reboot, "HistoryOperationReboot");
        _operationFilter.SelectedIndex = 0;
        _resultFilter.Items.Add(new HistoryFilterItem("", AppLocalizer.Get("HistoryAllResults")));
        AddResultFilter(HistoryOperationResults.Succeeded, "HistoryResultSucceeded");
        AddResultFilter(HistoryOperationResults.Failed, "HistoryResultFailed");
        AddResultFilter(HistoryOperationResults.Cancelled, "HistoryResultCancelled");
        _resultFilter.SelectedIndex = 0;
        _operationFilter.Margin = Padding.Empty;
        _operationFilter.Anchor = AnchorStyles.Left;
        _resultFilter.Margin = Padding.Empty;
        _resultFilter.Anchor = AnchorStyles.Left;
        var refresh = new Button { Text = AppLocalizer.Get("Refresh"), AutoSize = true };
        refresh.Margin = Padding.Empty;
        refresh.Anchor = AnchorStyles.Left;
        refresh.Click += async (_, _) => await RefreshEntriesAsync();
        filters.Controls.Add(_search, 0, 0);
        filters.Controls.Add(_operationFilter, 1, 0);
        filters.Controls.Add(_resultFilter, 2, 0);
        filters.Controls.Add(refresh, 3, 0);
        root.Controls.Add(filters, 0, 1);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add("Date", AppLocalizer.Get("HistoryDate"));
        _grid.Columns.Add("Server", AppLocalizer.Get("ColumnName"));
        _grid.Columns.Add("Host", AppLocalizer.Get("ColumnHost"));
        _grid.Columns.Add("Operation", AppLocalizer.Get("HistoryOperation"));
        _grid.Columns.Add("Result", AppLocalizer.Get("HistoryResult"));
        _grid.Columns.Add("Packages", AppLocalizer.Get("HistoryPackages"));
        root.Controls.Add(_grid, 0, 2);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnCount = 4,
            RowCount = 1
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var retention = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Anchor = AnchorStyles.Left
        };
        retention.Controls.Add(new Label
        {
            Text = AppLocalizer.Get("HistoryRetention"),
            AutoSize = true,
            Margin = new Padding(0, 5, 5, 0)
        });
        _retentionDays.Value = _configuration.HistoryRetentionDays;
        _retentionDays.Margin = new Padding(0, 1, 0, 0);
        retention.Controls.Add(_retentionDays);
        retention.Controls.Add(new Label
        {
            Text = AppLocalizer.Get("HistoryRetentionHint"),
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(4, 5, 4, 0)
        });
        var saveRetention = new Button
        {
            Text = AppLocalizer.Get("Save"),
            AutoSize = true,
            MinimumSize = new Size(78, 26),
            Margin = new Padding(4, 0, 8, 0),
            Anchor = AnchorStyles.Left
        };
        saveRetention.Click += SaveRetentionClicked;
        retention.Controls.Add(saveRetention);
        footer.Controls.Add(retention, 0, 0);
        _status.Dock = DockStyle.Fill;
        _status.AutoEllipsis = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Margin = new Padding(4, 0, 8, 0);
        footer.Controls.Add(_status, 1, 0);
        _entryCount.Anchor = AnchorStyles.Right;
        _entryCount.TextAlign = ContentAlignment.MiddleRight;
        _entryCount.Margin = new Padding(8, 0, 8, 0);
        footer.Controls.Add(_entryCount, 2, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right
        };
        var close = new Button { Text = AppLocalizer.Get("Close"), AutoSize = true, DialogResult = DialogResult.OK };
        var exportCsv = new Button { Text = AppLocalizer.Get("HistoryExportCsv"), AutoSize = true };
        var exportText = new Button { Text = AppLocalizer.Get("HistoryExportText"), AutoSize = true };
        exportCsv.Click += async (_, _) => await ExportAsync(csv: true);
        exportText.Click += async (_, _) => await ExportAsync(csv: false);
        buttons.Controls.Add(close);
        buttons.Controls.Add(exportCsv);
        buttons.Controls.Add(exportText);
        footer.Controls.Add(buttons, 3, 0);
        root.Controls.Add(footer, 0, 3);

        Controls.Add(root);
        AcceptButton = close;
    }

    private void AddOperationFilter(string value, string resourceKey) =>
        _operationFilter.Items.Add(new HistoryFilterItem(value, AppLocalizer.Get(resourceKey)));

    private void AddResultFilter(string value, string resourceKey) =>
        _resultFilter.Items.Add(new HistoryFilterItem(value, AppLocalizer.Get(resourceKey)));

    private void RestartFilterTimer()
    {
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private async Task RefreshEntriesAsync()
    {
        try
        {
            var operationType = (_operationFilter.SelectedItem as HistoryFilterItem)?.Value ?? string.Empty;
            var result = (_resultFilter.SelectedItem as HistoryFilterItem)?.Value ?? string.Empty;
            _entries = await _historyService.SearchAsync(new OperationHistoryFilter(
                _search.Text,
                operationType,
                result));
            _grid.Rows.Clear();
            foreach (var entry in _entries)
            {
                var rowIndex = _grid.Rows.Add(
                    entry.StartedAtUtc.ToLocalTime().ToString("g", AppLocalizer.CurrentCulture),
                    entry.ServerName,
                    entry.ServerHost,
                    LocalizeOperation(entry.OperationType),
                    LocalizeResult(entry.Result),
                    entry.PackageCount?.ToString(AppLocalizer.CurrentCulture) ?? "—");
                _grid.Rows[rowIndex].Tag = entry.Id;
            }

            _entryCount.Text = string.Format(AppLocalizer.Get("HistoryEntryCount"), _entries.Count);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, AppLocalizer.Get("HistoryTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void SaveRetentionClicked(object? sender, EventArgs e)
    {
        var previousValue = _configuration.HistoryRetentionDays;
        _configuration.HistoryRetentionDays = (int)_retentionDays.Value;
        try
        {
            await _configurationService.SaveAsync(_configuration);
        }
        catch (Exception exception)
        {
            _configuration.HistoryRetentionDays = previousValue;
            _retentionDays.Value = previousValue;
            MessageBox.Show(this, exception.Message, AppLocalizer.Get("HistoryTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _status.Text = AppLocalizer.Get("HistoryRetentionSaved");
        try
        {
            if (_configuration.HistoryRetentionDays > 0)
            {
                await _historyService.DeleteOlderThanAsync(
                    DateTimeOffset.UtcNow.AddDays(-_configuration.HistoryRetentionDays));
            }

            await RefreshEntriesAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                string.Format(AppLocalizer.Get("HistoryPurgeFailed"), exception.Message),
                AppLocalizer.Get("HistoryTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task ExportAsync(bool csv)
    {
        try
        {
            var filePath = await ShowExportDialogAsync(csv);
            if (filePath is null)
            {
                return;
            }

            var content = csv ? BuildCsv(_entries) : BuildText(_entries);
            await File.WriteAllTextAsync(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _status.Text = string.Format(AppLocalizer.Get("HistoryExportSucceeded"), filePath);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, AppLocalizer.Get("HistoryTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private Task<string?> ShowExportDialogAsync(bool csv)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerHandle = Handle;
        var extension = csv ? "csv" : "txt";
        var dialogThread = new Thread(() =>
        {
            try
            {
                using var dialog = new SaveFileDialog
                {
                    Title = AppLocalizer.Get(csv ? "HistoryExportCsv" : "HistoryExportText"),
                    Filter = csv ? AppLocalizer.Get("HistoryCsvFilter") : AppLocalizer.Get("HistoryTextFilter"),
                    DefaultExt = extension,
                    AddExtension = true,
                    FileName = $"LinuxUpdateManager-history-{DateTime.Now:yyyy-MM-dd}.{extension}",
                    InitialDirectory = AppContext.BaseDirectory,
                    AutoUpgradeEnabled = true,
                    RestoreDirectory = true,
                    OverwritePrompt = true
                };
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
            Name = "HistoryExportFileDialog"
        };

        dialogThread.SetApartmentState(ApartmentState.STA);
        dialogThread.Start();
        return completion.Task;
    }

    private sealed class WindowHandleOwner(IntPtr handle) : IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }

    private void ShowDetails(long entryId)
    {
        var entry = _entries.FirstOrDefault(item => item.Id == entryId);
        if (entry is null)
        {
            return;
        }

        using var details = new OperationHistoryDetailsForm(entry);
        details.ShowDialog(this);
    }

    private static string LocalizeOperation(string operationType) =>
        AppLocalizer.Get($"HistoryOperation{operationType}");

    private static string LocalizeResult(string result) =>
        AppLocalizer.Get($"HistoryResult{result}");

    private static string BuildText(IEnumerable<OperationHistoryEntry> entries)
    {
        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            builder.AppendLine($"{entry.StartedAtUtc.ToLocalTime():O} | {entry.ServerName} ({entry.ServerHost}) | {LocalizeOperation(entry.OperationType)} | {LocalizeResult(entry.Result)}");
            builder.AppendLine($"Server ID: {entry.ServerId:D}");
            builder.AppendLine($"Exit status: {entry.ExitStatus?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
            builder.AppendLine($"Packages: {entry.PackageCount?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
            builder.AppendLine($"Security updates: {entry.SecurityUpdateCount?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
            if (!string.IsNullOrWhiteSpace(entry.ErrorSummary))
            {
                builder.AppendLine($"Error: {entry.ErrorSummary}");
            }

            builder.AppendLine(entry.SanitizedOutput);
            builder.AppendLine(new string('-', 80));
        }

        return builder.ToString();
    }

    private static string BuildCsv(IEnumerable<OperationHistoryEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Id,ServerId,Server,Host,Operation,StartedUtc,EndedUtc,Result,ExitStatus,PackageCount,SecurityUpdateCount,ErrorSummary,SanitizedOutput");
        foreach (var entry in entries)
        {
            var values = new object?[]
            {
                entry.Id,
                entry.ServerId.ToString("D"),
                entry.ServerName,
                entry.ServerHost,
                LocalizeOperation(entry.OperationType),
                entry.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                entry.EndedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                LocalizeResult(entry.Result),
                entry.ExitStatus,
                entry.PackageCount,
                entry.SecurityUpdateCount,
                entry.ErrorSummary,
                entry.SanitizedOutput
            };
            builder.AppendLine(string.Join(',', values.Select(QuoteCsv)));
        }

        return builder.ToString();
    }

    private static string QuoteCsv(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = $"'{text}";
        }

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private sealed record HistoryFilterItem(string Value, string Label)
    {
        public override string ToString() => Label;
    }
}
