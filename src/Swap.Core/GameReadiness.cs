// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
namespace Swap.Core;

// KraicDev. What games actually load after a card change: Direct3D 12, Vulkan and OpenCL. Observations from the runtimes
// themselves, gathered in a separate process so a broken leftover layer or ICD cannot take Swap down.
public sealed record ApiDevice(string Api, string Name, int VendorId, int DeviceId, string Version);
public sealed record ApiReport(DateTimeOffset Generated, bool Completed, IReadOnlyList<ApiDevice> D3D12, string Vulkan, IReadOnlyList<ApiDevice> VulkanDevices,
    string OpenCl, IReadOnlyList<ApiDevice> OpenClPlatforms, IReadOnlyList<string> Errors);

public static class GameReadiness
{
    public static int PciVendor(Vendor vendor) => vendor switch { Vendor.Nvidia => 0x10DE, Vendor.Amd => 0x1002, Vendor.Intel => 0x8086, _ => 0 };
    public static Vendor FromPciVendor(int id) => id switch { 0x10DE => Vendor.Nvidia, 0x1002 => Vendor.Amd, 0x8086 => Vendor.Intel, _ => Vendor.Unknown };
    public static Vendor FromOpenClVendor(string vendor) => vendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? Vendor.Nvidia :
        vendor.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) || vendor.StartsWith("AMD", StringComparison.OrdinalIgnoreCase) ? Vendor.Amd :
        vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? Vendor.Intel : Vendor.Unknown;
    public static string VulkanVersion(uint value) => $"{(value >> 22) & 0x7F}.{(value >> 12) & 0x3FF}.{value & 0xFFF}";
    public static string FeatureLevel(int level) => level <= 0 ? "" : $"{level >> 12}_{(level >> 8) & 0xF}";
    private static int DeviceId(string instanceId)
    {
        var index = instanceId.IndexOf("DEV_", StringComparison.OrdinalIgnoreCase);
        return index >= 0 && instanceId.Length >= index + 8 && int.TryParse(instanceId.AsSpan(index + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out var id) ? id : -1;
    }

    // Findings for the selected GPU. Leftover runtime entries of a brand that is no longer connected are called out.
    public static IReadOnlyList<HealthFinding> Evaluate(Adapter? target, ApiReport report, IReadOnlySet<Vendor> connected)
    {
        var items = new List<HealthFinding>();
        if (!report.Completed) { items.Add(new("apiProbeFailed", string.Join("; ", report.Errors), true)); return items; }
        if (target is null) return items;
        int vendor = PciVendor(target.Vendor), device = DeviceId(target.Id);
        bool Mine(ApiDevice d) => d.VendorId == vendor && (device < 0 || d.DeviceId == device || d.DeviceId == 0);
        var d3d12 = report.D3D12.Where(Mine).ToArray();
        items.Add(d3d12.Length > 0 ? new("d3d12Ok", "FL " + d3d12[0].Version) : new("d3d12Missing", "", true));
        switch (report.Vulkan)
        {
            case "missing": items.Add(new("vulkanMissing", "", true)); break;
            case "ok":
                var vk = report.VulkanDevices.Where(Mine).ToArray();
                items.Add(vk.Length > 0 ? new("vulkanOk", vk[0].Name + " · Vulkan " + vk[0].Version) : new("vulkanNoTarget", "", true));
                break;
            default: items.Add(new("vulkanFailed", report.Vulkan, true)); break;
        }
        foreach (var stale in report.VulkanDevices.Where(d => FromPciVendor(d.VendorId) is var v && v != Vendor.Unknown && !connected.Contains(v)))
            items.Add(new("vulkanStale", stale.Name, true));
        if (report.OpenCl == "ok")
        {
            // OpenCL entries carry the number of GPU devices of the platform in DeviceId.
            var mine = report.OpenClPlatforms.FirstOrDefault(p => p.VendorId == vendor && p.DeviceId > 0);
            items.Add(mine is null ? new("openclNoTarget", "") : new("openclOk", mine.Name + " · " + mine.Version));
            foreach (var stale in report.OpenClPlatforms.Where(p => FromPciVendor(p.VendorId) is var v && v != Vendor.Unknown && !connected.Contains(v)))
                items.Add(new("openclStale", stale.Name, true));
        }
        else items.Add(new(report.OpenCl == "missing" ? "openclMissing" : "openclFailed", report.OpenCl == "missing" ? "" : report.OpenCl));
        return items;
    }
}

// KraicDev. "Prepare the swap": remembered before the old card is removed, resolved on the first start with the new card.
// Phase: prepared (waiting for the new card), installing (driver change running or continuing after a restart), done.
public sealed record SwapIntent(int Schema, string MachineId, Vendor FromVendor, string FromAdapterId, string FromName, string FromDriver,
    DateTimeOffset Created, int Starts, string BackupSession, ApiReport? Before, IReadOnlyList<string>? BeforeAdapters = null,
    string DataRoot = "", string Phase = "prepared", GraphicsCheckpoint? Graphics = null, string ToAdapterId = "", bool Simulated = false);

public static class SwapDetection
{
    public const int Schema = 1;
    public const int MaximumStarts = 10;
    public static bool Valid(SwapIntent? intent) => intent is { Schema: Schema, MachineId.Length: > 0, FromAdapterId.Length: > 0 } &&
        intent.Phase is "prepared" or "installing" or "done" && intent.Starts is >= 0 and <= 1000;
    // waiting: the old card is still connected; swapped: exactly one GPU that was not there before; ambiguous: several.
    public static (string State, Adapter? Newcomer) State(SwapIntent intent, Snapshot current)
    {
        if (intent.MachineId != current.MachineId) return ("otherMachine", null);
        var known = current.Adapters.Where(a => a.Vendor is Vendor.Nvidia or Vendor.Amd or Vendor.Intel).ToArray();
        if (known.Any(a => a.Id.Equals(intent.FromAdapterId, StringComparison.OrdinalIgnoreCase))) return ("waiting", null);
        var before = (intent.BeforeAdapters ?? [intent.FromAdapterId]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newcomers = known.Where(a => !before.Contains(a.Id)).ToArray();
        return newcomers.Length switch { 0 => ("noGpu", null), 1 => ("swapped", newcomers[0]), _ => ("ambiguous", null) };
    }
}
