using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;
using System.Net;
using LinuxUpdateManager.App;
using LinuxUpdateManager.Core.Updates;

namespace LinuxUpdateManager.Tests;

public sealed class ApplicationUpdateTests
{
    private const string TestTag = "v1.2.3";

    [Fact]
    public void ReleaseTagAcceptsOnlyThreePartStableVersions()
    {
        Assert.True(ApplicationUpdateService.TryParseReleaseTag("v1.2.3", out var version));
        Assert.Equal(new Version(1, 2, 3), version);
        Assert.False(ApplicationUpdateService.TryParseReleaseTag("1.2.3", out _));
        Assert.False(ApplicationUpdateService.TryParseReleaseTag("v1.2.3-rc1", out _));
        Assert.False(ApplicationUpdateService.TryParseReleaseTag("v1.2.3.4", out _));
    }

    [Fact]
    public void SignedManifestIsAcceptedOnlyWhenSignedBytesAndFieldsAreValid()
    {
        using var rsa = RSA.Create(2048);
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var manifest = new UpdatePackageManifest("1.2.3", "v1.2.3", ApplicationUpdateService.PackageAssetName, new string('A', 64));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        Assert.Contains("\"version\"", System.Text.Encoding.UTF8.GetString(bytes));
        var signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var parsed = UpdatePackageManifest.VerifyAndParse(bytes, signature, publicKey);
        Assert.Equal(manifest, parsed);

        bytes[^2] ^= 1;
        Assert.Throws<CryptographicException>(() => UpdatePackageManifest.VerifyAndParse(bytes, signature, publicKey));
    }

    [Fact]
    public void SignedManifestAcceptsLegacyPascalCaseFields()
    {
        using var rsa = RSA.Create(2048);
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            """{"Version":"1.2.3","TagName":"v1.2.3","AssetName":"LinuxUpdateManager-win-x64.zip","Sha256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""");
        var signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var parsed = UpdatePackageManifest.VerifyAndParse(bytes, signature, publicKey);

