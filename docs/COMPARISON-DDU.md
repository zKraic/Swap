# Swap 0.6.0 and Display Driver Uninstaller, measured

KraicDev · 13 October 2026.

Both tools cleaned **identical copies** of the same Windows state: the official AMD display driver installed on a Radeon-class PCI device, then the card removed. Each copy was fingerprinted before the driver, after the removal and after the tool. This is one driver, one direction and one virtual machine; it is evidence, not a certification.

## Result

| What the AMD driver left after the card was removed | Created | Display Driver Uninstaller 18.1.5.3 left | Swap 0.6.0 left |
|---|---:|---:|---:|
| Driver Store packages (display, extension, OpenCL and Windows-support components) | 4 | **1** (`amdwddme.inf`, class Extension) | **0** |
| Device-database entries of those packages (`DriverDatabase\DeviceIds`) | 36 | **36** | **0** |
| Services (kernel driver, AMD External Events Utility, event sources) | 4 | 0 | 0 |
| Files in System32 / SysWOW64 | 47 / 28 | 1 / 0 (catalog of the kept package) | 0 / 0 |
| Registry values (display class, AMD settings, PnP protection entries, packages) | 914 | **57** (51 `PnpLockdownFiles` values, 6 package keys) | **0** |
| COM classes of AMD codecs | 4 | 0 | 1 (empty class without a server file) |
| Anything that existed **before** the AMD driver (AMD chipset packages, other devices, policies) | — | 0 removed | 0 removed |

Both removed the removed card's own device entry, which is expected. A Windows licensing cache that changes by itself is excluded from the file counts.

| Behaviour | Display Driver Uninstaller | Swap |
|---|---|---|
| Mode | Recommends Safe Mode; ran in normal mode | Normal mode by design: the old card is gone |
| Before removing | Tried a restore point (failed in this guest) | Restore point request, exported and sealed driver packages, registry exports |
| Undo | No | **Undo last cleanup** (see below) |
| Written to the system | Its program folder, uninstall entry and logs | `%ProgramData%\Swap\Cleanup` (the backups that undo needs) |
| Defaults outside the old brand | `RemoveVulkan=True`, `RemoveMonitors=True` | Never removes the shared Vulkan/OpenCL loaders or monitors |
| Driver search policy / boot configuration | Unchanged with these defaults | Unchanged |
| Time in the emulated guest | 298 s | 633 s (128 s inventory, export and sealing of 1.4 GB of packages before deletion) |
| Explains each item | Log | Reason per item, preview, selection |

**Undo, on the same copy after Swap's cleanup and a restart:** 98 items restored (the four packages back in the Driver Store, both services, 87 protection entries, the codec COM classes and the AMD settings), 94 skipped with a reason (what Windows had already removed by itself, the removed card's device entry and stopped processes) and 0 failures.

## What the lab taught Swap

The first runs found four gaps in Swap that were fixed before the final run on a fresh copy:

1. **Windows already removes System32 links.** Current Windows installs these files as hard links to the Driver Store and deletes them when the package is deleted (the setup log shows `Hardlinking` and `DeleteFile`). Swap planned them and reported them as failures; it now records them as removed by Windows, and still quarantines what Windows leaves (packages removed by other tools, older builds). The setup-log reader now understands `Hardlinking` lines.
2. **Media Foundation location.** Transforms are registered under `HKCR\MediaFoundation\Transforms` (and its 32-bit view), with copies under `Setup\PnpResources`; Swap now plans those, by evidence of the CLSID's server file.
3. **AMD software components.** The OpenCL and Windows-support packages use IDs such as `SWC\AMDOCL-23.19`, outside `VEN_1002`. Swap now takes them from the display INF's `AddComponent`/`ComponentIDs`, so only components of that driver qualify.
4. **`HKLM\SOFTWARE\AMD\EEU`.** The External Events Utility settings are now removed; the shared `SOFTWARE\AMD` root goes only if nothing else (for example chipset software) is left in it.

Display Driver Uninstaller removed one COM class with no server file from its built-in list. Swap has no evidence that ties such an empty class to the brand and leaves it.

## Setup

- **Guest.** Windows 10 Enterprise LTSC 2021 (19044) in QEMU 11.1 with software emulation, no network. The processor reports *AMD Ryzen 7 5800X* (no integrated graphics). Two signed AMD chipset packages (`amdgpio2.inf` 2.2.0.137, `AMDInterface.inf` 2.0.0.29) are staged first, as on a Ryzen PC. Hibernation off so every boot is a full boot.
- **Driver.** `u0202828.inf` 31.0.21924.13002 from the Microsoft Update Catalog, catalog signed by *Microsoft Windows Hardware Compatibility Publisher* (CAB SHA-256 `647A65B7D79A863036B4FD047F77FA64870E5AC86AF5F25135E6CCBA2A1D74F2`). QEMU only emulates two old ATI devices, so the package was bound to the emulated `PCI\VEN_1002&DEV_5159` through the *Have Disk* route with the model `PCI\VEN_1002&DEV_1638` (section `ati2mtag_Cezanne`). Windows reports Code 43 because there is no real Radeon, but the installation is complete: kernel driver, AMD External Events Utility, 76 System32/SysWOW64 files, extension, OpenCL and Windows-support components, codecs and about 900 registry values.
- **Removal.** The guest was shut down and started without the device. That state was frozen; each tool ran on its own copy and the guest was restarted afterwards.
- **Display Driver Uninstaller.** Official signed installer (Wagnardsoft, `Display Driver Uninstaller_18.1.5.3_Machine_X64_nullsoft_en-US.exe`, SHA-256 `1E854C00AA7086A85559551ABBFB8F0F3EBBB88A1C6126C6E7A1D0A61F25C0A1`), `-Silent -CleanAmd -NoSafeModeMsg` with its default settings, as SYSTEM in normal mode, no restart by the tool.
- **Swap.** The real planner and engine with the real Windows platform (`Swap.Tests.exe --amd-cleanup-lab`): the brands the app proposes and its default selection, exactly what **Install and clean** runs after a verified driver change.
- **Fingerprints.** Driver Store, devices, services, System32/SysWOW64 files, Program Files/ProgramData folders, registry (Khronos, AMD/ATI, display class, Run, `PnpLockdownFiles`, uninstall, `DriverDatabase`), COM classes, tasks, Store apps, driver search policy and boot configuration; plus Media Foundation, `PnpResources` and CLSID key names. Scripts and results are in [validation/comparison](validation/comparison/).

## Limits

One driver package and one direction (an AMD card removed), an emulated device with Code 43, a Windows 10 guest under software emulation, and no AMD Software (Adrenalin) suite installed. NVIDIA and Intel were not compared here. Display Driver Uninstaller ran through the guest agent as SYSTEM, without its Safe Mode workflow; its author recommends Safe Mode for stability. Both tools may behave differently with other drivers or versions. Physical card changes and games remain to be confirmed by people with the hardware: [hardware validation](https://github.com/zKraic/Swap/issues/new?template=hardware_validation.yml).
