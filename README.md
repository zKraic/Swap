<p align="center"><img src=".github/media/banner.svg" alt="Swap — GPU driver utility by KraicDev" width="100%"></p>

<p align="center"><a href="https://zkraic.github.io/Swap/">Website</a> · <a href="https://github.com/zKraic/Swap/releases/tag/v0.4.0-preview">Download EXE</a> · <a href="README.es.md">Español</a> · <a href="CONTRIBUTING.md">Contribute</a></p>

**A portable Windows utility for changing GPU drivers.** NVIDIA, AMD and Intel Arc. Free, open source, no account or subscription.

> **Experimental preview · Windows 10/11 x64.** Driver installation/removal has been tested with a simulated backend, not a physical GPU replacement. Evaluate diagnostics and demo first. The executable is unsigned; trusted signing depends on external approval. [Validation](docs/validation/V04-RESULTS.md) · [Code signing policy](docs/SIGNING.md).

![Actual Swap interface in demo mode](.github/media/app-dark.png)

## A complete workflow

With the new card connected, **Install and clean** finds a matching Display driver through Windows Update, downloads it, backs up existing graphics packages, installs it and verifies the active signed driver. Only then does it clean unused Display packages from absent GPU brands. Present GPUs, integrated graphics and hybrid systems are preserved.

| Graphics | Recovery | Diagnostics |
|---|---|---|
| Hardware-matched download and installation | **Independent graphics backup** with file integrity checks | GPU presence, PnP status and signed driver |
| Resume when Windows requires a restart | Signed INF library and compatible backup restore | Hardware Direct3D probe, resolution and Hz |
| Stop on uncertainty; preserve present GPUs | Local history and support reports | Graphics incidents and before/after comparison |

Independent backup works without changing drivers. It checks altered, missing or added files before restoration; Windows signature checks still apply. Checksums detect changes, not a party replacing both files and manifest. [Community requests](docs/COMMUNITY.md).

## Download and open

1. Download **[Swap.exe](https://github.com/zKraic/Swap/releases/download/v0.4.0-preview/Swap.exe)** and optionally its [SHA-256](https://github.com/zKraic/Swap/releases/download/v0.4.0-preview/Swap.exe.sha256).
2. Open it and accept Windows administrator permissions. No Swap installer, ZIP or separate .NET installation.
3. Review the detected card and choose an operation. Diagnostics need no reboot. Windows may require one after a driver change; Swap saves the session and preserves pending packages.

Automatic mode requires internet, a working Windows Update service, PowerShell 5.1 and at least 4 GB free plus driver space. It respects existing policies. Catalog drivers may be older than manufacturer releases. It does not automatically install the full Adrenalin/NVIDIA suite, PhysX or optional vendor applications. No DDU, forced package removal, registry cleaning or game tweaks.

Windows 7/8, x86 and ARM64 are not validated targets. Driver changes are blocked during remote sessions and pending Windows servicing restarts. Software inventory cannot certify GPU power, PCIe seating or every game.

## Portable by default

The EXE includes its runtime. Local data is stored in `%LOCALAPPDATA%\Swap`; create a writable `Data` folder beside the EXE before opening it for removable-drive storage. No permanent service or scheduled task. Driver operations requiring restart may temporarily register RunOnce and cache Swap for continuation; another UAC prompt may occur.

Six languages follow the PC language with English fallback: Spanish, English, Portuguese, French, German and Italian. Light/dark/system themes, keyboard navigation and high contrast.

```powershell
Swap.exe --demo       # Simulated hardware; no real driver changes
Swap.exe --readonly   # Native diagnostics; driver operations disabled
```

## Build and contribute

**C# · WPF · .NET 10.** Windows and the .NET 10 SDK:

```powershell
./scripts/Test.ps1
./scripts/Build.ps1
./dist/Swap.exe --demo
```

Build runs logic tests and creates the single EXE plus checksum. [Contribute](CONTRIBUTING.md) · [Architecture](docs/ARCHITECTURE.md) · [Testing](docs/TESTING.md) · [Recovery](docs/RECOVERY.md) · [Security](SECURITY.md).

## License and official releases

Copyright © 2026 **KraicDev** · [GPL-3.0-or-later](LICENSE). Distributed modifications must provide corresponding source under the GPL and retain required notices. Commercial reuse is allowed; impersonation or presenting a fork as official is not authorized by the [brand policy](TRADEMARKS.md). Official downloads remain free, without ads or subscriptions.

Official repository: **[zKraic/Swap](https://github.com/zKraic/Swap)**. NVIDIA, AMD, Intel and Microsoft do not sponsor or certify Swap. Their drivers are not bundled. [Third-party notices](THIRD_PARTY_NOTICES.md) · [Privacy](docs/PRIVACY.md) · [Code signing policy](docs/SIGNING.md).
