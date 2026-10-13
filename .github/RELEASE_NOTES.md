Use Swap **after connecting the new graphics card** — or, new in 0.6.0, **before you change it**. NVIDIA, AMD and Intel Arc.

**I'm changing my card.** Press it before shutting down: Swap backs up the graphics drivers and records how games and displays see the current card. Change the card and power on; after sign-in Swap opens, recognizes the new card, waits 20 seconds (cancellable), installs its driver, removes the old brand with a backup and gives you a validation report. Nothing is shut down or restarted without confirmation.

**Games: Direct3D 12, Vulkan and OpenCL.** A separate Swap process asks the runtimes which GPU they actually see (Direct3D 12 feature level, Vulkan devices and version, OpenCL platforms) and warns when they still point to the old brand or miss the new card. Read-only; checked on a real GeForce GTX 1060.

**Deeper cleanup, measured against Display Driver Uninstaller.** On identical copies of a Windows guest with the official AMD display driver and the card removed, DDU 18.1.5.3 (default AMD cleanup) left the AMD extension package, its 36 device-database entries and 57 registry values; Swap 0.6.0 left one empty COM class and nothing that existed before the AMD driver was touched by either tool. Swap's **Undo** then restored 98 items with 0 failures. New in the cleanup: the driver's software components (OpenCL, support components) through its own `ComponentIDs`, files a driver left in System32/SysWOW64, Vulkan/OpenCL registrations, COM codecs and Media Foundation transforms, PnP protection entries. Shared Vulkan/OpenCL loaders and overlay layers (Steam, OBS, RTSS…) stay. [Comparison with data and scripts](https://github.com/zKraic/Swap/blob/main/docs/COMPARISON-DDU.md).

**Validation report.** English Markdown/JSON for a GitHub hardware issue, without machine identifiers or account names; **Copy for GitHub** only copies it and opens the form.

**Experimental preview for Windows 10/11 x64.** Physical GPU replacement and games are not yet confirmed on real hardware; the comparison is one driver in one virtual machine. Start with diagnostics or demo mode. Remote sessions and pending servicing restarts block driver changes.

**Exact executable validated.** SHA-256 `CE5BF7F858E1B0D25111C12E15321880596D62562B8C73E06D04034062D825CC` (source `db194ee`) passed 190/190 logic tests in CI and, in an isolated Windows 10 guest, 196/196 checks including the graphics API check and the real deep-cleanup lab with undo, 157/157 rendered interface cases (games panel and guided card change included) and 12 environment and identity checks. Validating this build in a slow guest found and fixed three robustness problems on slow PCs.

Download **Swap.exe** directly. No Swap installer, ZIP or separate .NET installation is required. Windows administrator permission is required; Windows may require a restart after changing a driver.

**Unsigned:** Windows will show an unknown publisher. The free SignPath Foundation application has been submitted; provider review and trusted signing remain pending. See the [Code signing policy](https://github.com/zKraic/Swap/blob/main/docs/SIGNING.md).

[Changelog](https://github.com/zKraic/Swap/blob/main/CHANGELOG.md) · [Validation and limits](https://github.com/zKraic/Swap/blob/main/docs/validation/V060-RESULTS.md) · [Comparison with DDU](https://github.com/zKraic/Swap/blob/main/docs/COMPARISON-DDU.md) · [Recovery](https://github.com/zKraic/Swap/blob/main/docs/RECOVERY.md) · [GPL-3.0-or-later](https://github.com/zKraic/Swap/blob/main/LICENSE) · [Brand policy](https://github.com/zKraic/Swap/blob/main/TRADEMARKS.md)

Copyright 2026 KraicDev. Official downloads are free; no ads, accounts or subscriptions. NVIDIA, AMD, Intel, Microsoft and the Display Driver Uninstaller project do not sponsor or certify this project.
