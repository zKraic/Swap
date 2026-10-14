# Swap community — KraicDev

Swap is free and developed in public. Useful contributions include reproducible compatibility reports, translations, code review and measurements from hardware that was actually tested.

- [Feedback and support](https://github.com/zKraic/Swap/discussions/1)
- [Report an observed hardware result](https://github.com/zKraic/Swap/issues/new?template=hardware_validation.yml)
- [Source and contributions](../CONTRIBUTING.md)

For a GPU transition report, distinguish the old card from the new card already connected. Record the Windows build, Swap version, active driver version, installation/cleanup result, packages retained and any restart. A demo, build or VM result must be described as such. Include game results only if you actually ran those games on that hardware.

From 0.6.0, the easiest report is the **validation report**: after a guided card change press **Copy for GitHub**, or use **Diagnostics → Validation report** at any time. It is English Markdown with the before/after GPU and driver, cleanup counts, leftovers found afterwards, Direct3D 12/Vulkan/OpenCL and displays, without machine identifiers or account names. Add the games you actually played. One report from a real swap is worth more than any lab run; 1.0.0 was validated this way on a GTX 1060 to RX 7600 change, and each new combination (AMD to NVIDIA, Intel Arc, laptops) needs its own report.

From 0.5.0, each deep cleanup is a `*-CLEAN-*` session with `cleanup-results.json`: every item with its status (removed, to review, skipped) and reason. For a report, say which brand was disconnected, what was removed, what was kept and whether you used Undo. Backups in `%ProgramData%\Swap\Cleanup` contain vendor settings and paths; never attach them.

From 0.4.2, successful driver preparation stores `preparation.json` in the session folder opened from Recovery. It records final driver/Direct3D observations and backup integrity, explains retained packages and marks demo results as simulated. The diagnostic export contains a fresh readiness assessment; it does not imply that an installation occurred.

Share the official project link if you find it useful. Disclose your relationship to the project when posting, read each community's rules and respect moderation decisions. No duplicated announcements, manufactured engagement, unverifiable endorsements or promises of universal compatibility are part of the launch.
