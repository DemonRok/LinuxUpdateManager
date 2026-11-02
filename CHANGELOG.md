# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Added

- Initial .NET 10 solution with WinForms, Core, and unit test projects.
- Initial dashboard and English/Italian runtime language selection.
- Versioned JSON configuration with atomic persistence and malformed-file recovery.
- Apache License 2.0 project documentation.
- Multi-resolution Windows application icon.
- Validated, schema-versioned server inventory in `servers.json` with atomic writes and malformed-file recovery.
- Add, edit, remove, search, group, and filter server entries in the dashboard.
- Windows Credential Manager support for SSH, sudo, root, and private-key passphrase credentials.
- SSH connection checks using SSH.NET, with first-use SHA-256 host-key confirmation and pinning.
- Root privilege checks for direct root, `sudo`, `sudo -i`, and `su -` configurations.
- Debian and Ubuntu detection, APT index refresh, package update details, security update counts when repository metadata identifies them, and reboot-required status.

### Changed

- Dashboard window sizing adapts to localized column content and available screen space.
- Replaced synthetic dashboard rows with the local server inventory.
