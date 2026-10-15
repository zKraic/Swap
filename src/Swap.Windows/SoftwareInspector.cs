// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Win32;
using Swap.Core;
namespace Swap.Windows;

// KraicDev. Read-only facts for the software check: registry values, files and present devices. Nothing is installed or changed.
public static class SoftwareInspector
{
    public static async Task<SoftwareFacts> ReadAsync(Vendor vendor, bool pendingRestart)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var (x64, version) = VcRuntime(RegistryView.Registry64, "x64");
        var (x86, _) = VcRuntime(RegistryView.Registry32, "x86");
        bool directX = File.Exists(Path.Combine(windows, "System32", "d3dx9_43.dll")) && File.Exists(Path.Combine(windows, "SysWOW64", "d3dx9_43.dll"));
        var suite = SuiteVersion(vendor);
        return new(suite is not null, suite ?? "", x64, x86, version, directX, await HdmiAudioAsync(vendor), Programs("PhysX") is not null, HardwareScheduling(), pendingRestart);
    }

    // Visual C++ 2015–2022 redistributable, as its installer registers it.
    private static (bool Installed, string Version) VcRuntime(RegistryView view, string architecture)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\" + architecture);
            return key?.GetValue("Installed") is int installed && installed == 1 ? (true, key.GetValue("Version") as string ?? "") : (false, "");
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return (false, ""); }
    }

    private static string? SuiteVersion(Vendor vendor) => vendor switch
    {
        Vendor.Amd => Programs("AMD Software"),
        Vendor.Nvidia => Programs("NVIDIA App") ?? Programs("NVIDIA GeForce Experience"),
        Vendor.Intel => Programs("Intel® Graphics Software") ?? Programs("Intel Graphics Software") ?? Programs("Intel® Arc"),
        _ => null
    };
    // The display version of the first Programs entry whose name starts with the given text; null when there is none.
    private static string? Programs(string name)
    {
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Registry64) })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"); if (uninstall is null) continue;
                foreach (var subkey in uninstall.GetSubKeyNames())
                {
                    using var app = uninstall.OpenSubKey(subkey);
                    if (app?.GetValue("DisplayName") is string display && display.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return app.GetValue("DisplayVersion") as string ?? "";
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return null;
    }

    // Hardware-accelerated GPU scheduling: 2 on, 1 off, 0 not reported by Windows.
    private static int HardwareScheduling()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            return key?.GetValue("HwSchMode") is int mode ? mode : 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return 0; }
    }

    // The card's own HDMI/DisplayPort audio function, present and working.
    private static async Task<bool> HdmiAudioAsync(Vendor vendor)
    {
        var id = vendor switch { Vendor.Nvidia => "10DE", Vendor.Amd => "1002", Vendor.Intel => "8086", _ => "" };
        if (id.Length == 0) return false;
        try
        {
            var output = await WindowsPlatform.PowerShellAsync("@(Get-PnpDevice -PresentOnly -Class MEDIA -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'HDAUDIO\\FUNC_01&VEN_" + id + "*' -and $_.Status -eq 'OK' }).Count", 120);
            return int.TryParse(output.Trim(), out var count) && count > 0;
        }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException or IOException) { return false; }
    }
}
