using LinuxUpdateManager.App.Localization;

namespace LinuxUpdateManager.App;

internal sealed class UpdateExecutionForm : Form
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Label _status = new();
    private readonly TextBox _output = new();
    private readonly Button _cancelClose = new();
    private bool _completed;

    public UpdateExecutionForm()
    {
        Text = AppLocalizer.Get("UpdateProgressTitle");
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 360);
        ClientSize = new Size(780, 480);
        Font = new Font("Segoe UI", 9F);

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Text = AppLocalizer.Get("UpdateConnecting");

        _output.Dock = DockStyle.Fill;
        _output.Multiline = true;
        _output.ReadOnly = true;
        _output.WordWrap = false;
        _output.ScrollBars = ScrollBars.Both;
        _output.Font = new Font("Consolas", 9F);

        _cancelClose.Text = AppLocalizer.Get("Cancel");
        _cancelClose.AutoSize = true;
        _cancelClose.Click += CancelCloseClicked;

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            WrapContents = false
        };
        footer.Controls.Add(_cancelClose);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(_status, 0, 0);
        layout.Controls.Add(_output, 0, 1);
        layout.Controls.Add(footer, 0, 2);
        Controls.Add(layout);

        CancelButton = _cancelClose;
        FormClosing += UpdateExecutionFormClosing;
        FormClosed += (_, _) => _cancellation.Dispose();
    }

    public CancellationToken CancellationToken => _cancellation.Token;

    public void SetStatus(string status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetStatus(status)));
            return;
        }

        _status.Text = status;
    }

    public void AppendOutput(string text)
    {
        if (string.IsNullOrEmpty(text) || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => AppendOutput(text)));
            return;
        }

        _output.AppendText(text);
        if (_output.TextLength > 200_000)
        {
            _output.Text = _output.Text[^200_000..];
        }

        _output.SelectionStart = _output.TextLength;
        _output.ScrollToCaret();
    }

    public void Complete(string summary)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => Complete(summary)));
            return;
        }

        _completed = true;
        _status.Text = summary;
        _cancelClose.Text = AppLocalizer.Get("Close");
        _cancelClose.Enabled = true;
        _cancelClose.DialogResult = DialogResult.OK;
        AcceptButton = _cancelClose;
        CancelButton = null;
    }

    private void CancelCloseClicked(object? sender, EventArgs e)
    {
        if (_completed)
        {
            Close();
            return;
        }

        RequestCancellation();
    }

    private void UpdateExecutionFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_completed)
        {
            e.Cancel = true;
            RequestCancellation();
        }
    }

    private void RequestCancellation()
    {
        if (_cancellation.IsCancellationRequested)
        {
            return;
        }

        _cancellation.Cancel();
        _cancelClose.Enabled = false;
        _status.Text = AppLocalizer.Get("OperationCancelling");
    }
}
