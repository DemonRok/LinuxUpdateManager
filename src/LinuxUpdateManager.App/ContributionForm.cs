using System.Diagnostics;
using LinuxUpdateManager.App.Localization;

namespace LinuxUpdateManager.App;

internal sealed class ContributionForm : Form
{
    private readonly TextBox _bitcoinAddress = new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill,
        TabStop = false
    };
    private readonly Label _status = new() { AutoSize = false, ForeColor = Color.DimGray };

    public ContributionForm()
    {
        Text = AppLocalizer.Get("SupportProject");
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(620, 260);
        MinimumSize = Size;

        BuildLayout();
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 6
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = AppLocalizer.Get("SupportProject"),
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(title, 0, 0);

        var intro = new Label
        {
            Text = AppLocalizer.Get("SupportIntro"),
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(intro, 0, 1);

        var payPalRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        var openPayPal = new Button { Text = AppLocalizer.Get("SupportPayPal"), AutoSize = true };
        openPayPal.Click += OpenPayPalClicked;
        payPalRow.Controls.Add(openPayPal);
        layout.Controls.Add(payPalRow, 0, 2);

        var bitcoinLabel = new Label
        {
            Text = AppLocalizer.Get("SupportBitcoin"),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(bitcoinLabel, 0, 3);

        var bitcoinRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        bitcoinRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bitcoinRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _bitcoinAddress.Text = ContributionDestinations.BitcoinAddress;
        var copyAddress = new Button { Text = AppLocalizer.Get("SupportCopyBitcoin"), AutoSize = true };
        copyAddress.Click += CopyAddressClicked;
        bitcoinRow.Controls.Add(_bitcoinAddress, 0, 0);
        bitcoinRow.Controls.Add(copyAddress, 1, 0);
        layout.Controls.Add(bitcoinRow, 0, 4);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.AutoEllipsis = true;
        footer.Controls.Add(_status, 0, 0);
        var close = new Button
        {
            Text = AppLocalizer.Get("Close"),
            AutoSize = true,
            DialogResult = DialogResult.OK
        };
        footer.Controls.Add(close, 1, 0);
        layout.Controls.Add(footer, 0, 5);

        Controls.Add(layout);
        AcceptButton = close;
        CancelButton = close;
    }

    private void OpenPayPalClicked(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ContributionDestinations.PayPalUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                string.Format(AppLocalizer.Get("SupportOpenFailed"), exception.Message),
                AppLocalizer.Get("SupportProject"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void CopyAddressClicked(object? sender, EventArgs e)
    {
        try
        {
            Clipboard.SetText(_bitcoinAddress.Text);
            _status.Text = AppLocalizer.Get("SupportCopied");
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                string.Format(AppLocalizer.Get("SupportCopyFailed"), exception.Message),
                AppLocalizer.Get("SupportProject"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
