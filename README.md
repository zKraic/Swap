<p align="center"><img src=".github/media/banner.svg" alt="Swap — GPU driver utility by KraicDev" width="100%"></p>

<p align="center"><a href="https://zkraic.github.io/Swap/">Website</a> · <a href="https://github.com/zKraic/Swap/releases">Download EXE</a> · <a href="README.es.md">Español</a> · <a href="CONTRIBUTING.md">Contribute</a></p>

**Prepare Windows after connecting a graphics card of another brand.** NVIDIA, AMD and Intel Arc. Installs the new driver, then removes what the old card left behind, with a backup and an undo button. Free, open source, no account or subscription.

> **Experimental preview · Windows 10/11 x64.** Driver installation and the guided card change have been tested with a simulated backend, not a physical GPU replacement or games. Deep cleanup was exercised on an isolated Windows 10 guest with real signed driver packages and measured against Display Driver Uninstaller with the official AMD display driver. Evaluate diagnostics and demo first. The executable is unsigned; trusted signing depends on external approval. [Validation](docs/validation/V060-RESULTS.md) · [Comparison with DDU](docs/COMPARISON-DDU.md) · [Code signing policy](docs/SIGNING.md).

![Actual Swap interface in demo mode](.github/media/app-dark-en.png)

## Driver transition

With the new card connected, **Install and clean** finds a matching Display driver through Windows Update, downloads it, backs up existing graphics packages, installs it and verifies the active signed driver and hardware Direct3D. If several GPUs remain ambiguous, explicitly select the new card; Swap does not guess. Only then does it clean up the brand that is no longer connected. Present GPUs, integrated graphics and hybrid systems are preserved.

Windows keeps a removed card as a *disconnected device* that still owns its driver package, and refuses to delete that package. Swap removes only that disconnected entry of the same brand first, then the package. (Versions up to 0.4.2 stopped at this point on a real swap.)

### I'm changing my card

Press **I'm changing my card** before you shut down. Swap backs up the graphics drivers and records how games and displays see the current card. Change the card and power on: after sign-in Swap opens by itself, recognizes the new card, gives you 20 seconds to cancel, installs its driver, removes the old brand with a backup and hands you a **validation report**. The old card still connected, no new card detected, several new cards and a driver restart are handled; nothing is shut down or restarted without your confirmation.

## Deep cleanup

![Deep cleanup preview in demo mode](.github/media/cleanup-dark-en.png)

The **Cleanup** page does what people usually run Display Driver Uninstaller for, without Safe Mode, because the old card is already gone:

| Removed with evidence | Kept on purpose |
|---|---|
| Disconnected GPU, HDMI-audio and USB-C devices; vendor virtual devices | Anything of a brand that is still connected |
| Driver packages of every graphics class (Display, HDMI audio, USB-C, extensions) and the software components they create (OpenCL, support components) | AMD chipset (VEN_1022), Intel network/storage/platform packages |
| Services, scheduled tasks and startup entries whose program is gone or being removed | PhysX (some games need it on any GPU) |
| Files a driver left in System32/SysWOW64, Vulkan/OpenCL registrations, COM codecs and Media Foundation transforms, PnP protection entries | The shared Vulkan/OpenCL loaders and Microsoft runtimes; overlay layers of Steam, OBS, RTSS, Overwolf, Epic, Rockstar |
| Control panels from the Store, vendor folders (quarantined), shader caches, settings keys, orphaned Programs entries | The processor's integrated GPU and laptops: shown unselected |

Ownership comes from device instances and the hardware IDs in each driver's INF, not from product names. Every item shows why it qualifies and can be unselected. Before changing anything Swap creates a restore point, exports packages and registry data and moves folders to an integrity-sealed quarantine in `%ProgramData%\Swap\Cleanup` (Administrators and SYSTEM only). **Undo last cleanup** restores everything that has a backup; removed disconnected devices, Store apps and caches come back on their own when needed.

The same page can remove **older unused versions of the current driver** (often several GB), keeping the active one and the newest previous version for recovery.

