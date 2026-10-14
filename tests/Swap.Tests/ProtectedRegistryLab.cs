// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Swap.Windows;

// KraicDev. Real PnP lockdown entry round trip (elevated, explicit flag only): a lab entry that only TrustedInstaller may change
// is created with the restore privilege, then saved, deleted and restored by the cleanup platform exactly as a real entry would be.
static class ProtectedRegistryLab
{
    private const string Lockdown = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\PnpLockdownFiles";
    public const string Entry = "%SystemRoot%/System32/SwapLab/swap-protected-lab.test";
    public static async Task RunAsync(Action<bool, string> assert, string folder)
    {
        void Assert(bool value, string message) => assert(value, message);
        var key = @"HKLM\" + Lockdown + @"\" + Entry;
        var platform = new WindowsCleanupPlatform();
        Directory.CreateDirectory(folder);
        var backup = Path.Combine(folder, "registry.reg");
        try
        {
            Assert(Create(), "Could not create the lab entry with the restore privilege.");
            Assert(platform.KeyExists(key) && Values() == "1|SwapLab", "Lab entry not readable: " + Values());
            var export = await platform.ExportRegistryAsync(key, backup);
            Assert(export.Success && File.Exists(backup) && File.Exists(backup + ".hive"), "Export or hive failed: " + export.Output);
            var delete = await platform.DeleteRegistryAsync(key);
            Assert(delete.Success && !platform.KeyExists(key), "Privileged delete failed: " + delete.Output);
            var restore = await platform.ImportRegistryAsync(backup);
            Assert(restore.Success && platform.KeyExists(key) && Values() == "1|SwapLab", "Restore failed: " + restore.Output + " " + Values());
            Console.WriteLine($"  protected entry: export={export.Output}; delete={delete.Output}; restore={restore.Output}");
        }
        finally { await platform.DeleteRegistryAsync(key); }
        Assert(!platform.KeyExists(key), "The lab entry was left behind.");
    }
    private static string Values()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var entry = root.OpenSubKey(Lockdown + @"\" + Entry);
            return entry is null ? "missing" : entry.GetValue("Class") + "|" + entry.GetValue("Source");
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return e.GetType().Name; }
    }

    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LookupPrivilegeValueW")] private static extern bool LookupPrivilege(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivilege state, int length, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegCreateKeyExW")]
    private static extern int RegCreateKeyEx(UIntPtr key, string subKey, int reserved, string? className, int options, int access, IntPtr security, out UIntPtr result, out int disposition);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegSetValueExW")] private static extern int RegSetValueEx(UIntPtr key, string name, int reserved, int type, byte[] data, int size);
    [DllImport("advapi32.dll")] private static extern int RegCloseKey(UIntPtr key);
    [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct TokenPrivilege { public int Count; public long Luid; public int Attributes; }
    private static bool Enable(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out var token)) return false;
        try { if (!LookupPrivilege(null, name, out var luid)) return false; var state = new TokenPrivilege { Count = 1, Luid = luid, Attributes = 2 }; return AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0; }
        finally { CloseHandle(token); }
    }
    // The parent only grants TrustedInstaller write access; a real entry has a REG_DWORD "Class" and a REG_SZ "Source".
    private static bool Create()
    {
        if (!Enable("SeRestorePrivilege") || !Enable("SeBackupPrivilege")) return false;
        if (RegCreateKeyEx(unchecked((UIntPtr)0x80000002u), Lockdown + @"\" + Entry, 0, null, 0x4, 0x0100, IntPtr.Zero, out var handle, out _) != 0) return false;
        try
        {
            var source = System.Text.Encoding.Unicode.GetBytes("SwapLab\0");
            return RegSetValueEx(handle, "Class", 0, 4, BitConverter.GetBytes(1), 4) == 0 && RegSetValueEx(handle, "Source", 0, 1, source, source.Length) == 0;
        }
        finally { RegCloseKey(handle); }
    }
}
