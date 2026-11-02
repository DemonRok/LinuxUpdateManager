using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LinuxUpdateManager.Core.Updates;

public sealed record UpdatePackageManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("tagName")] string TagName,
    [property: JsonPropertyName("assetName")] string AssetName,
    [property: JsonPropertyName("sha256")] string Sha256)
{
    public static UpdatePackageManifest VerifyAndParse(
        ReadOnlySpan<byte> manifestBytes,
        ReadOnlySpan<byte> signatureBytes)
        => VerifyAndParse(manifestBytes, signatureBytes, UpdateSigningPublicKey.Pem);

    internal static UpdatePackageManifest VerifyAndParse(
        ReadOnlySpan<byte> manifestBytes,
        ReadOnlySpan<byte> signatureBytes,
        string publicKeyPem)
    {
        if (manifestBytes.Length is 0 or > 32 * 1024 || signatureBytes.Length is 0 or > 8 * 1024)
        {
            throw new InvalidDataException("The signed update manifest has an invalid size.");
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        if (!rsa.VerifyData(manifestBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            throw new CryptographicException("The update manifest signature is invalid.");
        }

        var manifest = JsonSerializer.Deserialize<UpdatePackageManifest>(
                manifestBytes,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The update manifest is empty.");
        manifest.Validate();
        return manifest;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version) ||
            !System.Version.TryParse(Version, out var parsedVersion) ||
            parsedVersion.Build < 0 || parsedVersion.Revision > 0)
        {
            throw new InvalidDataException("The update manifest version is invalid.");
        }

        if (string.IsNullOrWhiteSpace(TagName) ||
            !string.Equals(TagName, $"v{parsedVersion.Major}.{parsedVersion.Minor}.{parsedVersion.Build}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The update manifest tag does not match its version.");
        }

        if (!string.Equals(AssetName, ApplicationUpdateService.PackageAssetName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The update package has an unexpected name.");
        }

        if (string.IsNullOrWhiteSpace(Sha256) ||
            !Regex.IsMatch(Sha256, "\\A[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException("The update package checksum is invalid.");
        }
    }
}
