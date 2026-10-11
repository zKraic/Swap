# Swap 0.4.0 preview validation

KraicDev · 11 October 2026.

**The exact CI executable completed isolated Windows validation.** This is an experimental preview, not physical GPU or game certification.

| Item | Result |
| --- | --- |
| Source commit | `f23babc69e2528e2595e1560a6c8855e25c41019` |
| Executable SHA-256 | `E91A52F941A8DAD654B44E54142D763FD4D2E1E5F2EFC2FF1358FD8C8BE13DAC` |
| Executable bytes | 65,641,080 |
| CI logic tests | 115 passed, 0 failed |
| Guest logic and native Windows checks | 118 passed, 0 failed |
| Actual WPF interface and simulated workflows | 127 passed, 0 failed |
| Environment and executable identity checks | 12 passed |
| Rendered guest captures | 33 |
| Initial window fits the guest work area | Passed |
| Guest | Windows 10 Enterprise LTSC Evaluation, 10.0.19044 x64 |
| Separate preinstalled .NET runtime | None |
| Guest network / physical GPU passthrough | Disabled / none |
| Guest active display driver and Display driver store | Unchanged |
| Host active display drivers / host restart | Unchanged / none |
| Trusted Authenticode signature | Not signed |
| Physical NVIDIA / AMD / Intel Arc installation and removal | Not tested |
| Games and physical GPU replacement | Not tested |

[Exact successful CI build](https://github.com/zKraic/Swap/actions/runs/38165794734) · [Machine-readable record](v04-result.json).

The portable app was downloaded from GitHub Actions, checked against the CI archive digest and executable checksum, copied into the isolated guest, and tested without installing .NET. Reports were extracted offline after normal guest shutdown and checked against that same executable. Raw guest account details, machine identifiers, credentials and virtual disks are not public.

The interface audit covers six languages, two themes, two widths and five pages (120 layout cases), three full simulated vendor workflows, checkpoint save/read, expanded diagnostic sections, independent backup and diagnostic-report export (127 total). It checks initial work-area fit and excessive Home/Settings scroll at the wide size, not every DPI or monitor arrangement. Diagnostic export now reads the version from the executable; its metadata and inventory are checked.

Native guest checks exercised complete read-only device/driver inventory, graphics APIs and events, and temporary RunOnce registration/removal. Driver transitions and backup scenarios use simulated adapters. RunOnce registration is not proof of interactive startup after reboot.

**Still unverified:** real Windows Update downloads, real driver installation/removal, OEM/hybrid laptops, physical monitor/cable/PCIe/power validation, interactive continuation after reboot, Windows 11 hardware and games. No universal compatibility or zero-error claim is made. A future signed executable needs its own checksum and repeated exact-binary validation.

See [testing](../TESTING.md), [recovery](../RECOVERY.md) and the [code signing policy](../SIGNING.md).
