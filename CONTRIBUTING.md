# Contributing to OpenVP

Thanks for helping. Bug reports, missing-API reports and pull requests are all welcome.

## Before you start

- **No Crestron code, ever.** OpenVP is a clean-room implementation. Don't paste, decompile or copy code, firmware, SDK files or documentation text from Crestron or anyone else whose license doesn't allow it. Work from observed behavior, public descriptions and your own tests.
- **No customer programs.** Never attach or commit `.smw`, `.lpz`, `.cpz`, `.sig` or other files from real jobs unless you own them and have the right to publish them. They can contain building layouts, IP addresses and passwords. If you need to show a problem, cut it down to a small program you wrote yourself, or describe it (symbol name, SmC code, compiled type, pins).
- For big changes, open an issue first so we can agree on the approach.

## Build and test

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build OpenVP.sln -c Release
dotnet run --project tests/OpenVP.Tests -c Release --no-build   # the project's own test runner
dotnet run --project src/OpenVP.Server                           # then open http://localhost:8080
```

The desktop app lives in `desktop/` (Electron); `publish-desktop.bat` builds `OpenVP.exe` on Windows.

Tests use a small built-in runner (`tests/OpenVP.Tests/TestRunner.cs`): mark a static method with `[Test]` and use `Assert`. Some docs are generated and checked by the tests; if a test tells you a doc is out of date, regenerate it with the command it prints.

## Pull requests

- Keep each pull request to one change, with a test where you can.
- CI (`.github/workflows/ci.yml`) must pass.
- Explain how you know the behavior is right: a real processor trace, a program you wrote, or the observation it's based on.
- By submitting a pull request you agree that your contribution is licensed under the project's [MIT License](LICENSE).

## Reporting unsupported APIs or symbols

OpenVP lists what a program needs that isn't implemented (slot log, `/api/slots/{n}/compat`, [docs/unsupported.md](docs/unsupported.md)). Paste that list into an issue; it's the most useful report you can give.
