# Testing · KraicDev

The exact release-binary results are in [validation/V04-RESULTS.md](validation/V04-RESULTS.md); historical/private runs are not substitutes.

Run scripts/Test.ps1 for logic and build checks. scripts/Build.ps1 runs logic tests before self-contained single-EXE publication. Test-only WPF audit: Swap.exe --ui-audit=C:\SwapUiAudit; it uses DemoPlatform and never performs real driver changes.

Coverage includes all six NVIDIA/AMD/Intel directions, integrated GPU preservation, hardware ID boundaries, catalog ranking, upgrades/OEM protection, pending reboot/remote/admin/incomplete inventory blocks, signed driver/PnP/version verification, backup/export/install/cleanup failures, cancellation, concurrency, hot-plug, continuation and final-state drift.

Independent-backup tests cover Display-only export, no driver mutations, altered/added/missing files, nested manifest names, malicious manifest paths, incomplete/admin/duplicate inventory rejection, export failures and cancellation. Manifest hashes are integrity metadata, not publisher authentication; signatures are checked separately.

Native guest checks cover inventory, graphics APIs/events and temporary RunOnce register/remove. The WPF audit covers 120 combinations of six languages, two themes, two sizes and five pages; three full simulated workflows; checkpoint/disclosures; and independent backup. It checks layout and excessive Home/Settings scroll at the wide size, not every DPI or monitor configuration.

Before a stable release: validate physical GPU replacement in each vendor direction, same-vendor upgrades, OEM/hybrid laptops, real Windows Update downloads/install/removal, interruption/recovery, interactive restart continuation, Windows 11, and representative games/display/audio configurations. Record hardware/OS/version/hash and each outcome. Do not test privileged driver changes on a user's working remote PC.

A successful build or VM render is not evidence of real driver safety or game stability. No zero-error or universal compatibility claim is made.
