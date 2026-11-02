# Changelog

All notable changes to this project are documented here.

## [Unreleased]

## [1.0.5] - 2026-11-02

### Fixed

- label PayPal.Me contribution button

## [1.0.4] - 2026-11-02

### Fixed

- clarify support project contribution message

## [1.0.3] - 2026-11-02

### Fixed

- handle exited update parent process

## [1.0.2] - 2026-11-02

### Fixed

- publish valid zip update packages

## [1.0.1] - 2026-11-02

### Fixed

- release patches after tagged versions
- parse signed release manifests

## [1.0.0] - 2026-11-02

### Added

- Optional Support the Project window with PayPal and Bitcoin links, with destinations stored in a dedicated resource.
- Asynchronous GitHub application update checks, RSA-signed release manifest verification, safe package extraction, external installation replacement, and previous-version rollback.
- Windows GitHub Actions validation, history-wide secret scanning, repository content checks, automatic Conventional Commit versioning, and signed self-contained release packaging.
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
- Automatic privilege selection for root, passwordless `sudo`/`doas`, or stored `sudo`, `doas`, and `su` credentials; existing explicit methods remain available.
- Debian and Ubuntu detection with APT, plus Fedora and RHEL-family detection with DNF, including package update details and reboot checks where supported.
- A version number in the main window title, without commit metadata.
- Confirmed APT and DNF package upgrades with streamed output, cancellation, and bounded multi-server operations.
- SQLite operation history with search, result and operation filters, detailed output, TXT/CSV export, and configurable retention.
- Versioned `.lum` configuration, credentials-only, and full backups, with Argon2id-derived AES-256-GCM encryption for credential and private-key data.
- Backup preview with merge/replace, duplicate-server detection, credential restoration to Windows Credential Manager, and selected private keys restored under local application data.
- A dedicated backup and restore window with per-server selection.
- Click-to-sort dashboard columns, with server names sorted alphabetically by default.

### Changed

- Dashboard window sizing adapts to localized column content and available screen space.
- Replaced synthetic dashboard rows with the local server inventory.
- Added local SQLite operation history and backup/restore controls to the dashboard.

### Fixed

- Prevented the private-key file picker from hanging by using the classic Windows dialog and a known local starting folder.
- Prevented the operation-history TXT and CSV export dialogs from blocking the interface and opened them in the application folder.
- Kept operation-history retention, status, count, and export controls aligned and visible within the available screen area.
- Tightened the Backup and Restore layout and adapted its size to the available screen area.