        Assert.Equal("1.2.3", parsed.Version);
        Assert.Equal("v1.2.3", parsed.TagName);
    }

    [Fact]
    public void SignedManifestRejectsUnexpectedAssetAndChecksum()
    {
        using var rsa = RSA.Create(2048);
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var manifest = new UpdatePackageManifest("1.2.3", "v1.2.3", "untrusted.zip", "not-a-checksum");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        Assert.Throws<InvalidDataException>(() => UpdatePackageManifest.VerifyAndParse(bytes, signature, publicKey));
    }

    [Fact]
    public async Task CheckForUpdateAcceptsOnlyOfficialStableReleaseAssets()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CreateReleaseJson())
        });
        using var client = new HttpClient(handler);
        using var rsa = RSA.Create(2048);
        using var service = new ApplicationUpdateService(client, rsa.ExportSubjectPublicKeyInfoPem());

        var release = await service.CheckForUpdateAsync(new Version(0, 9, 2));

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 2, 3), release.Version);
        Assert.Equal(TestTag, release.TagName);
        Assert.Equal("Release notes", release.ReleaseNotes);
        Assert.Equal(AssetUri(ApplicationUpdateService.PackageAssetName), release.PackageUri);
        Assert.Equal(AssetUri(ApplicationUpdateService.ManifestAssetName), release.ManifestUri);
        Assert.Equal(AssetUri(ApplicationUpdateService.SignatureAssetName), release.SignatureUri);
    }

    [Fact]
    public async Task CheckForUpdateRejectsAssetsFromAnUntrustedHost()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CreateReleaseJson(
                packageUrl: "https://example.invalid/LinuxUpdateManager-win-x64.zip"))
        });
        using var client = new HttpClient(handler);
        using var rsa = RSA.Create(2048);
        using var service = new ApplicationUpdateService(client, rsa.ExportSubjectPublicKeyInfoPem());

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckForUpdateAsync(new Version(0, 9, 2)));
    }

    [Fact]
    public async Task CheckForUpdateIgnoresTheInstalledVersion()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(JsonResponse(new
            {
                tag_name = "v0.9.2",
                draft = false,
                prerelease = false,
                assets = Array.Empty<object>()
            }))
        });
        using var client = new HttpClient(handler);
        using var rsa = RSA.Create(2048);
        using var service = new ApplicationUpdateService(client, rsa.ExportSubjectPublicKeyInfoPem());

        var release = await service.CheckForUpdateAsync(new Version(0, 9, 2));

        Assert.Null(release);
    }

    [Fact]
    public async Task PackageDownloadRequiresTheSignedHash()
    {
        using var rsa = RSA.Create(2048);
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var packageBytes = "verified package"u8.ToArray();
        var (manifestBytes, signatureBytes) = CreateSignedManifest(rsa, packageBytes);
        var handler = new StubHttpMessageHandler(uri => uri.AbsoluteUri switch
        {
            var value when value == AssetUri(ApplicationUpdateService.ManifestAssetName).AbsoluteUri =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(manifestBytes) },
            var value when value == AssetUri(ApplicationUpdateService.SignatureAssetName).AbsoluteUri =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(signatureBytes)))
                },
            var value when value == AssetUri(ApplicationUpdateService.PackageAssetName).AbsoluteUri =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(packageBytes) },
            _ => throw new InvalidOperationException($"Unexpected request URI: {uri}")
        });
        using var client = new HttpClient(handler);
        using var service = new ApplicationUpdateService(client, publicKey);
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"lum-update-download-{Guid.NewGuid():N}");

        try
        {
            var packagePath = await service.DownloadAndVerifyPackageAsync(CreateRelease(), workingDirectory);

            Assert.Equal(packageBytes, await File.ReadAllBytesAsync(packagePath));
            Assert.Equal(manifestBytes, await File.ReadAllBytesAsync(Path.Combine(workingDirectory, ApplicationUpdateService.ManifestAssetName)));
            Assert.Equal(signatureBytes, await File.ReadAllBytesAsync(Path.Combine(workingDirectory, ApplicationUpdateService.SignatureAssetName)));
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PackageDownloadDeletesBytesWhenSignedHashDoesNotMatch()
    {
        using var rsa = RSA.Create(2048);
        var packageBytes = "expected package"u8.ToArray();
        var alteredBytes = "altered package"u8.ToArray();
        var (manifestBytes, signatureBytes) = CreateSignedManifest(rsa, packageBytes);
        var handler = new StubHttpMessageHandler(uri => uri.AbsoluteUri switch
        {
            var value when value == AssetUri(ApplicationUpdateService.ManifestAssetName).AbsoluteUri =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(manifestBytes) },
            var value when value == AssetUri(ApplicationUpdateService.SignatureAssetName).AbsoluteUri =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(signatureBytes)))
                },
            var value when value == AssetUri(ApplicationUpdateService.PackageAssetName).AbsoluteUri =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(alteredBytes) },
            _ => throw new InvalidOperationException($"Unexpected request URI: {uri}")
        });
        using var client = new HttpClient(handler);
        using var service = new ApplicationUpdateService(client, rsa.ExportSubjectPublicKeyInfoPem());
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"lum-update-download-{Guid.NewGuid():N}");

        try
        {
            await Assert.ThrowsAsync<CryptographicException>(() =>
                service.DownloadAndVerifyPackageAsync(CreateRelease(), workingDirectory));

            Assert.False(File.Exists(Path.Combine(workingDirectory, ApplicationUpdateService.PackageAssetName)));
            Assert.False(File.Exists(Path.Combine(workingDirectory, ApplicationUpdateService.ManifestAssetName)));
            Assert.False(File.Exists(Path.Combine(workingDirectory, ApplicationUpdateService.SignatureAssetName)));
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("./unexpected.txt")]
    [InlineData("C:/outside.txt")]
    public void UpdatePackageExtractionRejectsUnsafeArchivePaths(string entryName)
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"lum-update-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        var packagePath = Path.Combine(testDirectory, "package.zip");
        var destination = Path.Combine(testDirectory, "payload");

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            using (var entry = archive.CreateEntry(entryName).Open())
            using (var writer = new StreamWriter(entry))
            {
                writer.Write("test");
            }

            Assert.Throws<InvalidDataException>(() => ApplicationUpdateInstaller.ExtractPackage(packagePath, destination));
            Assert.False(Directory.Exists(destination) && Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).Any());
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static ApplicationUpdateRelease CreateRelease() => new(
        new Version(1, 2, 3),
        TestTag,
        "Release notes",
        AssetUri(ApplicationUpdateService.PackageAssetName),
        AssetUri(ApplicationUpdateService.ManifestAssetName),
        AssetUri(ApplicationUpdateService.SignatureAssetName));

    private static Uri AssetUri(string assetName) => new(
        $"https://github.com/DemonRok/LinuxUpdateManager/releases/download/{TestTag}/{assetName}");

    private static byte[] CreateReleaseJson(string packageUrl = "https://github.com/DemonRok/LinuxUpdateManager/releases/download/v1.2.3/LinuxUpdateManager-win-x64.zip")
    {
        var assets = new[]
        {
            new { name = ApplicationUpdateService.PackageAssetName, browser_download_url = packageUrl },
            new { name = ApplicationUpdateService.ManifestAssetName, browser_download_url = AssetUri(ApplicationUpdateService.ManifestAssetName).AbsoluteUri },
            new { name = ApplicationUpdateService.SignatureAssetName, browser_download_url = AssetUri(ApplicationUpdateService.SignatureAssetName).AbsoluteUri }
        };
        return JsonResponse(new
        {
            tag_name = TestTag,
            draft = false,
            prerelease = false,
            body = "Release notes",
            assets
        });
    }

    private static byte[] JsonResponse<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value);

    private static (byte[] ManifestBytes, byte[] SignatureBytes) CreateSignedManifest(RSA rsa, byte[] packageBytes)
    {
        var manifest = new UpdatePackageManifest(
            "1.2.3",
            TestTag,
            ApplicationUpdateService.PackageAssetName,
            Convert.ToHexString(SHA256.HashData(packageBytes)));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return (bytes, signature);
    }

    private sealed class StubHttpMessageHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request.RequestUri
                ?? throw new InvalidOperationException("The request URI is missing.")));
        }
    }
}
