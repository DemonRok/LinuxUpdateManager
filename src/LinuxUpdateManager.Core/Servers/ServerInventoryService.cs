using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LinuxUpdateManager.Core.Servers;

public sealed class ServerInventoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _inventoryPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ServerInventoryService(string? inventoryPath = null)
    {
        _inventoryPath = inventoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinuxUpdateManager",
            "servers.json");
    }

    public async Task<ServerInventoryDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_inventoryPath))
            {
                var emptyInventory = new ServerInventoryDocument();
                await SaveCoreAsync(emptyInventory, cancellationToken);
                return emptyInventory;
            }

            try
            {
                await using var stream = File.OpenRead(_inventoryPath);
                var document = await JsonSerializer.DeserializeAsync<ServerInventoryDocument>(
                    stream, JsonOptions, cancellationToken);
                Validate(document);
                return document!;
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                PreserveInvalidInventory();
                var emptyInventory = new ServerInventoryDocument();
                await SaveCoreAsync(emptyInventory, cancellationToken);
                return emptyInventory;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ServerInventoryDocument document, CancellationToken cancellationToken = default)
    {
        Validate(document);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveCoreAsync(document, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static void Validate(ServerInventoryDocument? document)
    {
        if (document is null || document.SchemaVersion != ServerInventoryDocument.CurrentSchemaVersion || document.Servers is null)
        {
            throw new InvalidDataException("The server inventory has an unsupported schema or missing data.");
        }

        if (document.Servers.Any(server => server is null))
        {
            throw new InvalidDataException("The server inventory contains an empty server entry.");
        }

        var ids = new HashSet<Guid>();
        var endpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var server in document.Servers)
        {
            ValidateServer(server);
            if (!ids.Add(server.Id))
            {
                throw new InvalidDataException("The server inventory contains duplicate server identifiers.");
            }

            var endpoint = $"{server.Username}@{server.Host}:{server.SshPort}";
            if (!endpoints.Add(endpoint))
            {
                throw new InvalidDataException("The server inventory contains a duplicate host, port, and username.");
            }
        }
    }

    public static void ValidateServer(ServerProfile? server)
    {
        if (server is null || server.Id == Guid.Empty)
        {
            throw new InvalidDataException("Every server must have a stable identifier.");
        }

        if (string.IsNullOrWhiteSpace(server.Name) || server.Name.Length > 100)
        {
            throw new InvalidDataException("A server name is required and must be at most 100 characters.");
        }

        if (string.IsNullOrWhiteSpace(server.Host) || server.Host.Length > 253 ||
            (IPAddress.TryParse(server.Host, out _) is false && Uri.CheckHostName(server.Host) == UriHostNameType.Unknown))
        {
            throw new InvalidDataException("Enter a valid hostname or IP address.");
        }

        if (server.SshPort is < 1 or > 65535)
        {
            throw new InvalidDataException("The SSH port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(server.Username) || server.Username.Length > 256 || server.Username.Any(char.IsWhiteSpace))
        {
            throw new InvalidDataException("Enter a valid SSH username without whitespace.");
        }

        if (!Enum.IsDefined(server.AuthenticationMethod) || !Enum.IsDefined(server.PrivilegeElevationMethod))
        {
            throw new InvalidDataException("The server contains an unsupported authentication or privilege method.");
        }

        if (server.Group is null || server.Group.Length > 100 ||
            server.Notes is null || server.Notes.Length > 2000 || server.UpdatePreferences is null ||
            server.PrivateKeyPath is null || server.PrivateKeyPath.Length > 2048)
        {
            throw new InvalidDataException("The server group, notes, or update preferences are invalid.");
        }

        if (server.TrustedHostKeyFingerprint is null || server.TrustedHostKeyFingerprint.Length > 128)
        {
            throw new InvalidDataException("The trusted SSH host-key fingerprint is invalid.");
        }
    }

    private async Task SaveCoreAsync(ServerInventoryDocument document, CancellationToken cancellationToken)
    {
        Validate(document);
        var directory = Path.GetDirectoryName(_inventoryPath)
            ?? throw new InvalidOperationException("The server inventory path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{_inventoryPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(_inventoryPath))
            {
                File.Replace(temporaryPath, _inventoryPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, _inventoryPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void PreserveInvalidInventory()
    {
        if (!File.Exists(_inventoryPath))
        {
            return;
        }

        var preservedPath = $"{_inventoryPath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        File.Move(_inventoryPath, preservedPath);
    }
}
