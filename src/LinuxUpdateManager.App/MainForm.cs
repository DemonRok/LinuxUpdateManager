using LinuxUpdateManager.App.Localization;
using LinuxUpdateManager.Core.Configuration;

namespace LinuxUpdateManager.App;

internal sealed class MainForm : Form
{
    private readonly AppConfiguration _configuration;
    private readonly ConfigurationService _configurationService;
    private readonly ComboBox _languageSelector = new();
    private readonly Label _languageLabel = new();
    private readonly Label _title = new();
    private readonly Label _notice = new();
    private readonly Label _status = new();
    private readonly DataGridView _servers = new();

    public MainForm(AppConfiguration configuration, ConfigurationService configurationService)
    {
        _configuration = configuration;
        _configurationService = configurationService;

        Text = AppLocalizer.Get("AppTitle");
        MinimumSize = new Size(1000, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        BuildLayout();
        ApplyLanguage();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _title.Font = new Font(Font, FontStyle.Bold);
        _title.Font = new Font(_title.Font.FontFamily, 16F, FontStyle.Bold);
        _title.Dock = DockStyle.Fill;
        _title.TextAlign = ContentAlignment.MiddleLeft;
        _languageSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageSelector.Width = 120;
        _languageSelector.Items.AddRange(["English", "Italiano"]);
        _languageSelector.SelectedIndexChanged += LanguageSelectorChanged;
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

        _servers.Dock = DockStyle.Fill;
        _servers.ReadOnly = true;
        _servers.AllowUserToAddRows = false;
        _servers.AllowUserToDeleteRows = false;
        _servers.AllowUserToResizeRows = false;
        _servers.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _servers.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _servers.RowHeadersVisible = false;
        _servers.BackgroundColor = SystemColors.Window;

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Color.DimGray;

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_notice, 0, 1);
        root.Controls.Add(_servers, 0, 2);
        root.Controls.Add(_status, 0, 3);
        Controls.Add(root);
    }

    private void ApplyLanguage()
    {
        Text = AppLocalizer.Get("AppTitle");
        _title.Text = AppLocalizer.Get("DashboardTitle");
        _notice.Text = AppLocalizer.Get("DemoNotice");
        _status.Text = AppLocalizer.Get("StatusDemo");
        _languageLabel.Text = AppLocalizer.Get("LanguageLabel");
        _languageSelector.AccessibleName = AppLocalizer.Get("LanguageLabel");

        var preferredIndex = _configuration.Language == "it-IT" ? 1 : 0;
        if (_languageSelector.SelectedIndex != preferredIndex)
        {
            _languageSelector.SelectedIndex = preferredIndex;
        }

        _servers.Columns.Clear();
        AddColumn("Name", "ColumnName");
        AddColumn("Host", "ColumnHost");
        AddColumn("Distribution", "ColumnDistribution");
        AddColumn("Version", "ColumnVersion");
        AddColumn("Connection", "ColumnConnection");
        AddColumn("Updates", "ColumnUpdates");
        AddColumn("Security", "ColumnSecurity");
        AddColumn("Reboot", "ColumnReboot");
        AddColumn("Checked", "ColumnChecked");
        AddColumn("Operation", "ColumnOperation");
        _servers.Columns["Operation"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _servers.Rows.Clear();
        _servers.Rows.Add(AppLocalizer.Get("ServerAlpha"), "server.example.com", "Ubuntu", "24.04 LTS",
            AppLocalizer.Get("StatusNotConnected"), "—", "—", AppLocalizer.Get("No"),
            AppLocalizer.Get("StatusNotChecked"), AppLocalizer.Get("StatusIdle"));
        _servers.Rows.Add(AppLocalizer.Get("ServerBeta"), "192.0.2.10", "Debian", "13",
            AppLocalizer.Get("StatusNotConnected"), "—", "—", AppLocalizer.Get("No"),
            AppLocalizer.Get("StatusNotChecked"), AppLocalizer.Get("StatusIdle"));
    }

    private void AddColumn(string name, string resourceKey) =>
        _servers.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = AppLocalizer.Get(resourceKey),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

    private async void LanguageSelectorChanged(object? sender, EventArgs e)
    {
        if (_languageSelector.SelectedIndex < 0)
        {
            return;
        }

        _configuration.Language = _languageSelector.SelectedIndex == 1 ? "it-IT" : "en-US";
        AppLocalizer.SetLanguage(_configuration.Language);
        ApplyLanguage();
        try
        {
            await _configurationService.SaveAsync(_configuration);
        }
        catch (IOException)
        {
            _status.Text = $"{AppLocalizer.Get("StatusDemo")} · {AppLocalizer.Get("ConfigSaveFailed")}";
        }
        catch (UnauthorizedAccessException)
        {
            _status.Text = $"{AppLocalizer.Get("StatusDemo")} · {AppLocalizer.Get("ConfigSaveFailed")}";
        }
    }
}
