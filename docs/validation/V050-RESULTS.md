# Swap 0.5.0 preview validation

KraicDev · 12 October 2026.

The exact GitHub Actions executable passed isolated Windows validation. **Experimental preview; physical GPU replacement and games remain untested.**

| Item | Result |
| --- | --- |
| Source commit | `7e87ef2225fa7e876ac551f56e46f5738ad3f15a` |
| EXE SHA-256 | `C4D1FCD857E633DBC688D917B9AB8D5AB36FD49D2F2EA52AE123FB455B3062F6` |
| Executable bytes | 65,762,201 |
| CI logic regression | 175 passed, 0 failed |
| Guest logic, native probes and deep-cleanup lab | 180 passed, 0 failed |
| Actual WPF interface and simulated workflows | 155 passed, 0 failed |
| Environment and executable identity checks | 12 passed |
| Guest captures / initial work-area fit | 43 / passed |
| Guest | Windows 10 Enterprise LTSC Evaluation, 10.0.19044 x64 |
| Preinstalled .NET / network / GPU passthrough | None / disabled / none |
| Guest active Display driver | Unchanged |
| Host Display drivers / host restart | Unchanged / none |
| Authenticode | Not signed; free provider review pending |

[Published release](https://github.com/zKraic/Swap/releases/tag/v0.5.0-preview) (asset re-downloaded anonymously after publication; identical SHA-256) · [Exact CI build](https://github.com/zKraic/Swap/actions/runs/38316687325) · [Machine-readable evidence](v050-result.json) · [Review](../REVIEW-050.md) · [Physical validation tracker](https://github.com/zKraic/Swap/issues/3).

The CI archive and its SHA-256 were checked before copying the executable to read-only VM media; the guest checked the same hash again. The guest ran as SYSTEM in a non-interactive session because no account was signed in; the WPF audit rendered all 155 cases there, while display enumeration in that session reports no monitors and was recorded as a read-only observation.

## Disconnected device experiment

A PCI device was hot-plugged into the guest, bound to a signed OEM driver package (`oem5.inf`) and hot-unplugged. `pnputil /delete-driver oem5.inf` failed with "One or more devices are presently installed using the specified INF" (0xE0000235) while the disconnected device existed. After `pnputil /remove-device`, deletion succeeded. This is the situation of a removed graphics card and the reason for the 0.5.0 fix.

## Deep cleanup lab

Four real signed NVIDIA driver packages (`nvhda.inf`, `nvpcf.inf`, `nvppc.inf`, `nvvad.inf`) were staged in a guest without NVIDIA hardware, together with vendor-like services, a scheduled task, a startup entry, a Programs entry, folders (including a protected PhysX folder), a per-user cache, settings and a running process. With the real Windows platform, Swap planned all of them with evidence, stopped the process first, exported and sealed backups in an administrator-only folder (ACL checked), removed 16 items and kept PhysX. Undo restored the 14 items that have backups: packages, services, task, startup entry, Programs entry, folders and settings. The USB-C package is offered unselected because Windows flags its class as boot-critical; the lab selected it explicitly.

**Not tested:** physical NVIDIA/AMD/Intel Arc replacement, real vendor suites, online Windows Update downloads, OEM/hybrid hardware, interactive continuation after a driver-change restart, Windows 11 hardware and games. Virtual observations and the lab do not certify those scenarios. All DPI/monitor combinations are not covered.
