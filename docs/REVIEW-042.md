# Swap 0.4.2 review — KraicDev

Use Swap after physically connecting the new NVIDIA, AMD or Intel Arc graphics card. The automatic scope is a compatible Windows Update Display driver, verified recovery material and eligible unused Display-package cleanup.

The review found that successful cleanup did not repeat the hardware Direct3D check at completion. A working driver before deletion does not prove that it still works afterward. Installation, no-offer maintenance and reboot continuation now all require final signed-driver identity, PnP health, hardware Direct3D, backup integrity and no remaining eligible unused packages. A new package appearing during the operation stops completion instead of being deleted without a reviewed backup.

`preparation.json` records the verified result, active driver and reasons for retaining other packages. Diagnostic exports include a fresh preparation assessment. These records describe observations at their timestamps, not future stability or electrical safety. Simulation is identified by the diagnostic report and the session's Windows inventory; simulated receipts are not hardware validation.

The installation workflow also repeats the planner's environment gates before every deletion and at completion. A remote tool or unsafe environment appearing during the operation cannot silently pass the initial preflight.

The WPF audit also checks that a backup-only demo with a basic Display driver reports preparation as incomplete. Eight new regression cases cover receipts in all three paths, three final Direct3D failures, package arrival, remote-session drift before deletion/at completion and incomplete preparation prerequisites. Local logic checks: 138/138; warnings-as-errors WPF build passed. Exact public CI binary and isolated VM evidence are recorded in the version's linked validation record. A release requires that record to match the tested bytes.

This does not remove every file associated with a former vendor. Shared components, current-brand recovery drivers, present GPUs, OEM/hybrid dependencies, HDMI audio, chipset, services and vendor applications are intentionally preserved. Full manufacturer suites, physical GPU replacements and games require separate validation. Windows may require a driver restart; Swap itself remains a portable EXE.

Cleanup continues to use Windows Driver Store package operations rather than indiscriminate file deletion. Microsoft documents the distinction between [removing a device and removing its driver package](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/using-device-manager-to-uninstall-devices-and-driver-packages).
