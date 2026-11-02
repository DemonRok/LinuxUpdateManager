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
        ClientSize = new Size(1050, 640);
        MinimumSize = new Size(820, 500);

        BuildLayout();
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var title = new Label
        {
            Text = AppLocalizer.Get("HistoryTitle"),
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(title, 0, 0);

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _search.PlaceholderText = AppLocalizer.Get("HistorySearchPlaceholder");
        _operationFilter.Items.Add(new HistoryFilterItem("", AppLocalizer.Get("HistoryAllOperations")));
        AddOperationFilter(HistoryOperationTypes.SshConnectionCheck, "HistoryOperationSshConnectionCheck");
        AddOperationFilter(HistoryOperationTypes.PrivilegeCheck, "HistoryOperationPrivilegeCheck");
        AddOperationFilter(HistoryOperationTypes.UpdateCheck, "HistoryOperationUpdateCheck");
        AddOperationFilter(HistoryOperationTypes.PackageUpgrade, "HistoryOperationPackageUpgrade");
        _operationFilter.SelectedIndex = 0;
        _resultFilter.Items.Add(new HistoryFilterItem("", AppLocalizer.Get("HistoryAllResults")));
        AddResultFilter(HistoryOperationResults.Succeeded, "HistoryResultSucceeded");
        AddResultFilter(HistoryOperationResults.Failed, "HistoryResultFailed");
        AddResultFilter(HistoryOperationResults.Cancelled, "HistoryResultCancelled");
        _resultFilter.SelectedIndex = 0;
        filters.Controls.Add(_search);
        filters.Controls.Add(_operationFilter);
        filters.Controls.Add(_resultFilter);
        var refresh = new Button { Text = AppLocalizer.Get("Refresh"), AutoSize = true };
        refresh.Click += async (_, _) => await RefreshEntriesAsync();
        filters.Controls.Add(refresh);
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

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var retention = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        retention.Controls.Add(new Label
        {
            Text = AppLocalizer.Get("HistoryRetention"),
            AutoSize = true,
            Margin = new Padding(0, 8, 5, 0)
        });
        _retentionDays.Value = _configuration.HistoryRetentionDays;
        retention.Controls.Add(_retentionDays);
        retention.Controls.Add(new Label
        {
            Text = AppLocalizer.Get("HistoryRetentionHint"),
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(4, 8, 8, 0)
        });
        var saveRetention = new Button { Text = AppLocalizer.Get("Save"), AutoSize = true };
        saveRetention.Click += SaveRetentionClicked;
        retention.Controls.Add(saveRetention);
        retention.Controls.Add(_status);
        footer.Controls.Add(retention, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var close = new Button { Text = AppLocalizer.Get("Close"), AutoSize = true, DialogResult = DialogResult.OK };
        var exportCsv = new Button { Text = AppLocalizer.Get("HistoryExportCsv"), AutoSize = true };
        var exportText = new Button { Text = AppLocalizer.Get("HistoryExportText"), AutoSize = true };
        exportCsv.Click += async (_, _) => await ExportAsync(csv: true);
        exportText.Click += async (_, _) => await ExportAsync(csv: false);
        buttons.Controls.Add(close);
        buttons.Controls.Add(exportCsv);
        buttons.Controls.Add(exportText);
        footer.Controls.Add(buttons, 1, 0);
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

            _status.Text = string.Format(AppLocalizer.Get("HistoryEntryCount"), _entries.Count);
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
        var extension = csv ? "csv" : "txt";
        using var dialog = new SaveFileDialog
        {
            Title = AppLocalizer.Get(csv ? "HistoryExportCsv" : "HistoryExportText"),
            Filter = csv ? AppLocalizer.Get("HistoryCsvFilter") : AppLocalizer.Get("HistoryTextFilter"),
            DefaultExt = extension,
            AddExtension = true,
            FileName = $"LinuxUpdateManager-history-{DateTime.Now:yyyy-MM-dd}.{extension}",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var content = csv ? BuildCsv(_entries) : BuildText(_entries);
            await File.WriteAllTextAsync(dialog.FileName, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _status.Text = string.Format(AppLocalizer.Get("HistoryExportSucceeded"), dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, AppLocalizer.Get("HistoryTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
