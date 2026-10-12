namespace Swap.Core;

public enum Vendor { Unknown, Nvidia, Amd, Intel }
public sealed record Adapter(string Name, string Id, Vendor Vendor, string Inf, string Version, int Problem, bool Signed)
{
    public string SelectionLabel => Planner.IsOem(Inf) ? Name : Name + " · " + (Vendor switch { Vendor.Nvidia => "NVIDIA", Vendor.Amd => "AMD", Vendor.Intel => "Intel", _ => "PCI" }) + " · " + (Id.Split('&').FirstOrDefault(s => s.StartsWith("DEV_", StringComparison.OrdinalIgnoreCase))?.Split('\\')[0] ?? "PCI");
}

public static class TargetChoice
{
    public static Adapter? Select(IEnumerable<Adapter> adapters, string? selectedId)
    {
        var known = adapters.Where(a => a.Vendor is Vendor.Nvidia or Vendor.Amd or Vendor.Intel).ToArray();
        var selected = known.SingleOrDefault(a => a.Id == selectedId);
        if (selected is not null) return selected;
        var unconfigured = known.Where(a => a.Problem != 0 || !a.Signed || !Planner.IsOem(a.Inf)).ToArray();
        return unconfigured.Length == 1 ? unconfigured[0] : known.Length == 1 ? known[0] : null;
    }
}
public sealed record Driver(string Inf, string Provider, string ClassGuid, string Version, bool BootCritical);
public sealed record Snapshot(string Os, string MachineId, bool Admin, bool PendingReboot, bool Remote, bool Laptop,
    long FreeBytes, IReadOnlyList<Adapter> Adapters, IReadOnlyList<Driver> Drivers, bool Complete, IReadOnlyList<string> Warnings, string BootId = "",
    IReadOnlyList<Adapter>? Phantoms = null, string CpuVendor = "", string CpuName = "")
{
    // Display adapters Windows remembers but that are no longer connected (the card that was removed).
    public IReadOnlyList<Adapter> Disconnected => Phantoms ?? [];
    public bool IntegratedCaution(Vendor vendor) => vendor != Vendor.Unknown && DeepCleanPlanner.CpuVendor(CpuVendor) == vendor && DeepCleanPlanner.LikelyIntegratedGraphics(CpuVendor, CpuName);
}
public sealed record Package(string Path, Vendor Vendor, string Version, IReadOnlyList<string> HardwareIds,
    string Sha256, bool Trusted, bool IsExe, bool IsWindowsUpdate = false);
public sealed record Plan(Snapshot Before, Vendor Source, Vendor Target, Package Package, IReadOnlyList<Driver> Remove,
    IReadOnlyList<string> Notes, string TargetId = "", bool CleanupAllAbsent = false);
public sealed record CommandResult(int Code, string Output) { public bool Success => Code is 0 or 3010; }
public sealed record Outcome(bool Verified, bool RebootRequired, string SessionPath, IReadOnlyList<string> Messages);
public static class Vendors
{
    public static Vendor FromId(string id) => id.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) ? Vendor.Nvidia :
        id.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) ? Vendor.Amd :
        id.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase) ? Vendor.Intel : Vendor.Unknown;
    // Exact provider names only; "Mystery NVIDIA Driver" or a substring is never an identity.
    private static readonly Dictionary<string, Vendor> Providers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NVIDIA"] = Vendor.Nvidia, ["NVIDIA Corporation"] = Vendor.Nvidia,
        ["Advanced Micro Devices, Inc."] = Vendor.Amd, ["Advanced Micro Devices, Inc"] = Vendor.Amd, ["Advanced Micro Devices"] = Vendor.Amd,
        ["AMD"] = Vendor.Amd, ["ATI Technologies Inc."] = Vendor.Amd, ["ATI Technologies, Inc."] = Vendor.Amd,
        ["Intel"] = Vendor.Intel, ["Intel Corporation"] = Vendor.Intel, ["Intel(R) Corporation"] = Vendor.Intel,
    };
    public static Vendor FromProvider(string provider) => Providers.TryGetValue(provider.Trim(), out var vendor) ? vendor : Vendor.Unknown;
    public static bool Matches(string hardwareId, string instanceId) =>
        instanceId.StartsWith(hardwareId + "\\", StringComparison.OrdinalIgnoreCase) ||
        instanceId.StartsWith(hardwareId + "&", StringComparison.OrdinalIgnoreCase) ||
        instanceId.Equals(hardwareId, StringComparison.OrdinalIgnoreCase);
}
