using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace LinuxUpdateManager.Core.Updates;

public sealed class ApplicationUpdateService : IDisposable
{
    public const string PackageAssetName = "LinuxUpdateManager-win-x64.zip";
    public const string ManifestAssetName = "update-manifest.json";
    public const string SignatureAssetName = "update-manifest.sig";

    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/DemonRok/LinuxUpdateManager/releases/latest");
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _publicKeyPem;

    public ApplicationUpdateService(HttpClient? httpClient = null)
        : this(httpClient, UpdateSigningPublicKey.Pem)
    {
    }

    internal ApplicationUpdateService(HttpClient? httpClient, string publicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _publicKeyPem = publicKeyPem;
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LinuxUpdateManager/1.0 (+https://github.com/DemonRok/LinuxUpdateManager)");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ApplicationUpdateRelease?> CheckForUpdateAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseUri, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tagName = root.GetProperty("tag_name").GetString();
        if (string.IsNullOrWhiteSpace(tagName) || !TryParseReleaseTag(tagName, out var latestVersion))
        {
            throw new InvalidDataException("The latest GitHub release has an invalid version tag.");
        }

        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean() ||
            root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
        {
            return null;
        }

        if (latestVersion <= NormalizeVersion(currentVersion))
        {
            return null;
        }

        var assets = root.GetProperty("assets").EnumerateArray()
            .ToDictionary(asset => asset.GetProperty("name").GetString() ?? string.Empty, StringComparer.Ordinal);
        return new ApplicationUpdateRelease(
            latestVersion,
            tagName,
            root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty,
            GetOfficialAssetUri(assets, PackageAssetName, tagName),
            GetOfficialAssetUri(assets, ManifestAssetName, tagName),
            GetOfficialAssetUri(assets, SignatureAssetName, tagName));
    }

    public async Task<string> DownloadAndVerifyPackageAsync(
        ApplicationUpdateRelease release,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var manifestBytes = await DownloadLimitedAsync(release.ManifestUri, 32 * 1024, cancellationToken);
        var signatureText = await DownloadLimitedAsync(release.SignatureUri, 8 * 1024, cancellationToken);
        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(signatureText).Trim());
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The update manifest signature is malformed.", exception);
        }

        var manifest = UpdatePackageManifest.VerifyAndParse(manifestBytes, signatureBytes, _publicKeyPem);
        if (!Version.TryParse(manifest.Version, out var manifestVersion) || manifestVersion != release.Version ||
            !string.Equals(manifest.TagName, release.TagName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The signed update manifest does not match the selected GitHub release.");
        }

        Directory.CreateDirectory(workingDirectory);
        var packagePath = Path.Combine(workingDirectory, PackageAssetName);
        if (File.Exists(packagePath))
        {
            throw new IOException("A file with the update package name already exists in the staging folder.");
        }

        try
        {
            using var response = await _httpClient.GetAsync(release.PackageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 1_073_741_824)
            {
                throw new InvalidDataException("The update package exceeds the allowed size.");
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(packagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long totalBytes = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > 1_073_741_824)
                {
                    throw new InvalidDataException("The update package exceeds the allowed size.");
                }

                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await output.FlushAsync(cancellationToken);
            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash),
                    Convert.FromHexString(manifest.Sha256)))
            {
                throw new CryptographicException("The downloaded update package checksum does not match its signed manifest.");
            }

            await File.WriteAllBytesAsync(Path.Combine(workingDirectory, ManifestAssetName), manifestBytes, cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(workingDirectory, SignatureAssetName), signatureBytes, cancellationToken);
            return packagePath;
        }
        catch
        {
            TryDelete(packagePath);
            throw;
        }
    }

    public static bool TryParseReleaseTag(string tagName, out Version version)
    {
        version = new Version(0, 0, 0);
        if (!tagName.StartsWith('v') || !Version.TryParse(tagName[1..], out var parsed) ||
            parsed.Build < 0 || parsed.Revision > 0)
        {
            return false;
        }

        version = NormalizeVersion(parsed);
        return true;
    }

    private static Uri GetOfficialAssetUri(Dictionary<string, JsonElement> assets, string assetName, string tagName)
    {
        if (!assets.TryGetValue(assetName, out var asset) ||
            !asset.TryGetProperty("browser_download_url", out var downloadUrl) ||
            !Uri.TryCreate(downloadUrl.GetString(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidDataException($"The GitHub release is missing the official '{assetName}' asset.");
        }

        var expectedPath = $"/DemonRok/LinuxUpdateManager/releases/download/{Uri.EscapeDataString(tagName)}/{assetName}";
        if (!string.Equals(uri.AbsolutePath, expectedPath, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The GitHub release asset '{assetName}' points to an unexpected location.");
        }

        return uri;
    }

    private async Task<byte[]> DownloadLimitedAsync(Uri uri, int maxBytes, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > maxBytes)
        {
            throw new InvalidDataException("The update manifest asset exceeds the allowed size.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidDataException("The update manifest asset exceeds the allowed size.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static Version NormalizeVersion(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
