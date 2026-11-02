using LinuxUpdateManager.Core.Configuration;
using LinuxUpdateManager.Core.Security;
using LinuxUpdateManager.Core.Servers;

namespace LinuxUpdateManager.Core.Backups;

public enum BackupRestoreMode
{
    Merge,
    Replace
}

public sealed record BackupRestoreResult(
    AppConfiguration Configuration,
    ServerInventoryDocument Inventory,
    int ImportedServerCount,
    int SkippedServerCount,
    int RestoredCredentialCount,
    int RestoredPrivateKeyCount,
    int SkippedCredentialCount,
    IReadOnlyList<string> Warnings);

public sealed class BackupRestoreException(string message, Exception innerException, IReadOnlyList<string> rollbackErrors)
    : IOException(message, innerException)
{
    public IReadOnlyList<string> RollbackErrors { get; } = rollbackErrors;
}

public sealed class BackupRestoreService(
    ConfigurationService configurationService,
    ServerInventoryService inventoryService,
    CredentialService credentialService,
    string? privateKeyDirectory = null)
{
    private readonly string _privateKeyDirectory = privateKeyDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LinuxUpdateManager",
        "keys");

    public async Task<BackupRestoreResult> RestoreAsync(
        BackupPayload payload,
        BackupRestoreMode mode,
        AppConfiguration currentConfiguration,
        ServerInventoryDocument currentInventory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(currentConfiguration);
        ArgumentNullException.ThrowIfNull(currentInventory);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        BackupService.ValidatePayload(payload);

        var originalConfiguration = CloneConfiguration(currentConfiguration);
        var originalInventory = CloneInventory(currentInventory);
        var resultingConfiguration = payload.IncludesConfiguration && mode == BackupRestoreMode.Replace
            ? CloneConfiguration(payload.Configuration!)
            : CloneConfiguration(currentConfiguration);

        var resultingServers = new List<ServerProfile>();
        var importedServerIds = new HashSet<Guid>();
        var skippedServerCount = 0;
        if (payload.IncludesConfiguration)
        {
            if (mode == BackupRestoreMode.Replace)
            {
                resultingServers.AddRange(payload.Inventory!.Servers.Select(CloneServer));
                importedServerIds.UnionWith(resultingServers.Select(server => server.Id));
            }
            else
            {
                resultingServers.AddRange(currentInventory.Servers.Select(CloneServer));
                var ids = resultingServers.Select(server => server.Id).ToHashSet();
                var endpoints = resultingServers.Select(EndpointKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var server in payload.Inventory!.Servers)
                {
                    if (ids.Contains(server.Id) || !endpoints.Add(EndpointKey(server)))
                    {
                        skippedServerCount++;
                        continue;
                    }

                    ids.Add(server.Id);
                    resultingServers.Add(CloneServer(server));
                    importedServerIds.Add(server.Id);
                }
            }
        }
        else
        {
            resultingServers.AddRange(currentInventory.Servers.Select(CloneServer));
            importedServerIds.UnionWith(payload.Credentials
                .Where(credential => resultingServers.Any(server => server.Id == credential.ServerId))
                .Select(credential => credential.ServerId));
        }

        var resultingInventory = payload.IncludesConfiguration
            ? new ServerInventoryDocument
            {
                SchemaVersion = ServerInventoryDocument.CurrentSchemaVersion,
                Servers = resultingServers
            }
            : CloneInventory(currentInventory);
        ServerInventoryService.Validate(resultingInventory);

        var incomingCredentials = payload.Credentials.ToDictionary(
            credential => (credential.ServerId, credential.Kind), credential => credential.Secret);
        var credentialChanges = CreateCredentialChanges(
            payload,
            originalInventory,
            resultingInventory,
            importedServerIds,
            incomingCredentials,
            mode);
        var credentialSnapshot = new Dictionary<(Guid ServerId, CredentialKind Kind), string?>();
        foreach (var change in credentialChanges)
        {
            credentialSnapshot[change.Key] = credentialService.Retrieve(change.Key.ServerId, change.Key.Kind);
        }

        var createdKeyPaths = new List<string>();
        var credentialsChanged = new List<(Guid ServerId, CredentialKind Kind)>();
        var inventorySaved = false;
        var configurationSaved = false;
        var warnings = new List<string>();
        var restoredKeyCount = 0;
        var restoredCredentialCount = 0;
        var skippedCredentialCount = 0;

        try
        {
            if (payload.IncludesPrivateKeys)
            {
                Directory.CreateDirectory(_privateKeyDirectory);
                foreach (var keyFile in payload.PrivateKeys)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!importedServerIds.Contains(keyFile.ServerId))
                    {
                        continue;
                    }

                    var restoredPath = await WritePrivateKeyAsync(keyFile, cancellationToken);
                    createdKeyPaths.Add(restoredPath);
                    var profile = resultingServers.First(server => server.Id == keyFile.ServerId);
                    profile.PrivateKeyPath = restoredPath;
                    restoredKeyCount++;
                }
            }

            foreach (var change in credentialChanges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (change.Value is null)
                {
                    credentialService.Delete(change.Key.ServerId, change.Key.Kind);
                }
                else
                {
                    credentialService.Save(change.Key.ServerId, change.Key.Kind, change.Value);
                    restoredCredentialCount++;
                }

                credentialsChanged.Add(change.Key);
            }

            if (payload.IncludesConfiguration)
            {
                await inventoryService.SaveAsync(resultingInventory, cancellationToken);
                inventorySaved = true;

                if (mode == BackupRestoreMode.Replace)
                {
                    await configurationService.SaveAsync(resultingConfiguration, cancellationToken);
                    configurationSaved = true;
                }
            }

            if (payload.IncludesCredentials || !payload.IncludesConfiguration)
            {
                skippedCredentialCount = payload.Credentials.Count(credential =>
                    !importedServerIds.Contains(credential.ServerId));
                if (skippedCredentialCount > 0)
                {
                    warnings.Add($"{skippedCredentialCount} credential entrie(s) referenced servers that are not present and were skipped.");
                }
            }

            if (mode == BackupRestoreMode.Replace && payload.IncludesConfiguration && !payload.IncludesCredentials)
            {
                warnings.Add("Credentials for servers retained by ID were preserved; removed servers' credentials were deleted.");
            }

            return new BackupRestoreResult(
                resultingConfiguration,
                resultingInventory,
                payload.IncludesConfiguration ? importedServerIds.Count : 0,
                skippedServerCount,
                restoredCredentialCount,
                restoredKeyCount,
                skippedCredentialCount,
                warnings);
        }
        catch (Exception exception)
        {
            var rollbackErrors = new List<string>();
            foreach (var key in credentialsChanged.AsEnumerable().Reverse())
            {
                try
                {
                    var previous = credentialSnapshot[key];
                    if (previous is null)
                    {
                        credentialService.Delete(key.ServerId, key.Kind);
                    }
                    else
                    {
                        credentialService.Save(key.ServerId, key.Kind, previous);
                    }
                }
                catch (Exception rollbackException)
                {
                    rollbackErrors.Add($"Credential rollback failed: {rollbackException.GetType().Name}.");
                }
            }

            if (configurationSaved)
            {
                try
                {
                    await configurationService.SaveAsync(originalConfiguration, CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    rollbackErrors.Add($"Application settings rollback failed: {rollbackException.GetType().Name}.");
                }
            }

            if (inventorySaved)
            {
                try
                {
                    await inventoryService.SaveAsync(originalInventory, CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    rollbackErrors.Add($"Server inventory rollback failed: {rollbackException.GetType().Name}.");
                }
            }

            foreach (var path in createdKeyPaths)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception rollbackException)
                {
                    rollbackErrors.Add($"Private key file rollback failed: {rollbackException.GetType().Name}.");
                }
            }

            if (rollbackErrors.Count > 0)
            {
                throw new BackupRestoreException(
                    "The restore failed and some rollback steps also failed.", exception, rollbackErrors);
            }

            throw;
        }
        finally
        {
            foreach (var keyFile in payload.PrivateKeys)
            {
                if (keyFile.Contents is not null)
                {
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(keyFile.Contents);
                }
            }
        }
    }

    private Dictionary<(Guid ServerId, CredentialKind Kind), string?> CreateCredentialChanges(
        BackupPayload payload,
        ServerInventoryDocument originalInventory,
        ServerInventoryDocument resultingInventory,
        HashSet<Guid> importedServerIds,
        IReadOnlyDictionary<(Guid ServerId, CredentialKind Kind), string> incomingCredentials,
        BackupRestoreMode mode)
    {
        var changes = new Dictionary<(Guid ServerId, CredentialKind Kind), string?>();
        var resultingIds = resultingInventory.Servers.Select(server => server.Id).ToHashSet();

        foreach (var removedServerId in originalInventory.Servers
                     .Select(server => server.Id)
                     .Where(serverId => !resultingIds.Contains(serverId)))
        {
            foreach (var kind in Enum.GetValues<CredentialKind>())
            {
                changes[(removedServerId, kind)] = null;
            }
        }

        if (!payload.IncludesCredentials)
        {
            return changes;
        }

        foreach (var serverId in importedServerIds)
        {
            foreach (var kind in Enum.GetValues<CredentialKind>())
            {
                var key = (serverId, kind);
                if (incomingCredentials.TryGetValue(key, out var secret))
                {
                    changes[key] = secret;
                }
                else if (mode == BackupRestoreMode.Replace && payload.IncludesConfiguration)
                {
                    changes[key] = null;
                }
            }
        }

        return changes;
    }

    private async Task<string> WritePrivateKeyAsync(BackupPrivateKey keyFile, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(keyFile.FileName);
        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var safeStem = string.Concat(stem.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        if (string.IsNullOrWhiteSpace(safeStem))
        {
            safeStem = "private-key";
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(
                _privateKeyDirectory,
                $"{keyFile.ServerId:N}-{safeStem}-{Guid.NewGuid():N}{extension}");
            FileStream stream;
            try
            {
                stream = new FileStream(
                    destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            }
            catch (IOException) when (File.Exists(destination))
            {
                continue;
            }

            try
            {
                await using (stream)
                {
                await stream.WriteAsync(keyFile.Contents, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                }

                return destination;
            }
            catch
            {
                try
                {
                    File.Delete(destination);
                }
                catch
                {
                    // A failed key write must not leave an incomplete private key behind.
                }

                throw;
            }
        }
    }

    private static string EndpointKey(ServerProfile server) =>
        $"{server.Username}@{server.Host}:{server.SshPort}";

    private static AppConfiguration CloneConfiguration(AppConfiguration source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Language = source.Language,
        SshConnectionTimeoutSeconds = source.SshConnectionTimeoutSeconds,
        CommandTimeoutSeconds = source.CommandTimeoutSeconds,
        MaximumConcurrentOperations = source.MaximumConcurrentOperations,
        CheckForUpdatesOnStartup = source.CheckForUpdatesOnStartup,
        EnableFileLogging = source.EnableFileLogging,
        HistoryRetentionDays = source.HistoryRetentionDays
    };

    private static ServerInventoryDocument CloneInventory(ServerInventoryDocument source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Servers = source.Servers.Select(CloneServer).ToList()
    };

    private static ServerProfile CloneServer(ServerProfile source) => new()
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
        UpdatePreferences = new ServerUpdatePreferences
        {
            CheckForUpdates = source.UpdatePreferences.CheckForUpdates
        }
    };
}
