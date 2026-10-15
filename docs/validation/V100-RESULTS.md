# Swap 1.0.0 validation

KraicDev · 14 October 2026.

Swap 1.0.0 is the first release validated on a real card change. The exact GitHub Actions executable also passed the logic tests, an isolated Windows guest and the interface audit, and was run again on the same real PC after the change.

| Item | Result |
| --- | --- |
| Source commit | `88ee5b7a2e08c47c095ab5b5ae3668978175fb4b` |
| EXE SHA-256 | `035C7A910F4BA9A9E2FCB2123D7E85B61731A097E25E6183A807BE0858B9980A` |
| Executable bytes | 65,957,585 |
| CI logic regression | 199 passed, 0 failed |
| Real PC after the change, exact tests (elevated) | 205 passed, 0 failed |
| Real PC, exact executable interface audit | 158 passed, 0 failed, 48 captures |
| Isolated guest: logic, native probes, API check, deep-cleanup lab, protected PnP entry lab | 206 passed, 0 failed |
| Isolated guest: actual WPF interface and simulated workflows | 158 passed, 0 failed, 48 captures |
| Isolated guest: environment and executable identity checks | 12 passed |
| Authenticode | Not signed; free provider review pending |

[Published release](https://github.com/zKraic/Swap/releases/tag/v1.0.0) (published from the maintainer account; asset re-downloaded anonymously, identical SHA-256) · [Exact CI build](https://github.com/zKraic/Swap/actions/runs/38639242945) · [Machine-readable evidence](v100-result.json) · [Comparison with Display Driver Uninstaller](../COMPARISON-DDU.md) · [Physical validation tracker](https://github.com/zKraic/Swap/issues/3).

## The real card change

| | Before | After |
| --- | --- | --- |
| GPU | MSI GeForce GTX 1060 6GB (`PCI\VEN_10DE&DEV_1C03`) | ASUS Radeon RX 7600 (`PCI\VEN_1002&DEV_7480`) |
| Driver | NVIDIA 32.0.15.8278 (582.78) with NVIDIA App 11.0.5 | AMD 32.0.21030.2001, installed by Windows Update, signed, no PnP error |
| Direct3D 12 | Feature level 12_1 | Feature level 12_2 |
| Vulkan | 1.4.312 on the GTX 1060 | 1.4.315 on the RX 7600 |
| OpenCL | 3.0 CUDA | 2.1 AMD-APP, 1 GPU |
| PCIe link | — | x8 Gen 4 of x8 Gen 4 |
| Displays | 1920×1080 at 179 Hz, 3840×2160 at 60 Hz | 1920×1080 at 180 Hz, 3840×2160 at 60 Hz |

PC: Intel Core i5-9400F (no integrated graphics), Gigabyte H310M DS2 2.0, 16 GB, Windows 11 Pro build 26300 in Spanish, UEFI with Secure Boot, BitLocker off, TeamViewer installed and idle, 9 GB free on the system drive (Swap ran portable with its data on another internal drive).

1. **Preparation** (release candidate `6350463`): **I'm changing my card** backed up the NVIDIA Display package (2.46 GB) and registered the sign-in task.
2. **Test restart with the old card** to install pending Windows updates: the task opened Swap elevated without a UAC prompt, the RunOnce fallback stepped aside, and the change stayed prepared. This start showed "Windows 11 Pro Â· 10.0.26300": an encoding bug fixed in build `58397d6`, which was then armed in place of the candidate.
3. **Full shutdown from Swap, power unplugged, card replaced.** At the first sign-in Windows had already installed the AMD driver through Windows Update. Swap opened by itself without UAC, recognised the RX 7600, verified the signed driver and hardware Direct3D, removed the disconnected GTX 1060 and its package, and ran the deep cleanup.
4. **That cleanup removed 51 items and reported 1,890 for review**: Windows 11 denied deleting 1,889 PnP lockdown entries reserved for TrustedInstaller, and restarted NVIDIA programs kept `C:\ProgramData\NVIDIA Corporation` open. Translated Programs entries, hidden sub-components, NVIDIA App capture modules and the Virtual Audio service had also stayed. All of this is fixed in 1.0.0.
5. **Second cleanup with the fixes** (build `6b6e06f`): 1,895 removed, 0 failed, including the 1,889 protected entries (removed with the restore privilege after a hive backup). Only `GfnRuntimeSdk` stayed, held open by Epic Games Launcher, which ships NVIDIA's GeForce NOW SDK; 1.0.0 keeps that folder on purpose.
6. **Final state:** no NVIDIA device, service, task, process or NVIDIA-authored System32/SysWOW64 file; the RX 7600 at 180 Hz. Kept on purpose: PhysX, the GeForce NOW SDK folder, the boot-critical `nvppc` package (opt-in), one hidden NVIDIA installer entry with no file evidence, and two Driver Store folders Windows no longer registers.

The exact 1.0.0 tests then ran on this PC (elevated): the real sign-in task registration and removal with the protected copy, a protected PnP entry round trip (hive backup, privileged delete, restore), the read-only cleanup preview (nothing left to remove automatically) and the graphics API check above.

## Isolated guest

Windows 10 Enterprise LTSC Evaluation 10.0.19044 x64 under QEMU, no network, no GPU passthrough, no preinstalled .NET, as SYSTEM in a non-interactive session; the evaluation image powers itself off every hour, so the logic and lab checks run in one boot and the interface audit in the next one of the same clean guest. The deep-cleanup lab stages four real signed NVIDIA driver packages without NVIDIA hardware, with vendor-like services, a task, a startup entry, a Programs entry, folders, settings and a running process, cleans them with the real Windows platform, checks the administrator-only backup and undoes it. Result: 16 items removed, 14 restored by undo (everything that has a backup), 0 failures. The protected PnP entry round trip and the graphics API check also passed in the guest. A first attempt was discarded because an overdue Windows Defender full scan saturated the two emulated CPUs and pushed the inventory past its limit; the scan was cancelled in the disposable guest (real-time protection stayed on) and the run was repeated from scratch.

The measured comparison with the official AMD display driver and Display Driver Uninstaller is in [COMPARISON-DDU.md](../COMPARISON-DDU.md) (Swap 0.6.0; 1.0.0 additionally removes the protected PnP entries Windows 11 reserves).

**Not tested:** other directions (AMD to NVIDIA, either to or from Intel Arc), laptops and hybrid graphics, vendor installer suites such as AMD Software, games beyond the runtime checks, and every DPI/monitor combination. One real change is evidence, not a certification of every system; reports from other combinations are welcome through the hardware validation form.
