# Changelog · KraicDev

## 0.5.0 preview

Deep cleanup of the previous brand, a fix for a failure that every real card swap would have hit, and two new diagnostics.

- **Fix (critical): disconnected card blocked cleanup.** Windows keeps the removed GPU as a disconnected device that still uses its driver package, and `pnputil /delete-driver` refuses such a package ("One or more devices are presently installed using the specified INF"). Reproduced in an isolated Windows 10 guest with a real hot-unplugged PCI device. Installation, no-offer maintenance and restart continuation now remove only that disconnected device entry of the same brand (`pnputil /remove-device`) right before deleting its package; a connected device is never touched.
- **New: Deep cleanup page.** Finds what a disconnected brand left behind and removes it with a preview, reasons and selection: disconnected devices, the brand's virtual devices, driver packages of every graphics-related class (HDMI audio, USB-C, software components, extensions), orphaned services, scheduled tasks, startup entries, Programs entries, Store control panels, vendor folders, shader caches and settings keys. Ownership comes from hardware evidence (device instances and INF hardware IDs), not product names. AMD chipset hardware (VEN_1022) and Intel network, storage and platform packages cannot match.
- Deep cleanup creates a restore point, exports packages and registry data, records tasks and startup values and moves folders to an integrity-sealed quarantine before each change. Backups live in `%ProgramData%\Swap\Cleanup`, restricted to Administrators and SYSTEM. **Undo last cleanup** restores everything that has a backup.
- Packages Windows flags as boot-critical only because of their class (for example the GPU USB-C controller) are offered unselected. PhysX is kept because some games need it on any GPU. The processor brand's integrated GPU (possibly disabled in firmware) and laptops are left unselected. Automatic Display cleanup now also keeps the processor brand's packages unless that brand is selected explicitly.
- **Install and clean** runs the deep cleanup after the new driver is verified (Settings can turn it off). A leftover that needs review never turns a verified driver change into a failure.
- **New: older versions of the current driver.** Removes unused older Display/HDMI-audio packages of a connected brand while keeping the active one and the newest previous version for recovery.
- **New: PCIe link check.** Diagnostics report the GPU's link width; fewer lanes than the card supports points to a reduced slot or a card that is not fully seated. Idle link speed is not treated as a fault.
- **New: refresh rate after the swap.** When a display runs below its highest reported rate (Windows often falls back to 60 Hz), Diagnostics offers it with a 15-second keep/revert confirmation.
- Fixes: recent graphics incidents were not sorted (PowerShell 5.1 `Sort-Object` ignores hashtable keys); machines reporting an all-zero UUID now use the Windows installation GUID; provider names `AMD` and `INTEL` are recognized exactly.
- Tests: 175 logic cases (37 new), a destructive real-Windows deep-cleanup lab with undo, and a WPF audit of 155 cases across six pages.

## 0.4.2 preview

- Repeats hardware Direct3D verification after cleanup and stores `preparation.json` with the verified result and reasons for retained packages.
- Repeats planner and remote-session gates before each deletion and at completion; refuses completion if new eligible packages appear.
- Diagnostic exports include current preparation prerequisites.

## 0.4.1 preview

- Use after connecting the new graphics card: clearer app and project description.
- Preview eligible cleanup packages and the reason each remaining package is retained.
- Seal and verify automatic transition backups before installation, cleanup and continuation.
- Reject reused session folders and detect remaining INF packages despite metadata changes.
- Fifteen new regression cases and an expanded cleanup disclosure UI check.

## 0.4.0 preview

Initial public experimental release.

- Independent graphics-driver backup without installation or cleanup; SHA-256 manifests and verification before restoring new backups.
- Portable single EXE for Windows 10/11 x64, six interface languages, light/dark/system themes.
- Automatic Windows Update Display-driver workflow with backup, signed active-driver verification and conservative cleanup.
- GPU/display/Direct3D diagnostics, recent graphics incidents and persistent before/after checkpoints.
- Public source, contributor guides, GPL-3.0-or-later, identity policy and exact-binary validation evidence.
- Project website and direct release downloads. Trusted signing requires external approval.

Physical GPU replacement, real installation/removal and games remain untested. Old local deliveries are preserved outside the initial public repository.
