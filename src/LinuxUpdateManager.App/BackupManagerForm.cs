using LinuxUpdateManager.App.Localization;
using LinuxUpdateManager.Core.Backups;
using LinuxUpdateManager.Core.Configuration;
using LinuxUpdateManager.Core.Security;
using LinuxUpdateManager.Core.Servers;

namespace LinuxUpdateManager.App;

internal sealed class BackupManagerForm : Form
{
    private readonly AppConfiguration _configuration;
    private readonly ServerInventoryDocument _inventory;
    private readonly CredentialService _credentialService;
    private readonly BackupService _backupService = new();
    private readonly BackupRestoreService _restoreService;
    private readonly Func<BackupRestoreResult, Task> _onRestored;
    private readonly ComboBox _mode = new();
    private readonly DataGridView _serverSelection = new();
    private readonly Button _export = new();
    private readonly Button _import = new();
    private readonly Label _status = new();

    public BackupManagerForm(
        AppConfiguration configuration,
        ServerInventoryDocument inventory,
        ConfigurationService configurationService,
        ServerInventoryService inventoryService,
        CredentialService credentialService,
        Func<BackupRestoreResult, Task> onRestored)
    {
        _configuration = configuration;
        _inventory = inventory;
        _credentialService = credentialService;
        _restoreService = new BackupRestoreService(configurationService, inventoryService, credentialService);
        _onRestored = onRestored;

        Text = AppLocalizer.Get("BackupManagerTitle");
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(720, 500);
        MinimumSize = Size;

        BuildLayout();
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 7
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        var title = new Label
        {
            Text = AppLocalizer.Get("BackupManagerTitle"),
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(title, 0, 0);

        _mode.DropDownStyle = ComboBoxStyle.DropDownList;
        _mode.Dock = DockStyle.Fill;
        _mode.Items.Add(new BackupModeItem(BackupKind.Full, AppLocalizer.Get("BackupModeFull")));
        _mode.Items.Add(new BackupModeItem(BackupKind.ConfigurationOnly, AppLocalizer.Get("BackupModeConfiguration")));
        _mode.Items.Add(new BackupModeItem(BackupKind.CredentialsOnly, AppLocalizer.Get("BackupModeCredentials")));
        _mode.SelectedIndex = 0;
        layout.Controls.Add(_mode, 0, 1);

        var selectionTools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        selectionTools.Controls.Add(new Label
        {
            Text = AppLocalizer.Get("BackupSelectServers"),
            AutoSize = true,
            Margin = new Padding(0, 8, 8, 0)
        });
        var selectAll = new Button { Text = AppLocalizer.Get("SelectAll"), AutoSize = true };
        var selectNone = new Button { Text = AppLocalizer.Get("SelectNone"), AutoSize = true };
        selectAll.Click += (_, _) => SetAllServersSelected(true);
        selectNone.Click += (_, _) => SetAllServersSelected(false);
        selectionTools.Controls.Add(selectAll);
        selectionTools.Controls.Add(selectNone);
        layout.Controls.Add(selectionTools, 0, 2);

        _serverSelection.Dock = DockStyle.Fill;
        _serverSelection.ReadOnly = false;
        _serverSelection.AllowUserToAddRows = false;
        _serverSelection.AllowUserToDeleteRows = false;
        _serverSelection.AllowUserToResizeRows = false;
        _serverSelection.RowHeadersVisible = false;
        _serverSelection.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _serverSelection.MultiSelect = false;
        _serverSelection.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _serverSelection.EditMode = DataGridViewEditMode.EditOnEnter;
        _serverSelection.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Selected",
            HeaderText = "",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Width = 34,
            FillWeight = 10
        });
        _serverSelection.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = AppLocalizer.Get("ColumnName"),
            ReadOnly = true,
            FillWeight = 40
        });
        _serverSelection.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Host",
            HeaderText = AppLocalizer.Get("ColumnHost"),
            ReadOnly = true,
            FillWeight = 50
        });
        foreach (var server in _inventory.Servers.OrderBy(server => server.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var rowIndex = _serverSelection.Rows.Add(false, server.Name, server.Host);
            _serverSelection.Rows[rowIndex].Tag = server.Id;
        }

        _serverSelection.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_serverSelection.IsCurrentCellDirty)
            {
                _serverSelection.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        layout.Controls.Add(_serverSelection, 0, 3);

        var privacyNote = new Label
        {
            Text = AppLocalizer.Get("BackupManagerDescription"),
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
            AutoEllipsis = true
        };

        layout.Controls.Add(privacyNote, 0, 4);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _export.Text = AppLocalizer.Get("BackupExport");
        _export.AutoSize = true;
        _import.Text = AppLocalizer.Get("BackupImport");
        _import.AutoSize = true;
        _export.Click += ExportClicked;
        _import.Click += ImportClicked;
        actions.Controls.Add(_export);
        actions.Controls.Add(_import);
        layout.Controls.Add(actions, 0, 5);

        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Color.DimGray;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_status, 0, 6);
        Controls.Add(layout);
    }

    private async void ExportClicked(object? sender, EventArgs e)
    {
        if (_mode.SelectedItem is not BackupModeItem selected)
        {
            return;
        }

        var includeCredentials = selected.Kind != BackupKind.ConfigurationOnly;
        var includePrivateKeys = selected.Kind == BackupKind.Full;
        var selectedServers = SelectedServers();
        if (includeCredentials && selectedServers.Count == 0)
        {
            MessageBox.Show(this, AppLocalizer.Get("BackupSelectAtLeastOneServer"),
                AppLocalizer.Get("BackupManagerTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (includePrivateKeys && MessageBox.Show(
                this,
                string.Format(AppLocalizer.Get("BackupConfirmFullExport"), selectedServers.Count),
                AppLocalizer.Get("BackupManagerTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        if (includeCredentials && !includePrivateKeys && MessageBox.Show(
                this,
                string.Format(AppLocalizer.Get("BackupConfirmCredentialExport"), selectedServers.Count),
                AppLocalizer.Get("BackupManagerTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = AppLocalizer.Get("BackupExport"),
            Filter = AppLocalizer.Get("BackupFileFilter"),
            DefaultExt = "lum",
            AddExtension = true,
            FileName = $"LinuxUpdateManager-backup-{DateTime.Now:yyyy-MM-dd}.lum",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        string? password = null;
        if (includeCredentials)
        {
            using var passwordForm = new BackupPasswordForm(confirmPassword: true);
            if (passwordForm.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            password = passwordForm.Password;
            passwordForm.ClearPassword();
        }

        SetBusy(true);
        BackupPayload? payload = null;
        try
        {
            payload = await CreatePayloadAsync(selected.Kind, selectedServers);
            await _backupService.ExportAsync(dialog.FileName, payload, password);
            _status.Text = string.Format(AppLocalizer.Get("BackupExportSucceeded"), dialog.FileName);
            MessageBox.Show(this, _status.Text, AppLocalizer.Get("BackupManagerTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _status.Text = AppLocalizer.Get("BackupImportCancelled");
        }
        catch (BackupRestoreException exception)
        {
            _status.Text = AppLocalizer.Get("BackupOperationFailed");
            var rollbackSummary = exception.RollbackErrors.Count == 0
                ? string.Empty
                : Environment.NewLine + string.Join(Environment.NewLine, exception.RollbackErrors);
            MessageBox.Show(this, exception.Message + rollbackSummary, AppLocalizer.Get("BackupManagerTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception exception)
        {
            _status.Text = AppLocalizer.Get("BackupOperationFailed");
            MessageBox.Show(this, exception.Message, AppLocalizer.Get("BackupManagerTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (payload is not null)
            {
                ClearKeyFileBuffers(payload);
            }

            password = null;
            SetBusy(false);
        }
    }

    private async Task<BackupPayload> CreatePayloadAsync(BackupKind kind, IReadOnlyList<ServerProfile> servers)
    {
        var includeConfiguration = kind != BackupKind.CredentialsOnly;
        var includeCredentials = kind != BackupKind.ConfigurationOnly;
        var includePrivateKeys = kind == BackupKind.Full;
        var payload = new BackupPayload
        {
            IncludesConfiguration = includeConfiguration,
            IncludesCredentials = includeCredentials,
            IncludesPrivateKeys = includePrivateKeys,
            Configuration = includeConfiguration ? _configuration : null,
            Inventory = includeConfiguration
                ? new ServerInventoryDocument
                {
                    SchemaVersion = ServerInventoryDocument.CurrentSchemaVersion,
                    Servers = servers.ToList()
                }
                : null
        };

        try
        {
            if (includeCredentials)
            {
                foreach (var server in servers)
                {
                    foreach (var kindValue in Enum.GetValues<CredentialKind>())
                    {
                        var secret = _credentialService.Retrieve(server.Id, kindValue);
                        if (secret is not null)
                        {
                            payload.Credentials.Add(new BackupCredential(server.Id, kindValue, secret));
                        }
                    }
                }
            }

            if (!includePrivateKeys)
            {
                return payload;
            }

            foreach (var server in servers)
            {
                if (server.AuthenticationMethod != SshAuthenticationMethod.PrivateKey &&
                    string.IsNullOrWhiteSpace(server.PrivateKeyPath))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(server.PrivateKeyPath) || !File.Exists(server.PrivateKeyPath))
                {
                    throw new FileNotFoundException(
                        string.Format(AppLocalizer.Get("BackupPrivateKeyMissing"), server.Name),
                        server.PrivateKeyPath);
                }

                var info = new FileInfo(server.PrivateKeyPath);
                if (info.Length is <= 0 or > 16 * 1024 * 1024)
                {
                    throw new InvalidDataException(
                        string.Format(AppLocalizer.Get("BackupPrivateKeySizeInvalid"), server.Name));
                }

                var contents = await File.ReadAllBytesAsync(server.PrivateKeyPath);
                payload.PrivateKeys.Add(new BackupPrivateKey(server.Id, Path.GetFileName(server.PrivateKeyPath), contents));
            }

            return payload;
        }
        catch
        {
            ClearKeyFileBuffers(payload);
            throw;
        }
    }

    private async void ImportClicked(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = AppLocalizer.Get("BackupImport"),
            Filter = AppLocalizer.Get("BackupFileFilter"),
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        SetBusy(true);
        BackupPreview? openedPreview = null;
        try
        {
            var preview = openedPreview = await OpenBackupWithPasswordAsync(dialog.FileName);
            using var review = new BackupImportReviewForm(preview, _inventory);
            if (review.ShowDialog(this) != DialogResult.OK)
            {
                ClearKeyFileBuffers(preview.Payload);
                return;
            }

            if ((preview.IncludesCredentials || preview.IncludesPrivateKeys) &&
                MessageBox.Show(this,
                    string.Format(AppLocalizer.Get("BackupConfirmSecretImport"),
                        preview.Payload.Credentials.Count, preview.Payload.PrivateKeys.Count),
                    AppLocalizer.Get("BackupManagerTitle"), MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                ClearKeyFileBuffers(preview.Payload);
                return;
            }

            var result = await _restoreService.RestoreAsync(
                preview.Payload,
                review.RestoreMode,
                _configuration,
                _inventory);
            await _onRestored(result);

            var summary = string.Format(AppLocalizer.Get("BackupImportSucceeded"),
                result.ImportedServerCount,
                result.SkippedServerCount,
                result.RestoredCredentialCount,
                result.RestoredPrivateKeyCount,
                result.SkippedCredentialCount);
            _status.Text = summary;
            if (result.Warnings.Count > 0)
            {
                summary += Environment.NewLine + string.Join(Environment.NewLine, result.Warnings);
            }

            MessageBox.Show(this, summary, AppLocalizer.Get("BackupManagerTitle"),
                MessageBoxButtons.OK, result.Warnings.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            _status.Text = AppLocalizer.Get("BackupOperationFailed");
            MessageBox.Show(this, exception.Message, AppLocalizer.Get("BackupManagerTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (openedPreview is not null)
            {
                ClearKeyFileBuffers(openedPreview.Payload);
            }

            SetBusy(false);
        }
    }

    private async Task<BackupPreview> OpenBackupWithPasswordAsync(string filePath)
    {
        try
        {
            return await _backupService.OpenAsync(filePath);
        }
        catch (BackupPasswordRequiredException)
        {
            using var passwordForm = new BackupPasswordForm();
            if (passwordForm.ShowDialog(this) != DialogResult.OK)
            {
                throw new OperationCanceledException("Backup import was cancelled.");
            }

            var password = passwordForm.Password;
            passwordForm.ClearPassword();
            try
            {
                return await _backupService.OpenAsync(filePath, password);
            }
            finally
            {
                password = null;
            }
        }
    }

    private void SetBusy(bool busy)
    {
        _mode.Enabled = !busy;
        _serverSelection.Enabled = !busy;
        _export.Enabled = !busy;
        _import.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private IReadOnlyList<ServerProfile> SelectedServers()
    {
        _serverSelection.EndEdit();
        var selectedIds = _serverSelection.Rows.Cast<DataGridViewRow>()
            .Where(row => row.Cells["Selected"].Value is true)
            .Select(row => row.Tag)
            .OfType<Guid>()
            .ToHashSet();
        return _inventory.Servers.Where(server => selectedIds.Contains(server.Id)).ToArray();
    }

    private void SetAllServersSelected(bool selected)
    {
        foreach (DataGridViewRow row in _serverSelection.Rows)
        {
            row.Cells["Selected"].Value = selected;
        }
    }

    private static void ClearKeyFileBuffers(BackupPayload payload)
    {
        foreach (var key in payload.PrivateKeys)
        {
            if (key.Contents is not null)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(key.Contents);
            }
        }
    }

    private enum BackupKind
    {
        Full,
        ConfigurationOnly,
        CredentialsOnly
    }

    private sealed record BackupModeItem(BackupKind Kind, string Label)
    {
        public override string ToString() => Label;
    }
}
