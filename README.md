<p align="center"><img src=".github/media/banner.svg" alt="Swap — GPU driver utility by KraicDev" width="100%"></p>

<p align="center"><a href="https://zkraic.github.io/Swap/">Website</a> · <a href="https://github.com/zKraic/Swap/releases/latest">Download EXE</a> · <a href="README.es.md">Español</a> · <a href="CONTRIBUTING.md">Contribute</a></p>

**Change your graphics card brand on Windows without leftovers.** NVIDIA, AMD and Intel Arc. Before the swap Swap prepares everything; after it, Swap installs the new driver and removes what the old card left behind, with a backup and an undo button. Free, open source, portable, no account or subscription.

> **Swap 1.1 · Windows 10/11 x64 · tested on a real card change.** A GeForce GTX 1060 was replaced with a Radeon RX 7600 using **I'm changing my card**: Swap opened by itself after sign-in without a UAC prompt, verified the AMD driver Windows installed, removed the NVIDIA devices, packages, services, folders, Programs entries and protected PnP entries, and Direct3D 12, Vulkan and OpenCL saw the new card. Every release executable is also validated as built in CI, in an isolated Windows guest and with the interface audit. [Validation](docs/validation/V100-RESULTS.md) · [Comparison with DDU](docs/COMPARISON-DDU.md). Not yet code-signed: trusted signing depends on external approval ([policy](docs/SIGNING.md)).

![Actual Swap interface in demo mode](.github/media/app-dark-en.png)

## Driver transition

With the new card connected, **Install and clean** finds a matching Display driver through Windows Update, downloads it, backs up existing graphics packages, installs it and verifies the active signed driver and hardware Direct3D. If several GPUs remain ambiguous, explicitly select the new card; Swap does not guess. Only then does it clean up the brand that is no longer connected. Present GPUs, integrated graphics and hybrid systems are preserved.

Windows keeps a removed card as a *disconnected device* that still owns its driver package, and refuses to delete that package. Swap removes only that disconnected entry of the same brand first, then the package. (Versions up to 0.4.2 stopped at this point on a real swap.)

### I'm changing my card

Press **I'm changing my card** before you shut down. Swap backs up the graphics drivers and records how games and displays see the current card. Press **Shut down** in Swap (a full shutdown, so the PC can be unplugged), change the card and power on: after sign-in Swap opens by itself with administrator rights and no UAC prompt, recognizes the new card, gives you 20 seconds to cancel, installs its driver, removes the old brand with a backup and hands you a **validation report**. If the network is still connecting or Windows is installing the same driver on its own, Swap waits and tries again for up to eight minutes and keeps a driver Windows installed once it is verified. The old card still connected, no new card detected, several new cards and a driver restart are handled; nothing is shut down or restarted without your confirmation. The status bar shows each step with a percentage, and the cleanup counts item by item. When it finishes, the banner offers the display's highest refresh rate in one click, or installs the manufacturer's optional control panel if Windows Update installed the driver without it.

### Manufacturer software

Diagnostics → **Software and components** checks what games and the card rely on besides the driver: the manufacturer's control panel, the Visual C++ 2015–2022 runtimes (x64 and x86), the legacy DirectX 9 files older games use, the card's HDMI/DisplayPort audio, PhysX on NVIDIA, hardware-accelerated GPU scheduling and a pending restart. Optional components are named, never reported as errors.

