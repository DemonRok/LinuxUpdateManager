# Linux Update Manager

## Overview

Linux Update Manager is a Windows desktop application for managing Debian and Ubuntu servers over SSH. The project is free software under the Apache License 2.0 and is intended to remain agentless, with application data stored locally on the Windows computer.

## Implemented Features

The current foundation includes a Windows Forms dashboard, English and Italian runtime localization, versioned application settings, server inventory management, grouping and filtering, Windows Credential Manager integration, SSH connection checks, and root privilege verification.

## Architecture

- `LinuxUpdateManager.App` contains the Windows Forms entry point, dashboard, server editor, credential manager UI, and `.resx` localization resources.
- `LinuxUpdateManager.Core` contains application configuration, validated server inventory persistence, Windows Credential Manager access, SSH connectivity, and privilege verification, independent of WinForms controls.
- `LinuxUpdateManager.Tests` contains unit tests for configuration defaults, persistence, validation, and recovery.

## Configuration

The application stores `config.json` and `servers.json` under `%LOCALAPPDATA%\LinuxUpdateManager`. The server inventory is schema-versioned and stores stable IDs, display names, hosts, SSH ports and usernames, authentication and privilege choices, optional key paths, groups, notes, update preferences, and non-secret Credential Manager references. Inventory writes are atomic, duplicate endpoints are rejected, and malformed or unsupported files are preserved before an empty inventory is restored.

The first-run UI language is English (`en-US`). Credentials are stored separately in Windows Credential Manager under stable per-server references. SSH passwords, sudo passwords, root passwords, and private-key passphrases are supported. No credential values are written to JSON. The first successful SSH connection asks the user to review and trust the server's SHA-256 host-key fingerprint; the accepted fingerprint is saved in the server inventory. A changed fingerprint blocks the connection and requires administrator verification. The dashboard provides separate SSH connection and configured root-access checks; it does not perform package operations yet.

## Security

The repository uses synthetic values only. Runtime configuration, logs, backup files, build output, and common key or certificate files are excluded by `.gitignore`. Inventory entries and credentials are local to the Windows user profile. SSH host keys are pinned per server after explicit first-use confirmation. The privilege check executes only the fixed read-only command `id -u`, using the selected elevation method; package-management operations are not implemented yet.

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
