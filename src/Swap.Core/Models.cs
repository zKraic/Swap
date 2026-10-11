namespace Swap.Core;

public enum Vendor { Unknown, Nvidia, Amd, Intel }
public sealed record Adapter(string Name, string Id, Vendor Vendor, string Inf, string Version, int Problem, bool Signed);
public sealed record Driver(string Inf, string Provider, string ClassGuid, string Version, bool BootCritical);
public sealed record Snapshot(string Os, string MachineId, bool Admin, bool PendingReboot, bool Remote, bool Laptop,
    long FreeBytes, IReadOnlyList<Adapter> Adapters, IReadOnlyList<Driver> Drivers, bool Complete, IReadOnlyList<string> Warnings, string BootId = "");
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
    public static Vendor FromProvider(string provider) => provider.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("NVIDIA Corporation", StringComparison.OrdinalIgnoreCase) ? Vendor.Nvidia :
        provider.Equals("Advanced Micro Devices, Inc.", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("Advanced Micro Devices, Inc", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("ATI Technologies Inc.", StringComparison.OrdinalIgnoreCase) ? Vendor.Amd :
        provider.Equals("Intel", StringComparison.OrdinalIgnoreCase) || provider.Equals("Intel Corporation", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("Intel(R) Corporation", StringComparison.OrdinalIgnoreCase) ? Vendor.Intel : Vendor.Unknown;
    public static bool Matches(string hardwareId, string instanceId) =>
        instanceId.StartsWith(hardwareId + "\\", StringComparison.OrdinalIgnoreCase) ||
        instanceId.StartsWith(hardwareId + "&", StringComparison.OrdinalIgnoreCase) ||
        instanceId.Equals(hardwareId, StringComparison.OrdinalIgnoreCase);
}