When **NVIDIA App** or **AMD Software: Adrenalin Edition** is missing, one click installs the latest version: Swap reads the installer link from the vendor's official page, downloads it over HTTPS only from the vendor's own download host, refuses it unless Windows reports a valid signature from exactly that manufacturer, and runs it (NVIDIA App silently; AMD's installer for you to complete). It then checks that Windows lists it, deletes the installer and saves an install report. Nothing is downloaded without your confirmation. Without administrator rights or during a remote session, and for Intel (whose site does not allow automated downloads), Swap opens the official page instead.

## Deep cleanup

![Deep cleanup preview in demo mode](.github/media/cleanup-dark-en.png)

The **Cleanup** page does what people usually run Display Driver Uninstaller for, without Safe Mode, because the old card is already gone:

| Removed with evidence | Kept on purpose |
|---|---|
| Disconnected GPU, HDMI-audio and USB-C devices; vendor virtual devices | Anything of a brand that is still connected |
| Driver packages of every graphics class (Display, HDMI audio, USB-C, extensions) and the software components they create (OpenCL, support components) | AMD chipset (VEN_1022), Intel network/storage/platform packages |
| Services, scheduled tasks and startup entries whose program is gone or being removed | PhysX (some games need it on any GPU) and the GeForce NOW SDK folder game launchers use |
| Files a driver or the vendor installer left in System32/SysWOW64, Vulkan/OpenCL registrations, COM codecs and Media Foundation transforms, PnP protection entries (including those Windows 11 reserves for TrustedInstaller) | The shared Vulkan/OpenCL loaders and Microsoft runtimes; overlay layers of Steam, OBS, RTSS, Overwolf, Epic, Rockstar |
| Control panels from the Store, vendor folders (quarantined), shader caches, settings keys, orphaned Programs entries in any Windows language | The processor's integrated GPU and laptops: shown unselected |

Ownership comes from device instances, the hardware IDs in each driver's INF and the file's author, not from product names. Every item shows why it qualifies and can be unselected; **Select all** and **Select none** help, and what you untick stays unticked when the plan is rebuilt. Open programs of the old brand are named before they are closed, and a program with a window is asked to close first. Before changing anything Swap creates a restore point, exports packages and registry data and moves folders to an integrity-sealed quarantine in `%ProgramData%\Swap\Cleanup` (Administrators and SYSTEM only). Protected Windows entries are saved as a registry hive and removed with the administrator's restore privilege, never re-permissioned. A vendor program that restarts and keeps a folder open is stopped and the move retried; anything a third-party program still holds stays in place. After a card change a second pass takes what the first one orphaned. **Undo last cleanup** restores everything that has a backup; removed disconnected devices, Store apps and caches come back on their own when needed. Afterwards the page lists **what could not be removed and why**, in plain words, and a cleanup report in your language is saved: what was removed, what was not and why, and what was kept on purpose. Once everything works, **Free cleanup backups** releases that space for good.

The same page can remove **older unused versions of the current driver** (often several GB), keeping the active one and the newest previous version for recovery.

| Graphics | Recovery | Diagnostics |
|---|---|---|
| Hardware-matched download and installation | **Independent graphics backup** with file integrity checks | GPU presence, PnP status and signed driver |
| Guided card change; resume when Windows requires a restart | Signed INF library, compatible backup restore and cleanup undo | **Games: Direct3D 12, Vulkan and OpenCL** as the runtimes see the GPU |
| Stop on uncertainty; preserve present GPUs | Local history and support reports | Resolution, **highest refresh rate** with keep/revert, **PCIe lanes**, incidents, before/after, **validation report** |

**Games** asks Direct3D 12, Vulkan and OpenCL themselves, in a separate Swap process, which GPU they see, and warns when Vulkan or OpenCL still point to the old brand or do not see the new card. After a swap Windows often runs the monitor at 60 Hz; Diagnostics offers the highest rate the display reports and reverts after 15 seconds unless you keep it. A card running with fewer PCIe lanes than it supports (wrong slot or not fully seated) is flagged. The **validation report** is English Markdown for a GitHub hardware issue, without machine identifiers or account names. [Measured comparison with DDU](docs/COMPARISON-DDU.md) · [Critical review of 0.5.0](docs/REVIEW-050.md).

## Download and open

1. Download **[Swap.exe 1.0.0](https://github.com/zKraic/Swap/releases/download/v1.0.0/Swap.exe)** and optionally its [SHA-256](https://github.com/zKraic/Swap/releases/download/v1.0.0/Swap.exe.sha256) (`035C7A91…B9980A`). [Release notes](https://github.com/zKraic/Swap/releases/tag/v1.0.0).
2. Open it and accept Windows administrator permissions. No Swap installer, ZIP or separate .NET installation.
3. Review the detected card and choose an operation. Diagnostics need no reboot. Windows may require one after a driver change; Swap saves the session and preserves pending packages.

Automatic mode requires internet, a working Windows Update service, PowerShell 5.1 and at least 4 GB free plus driver space. It respects existing policies. Catalog drivers may be older than manufacturer releases. The manufacturer's control panel is never installed on its own: it is offered and installed only when you confirm. No forced package removal, Safe Mode boot changes or game tweaks.

Windows 7/8, x86 and ARM64 are not validated targets. Driver changes are blocked in Windows RDP sessions, during a remote-control session (TeamViewer while someone is connected; AnyDesk, RustDesk, Chrome Remote Desktop or Parsec whenever they run) and while Windows servicing waits for a restart. Pending deletions of leftover installer files do not count as a pending restart. Diagnostics and independent backup remain available. Software inventory cannot certify GPU power or every game.

## Portable by default

The EXE includes its runtime. Local data is stored in `%LOCALAPPDATA%\Swap`; create a writable `Data` folder beside the EXE before opening it for removable-drive storage. Cleanup backups that could restore services or registry data stay in `%ProgramData%\Swap\Cleanup`. No permanent service or scheduled task. A prepared card change, or a driver change that needs a restart, registers one sign-in task for your account (plus a RunOnce fallback that asks for UAC only if the task cannot run). It opens a copy of Swap in `%ProgramData%\Swap\Continuation` that only administrators can change, and Swap deletes the task when it finishes or you cancel.

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

1.1.0 adds the manufacturer's control panel in one click (official page, vendor host, vendor signature), a software and components check, and a cleanup you can follow: select all/none, choices that stay, what could not be removed and why, and a cleanup report. 1.0.0 was the first release validated on a real card change. [Changelog](CHANGELOG.md) · [Community and hardware results](docs/COMMUNITY.md).
