# Isolated Windows validation · KraicDev

Current public evidence: [V04-RESULTS.md](validation/V04-RESULTS.md). Match the executable SHA-256 before using any test result.

The private lab uses a dedicated QEMU Windows 10 x64 guest with software emulation and no network. It does not require a host restart or driver change. Windows evaluation media, VHDs, hives, account credentials and private test outputs are not distributed. Obtain licensed/evaluation Windows from Microsoft and QEMU from its official provider.

The runner verifies guest identity and rejects the host, then runs simulated workflow tests, native read-only inventory/graphics checks, bounded RunOnce registration/removal and actual WPF rendering. Results are exported only after normal guest shutdown and compared with the current executable hash. Native RunOnce registration does not prove interactive startup after reboot.

Build the app with scripts/Build.ps1, publish the self-contained tests, prepare your own dedicated guest with scripts/New-QemuTestVm.ps1, and use Start/Update-Payload/Start-GuestTests/Read-GuestFile/Stop/Export scripts. Read script parameters and lab identity guards first; private credentials must never be committed. The Sandbox option requires available Windows features and must not restart a working remote host.

A virtual GPU and demo backend do not validate physical NVIDIA/AMD/Intel driver replacement, games, HDMI audio or hybrid/OEM laptops. Network-disabled VM tests do not validate actual Windows Update download. Diagnostics probe Direct3D availability, not performance or electrical connection.
