Use Swap **after connecting the new graphics card**, including NVIDIA, AMD and Intel Arc. The primary button downloads a compatible Windows Update Display driver, backs up existing graphics packages, installs and verifies the active signed driver, then cleans up the brand that is no longer connected.

**0.5.0 fixes cleanup after a real card removal.** Windows keeps the removed GPU as a disconnected device that still uses its driver package and refuses to delete it; previous versions stopped there. Swap now removes only that disconnected device entry of the same brand right before deleting its package. Reproduced and verified in an isolated Windows 10 guest with a real hot-unplugged PCI device.

**New Cleanup page (DDU-style, without Safe Mode).** Disconnected devices, virtual devices, graphics driver packages of every class, orphaned services, tasks, startup entries and Programs entries, Store control panels, vendor folders, shader caches and settings of a disconnected brand. Ownership comes from hardware evidence; AMD chipset and Intel platform/network packages cannot match; PhysX is kept; the processor's integrated GPU and laptops are left unselected. A restore point, exported packages and registry data and an integrity-sealed quarantine come first, in an administrator-only folder. **Undo last cleanup** restores everything with a backup. Install and clean runs it automatically after verification (can be turned off).

Also new: removal of older unused versions of the current driver (keeps the active and newest previous), a PCIe-lane check for cards in a reduced slot or not fully seated, and the display's highest refresh rate with keep/revert confirmation.

**Experimental preview for Windows 10/11 x64.** Driver installation uses a simulated backend in tests; physical GPU replacement and games are not certified. Start with diagnostics or demo mode. Remote sessions and pending servicing restarts block driver changes.

Download **Swap.exe** directly. No Swap installer, ZIP or separate .NET installation is required. Windows administrator permission is required; Windows may require a restart after changing a driver.

**Unsigned:** Windows will show an unknown publisher. The free SignPath Foundation application has been submitted; provider review and trusted signing remain pending. See the [Code signing policy](https://github.com/zKraic/Swap/blob/main/docs/SIGNING.md).

[Changelog](https://github.com/zKraic/Swap/blob/main/CHANGELOG.md) · [Validation and limits](https://github.com/zKraic/Swap/blob/main/docs/validation/V050-RESULTS.md) · [Review](https://github.com/zKraic/Swap/blob/main/docs/REVIEW-050.md) · [Recovery](https://github.com/zKraic/Swap/blob/main/docs/RECOVERY.md) · [GPL-3.0-or-later](https://github.com/zKraic/Swap/blob/main/LICENSE) · [Brand policy](https://github.com/zKraic/Swap/blob/main/TRADEMARKS.md)

Copyright 2026 KraicDev. Official downloads are free; no ads, accounts or subscriptions. NVIDIA, AMD, Intel, Microsoft and the Display Driver Uninstaller project do not sponsor or certify this project.
