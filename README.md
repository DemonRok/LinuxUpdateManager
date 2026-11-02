# Linux Update Manager

## Overview

Linux Update Manager is a Windows desktop application for managing Debian and Ubuntu servers over SSH. The project is free software under the Apache License 2.0 and is intended to remain agentless, with application data stored locally on the Windows computer.

## Implemented Features

Phase 1 establishes the .NET 10 solution, a WinForms dashboard with synthetic demonstration rows, English and Italian resources, runtime language selection, and versioned JSON application configuration. The dashboard does not connect to servers or perform package operations yet.

## Architecture

- `LinuxUpdateManager.App` contains the Windows Forms entry point, dashboard, and `.resx` localization resources.
- `LinuxUpdateManager.Core` contains application configuration models and file persistence, independent of WinForms.
- `LinuxUpdateManager.Tests` contains unit tests for configuration defaults, persistence, validation, and recovery.

## Configuration

The application stores `config.json` under `%LOCALAPPDATA%\LinuxUpdateManager`. The current schema stores the language preference, SSH and command timeout defaults, maximum concurrent operation setting, startup update-check preference, and file logging preference. Missing configuration is created with English (`en-US`) as the default. Malformed or unsupported configuration is preserved beside the original file before defaults are restored.

No server inventory or credentials are stored by this phase.

## Security

The repository uses synthetic demonstration values only. Runtime configuration, logs, backup files, build output, and common key or certificate files are excluded by `.gitignore`. SSH host-key verification, credential storage, and privilege elevation are planned for later phases and are not implemented yet.

## Build & Release

Requirements: Windows, Visual Studio 2022 or the .NET 10 SDK.

```sh
dotnet build LinuxUpdateManager.slnx
dotnet test LinuxUpdateManager.slnx
```

The project targets `win-x64` for distribution in a later phase. GitHub Actions and GitHub Releases have not been configured yet. No release packages are produced by Phase 1.

## License

Linux Update Manager is licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE). When redistributing a modified version, retain the license and applicable notices, mark modified files appropriately, and include any required NOTICE attributions as described by the license.

## Support the Project

The application is intended to remain free, without registration, feature restrictions, or required contributions. An optional contribution section is planned for a later phase; no payment processing or contribution UI is implemented in this phase.
