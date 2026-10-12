# Swap 0.4.1 preview validation

KraicDev · 11 October 2026.

The exact GitHub Actions executable passed isolated Windows validation. **Experimental preview; physical GPU replacement and games are not certified.**

| Item | Result |
| --- | --- |
| Source commit | `7a19a79c3a4bf1e8edf8ed235a2bcef0b19ad696` |
| EXE SHA-256 | `F6058C720201FE1146827ED5871EB5720213F616F235B55F2C14D9FA2F3337AC` |
| Executable bytes | 65,652,352 |
| CI logic regression | 130 passed, 0 failed |
| Guest logic and native checks | 133 passed, 0 failed |
| Actual WPF interface and simulated workflows | 129 passed, 0 failed |
| Environment and executable identity checks | 12 passed |
| Guest captures | 34 |
| Initial window fits guest work area | Passed |
| Guest | Windows 10 Enterprise LTSC Evaluation, 10.0.19044 x64 |
| Preinstalled .NET / network / physical GPU passthrough | None / disabled / none |
| Guest active Display drivers and Driver Store | Unchanged |
| Host active Display drivers / host reboot | Unchanged / none |
| Trusted Authenticode signature | Not signed; free application review pending |

[Exact CI build](https://github.com/zKraic/Swap/actions/runs/38192262072) · [Machine-readable evidence](v041-result.json) · [Critical review](../REVIEW-041.md).

The CI archive digest and executable checksum were checked before copying the EXE to read-only VM media. Tests ran in the isolated interactive guest. Reports were extracted offline after normal guest shutdown and checked against the same delivery. Guest-login recovery reused the original local test account; no host credential, GPU driver or restart was changed. Raw machine/account details and private VM media are not published.

Fifteen added logic cases cover transition backup integrity, corrupt/missing recovery files, session reuse, INF identity drift, hardware Direct3D gates, PCI display association, ambiguous GPU selection and known remote-tool names. The WPF audit covers six languages, two themes, two widths and five pages, three complete simulated vendor workflows, checkpoint and diagnostic disclosures, independent backup, diagnostic export/footer metadata, cleanup preview and explicit GPU selection (129 cases). Wide Home/Settings scroll and initial work-area fit are checked; all DPI/monitor combinations are not.

Native guest checks exercise complete read-only inventory, display/Direct3D enumeration, event queries and temporary RunOnce registration/removal. This is not proof of interactive app continuation after a driver-change reboot. Driver installation/removal and recovery scenarios use simulated adapters.

**Not tested:** physical NVIDIA/AMD/Intel Arc replacement, actual driver installation/removal, online Windows Update driver downloads, OEM/hybrid hardware, interactive driver-session continuation after reboot, Windows 11 hardware and games. No electrical/cable/power/FPS certification or universal error-free claim is made. A signed future binary requires a new checksum and repeated exact-binary checks.
