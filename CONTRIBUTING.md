# Contributing

Contributions are welcome. Please open an issue to discuss a substantial change before starting implementation.

## Development Requirements

- Windows 10 or later.
- .NET 10 SDK or Visual Studio 2022 with the .NET desktop development workload.

## Local Validation

```sh
dotnet build LinuxUpdateManager.slnx
dotnet test LinuxUpdateManager.slnx
```

Keep pull requests focused and include tests for changes to Core behavior. User interface text must be present in both `Strings.resx` and `Strings.it-IT.resx`. Documentation and code comments must be written in English.

Use only synthetic hostnames, addresses, usernames, and package data in code, tests, examples, screenshots, and pull requests. Never submit credentials, private keys, runtime configuration, logs, or real infrastructure data.

## Commit Messages

Use Conventional Commits, for example `feat: add server inventory model`, `fix: preserve malformed configuration`, or `docs: clarify build requirements`. The release automation described in the project specification is not implemented yet.

By submitting a contribution, you agree that it may be distributed under the project's Apache License 2.0.
