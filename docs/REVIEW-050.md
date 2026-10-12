# Swap 0.5.0 review — KraicDev

A critical review of 0.4.2 looking for reasons to reject it, followed by the changes in 0.5.0. Swap is used after the new NVIDIA, AMD or Intel Arc card is connected.

## Findings in 0.4.2

| Severity | Finding | Evidence | 0.5.0 |
|---|---|---|---|
| Critical | Cleanup could never complete after a real card swap. Windows keeps the removed GPU as a disconnected device that still uses its package, and `pnputil /delete-driver` refuses it: "One or more devices are presently installed using the specified INF". The engine then stopped with "Windows did not allow removing…". Simulated tests could not see it. | Reproduced in the isolated Windows 10 guest: a real PCI device with a signed OEM driver was hot-unplugged; deletion failed (0xE0000235), succeeded after removing the disconnected device entry. | The inventory records disconnected display adapters. Before each deletion, install, maintenance and continuation remove only disconnected entries of the same brand that use that package. A connected device or another brand's device stops the cleanup. |
| High | The promise "cleans old drivers completely" was not met. Only Display-class packages were removed. HDMI audio, USB-C controller, virtual audio and software-component packages, their services (for example a container service pointing to a deleted DriverStore folder), tasks, startup entries, Programs entries, Store control panels, folders, shader caches and settings stayed. | Host inventory with an NVIDIA card: `nvhda.inf` (MEDIA), `nvppc.inf` (USB), `nvvad.inf` (MEDIA, root device), `nvpcf.inf` (SoftwareDevice), plus vendor services. | New deep cleanup with evidence-based ownership, backup, quarantine and undo; runs after a verified installation. |
| High | A vendor's root-enumerated virtual device (NVIDIA Virtual Audio, `ROOT\UNNAMED_DEVICE`) remains *connected* after the card is removed, so its package is "in use" forever. | Host device list. | Root devices whose every hardware ID is in an absent brand's GPU domain are removed before their package. |
| Medium | Automatic mode removed Display packages of **every** absent brand. A processor's integrated GPU disabled in firmware looks absent, so an Intel desktop swapping AMD→NVIDIA could lose its iGPU driver. | Planner rule `allAbsent`. | Automatic mode keeps the processor brand's packages when the CPU model likely has integrated graphics; choosing the brand explicitly still removes them. Deep cleanup shows them unselected. |
| Low | Recent graphics incidents were not sorted: in PowerShell 5.1 `Sort-Object Time` ignores hashtable keys, so the newest events could be missing from the list. | Reproduced in Windows PowerShell 5.1. | Sorted by a script block. |
| Low | Some boards report an all-zero machine UUID (the test guest does), so plans and checkpoints from different PCs could match. | Guest environment record. | The Windows installation GUID is used when the UUID is all zeros or all F. |
| Low | Exact provider names `AMD` and `INTEL` were unknown, so such packages were never eligible. | Host driver store lists `INTEL` as provider. | Exact-name table; substrings still never match. |
| Info | Core log messages are Spanish-only in a six-language interface; the HTTPS helper `OfficialDownloader` is unused. | Source review. | Kept and documented; not a safety issue. |

## How the deep cleanup is safer than name-based uninstallers

Tools like Display Driver Uninstaller remove by product names and lists and recommend Safe Mode because the old card is usually still installed. Swap runs after the swap, when the old brand has no connected hardware, so normal mode is enough and nothing of the connected card is loaded.

- **Ownership by evidence.** A package qualifies only with the exact brand provider, a graphics class and hardware IDs that all belong to that brand's GPU domain (`PCI\VEN_10DE`, `HDAUDIO\FUNC_01&VEN_10DE`, `USB\VID_0955`, `ACPI\NVDA`; `PCI\VEN_1002`, `HDAUDIO\FUNC_01&VEN_1002`), or when it is used only by that brand's disconnected devices. AMD chipset (`VEN_1022`) cannot match. Intel, which uses `VEN_8086` everywhere, is limited to Display packages, display audio and extensions of known Intel GPU IDs; network, storage and platform packages stay. Mixed packages stay.
- **Software follows the files.** Services, tasks, startup entries and Programs entries go only when their program is removed in the same plan or already missing from a vendor location; a service used by a connected device stays.
- **Defaults that respect the person.** Connected brands are never cleaned. PhysX stays. Integrated graphics of the processor brand and laptops are shown but unselected. Official uninstallers are optional and only for machine-wide entries in protected folders.
- **Reversible.** Restore point, exported and sealed driver packages, registry exports, task XML, startup values and a sealed same-volume quarantine come before each change. Backups and the undo manifest live in `%ProgramData%\Swap\Cleanup` with an ACL for Administrators and SYSTEM only, so a non-elevated process cannot plant registry data or binaries that undo would restore as administrator.
- **Fresh decisions.** The plan is rebuilt from a new inventory immediately before running; only items that are still eligible execute, and a reconnected brand or another PC stops everything. A failed backup prevents deleting that item; one failed item never blocks the others; a final inventory marks what is still present.

## Issues found while building 0.5.0

- Current Windows versions do not store the `Class` value on device instance keys, only `ClassGUID`. A first inventory read empty classes on a real PC, which would have made a connected Intel GPU look absent. The class name is now resolved from `ClassGUID`, and a brand also counts as connected when a connected device uses its Display package.
- DISM flags whole classes such as USB as boot-critical, so the NVIDIA USB-C controller package was silently skipped. It is now listed and left unselected.
- Context matters: running as SYSTEM puts the temporary folder inside `C:\Windows`, where the cleanup correctly refuses to work; tests now use a sandbox outside it.

## Other additions

- Older unused versions of the connected brand's Display/HDMI-audio driver, keeping the active package and the newest previous one.
- PCIe link width check; idle link speed is reported, not judged.
- Highest reported refresh rate with a 15-second keep/revert confirmation.

## Evidence

- 175 logic cases pass (37 new), including the disconnected-device fix in all three workflows, deep-cleanup ownership rules, ordering, backups, undo, tampering and failure handling.
- The WPF audit renders 155 cases (six languages, two themes, two widths, six pages) plus demo deep cleanup with undo and refresh revert.
- In the isolated Windows 10 guest, the pnputil behavior above was reproduced with a real device, and the deep-cleanup lab ran against real signed NVIDIA packages and vendor-like services, task, startup, Programs entry, folders, settings and a running process, then undid it. See [V050-RESULTS.md](validation/V050-RESULTS.md).

## Not tested

Physical NVIDIA/AMD/Intel Arc replacement, real vendor suites (NVIDIA App, AMD Software, Intel Graphics Software), online Windows Update downloads, OEM/hybrid laptops, Windows 11 hardware and games. A simulated or virtual result is not a hardware certification.