| Graphics | Recovery | Diagnostics |
|---|---|---|
| Hardware-matched download and installation | **Independent graphics backup** with file integrity checks | GPU presence, PnP status and signed driver |
| Guided card change; resume when Windows requires a restart | Signed INF library, compatible backup restore and cleanup undo | **Games: Direct3D 12, Vulkan and OpenCL** as the runtimes see the GPU |
| Stop on uncertainty; preserve present GPUs | Local history and support reports | Resolution, **highest refresh rate** with keep/revert, **PCIe lanes**, incidents, before/after, **validation report** |

**Games** asks Direct3D 12, Vulkan and OpenCL themselves, in a separate Swap process, which GPU they see, and warns when Vulkan or OpenCL still point to the old brand or do not see the new card. After a swap Windows often runs the monitor at 60 Hz; Diagnostics offers the highest rate the display reports and reverts after 15 seconds unless you keep it. A card running with fewer PCIe lanes than it supports (wrong slot or not fully seated) is flagged. The **validation report** is English Markdown for a GitHub hardware issue, without machine identifiers or account names. [Measured comparison with DDU](docs/COMPARISON-DDU.md) · [Critical review of 0.5.0](docs/REVIEW-050.md).

## Download and open

1. Download **[Swap.exe 0.6.0 preview](https://github.com/zKraic/Swap/releases/download/v0.6.0-preview/Swap.exe)** and optionally its [SHA-256](https://github.com/zKraic/Swap/releases/download/v0.6.0-preview/Swap.exe.sha256) (`CE5BF7F8…2D825CC`). [Release notes](https://github.com/zKraic/Swap/releases/tag/v0.6.0-preview).
2. Open it and accept Windows administrator permissions. No Swap installer, ZIP or separate .NET installation.
3. Review the detected card and choose an operation. Diagnostics need no reboot. Windows may require one after a driver change; Swap saves the session and preserves pending packages.

Automatic mode requires internet, a working Windows Update service, PowerShell 5.1 and at least 4 GB free plus driver space. It respects existing policies. Catalog drivers may be older than manufacturer releases. It does not automatically install the full Adrenalin/NVIDIA suite, PhysX or optional vendor applications. No forced package removal, Safe Mode boot changes or game tweaks.

Windows 7/8, x86 and ARM64 are not validated targets. Driver changes are blocked in Windows RDP sessions, while a known remote-control tool is running (including idle TeamViewer services), and during pending Windows servicing restarts. Diagnostics and independent backup remain available. Software inventory cannot certify GPU power or every game.

## Portable by default

The EXE includes its runtime. Local data is stored in `%LOCALAPPDATA%\Swap`; create a writable `Data` folder beside the EXE before opening it for removable-drive storage. Cleanup backups that could restore services or registry data stay in `%ProgramData%\Swap\Cleanup`. No permanent service or scheduled task. Driver operations requiring restart may temporarily register RunOnce and cache Swap for continuation; another UAC prompt may occur.

Six languages follow the PC language with English fallback: Spanish, English, Portuguese, French, German and Italian. Light/dark/system themes, keyboard navigation and high contrast.

```powershell
Swap.exe --demo       # Simulated hardware, including deep cleanup; no real changes
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

Official repository: **[zKraic/Swap](https://github.com/zKraic/Swap)**. NVIDIA, AMD, Intel and Microsoft do not sponsor or certify Swap. Their drivers are not bundled. Display Driver Uninstaller is an independent project by Wagnardsoft; it is not used or included, and was run only inside an isolated virtual machine for the published comparison. [Third-party notices](THIRD_PARTY_NOTICES.md) · [Privacy](docs/PRIVACY.md) · [Code signing policy](docs/SIGNING.md).

0.6.0 adds the guided card change, the Direct3D 12/Vulkan/OpenCL games check, the validation report and a deeper cleanup measured against DDU. [Changelog](CHANGELOG.md) · [Community and hardware results](docs/COMMUNITY.md).
