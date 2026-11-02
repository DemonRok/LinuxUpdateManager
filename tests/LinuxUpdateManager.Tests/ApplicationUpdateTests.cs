using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;
using LinuxUpdateManager.App;
using LinuxUpdateManager.Core.Updates;

namespace LinuxUpdateManager.Tests;

public sealed class ApplicationUpdateTests
{
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
        var signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var parsed = UpdatePackageManifest.VerifyAndParse(bytes, signature, publicKey);
        Assert.Equal(manifest, parsed);

        bytes[^2] ^= 1;
        Assert.Throws<CryptographicException>(() => UpdatePackageManifest.VerifyAndParse(bytes, signature, publicKey));
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
}
