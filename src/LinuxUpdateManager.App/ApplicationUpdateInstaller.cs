using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using LinuxUpdateManager.App.Localization;
using LinuxUpdateManager.Core.Updates;

namespace LinuxUpdateManager.App;

internal sealed class ApplicationUpdateInstaller
{
    private const string ApplicationExecutableName = "LinuxUpdateManager.App.exe";
    private const string ApplicationAssemblyName = "LinuxUpdateManager.App.dll";
    private const long MaximumPackageBytes = 1_073_741_824;
    private const long MaximumExpandedBytes = 2_147_483_648;
    private const int MaximumArchiveEntries = 20_000;

    private readonly ApplicationUpdateService _updateService;

    public ApplicationUpdateInstaller(ApplicationUpdateService updateService)
    {
        _updateService = updateService;
    }

    public async Task StartAsync(ApplicationUpdateRelease release, CancellationToken cancellationToken = default)
    {
        var installDirectory = NormalizeDirectory(AppContext.BaseDirectory);
        if (!Directory.Exists(installDirectory) ||
            !File.Exists(Path.Combine(installDirectory, ApplicationExecutableName)) ||
            !File.Exists(Path.Combine(installDirectory, ApplicationAssemblyName)))
        {
            throw new InvalidOperationException("The current application folder is not a supported installation.");
        }

        var parentDirectory = Directory.GetParent(installDirectory)?.FullName
            ?? throw new InvalidOperationException("The application folder has no parent directory.");
        var workDirectory = Path.Combine(parentDirectory, $".LinuxUpdateManager-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);

        try
        {
            var downloadDirectory = Path.Combine(workDirectory, "download");
            Directory.CreateDirectory(downloadDirectory);
            var packagePath = await _updateService.DownloadAndVerifyPackageAsync(
                release,
                downloadDirectory,
                cancellationToken);

            var bootstrapDirectory = Path.Combine(workDirectory, "bootstrap");
            CopyDirectory(installDirectory, bootstrapDirectory);
            var bootstrapExecutable = Path.Combine(bootstrapDirectory, ApplicationExecutableName);
            if (!File.Exists(bootstrapExecutable))
            {
                throw new InvalidDataException("The external update helper could not be prepared.");
            }

            var startInfo = new ProcessStartInfo(bootstrapExecutable)
            {
                WorkingDirectory = bootstrapDirectory,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("--apply-update");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(installDirectory);
            startInfo.ArgumentList.Add(packagePath);
            startInfo.ArgumentList.Add(Path.Combine(downloadDirectory, ApplicationUpdateService.ManifestAssetName));
            startInfo.ArgumentList.Add(Path.Combine(downloadDirectory, ApplicationUpdateService.SignatureAssetName));
            startInfo.ArgumentList.Add(workDirectory);

            if (Process.Start(startInfo) is null)
            {
                throw new InvalidOperationException("Windows did not start the external update helper.");
            }
        }
        catch
        {
            TryDeleteDirectory(workDirectory);
            throw;
        }
    }

    public static bool IsUpdateCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "--apply-update", StringComparison.Ordinal);

    public static int ApplyUpdate(string[] args)
    {
        try
        {
            AppLocalizer.SetLanguage(LoadConfiguredLanguage());
            ApplyUpdateCore(args);
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                string.Format(AppLocalizer.Get("ApplicationUpdateFailed"), exception.Message),
                AppLocalizer.Get("ApplicationUpdateTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void ApplyUpdateCore(string[] args)
    {
        if (args.Length != 7 || !int.TryParse(args[1], out var parentProcessId) || parentProcessId <= 0)
        {
            throw new ArgumentException("The update helper arguments are invalid.");
        }

        var installDirectory = NormalizeDirectory(args[2]);
        var packagePath = Path.GetFullPath(args[3]);
        var manifestPath = Path.GetFullPath(args[4]);
        var signaturePath = Path.GetFullPath(args[5]);
        var workDirectory = NormalizeDirectory(args[6]);
        var expectedParent = Directory.GetParent(installDirectory)?.FullName
            ?? throw new InvalidOperationException("The installation folder has no parent directory.");

        if (!string.Equals(Directory.GetParent(workDirectory)?.FullName, expectedParent, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(workDirectory).StartsWith(".LinuxUpdateManager-update-", StringComparison.Ordinal) ||
            IsPathInside(workDirectory, installDirectory) ||
            !IsPathInside(packagePath, workDirectory) ||
            !IsPathInside(manifestPath, workDirectory) ||
            !IsPathInside(signaturePath, workDirectory))
        {
            throw new InvalidOperationException("The update staging folder is invalid.");
        }

        var applicationPath = Path.Combine(installDirectory, ApplicationExecutableName);
        var currentAssemblyPath = Path.Combine(installDirectory, ApplicationAssemblyName);
        if (!Directory.Exists(installDirectory) || !File.Exists(applicationPath) || !File.Exists(currentAssemblyPath))
        {
            throw new InvalidOperationException("The existing application installation could not be found.");
        }

        var manifestBytes = File.ReadAllBytes(manifestPath);
        var signatureBytes = File.ReadAllBytes(signaturePath);
        var manifest = UpdatePackageManifest.VerifyAndParse(manifestBytes, signatureBytes);
        if (!string.Equals(Path.GetFileName(packagePath), manifest.AssetName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The staged update package name does not match its signed manifest.");
        }

        var packageInfo = new FileInfo(packagePath);
        if (!packageInfo.Exists || packageInfo.Length > MaximumPackageBytes)
        {
            throw new InvalidDataException("The staged update package is missing or too large.");
        }

        using (var package = File.OpenRead(packagePath))
        {
            var actualHash = Convert.ToHexString(SHA256.HashData(package));
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash),
                    Convert.FromHexString(manifest.Sha256)))
            {
                throw new CryptographicException("The staged update package checksum is invalid.");
            }
        }

        var currentVersion = ReadAssemblyVersion(currentAssemblyPath);
        var updateVersion = Version.Parse(manifest.Version);
        if (updateVersion <= currentVersion)
        {
            throw new InvalidDataException("The signed update is not newer than the installed application.");
        }

        var payloadDirectory = Path.Combine(workDirectory, "payload");
        if (Directory.Exists(payloadDirectory))
        {
            Directory.Delete(payloadDirectory, recursive: true);
        }

        ExtractPackage(packagePath, payloadDirectory);
        var updatedExecutable = Path.Combine(payloadDirectory, ApplicationExecutableName);
        var updatedAssembly = Path.Combine(payloadDirectory, ApplicationAssemblyName);
        if (!File.Exists(updatedExecutable) || !File.Exists(updatedAssembly) ||
            ReadAssemblyVersion(updatedAssembly) != updateVersion)
        {
            throw new InvalidDataException("The signed package does not contain the expected application version.");
        }

        WaitForParentProcessExit(parentProcessId, applicationPath);

        ReplaceInstallation(installDirectory, payloadDirectory, workDirectory);
    }

    internal static void WaitForParentProcessExit(int parentProcessId, string expectedApplicationPath)
    {
        Process parentProcess;
        try
        {
            parentProcess = Process.GetProcessById(parentProcessId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (parentProcess)
        {
            string? parentExecutable;
            try
            {
                parentExecutable = parentProcess.MainModule?.FileName;
            }
            catch (InvalidOperationException) when (HasProcessExited(parentProcess))
            {
                return;
            }
            catch (System.ComponentModel.Win32Exception) when (HasProcessExited(parentProcess))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(parentExecutable) ||
                !string.Equals(Path.GetFullPath(parentExecutable), expectedApplicationPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The process being updated does not match this installation.");
            }

            if (!parentProcess.WaitForExit((int)TimeSpan.FromMinutes(2).TotalMilliseconds))
            {
                throw new TimeoutException("The application did not close in time. No files were changed.");
            }
        }
    }

    private static bool HasProcessExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void ReplaceInstallation(string installDirectory, string payloadDirectory, string workDirectory)
    {
        var rollbackDirectory = installDirectory + ".rollback";
        var candidateBackup = Path.Combine(workDirectory, "previous-version");
        var oldRollback = Path.Combine(workDirectory, "previous-rollback");
        var movedOldRollback = false;
        var movedCurrentVersion = false;
        var movedNewVersion = false;
        var newApplicationStarted = false;
        Process? updatedProcess = null;

        try
        {
            if (Directory.Exists(rollbackDirectory))
            {
                Directory.Move(rollbackDirectory, oldRollback);
                movedOldRollback = true;
            }

            Directory.Move(installDirectory, candidateBackup);
            movedCurrentVersion = true;
            Directory.Move(payloadDirectory, installDirectory);
            movedNewVersion = true;

            updatedProcess = Process.Start(new ProcessStartInfo(Path.Combine(installDirectory, ApplicationExecutableName))
            {
                WorkingDirectory = installDirectory,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("Windows did not start the updated application.");

            if (!updatedProcess.WaitForInputIdle(30_000) || updatedProcess.HasExited)
            {
                throw new InvalidOperationException("The updated application did not start correctly.");
            }

            Directory.Move(candidateBackup, rollbackDirectory);
            newApplicationStarted = true;
        }
        catch
        {
            if (!newApplicationStarted)
            {
                if (updatedProcess is { HasExited: false })
                {
                    try
                    {
                        updatedProcess.Kill(entireProcessTree: true);
                        updatedProcess.WaitForExit(10_000);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }

                if (movedNewVersion && Directory.Exists(installDirectory))
                {
                    Directory.Delete(installDirectory, recursive: true);
                }

                if (movedCurrentVersion && Directory.Exists(candidateBackup))
                {
                    Directory.Move(candidateBackup, installDirectory);
                }

                if (movedOldRollback && Directory.Exists(oldRollback) && !Directory.Exists(rollbackDirectory))
                {
                    Directory.Move(oldRollback, rollbackDirectory);
                }
            }

            throw;
        }
        finally
        {
            updatedProcess?.Dispose();
        }

        if (movedOldRollback)
        {
            TryDeleteDirectory(oldRollback);
        }
    }

    internal static void ExtractPackage(string packagePath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var destinationRoot = NormalizeDirectory(destinationDirectory) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count is 0 or > MaximumArchiveEntries)
        {
            throw new InvalidDataException("The update archive has an invalid number of entries.");
        }

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var normalizedName = entry.FullName.Replace('\\', '/');
            var segments = normalizedName.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (normalizedName.StartsWith('/') || Path.IsPathRooted(entry.FullName) ||
                segments.Any(segment => segment is "." or ".." || segment.Contains(':')) ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            {
                throw new InvalidDataException("The update archive contains an unsafe path or link.");
            }

            var targetPath = Path.GetFullPath(Path.Combine(destinationDirectory, normalizedName.Replace('/', Path.DirectorySeparatorChar)));
            if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The update archive contains a path outside its staging folder.");
            }

            if (normalizedName.EndsWith('/'))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            expandedBytes += entry.Length;
            if (expandedBytes > MaximumExpandedBytes)
            {
                throw new InvalidDataException("The expanded update archive exceeds the allowed size.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            using var source = entry.Open();
            using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        }
    }

    private static Version ReadAssemblyVersion(string assemblyPath)
    {
        var version = AssemblyName.GetAssemblyName(assemblyPath).Version
            ?? throw new InvalidDataException("The application package does not include a version.");
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    private static string LoadConfiguredLanguage()
    {
        try
        {
            return new LinuxUpdateManager.Core.Configuration.ConfigurationService()
                .LoadAsync().GetAwaiter().GetResult().Language;
        }
        catch
        {
            return "en-US";
        }
    }

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsPathInside(string path, string directory)
    {
        var normalizedDirectory = NormalizeDirectory(directory) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDirectory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var destination = Path.Combine(destinationDirectory, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0)
            {
                CopyDirectory(entry, destination);
            }
            else
            {
                File.Copy(entry, destination);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
