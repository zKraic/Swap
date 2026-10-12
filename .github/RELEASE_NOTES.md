Use Swap **after connecting the new graphics card**, including changes between NVIDIA, AMD and Intel Arc. The primary button downloads a compatible Display driver through Windows Update, backs up existing graphics packages, installs and verifies the active signed driver, then removes eligible unused Display packages from absent brands.

0.4.1 adds a compact cleanup preview explaining what can be removed and why other packages are retained. Automatic transition backups now receive integrity checks before installation, cleanup and continuation. Reused session folders are rejected, and final cleanup checks track INF identity even if metadata changes. Eight new regression cases and an expanded WPF disclosure check cover these changes.

**Experimental preview for Windows 10/11 x64.** Installation and removal tests use a simulated backend; physical GPU replacement and games are not certified. Start with diagnostics or demo mode. Remote sessions and pending servicing restarts block driver changes.

Download **Swap.exe** directly. No Swap installer, ZIP or separate .NET installation is required. Windows administrator permission is required; Windows may require a restart after changing a driver.

**Unsigned:** Windows will show an unknown publisher. Free trusted signing is being prepared and depends on external approval. See the [Code signing policy](https://github.com/zKraic/Swap/blob/main/docs/SIGNING.md).

The automatic driver source is the configured Windows Update service. Full vendor software suites and optional components are not installed automatically.

[Validation and limits](https://github.com/zKraic/Swap/blob/main/docs/validation/V04-RESULTS.md) · [Recovery](https://github.com/zKraic/Swap/blob/main/docs/RECOVERY.md) · [GPL-3.0-or-later](https://github.com/zKraic/Swap/blob/main/LICENSE) · [Brand policy](https://github.com/zKraic/Swap/blob/main/TRADEMARKS.md)

Copyright 2026 KraicDev. Official downloads are free; no ads, accounts or subscriptions. NVIDIA, AMD, Intel and Microsoft do not sponsor or certify this project.
