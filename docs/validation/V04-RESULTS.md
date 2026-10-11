# Swap 0.4.0 preview validation

KraicDev · 11 October 2026.

**Release preparation is in progress. The downloadable release remains a draft until the exact CI executable completes the isolated Windows checks.** This is an experimental preview, not physical GPU or game certification.

| Item | Current result |
| --- | --- |
| Source commit | `b620c400e4d4a2293c880b8fbb4e1cf136cbe61a` |
| CI executable SHA-256 | `5AB888161BCB9A006121D6CE7A6C63025F80DE3D1B824BD0D966CAB31B8726C6` |
| Executable bytes | 65,640,253 |
| CI logic tests | 115 passed, 0 failed |
| Isolated Windows tests of this exact CI EXE | Pending |
| Trusted Authenticode signature | Not signed |
| Physical NVIDIA / AMD / Intel Arc driver installation and removal | Not tested |
| Games and physical GPU replacement | Not tested |

[Successful CI build](https://github.com/zKraic/Swap/actions/runs/38163608090) · [Build provenance](https://github.com/zKraic/Swap/actions/runs/38164092172).

The isolated guest has no network or physical GPU passthrough. Its checks exercise native read-only inventory and the actual self-contained WPF executable; driver transitions use simulated adapters. Expected checks: 118 logic/native cases, 126 rendered UI cases, 33 captures and 12 environment/identity checks. Expected counts are not reported as completed results.

During preparation, a UI rehearsal exposed a test fixture that had already removed every simulated display package before testing independent backup. The fixture was reset to a populated graphics inventory. Development UI checks passed after this repair; they do not replace verification of the CI release binary.

The latest guest launch exhausted its temporary unattended login. Lab recovery uses only the original isolated guest account. A stopped or incomplete run is not counted as a successful VM test. Raw lab records, machine identifiers, credentials and virtual disks remain private.

Further limits: automatic Windows Update downloads, physical monitor/cable/PCIe/power validation, interactive continuation after reboot, Windows 11 hardware and game compatibility need separate validation. Diagnostic inventory and Direct3D status cannot certify electrical installation or every game.

See [testing](../TESTING.md), [recovery](../RECOVERY.md) and the [code signing policy](../SIGNING.md).
