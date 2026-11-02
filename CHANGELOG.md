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

### Changed

- Dashboard window sizing adapts to localized column content and available screen space.
- Replaced synthetic dashboard rows with the local server inventory.
