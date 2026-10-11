# Code signing policy

KraicDev · Swap.

**Current status: unsigned.** No trusted code-signing identity has been issued for Swap. Metadata/SHA-256 do not remove UAC's unknown publisher. Self-signed certificates do not solve this for other PCs. No paid signing service will be purchased.

## Free route

[SignPath Foundation](https://signpath.org/terms.html) offers eligible open-source projects free signing. Acceptance is discretionary: released/maintained project, verifiable reputation, traceable builds, maintainer MFA and manual release approval. A new project cannot assume eligibility.

[Application](https://signpath.org/apply.html): zKraic/Swap, GPL-3.0-or-later, Swap, KraicDev (zKraic), self-contained WPF Windows x64 EXE, no bundled proprietary drivers.

If accepted, certificate publisher is **SignPath Foundation**; product author remains KraicDev. Provider attribution will be added only after approval. Committer, reviewer and signing approver: KraicDev (zKraic). External changes require review. Account MFA must be verified, not invented.

## Release checks

1. Review source, dependencies, workflow and version.
2. Build in CI and preserve source/artifact provenance and unsigned hash.
3. Submit the exact artifact to the approved service; require maintainer approval.
4. Verify Authenticode, certificate chain and RFC 3161 timestamp; generate a new hash.
5. Repeat VM checks for the signed EXE and publish its exact validation record. Never silently replace a tested binary.

`scripts/Sign.ps1` also supports an existing trusted local certificate. It preserves the unsigned EXE, uses official SignTool/SHA-256/timestamp and verifies the result. Missing identity fails without output. It creates no certificates, roots, keys or security bypasses.

A trusted signature does not guarantee absence of SmartScreen, antivirus or UAC prompts. External approval/identity issuance remains required. Download pages disclose unsigned status until a valid signature exists.

Privacy: operations explicitly requested by the user may access configured Windows Update; no project telemetry. See PRIVACY.md.

Sources: https://signpath.org/terms.html , https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool , https://learn.microsoft.com/en-us/windows/security/application-security/application-control/user-account-control/how-it-works .
