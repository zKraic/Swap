# Privacy

KraicDev · Swap.

No accounts, ads, analytics or project telemetry. Inventory, backups, checkpoints and sessions are local. Raw inventory can contain hardware IDs, machine identifiers and paths. Review reports before public attachment; never upload Data, driver packages, credentials or recovery keys.

At your request, automatic discovery/download uses the configured Windows Update service; manufacturer links open their websites. Those services' privacy policies apply. Structured incident reports omit full event messages. Swap does not upload local reports automatically.

The website has no tracking scripts, cookies, remote fonts or analytics. GitHub hosts the site/downloads and processes requests under https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement . Windows Update: https://privacy.microsoft.com/privacystatement .

Deep cleanup backups (exported driver packages, registry exports, task definitions and quarantined vendor folders) stay on this PC in `%ProgramData%\Swap\Cleanup`, readable only by administrators. They can contain vendor settings, usernames inside paths and file names; they are never uploaded.

To remove Swap, delete its EXE and local Data or `%LOCALAPPDATA%\Swap` when no operation is pending, and `%ProgramData%\Swap\Cleanup` (as administrator) once you no longer need to undo a cleanup. A temporary RunOnce entry/cache may exist after a driver operation requiring restart: finish or cancel the session as described in RECOVERY.md first. Deleting Swap does not uninstall GPU drivers.
