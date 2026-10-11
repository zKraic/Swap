# Swap 0.4.0 preview validation

KraicDev · 11 October 2026.

**The release remains an unpublished draft while the corrected CI executable completes isolated Windows validation.** This is an experimental preview, not physical GPU or game certification.

The final source review corrected diagnostic export to read its version from the executable. The WPF audit now also verifies the exported version, author, simulation flag and graphics inventory. Results for a previous binary are not substituted for the corrected release.

Expected checks: 115 CI logic cases; 118 guest logic/native cases; 127 rendered UI cases; 33 captures; and 12 environment/identity checks. These are targets, not completed results. The final executable checksum, source commit and measured outcomes will replace this preparation record.

The isolated Windows guest has no network or physical GPU passthrough. It exercises native read-only inventory and the actual self-contained WPF executable; driver transitions use simulated adapters. Windows Update downloads, real driver installation/removal, physical monitor/cable/PCIe/power validation, interactive continuation after reboot, Windows 11 hardware and games require separate validation.

The executable is unsigned. Raw lab records, machine identifiers, credentials and virtual disks remain private. See [testing](../TESTING.md), [recovery](../RECOVERY.md) and [signing](../SIGNING.md).
