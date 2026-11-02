namespace LinuxUpdateManager.Core.Updates;

public sealed record ApplicationUpdateRelease(
    Version Version,
    string TagName,
    string ReleaseNotes,
    Uri PackageUri,
    Uri ManifestUri,
    Uri SignatureUri);
