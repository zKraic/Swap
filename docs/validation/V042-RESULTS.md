# Swap 0.4.2 preview validation

KraicDev · 12 October 2026.

The exact GitHub Actions executable passed isolated Windows validation. **Experimental preview; physical GPU replacement and games remain untested.**

| Item | Result |
| --- | --- |
| Source commit | `0aa06427af62493c7031d0196e74193c046c036e` |
| EXE SHA-256 | `364DCDF53A19CD888718B5158780D1019189F602501B606A6BD59379CE1E8FB7` |
| Executable bytes | 65,656,815 |
| CI logic regression | 138 passed, 0 failed |
| Guest logic and native checks | 141 passed, 0 failed |
| Actual WPF interface and simulated workflows | 129 passed, 0 failed |
| Environment and executable identity checks | 12 passed |
| Guest captures / initial work-area fit | 34 / passed |
| Guest | Windows 10 Enterprise LTSC Evaluation, 10.0.19044 x64 |
| Preinstalled .NET / network / GPU passthrough | None / disabled / none |
| Guest Display drivers and Driver Store | Unchanged |
| Host Display drivers / host restart | Unchanged / none |
| Authenticode | Not signed; free provider review pending |

[Exact CI build](https://github.com/zKraic/Swap/actions/runs/38209081611) · [Machine-readable evidence](v042-result.json) · [Review](../REVIEW-042.md) · [Physical validation tracker](https://github.com/zKraic/Swap/issues/3).

The CI archive digest and EXE checksum were checked before copying the delivery to read-only VM media. The final result was exported offline after guest shutdown and compared with the same EXE. No host driver, credential or restart was changed; guest-login recovery reused the original isolated test account. Raw machine/account details and private VM media are excluded from publication.

Eight added logic cases cover final preparation receipts in all three workflows, final hardware Direct3D failures, unexpected package arrival, remote-session drift and incomplete prerequisites. The WPF audit exercises six languages, two themes, two widths and five pages, three simulated vendor transitions, independent backup, report export, cleanup preview, explicit GPU selection and checkpoints. A backup-only demo must report an incomplete driver preparation rather than a successful installation.

An initial UI assertion expected a healthy driver in the backup-only demo and was corrected. The final exact binary passed the complete rerun. An interrupted VM execution was not accepted as completed validation.

**Not tested:** physical NVIDIA/AMD/Intel Arc replacement, actual driver installation/removal, online Windows Update downloads, OEM/hybrid hardware, interactive continuation after a driver-change restart, Windows 11 hardware and games. Native guest observations and temporary RunOnce registration/removal do not certify those scenarios. All DPI/monitor combinations are not covered.
