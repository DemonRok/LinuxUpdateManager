using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Konscious.Security.Cryptography;
using LinuxUpdateManager.Core.Configuration;
using LinuxUpdateManager.Core.Security;
using LinuxUpdateManager.Core.Servers;

namespace LinuxUpdateManager.Core.Backups;

public sealed class BackupService
{
    public const string BackupExtension = ".lum";
    public const int CurrentFormatVersion = 1;
    private const string FormatName = "LinuxUpdateManagerBackup";
    private const string KdfName = "Argon2id";
    private const int ArgonMemoryKiB = 64 * 1024;
    private const int ArgonIterations = 3;
    private const int ArgonParallelism = 2;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int MaximumArchiveBytes = 100 * 1024 * 1024;
    private const int MaximumKeyFileBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task ExportAsync(
        string filePath,
        BackupPayload payload,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(payload);
        ValidatePayload(payload);
        var hasSecrets = payload.IncludesCredentials || payload.IncludesPrivateKeys ||
                         payload.Credentials.Count > 0 || payload.PrivateKeys.Count > 0;
        if (hasSecrets && string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("A backup password is required when credentials or private keys are included.", nameof(password));
        }

        var clearPayload = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        byte[]? passwordBytes = null;
        byte[]? key = null;
        byte[]? archiveBytes = null;
        try
        {
            BackupEnvelope envelope;
            if (string.IsNullOrEmpty(password))
            {
                envelope = new BackupEnvelope
                {
                    Format = FormatName,
                    Version = CurrentFormatVersion,
                    Encrypted = false,
                    Payload = payload
                };
            }
            else
            {
                passwordBytes = Encoding.UTF8.GetBytes(password);
                var salt = RandomNumberGenerator.GetBytes(SaltSize);
                var nonce = RandomNumberGenerator.GetBytes(NonceSize);
                var ciphertext = new byte[clearPayload.Length];
                var tag = new byte[TagSize];
                var aad = CreateAssociatedData(salt);
                try
                {
                    key = await DeriveKeyAsync(passwordBytes, salt, cancellationToken);
                    using (var aes = new AesGcm(key, TagSize))
                    {
                        aes.Encrypt(nonce, clearPayload, ciphertext, tag, aad);
                    }

                    envelope = new BackupEnvelope
                    {
                        Format = FormatName,
                        Version = CurrentFormatVersion,
                        Encrypted = true,
                        Kdf = KdfName,
                        MemoryKiB = ArgonMemoryKiB,
                        Iterations = ArgonIterations,
                        Parallelism = ArgonParallelism,
                        Salt = Convert.ToBase64String(salt),
                        Nonce = Convert.ToBase64String(nonce),
                        Tag = Convert.ToBase64String(tag),
                        Ciphertext = Convert.ToBase64String(ciphertext)
                    };
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(ciphertext);
                    CryptographicOperations.ZeroMemory(tag);
                    CryptographicOperations.ZeroMemory(aad);
                    CryptographicOperations.ZeroMemory(salt);
                    CryptographicOperations.ZeroMemory(nonce);
                }
            }

            archiveBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
            if (archiveBytes.Length > MaximumArchiveBytes)
            {
                throw new InvalidDataException("The backup exceeds the supported size limit.");
            }

            await WriteAtomicallyAsync(filePath, archiveBytes, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearPayload);
            if (passwordBytes is not null)
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }

            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }

