# Linux Update Manager

## Overview

Linux Update Manager is a Windows desktop application for managing Linux servers over SSH. The project is free software under the Apache License 2.0 and is intended to remain agentless, with application data stored locally on the Windows computer. Update checks currently support Debian and Ubuntu through APT, and Fedora and RHEL-family distributions through DNF.

## Implemented Features

The current foundation includes a Windows Forms dashboard, English and Italian runtime localization, versioned application settings, server inventory management, grouping, filtering and sortable columns, Windows Credential Manager integration, SSH connection checks, automatic or explicitly selected privilege elevation, APT/DNF update detection and confirmed package upgrades. Operational history is stored locally in SQLite and can be searched and exported. A dedicated window supports configuration-only, credentials-only and full `.lum` backups with server selection.

## Architecture

- `LinuxUpdateManager.App` contains the Windows Forms entry point, dashboard, server editor, credential manager, backup/restore and history windows, and `.resx` localization resources.
- `LinuxUpdateManager.Core` contains application configuration, validated server inventory persistence, Windows Credential Manager access, SSH connectivity, privilege verification, SQLite history and backup encryption/restoration, independent of WinForms controls.
- `LinuxUpdateManager.Tests` contains unit tests for configuration defaults, persistence, validation, and recovery.

## Configuration

The application stores `config.json`, `servers.json`, and `history.db` under `%LOCALAPPDATA%\LinuxUpdateManager`. Restored private keys are written under its `keys` subdirectory. The server inventory is schema-versioned and stores stable IDs, display names, hosts, SSH ports and usernames, authentication and privilege choices, optional key paths, groups, notes, update preferences, and non-secret Credential Manager references. Inventory writes are atomic, duplicate endpoints are rejected, and malformed or unsupported files are preserved before an empty inventory is restored.

The first-run UI language is English (`en-US`). Credentials are stored separately in Windows Credential Manager under stable per-server references. SSH passwords, sudo passwords, doas passwords, root passwords, and private-key passphrases are supported. No credential values are written to JSON or SQLite. The first successful SSH connection asks the user to review and trust the server's SHA-256 host-key fingerprint; the accepted fingerprint is saved in the server inventory. A changed fingerprint blocks the connection and requires administrator verification. Automatic privilege elevation checks for root, passwordless sudo/doas, and saved sudo, doas, or root credentials; existing explicit per-server choices remain available. APT checks refresh package indexes, report current and candidate versions, and classify security updates when repository metadata permits. DNF checks refresh metadata and report available candidates; its security classification and current package versions may be unknown. Package upgrades require explicit confirmation and never reboot servers.

## Security

The repository uses synthetic values only. Runtime configuration, SQLite databases, logs, backup files, build output, and common key or certificate files are excluded by `.gitignore`. Inventory entries and credentials are local to the Windows user profile. SSH host keys are pinned per server after explicit first-use confirmation. The privilege check uses the fixed read-only command `id -u`. Update detection refreshes APT or DNF metadata and queries candidate packages. Package upgrades require explicit confirmation, stream command output in the UI, and support cancellation. The app runs APT upgrade or DNF upgrade; it does not run full distribution upgrades or reboot servers. History output is sanitized against saved credentials and remains local. Configuration-only backups omit credential values. Credential and private-key backups require a user password and use Argon2id with AES-256-GCM; private key files are only included in an explicitly selected full backup.

## Build & Release

Requirements: Windows, Visual Studio 2022 or the .NET 10 SDK.

```sh
dotnet build LinuxUpdateManager.slnx
dotnet test LinuxUpdateManager.slnx
```

The project targets `win-x64` for distribution in a later phase. GitHub Actions and GitHub Releases have not been configured yet. No release packages are produced yet.

## License

Linux Update Manager is licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE). When redistributing a modified version, retain the license and applicable notices, mark modified files appropriately, and include any required NOTICE attributions as described by the license.

## Support the Project

The application is intended to remain free, without registration, feature restrictions, or required contributions. An optional contribution section is planned for a later phase; no payment processing or contribution UI is implemented yet.
