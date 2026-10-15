Change your graphics card brand on Windows without leftovers — **before or after the swap**. NVIDIA, AMD and Intel Arc. Free, portable, no account.

**Tested on a real card change.** On a Windows 11 desktop a GeForce GTX 1060 was replaced with a Radeon RX 7600 using **I'm changing my card**. Swap opened by itself after sign-in without a UAC prompt, verified the AMD driver Windows had installed, and removed the NVIDIA devices, driver packages, services, folders, Programs entries, System32 modules and 1,889 protected PnP entries. Direct3D 12 (FL 12_2), Vulkan 1.4 and OpenCL saw the new card, PCIe 4.0 x8 and 180 Hz. Only PhysX and a GeForce NOW SDK folder used by a game launcher stayed, on purpose.

**What 1.0 adds**
- **Fully automatic card change:** sign-in task without UAC (protected copy, deleted when done), retries while the network or Windows' own driver installation starts, full shutdown before unplugging.
- **Deeper cleanup:** protected Windows 11 PnP entries (saved as a hive, removed with the restore privilege, restored exactly by undo), Programs entries in any Windows language, vendor installer modules, a second pass for what the first one orphaned, and vendor programs that restart are stopped before moving their folders.
- **Progress with percentages**, **Free cleanup backups**, one-click highest refresh rate after the change, and a link to the manufacturer's optional control panel.
- Fixes: text encoding on non-English Windows, the taskbar icon, and the clean button after an analysis.

**Exact executable validated.** SHA-256 `035C7A910F4BA9A9E2FCB2123D7E85B61731A097E25E6183A807BE0858B9980A` (source `88ee5b7`): 199/199 logic tests in CI; on the same real PC after the change 205/205 exact tests (elevated) and 158/158 interface cases; in an isolated Windows 10 guest 206/206 checks including the real deep-cleanup lab with undo and the protected PnP entry lab, 158/158 interface cases and 12/12 environment and identity checks.

Download **Swap.exe** directly. No installer, ZIP or separate .NET. Administrator permission is required; Windows may require a restart after changing a driver.

**Not yet code-signed:** Windows shows an unknown publisher. The free SignPath Foundation application is pending. See the [Code signing policy](https://github.com/zKraic/Swap/blob/main/docs/SIGNING.md).

[Changelog](https://github.com/zKraic/Swap/blob/main/CHANGELOG.md) · [Validation and limits](https://github.com/zKraic/Swap/blob/main/docs/validation/V100-RESULTS.md) · [Comparison with DDU](https://github.com/zKraic/Swap/blob/main/docs/COMPARISON-DDU.md) · [Recovery](https://github.com/zKraic/Swap/blob/main/docs/RECOVERY.md) · [GPL-3.0-or-later](https://github.com/zKraic/Swap/blob/main/LICENSE) · [Brand policy](https://github.com/zKraic/Swap/blob/main/TRADEMARKS.md)

Copyright 2026 KraicDev. Official downloads are free; no ads, accounts or subscriptions. NVIDIA, AMD, Intel, Microsoft and the Display Driver Uninstaller project do not sponsor or certify this project.
