# Swap 0.4.1 critical review

KraicDev · 11 October 2026.

Swap is used **after the new graphics card is physically connected**. Hardware changes are performed by the owner while the PC is powered off; Swap does not connect a card or check its electrical installation.

## Findings and fixes

| Finding | Correction |
| --- | --- |
| Transition backups checked for an INF but did not receive independent backup integrity checks | Every exported Display package is sealed and verified before installation, before cleanup and during continuation. Missing or altered recovery files stop the operation. |
| Final cleanup checked an entire driver record; different metadata could hide a remaining INF | Final verification now checks the case-insensitive INF identity, both after installation and in no-offer maintenance. |
| A caller could reuse an existing session folder | New operations reject existing folders before system calls. Previous recovery material cannot be silently overwritten. |
| Users could not easily see the cleanup scope | The Home disclosure lists eligible packages and why others are retained. Diagnostic export includes the same decisions. |
| Driver verification covered signature and PnP without requiring hardware Direct3D | A target-associated Direct3D verification now gates cleanup after installation, maintenance and continuation. Unavailable or ambiguous hardware verification preserves old packages. |
| Public introduction did not emphasize when to run Swap | App and project descriptions now state that the new card must already be connected. |
| Window footer had a fixed old version and no preview marker | The footer reads executable metadata and shows experimental status in every language; the UI audit checks it. |
| Native display IDs can be a PCI hardware prefix, while PnP IDs include an instance suffix | Display association now uses bounded, case-insensitive hardware matching. Identical PCI models remain unverified instead of receiving guessed routing. |
| Multiple candidate GPUs could be selected arbitrarily | A single unconfigured card is preferred; ambiguous inventories require explicit selection. Basic adapters include their vendor and PCI device code. |
| Demo without a selected GPU could fall through to native diagnostics | Empty demo selection now remains synthetic. |
| Remote checks covered the app's RDP session only | Running TeamViewer, AnyDesk, RustDesk, Chrome Remote Desktop or Parsec tools conservatively block driver changes. No remote tool is stopped. This detects known tools, even when idle, not every possible remote session. |

## Automation, without overstating the scope

The primary button discovers a hardware-matched Display driver from the computer's configured Windows Update service, downloads it, backs up existing Display packages, installs the driver, verifies the selected active signed OEM driver and hardware Direct3D, and removes eligible unused Display packages from absent brands. Automatic detection considers all absent supported brands. It refreshes inventory before each deletion, never uses forced removal and preserves present GPUs.

If the catalog has no newer offer and the active driver is signed and healthy, Swap automatically performs the backed-up cleanup without reinstalling. If the target has no verified driver and no compatible offer, it stops. Network errors are not treated as an empty catalog. Managed Windows policies remain effective.

This is **not complete removal of every vendor component**: HDMI audio, chipset, PhysX, applications, services, profiles and registry entries are not scrubbed. They may be shared or still needed. Manufacturer EXEs imported by the user retain their own interactive setup. Catalog drivers are not guaranteed to be the newest gaming release or a complete vendor suite.

Opening the portable app requires no installer, separate .NET or host restart. Windows can require a restart after driver changes. Continuation requires the same cached executable and verified session; legacy unsealed transition backups cannot pass the new cleanup integrity gate. Read-only diagnostics remain available when driver changes are blocked.

Checksum manifests detect missing or changed files, not an attacker replacing both files and manifest. Driver signature verification remains required. A passing VM or simulation does not prove physical driver replacement, game stability or compatibility on all PCs.

## Regression evidence

Fifteen new behavioral cases exercise sealed transition backups, corruption before installation and cleanup, maintenance corruption, reboot-time tampering/missing manifests, existing-session protection, INF metadata drift, cleanup explanations, Direct3D gates, display association, GPU selection and known remote-tool names. The WPF audit additionally opens the cleanup disclosure, checks explicit GPU selection and verifies the footer against executable metadata. Final binary evidence is recorded separately in [0.4.1 validation](validation/V041-RESULTS.md).

## Distribution and community

The initial r/software announcement remains removed by Reddit's filters. That notice gives no exact cause; no moderator approval is presumed. Reposting the same announcement to evade removal is avoided. Future announcements describe post-connection use, Display-package scope, unsigned preview status and physical-test limits. Visibility and retention depend on community moderators, not the application author.
