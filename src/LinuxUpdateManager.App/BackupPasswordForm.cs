using LinuxUpdateManager.App.Localization;

namespace LinuxUpdateManager.App;

internal sealed class BackupPasswordForm : Form
{
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
    private readonly TextBox _confirmation = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
    private readonly Label _error = new() { AutoSize = true, ForeColor = Color.Firebrick };

    public string Password => _password.Text;

    public void ClearPassword()
    {
        _password.Clear();
        _confirmation.Clear();
    }

    public BackupPasswordForm() : this(false, false)
    {
    }

    public BackupPasswordForm(bool confirmPassword) : this(confirmPassword, true)
    {
    }

    public BackupPasswordForm(bool confirmPassword, bool isExport)
    {
        Text = AppLocalizer.Get("BackupPasswordTitle");
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(460, confirmPassword ? 185 : 140);
        MinimumSize = Size;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = confirmPassword ? 5 : 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        if (confirmPassword)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        }

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        layout.Controls.Add(new Label
        {
            Text = AppLocalizer.Get(isExport ? "BackupPasswordLabel" : "BackupPasswordPrompt"),
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 0);
        layout.Controls.Add(_password, 1, 0);
        if (confirmPassword)
        {
            layout.Controls.Add(new Label
            {
                Text = AppLocalizer.Get("BackupPasswordConfirm"),
                AutoSize = true,
                Anchor = AnchorStyles.Left
            }, 0, 1);
            layout.Controls.Add(_confirmation, 1, 1);
        }

        layout.Controls.Add(_error, 0, confirmPassword ? 2 : 1);
        layout.SetColumnSpan(_error, 2);
        var hint = new Label
        {
            Text = AppLocalizer.Get("BackupPasswordHint"),
            AutoSize = true,
            ForeColor = Color.DimGray,
            Anchor = AnchorStyles.Left
        };
        layout.Controls.Add(hint, 0, confirmPassword ? 3 : 2);
        layout.SetColumnSpan(hint, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var save = new Button { Text = AppLocalizer.Get("Continue"), AutoSize = true };
        save.Click += (_, _) => ValidateAndClose(confirmPassword, isExport);
        var cancel = new Button { Text = AppLocalizer.Get("Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, confirmPassword ? 4 : 3);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);

        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) => _password.Focus();
    }

    private void ValidateAndClose(bool confirmPassword, bool isExport)
    {
        if (string.IsNullOrWhiteSpace(_password.Text))
        {
            _error.Text = AppLocalizer.Get("BackupPasswordRequired");
            return;
        }

        if (isExport && _password.Text.Length < 12)
        {
            _error.Text = AppLocalizer.Get("BackupPasswordTooShort");
            return;
        }

        if (confirmPassword && !string.Equals(_password.Text, _confirmation.Text, StringComparison.Ordinal))
        {
            _error.Text = AppLocalizer.Get("BackupPasswordMismatch");
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