            if (archiveBytes is not null)
            {
                CryptographicOperations.ZeroMemory(archiveBytes);
            }
        }
    }

    public async Task<BackupPreview> OpenAsync(
        string filePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists || fileInfo.Length is <= 0 or > MaximumArchiveBytes)
        {
            throw new InvalidDataException("The backup file is missing, empty, or larger than the supported size limit.");
        }

        var archiveBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        byte[]? passwordBytes = null;
        byte[]? key = null;
        byte[]? clearPayload = null;
        try
        {
            var envelope = JsonSerializer.Deserialize<BackupEnvelope>(archiveBytes, JsonOptions)
                ?? throw new InvalidDataException("The backup file is empty or malformed.");
            ValidateEnvelope(envelope);

            BackupPayload payload;
            if (!envelope.Encrypted)
            {
                payload = envelope.Payload
                    ?? throw new InvalidDataException("The backup does not contain its configuration data.");
            }
            else
            {
                if (string.IsNullOrEmpty(password))
                {
                    throw new BackupPasswordRequiredException();
                }

                passwordBytes = Encoding.UTF8.GetBytes(password);
                var salt = DecodeBase64(envelope.Salt!, SaltSize, "salt", exactSize: true);
                var nonce = DecodeBase64(envelope.Nonce!, NonceSize, "nonce", exactSize: true);
                var tag = DecodeBase64(envelope.Tag!, TagSize, "authentication tag", exactSize: true);
                var ciphertext = DecodeBase64(envelope.Ciphertext!, MaximumArchiveBytes, "encrypted contents");
                key = await DeriveKeyAsync(passwordBytes, salt, cancellationToken);
                clearPayload = new byte[ciphertext.Length];
                var aad = CreateAssociatedData(salt);
                try
                {
                    using var aes = new AesGcm(key, TagSize);
                    aes.Decrypt(nonce, ciphertext, tag, clearPayload, aad);
                }
                catch (CryptographicException exception)
                {
                    throw new InvalidDataException(
                        "The password is incorrect or the backup failed its integrity check.", exception);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(salt);
                    CryptographicOperations.ZeroMemory(nonce);
                    CryptographicOperations.ZeroMemory(tag);
                    CryptographicOperations.ZeroMemory(ciphertext);
                    CryptographicOperations.ZeroMemory(aad);
                }

                payload = JsonSerializer.Deserialize<BackupPayload>(clearPayload, JsonOptions)
                    ?? throw new InvalidDataException("The decrypted backup contents are malformed.");
            }

            ValidatePayload(payload);
            if (!envelope.Encrypted &&
                (payload.IncludesCredentials || payload.IncludesPrivateKeys ||
                 payload.Credentials.Count > 0 || payload.PrivateKeys.Count > 0))
            {
                throw new InvalidDataException("Credential or private-key data must be stored in an encrypted backup.");
            }

            return new BackupPreview(
                payload,
                envelope.Encrypted,
                payload.IncludesConfiguration,
                payload.IncludesCredentials,
                payload.IncludesPrivateKeys,
                Path.GetFileName(filePath));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The backup file is malformed or contains unsupported data.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(archiveBytes);
            if (passwordBytes is not null)
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }

            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }

            if (clearPayload is not null)
            {
                CryptographicOperations.ZeroMemory(clearPayload);
            }
        }
    }

    public static void ValidatePayload(BackupPayload payload)
    {
        if (payload.SchemaVersion != 1 || payload.Credentials is null || payload.PrivateKeys is null)
        {
            throw new InvalidDataException("The backup has an unsupported schema or missing data.");
        }

        if (payload.IncludesConfiguration != (payload.Configuration is not null && payload.Inventory is not null) ||
            !payload.IncludesConfiguration && (payload.Configuration is not null || payload.Inventory is not null) ||
            payload.IncludesPrivateKeys && !payload.IncludesConfiguration ||
            !payload.IncludesCredentials && payload.Credentials.Count > 0 ||
            !payload.IncludesPrivateKeys && payload.PrivateKeys.Count > 0)
        {
            throw new InvalidDataException("The backup mode does not match the data it contains.");
        }

        if (payload.Configuration is { } configuration &&
            (configuration.SchemaVersion != ConfigurationService.CurrentSchemaVersion ||
             configuration.Language is not ("en-US" or "it-IT") ||
             configuration.SshConnectionTimeoutSeconds is < 1 or > 300 ||
             configuration.CommandTimeoutSeconds is < 1 or > 86400 ||
             configuration.MaximumConcurrentOperations is < 1 or > 32 ||
             configuration.HistoryRetentionDays is < 0 or > 3650))
        {
            throw new InvalidDataException("The backup contains invalid application settings.");
        }

        if (payload.Inventory is not null)
        {
            ServerInventoryService.Validate(payload.Inventory);
        }

        var serverIds = payload.Inventory?.Servers.Select(server => server.Id).ToHashSet() ?? [];
        var credentialKeys = new HashSet<(Guid ServerId, CredentialKind Kind)>();
        foreach (var credential in payload.Credentials)
        {
            if (credential.ServerId == Guid.Empty ||
                payload.IncludesConfiguration && !serverIds.Contains(credential.ServerId) ||
                !Enum.IsDefined(credential.Kind) ||
                string.IsNullOrEmpty(credential.Secret) || Encoding.Unicode.GetByteCount(credential.Secret) > 5 * 512 ||
                !credentialKeys.Add((credential.ServerId, credential.Kind)))
            {
                throw new InvalidDataException("The backup contains an invalid or duplicate credential entry.");
            }
        }

        var privateKeyIds = new HashSet<Guid>();
        foreach (var privateKey in payload.PrivateKeys)
        {
            if (!serverIds.Contains(privateKey.ServerId) || privateKey.Contents is null ||
                privateKey.Contents.Length is <= 0 or > MaximumKeyFileBytes ||
                !privateKeyIds.Add(privateKey.ServerId))
            {
                throw new InvalidDataException("The backup contains an invalid or duplicate private key file.");
            }

            if (!string.Equals(privateKey.FileName, NormalizePrivateKeyFileName(privateKey.FileName), StringComparison.Ordinal))
            {
                throw new InvalidDataException("The backup private key file name must be a file name, not a path.");
            }
        }
    }

    private static void ValidateEnvelope(BackupEnvelope envelope)
    {
        if (envelope.Format != FormatName || envelope.Version != CurrentFormatVersion)
        {
            throw new InvalidDataException("The backup format version is not supported.");
        }

        if (!envelope.Encrypted)
        {
            if (envelope.Payload is null || envelope.Ciphertext is not null || envelope.Salt is not null ||
                envelope.Nonce is not null || envelope.Tag is not null)
            {
                throw new InvalidDataException("The unencrypted backup has an invalid structure.");
            }

            return;
        }

        if (envelope.Payload is not null || envelope.Kdf != KdfName ||
            envelope.MemoryKiB != ArgonMemoryKiB || envelope.Iterations != ArgonIterations ||
            envelope.Parallelism != ArgonParallelism || envelope.Salt is null || envelope.Nonce is null ||
            envelope.Tag is null || envelope.Ciphertext is null)
        {
            throw new InvalidDataException("The encrypted backup has unsupported encryption settings.");
        }
    }

    private static async Task<byte[]> DeriveKeyAsync(
        byte[] passwordBytes,
        byte[] salt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(() =>
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                DegreeOfParallelism = ArgonParallelism,
                Iterations = ArgonIterations,
                MemorySize = ArgonMemoryKiB
            };
            return argon2.GetBytes(KeySize);
        }, cancellationToken);
    }

    private static byte[] CreateAssociatedData(byte[] salt) =>
        Encoding.UTF8.GetBytes($"{FormatName}|{CurrentFormatVersion}|{KdfName}|{ArgonMemoryKiB}|{ArgonIterations}|{ArgonParallelism}|{Convert.ToBase64String(salt)}");

    private static byte[] DecodeBase64(string value, int maximumLength, string field, bool exactSize = false)
    {
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"The backup {field} is malformed.", exception);
        }

        if (decoded.Length == 0 || decoded.Length > maximumLength || exactSize && decoded.Length != maximumLength)
        {
            CryptographicOperations.ZeroMemory(decoded);
            throw new InvalidDataException($"The backup {field} has an invalid size.");
        }

        return decoded;
    }

    private static async Task WriteAtomicallyAsync(
        string filePath,
        byte[] contents,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The backup file path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, useAsync: true))
            {
                await stream.WriteAsync(contents, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string NormalizePrivateKeyFileName(string? fileName)
    {
        var leafName = Path.GetFileName(fileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(leafName) || leafName is "." or ".." ||
            leafName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException("The backup contains an invalid private key file name.");
        }

        return leafName;
    }

    private sealed class BackupEnvelope
    {
        public string Format { get; set; } = string.Empty;

        public int Version { get; set; }

        public bool Encrypted { get; set; }

        public BackupPayload? Payload { get; set; }

        public string? Kdf { get; set; }

        public int? MemoryKiB { get; set; }

        public int? Iterations { get; set; }

        public int? Parallelism { get; set; }

        public string? Salt { get; set; }

        public string? Nonce { get; set; }

        public string? Tag { get; set; }

        public string? Ciphertext { get; set; }
    }
}

public sealed class BackupPasswordRequiredException() : InvalidOperationException("This backup requires a password.");
