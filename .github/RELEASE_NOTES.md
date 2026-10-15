Change your graphics card brand on Windows without leftovers — **before or after the swap**. NVIDIA, AMD and Intel Arc. Free, portable, no account.

**Tested on a real card change.** On a Windows 11 desktop a GeForce GTX 1060 was replaced with a Radeon RX 7600 using **I'm changing my card** (Swap 1.0). Swap opened by itself after sign-in without a UAC prompt, verified the AMD driver Windows had installed and removed what NVIDIA left behind, including 1,889 protected PnP entries. 1.1 adds what that change showed was still missing: the manufacturer's software, and a cleanup that explains itself.

**What 1.1 adds**
- **The manufacturer's control panel in one click.** When NVIDIA App or AMD Software: Adrenalin Edition is missing, Diagnostics → *Software and components* (and the banner at the end of a card change) installs the latest version: link read from the vendor's official page, download only from the vendor's own host over HTTPS, a valid signature from exactly that manufacturer required, then the vendor's installer (NVIDIA App silently, AMD's for you to complete) and an install report. Nothing is downloaded without your confirmation. Intel: Swap opens the official page.
- **Software and components check:** control panel, Visual C++ 2015–2022 (x64 and x86), DirectX 9 files for older games, HDMI/DisplayPort audio, PhysX on NVIDIA, GPU scheduling and a pending restart. Optional components are never shown as errors.
- **A cleanup you can follow:** *Select all* / *Select none*; unticked items stay unticked when the plan is rebuilt; *What could not be removed and why* in plain words; a cleanup report in your language (removed, not removed and why, kept on purpose, how to undo).
- **Programs closed in order:** the confirmation names the open programs of the old brand; each is asked to close like you would close it, and only ended if it is still running 10 seconds later.

__VALIDATION__

Download **Swap.exe** directly. No installer, ZIP or separate .NET. Administrator permission is required; Windows may require a restart after changing a driver.

**Not yet code-signed:** Windows shows an unknown publisher. The free SignPath Foundation application is pending. See the [Code signing policy](https://github.com/zKraic/Swap/blob/main/docs/SIGNING.md).

[Changelog](https://github.com/zKraic/Swap/blob/main/CHANGELOG.md) · [Validation and limits](https://github.com/zKraic/Swap/blob/main/docs/validation/V110-RESULTS.md) · [Real card change (1.0)](https://github.com/zKraic/Swap/blob/main/docs/validation/V100-RESULTS.md) · [Comparison with DDU](https://github.com/zKraic/Swap/blob/main/docs/COMPARISON-DDU.md) · [Recovery](https://github.com/zKraic/Swap/blob/main/docs/RECOVERY.md) · [GPL-3.0-or-later](https://github.com/zKraic/Swap/blob/main/LICENSE) · [Brand policy](https://github.com/zKraic/Swap/blob/main/TRADEMARKS.md)

Copyright 2026 KraicDev. Official downloads are free; no ads, accounts or subscriptions. NVIDIA, AMD, Intel, Microsoft and the Display Driver Uninstaller project do not sponsor or certify this project.
