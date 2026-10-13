// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Microsoft.Win32;
using Swap.Core;
namespace Swap.Windows;

// KraicDev. "I'm changing my card": the intent lives in the user's profile and HKCU RunOnce opens Swap after the next sign-in.
// Elevation still goes through Windows UAC. The intent only selects what the person already chose; every action is re-planned.
public static class SwapRegistration
{
    private const string RunOnce = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string ValueName = "Swap-KraicDev-AfterSwap";
    public const string Argument = "--after-swap";
    public static string IntentPath => Path.Combine(ResumeRegistration.Folder, "swap-intent.json");

    public static SwapIntent? Load(string path)
    {
        if (!File.Exists(path)) return null;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("Invalid card-change preparation.");
        var intent = JsonSerializer.Deserialize<SwapIntent>(File.ReadAllText(path));
        return SwapDetection.Valid(intent) ? intent : throw new IOException("Invalid card-change preparation.");
    }
    public static void Save(string path, SwapIntent intent)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(folder);
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked folder is not allowed.");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(intent)); File.Move(path + ".tmp", path, true);
    }
    public static async Task ArmAsync(string executable)
    {
        var (cached, _) = await ResumeRegistration.CacheAsync(executable);
        using var key = Registry.CurrentUser.CreateSubKey(RunOnce);
        key.SetValue(ValueName, '"' + cached + "\" " + Argument, RegistryValueKind.String);
    }
    // While a driver change continues after a restart, that continuation opens Swap instead.
    public static void Disarm()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunOnce, true); key?.DeleteValue(ValueName, false);
    }
    public static bool Armed()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunOnce); return key?.GetValue(ValueName) is string;
    }
    public static void Clear(string path)
    {
        Disarm();
        if (File.Exists(path)) File.Delete(path);
    }
}
