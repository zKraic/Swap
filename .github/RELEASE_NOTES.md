Use Swap **after connecting the new graphics card**, including changes between NVIDIA, AMD and Intel Arc. The primary button downloads a compatible Display driver through Windows Update, backs up existing graphics packages, installs and verifies the active signed driver, then removes eligible unused Display packages from absent brands.

0.4.1 adds a compact cleanup preview explaining what can be removed and why other packages are retained. Automatic transition backups receive integrity checks before installation, cleanup and continuation. Reused sessions are rejected; final cleanup tracks INF identity even if metadata changes.

Cleanup now requires hardware Direct3D verification on the selected GPU. Multiple ambiguous GPUs require explicit selection, and basic adapters show their PCI device code. Display association handles Windows hardware/instance identifiers without guessing identical-card routing. Known running remote-control tools block driver changes without stopping those tools. The footer reads the EXE version and displays experimental status in six languages.

Fifteen new logic regression cases and additional WPF checks cover these changes. See the attached exact-binary validation record and the [critical review](https://github.com/zKraic/Swap/blob/main/docs/REVIEW-041.md).

**Experimental preview for Windows 10/11 x64.** Installation and removal tests use a simulated backend; physical GPU replacement and games are not certified. Start with diagnostics or demo mode. Remote sessions and pending servicing restarts block driver changes.

Download **Swap.exe** directly. No Swap installer, ZIP or separate .NET installation is required. Windows administrator permission is required; Windows may require a restart after changing a driver.

**Unsigned:** Windows will show an unknown publisher. The free SignPath Foundation application has been submitted; provider review and trusted signing remain pending. See the [Code signing policy](https://github.com/zKraic/Swap/blob/main/docs/SIGNING.md).

The automatic driver source is the configured Windows Update service. Cleanup covers eligible unused Display packages from absent brands. Shared components, chipset, HDMI audio, PhysX, services, vendor applications and game profiles are preserved; full vendor suites are not installed automatically.

[Validation and limits](https://github.com/zKraic/Swap/blob/main/docs/validation/V041-RESULTS.md) · [Recovery](https://github.com/zKraic/Swap/blob/main/docs/RECOVERY.md) · [GPL-3.0-or-later](https://github.com/zKraic/Swap/blob/main/LICENSE) · [Brand policy](https://github.com/zKraic/Swap/blob/main/TRADEMARKS.md)

Copyright 2026 KraicDev. Official downloads are free; no ads, accounts or subscriptions. NVIDIA, AMD, Intel and Microsoft do not sponsor or certify this project.
