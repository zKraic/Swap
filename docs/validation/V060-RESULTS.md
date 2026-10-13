# Swap 0.6.0 preview validation

KraicDev · 13 October 2026.

The exact GitHub Actions executable passed isolated Windows validation. **Experimental preview; a physical GPU replacement and games remain to be confirmed on real hardware.**

| Item | Result |
| --- | --- |
| Source commit | `db194eeaff4c0e3972d7dc23fff4302229c07de4` |
| EXE SHA-256 | `CE5BF7F858E1B0D25111C12E15321880596D62562B8C73E06D04034062D825CC` |
| Executable bytes | 65,834,128 |
| CI logic regression | 190 passed, 0 failed |
| Guest logic, native probes, API check and deep-cleanup lab | 196 passed, 0 failed |
| Actual WPF interface and simulated workflows (games panel, guided card change) | 157 passed, 0 failed |
| Environment and executable identity checks | 12 passed |
| Guest captures / initial work-area fit | 47 / passed |
| Guest | Windows 10 Enterprise LTSC Evaluation, 10.0.19044 x64 |
| Preinstalled .NET / network / GPU passthrough | None / disabled / none |
| Guest active Display driver | Unchanged |
| Host Display drivers / host restart | Unchanged / none |
| Authenticode | Not signed; free provider review pending |

[Exact CI build](https://github.com/zKraic/Swap/actions/runs/38386927934) · [Machine-readable evidence](v060-result.json) · [Comparison with Display Driver Uninstaller](../COMPARISON-DDU.md) · [Physical validation tracker](https://github.com/zKraic/Swap/issues/3).

The CI archive and its SHA-256 were checked before copying the executable to read-only VM media; the guest checked the same hash again. The guest ran as SYSTEM in a non-interactive session; display enumeration there reports no monitors and is recorded as an observation. The evaluation image powers itself off every hour, so the logic and lab checks ran in one boot and the interface audit after a restart of the same clean guest.

## What the validation changed

Running the release candidate in the slow emulated guest found three robustness problems, fixed before this build: the main inventory and the display-event query finished after their 180-second limits and their complete results were discarded (signed display drivers are now filtered in WMI, 2.5 times faster on a real PC, and read-only queries get 10 minutes), and a scheduled-task export that finished late stopped the whole deep cleanup instead of being reported on that item.

## Graphics API check

`Swap.Tests.exe --api-probe-readonly` runs the same child-process check as the application. On a real GeForce GTX 1060 (host, read-only): Direct3D 12 feature level 12_1, Vulkan 1.4.312, OpenCL 3.0 CUDA, 1.4 s. In the guest (Microsoft Basic Display Adapter): no hardware Direct3D 12 device, no Vulkan or OpenCL loader, completed without errors in 4.3 s.

## Deep cleanup lab

Four real signed NVIDIA driver packages (`nvhda.inf`, `nvpcf.inf`, `nvppc.inf`, `nvvad.inf`) staged without NVIDIA hardware, with vendor-like services, a scheduled task, a startup entry, a Programs entry, folders (including a protected PhysX folder), a per-user cache, settings and a running process. The real Windows platform planned them with evidence, stopped the process, exported and sealed backups in an administrator-only folder, removed them, kept PhysX and undid the cleanup. Result: 16 items removed, 14 restored by undo (everything that has a backup), 0 failures; the backup folder ACL was checked to be Administrators and SYSTEM only.

The measured comparison with the official AMD display driver and Display Driver Uninstaller is documented separately in [COMPARISON-DDU.md](../COMPARISON-DDU.md).

**Not tested:** physical NVIDIA/AMD/Intel Arc replacement, real vendor suites, online Windows Update downloads, OEM/hybrid hardware, interactive continuation after a driver-change restart, Windows 11 hardware and games. Virtual observations and the labs do not certify those scenarios. All DPI/monitor combinations are not covered.
