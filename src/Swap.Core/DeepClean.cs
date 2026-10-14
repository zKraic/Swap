// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
namespace Swap.Core;

// KraicDev. Deep cleanup of a graphics brand that is no longer connected. Ownership comes from hardware evidence
// (device trees and INF hardware IDs), never from file or product names alone. Windows observations are data.
// New kinds are appended: undo manifests of earlier versions store the numeric value.
public enum RemnantKind { Process, Uninstaller, Device, Package, Service, Task, Startup, Appx, Folder, Cache, Registry, App, File, Registration }
public enum FolderRole { Folder, Cache }

public sealed record DeviceNode(string Id, string Class, string Inf, string Service, bool Present, IReadOnlyList<string> HardwareIds, string Name = "");
// SystemFiles: what the INF copies into System32/SysWOW64, relative to the Windows folder. Removing the package leaves those copies behind.
// ComponentIds: "SWC\<id>" software components the package creates (for example AMD OpenCL and Windows support components).
public sealed record StorePackage(string Inf, string OriginalName, string Provider, string ClassName, string Version, bool BootCritical,
    string Folder, long Bytes, IReadOnlyList<string> HardwareIds, IReadOnlyList<string>? SystemFiles = null, IReadOnlyList<string>? ComponentIds = null);
// An existing file in System32/SysWOW64 that a driver package deployed. Source: the Driver Store folder named by the Windows setup log.
public sealed record SystemFileEntry(string Path, long Bytes, string Company, string Source = "");
// A Khronos loader registration (Vulkan driver or layer, OpenCL vendor). Library: the DLL it makes games load.
public sealed record RegistrationEntry(string Key, string Name, string Library, bool Exists, string Company);
// A COM class whose server lives in the Driver Store or System32 (for example a vendor video encoder), and the Media
// Foundation transform registrations that point to it.
public sealed record ComServer(string Key, string Clsid, string Server, bool Exists, IReadOnlyList<string> Transforms);
// A Plug and Play file-protection entry (PnpLockdownFiles) of a file deployed by a driver package.
public sealed record LockdownEntry(string Key, string Path, bool Exists);
public sealed record ServiceEntry(string Name, string DisplayName, string Binary, bool BinaryExists);
public sealed record CommandEntry(string Key, string Name, IReadOnlyList<string> Files, bool Exists);
public sealed record AppEntry(string Key, string Name, string Publisher, string Version, IReadOnlyList<string> Files, bool Exists,
    bool Machine, bool Msi, string Uninstall);
public sealed record FolderSpec(Vendor Vendor, string Location, FolderRole Role, bool PerUser = false, string[]? Include = null, string[]? Exclude = null, string[]? Protect = null);
public sealed record FolderChild(string Name, string Path, long Bytes);
public sealed record FolderEntry(string Location, string Path, long Bytes, IReadOnlyList<FolderChild> Children);
public sealed record ProcessEntry(int Id, string Name, string Path);
public sealed record RemnantInventory(string MachineId, string CpuVendor, string CpuName, bool Admin, bool Laptop,
    IReadOnlyList<DeviceNode> Devices, IReadOnlyList<StorePackage> Packages, IReadOnlyList<ServiceEntry> Services, IReadOnlyList<string> ActiveServices,
    IReadOnlyList<CommandEntry> Tasks, IReadOnlyList<CommandEntry> Startup, IReadOnlyList<AppEntry> Apps, IReadOnlyList<string> Appx,
    IReadOnlyList<FolderEntry> Folders, IReadOnlyList<string> RegistryKeys, IReadOnlyList<ProcessEntry> Processes, bool Complete, IReadOnlyList<string> Warnings,
    IReadOnlyList<SystemFileEntry>? SystemFiles = null, IReadOnlyList<RegistrationEntry>? Registrations = null, string WindowsRoot = "",
    IReadOnlyList<ComServer>? ComServers = null, IReadOnlyList<LockdownEntry>? Lockdown = null);

public sealed record RemnantItem(string Key, RemnantKind Kind, Vendor Vendor, string Target, string Label, string Reason, bool Selected, long Bytes = 0, string Detail = "");
public sealed record DeepCleanPlan(string MachineId, IReadOnlyList<Vendor> Vendors, IReadOnlyList<RemnantItem> Items, IReadOnlyList<RemnantItem> Kept, IReadOnlyList<string> Notes, bool Versions = false)
{
    public long Bytes => Items.Where(i => i.Selected).Sum(i => i.Bytes);
}

// RuntimeMarkers: file names of the brand's Vulkan drivers and OpenCL vendors, used only for registrations whose file is already gone.
public sealed record VendorProfile(Vendor Vendor, string[] IdPrefixes, string[] Services, FolderSpec[] Folders, string[] RegistryKeys,
    string[] Appx, string[] AppNames, string[] AppExclusions, string[] PathMarkers, string[] Publishers, string[]? RuntimeMarkers = null);

public static class VendorProfiles
{
    // Strict GPU domains. AMD platform/chipset hardware uses VEN_1022 and is never matched; Intel has no strict domain.
    public static readonly VendorProfile Nvidia = new(Vendor.Nvidia,
        [@"PCI\VEN_10DE&", @"HDAUDIO\FUNC_01&VEN_10DE&", @"SWC\VEN_10DE&", @"USB\VID_0955&", @"ACPI\NVDA", @"ACPI\VEN_NVDA&"],
        ["nvlddmkm", "NVDisplay.ContainerLocalSystem", "NvContainerLocalSystem", "NvContainerNetworkService", "NvTelemetryContainer", "nvsvc",
         "nvvad_WaveExtensible", "nvvhci", "NVHDA", "NvStreamKms", "nvpcf", "NVWMI", "FvSvc", "NvBroadcast.ContainerLocalSystem"],
        [new(Vendor.Nvidia, @"%ProgramFiles%\NVIDIA Corporation", FolderRole.Folder, Protect: ["PhysX"]),
         new(Vendor.Nvidia, @"%ProgramFiles(x86)%\NVIDIA Corporation", FolderRole.Folder, Protect: ["PhysX"]),
         new(Vendor.Nvidia, @"%ProgramData%\NVIDIA Corporation", FolderRole.Folder),
         new(Vendor.Nvidia, @"%ProgramData%\NVIDIA", FolderRole.Folder),
         new(Vendor.Nvidia, @"%LocalAppData%\NVIDIA Corporation", FolderRole.Folder, true),
         new(Vendor.Nvidia, @"%LocalAppData%\NVIDIA", FolderRole.Cache, true),
         new(Vendor.Nvidia, @"%AppData%\NVIDIA", FolderRole.Cache, true),
         new(Vendor.Nvidia, @"%UserProfile%\AppData\LocalLow\NVIDIA", FolderRole.Cache, true)],
        [@"HKLM\SOFTWARE\NVIDIA Corporation", @"HKLM\SOFTWARE\WOW6432Node\NVIDIA Corporation", @"HKCU\SOFTWARE\NVIDIA Corporation"],
        ["NVIDIACorp.NVIDIAControlPanel"],
        ["NVIDIA Graphics Driver", "NVIDIA HD Audio", "NVIDIA GeForce Experience", "NVIDIA App", "NVIDIA FrameView", "NVIDIA USBC", "NVIDIA Control Panel",
         "NVIDIA 3D Vision", "NVIDIA Install Application", "NVIDIA Update", "NVIDIA Virtual Audio", "NVIDIA Telemetry", "NVIDIA Container", "NVIDIA ShadowPlay",
         "NVIDIA Ansel", "NVIDIA Backend", "NVIDIA Node.js", "NVIDIA Broadcast", "NVIDIA Display Container", "NVIDIA Platform Controllers"],
        ["PhysX", "GeForce NOW", "CUDA", "Nsight", "Omniverse"],
        [@"\NVIDIA Corporation\", @"\NVIDIA\"], ["NVIDIA Corporation", "NVIDIA"], ["nv-vk", "nvoglv", "nvopencl", "nvcuda"]);

    public static readonly VendorProfile Amd = new(Vendor.Amd,
        [@"PCI\VEN_1002&", @"HDAUDIO\FUNC_01&VEN_1002&", @"SWC\VEN_1002&"],
        ["amdkmdag", "amdkmdap", "amdwddmg", "AMD External Events Utility", "AMD Crash Defender Service", "amdfendr", "amdfendrmgr", "AtiHDAudioService",
         "amdlog", "AMD Log Utility", "AUEPLauncher", "AMDRadeonsettings"],
        [new(Vendor.Amd, @"%ProgramFiles%\AMD\CNext", FolderRole.Folder),
         new(Vendor.Amd, @"%ProgramFiles%\ATI Technologies", FolderRole.Folder),
         new(Vendor.Amd, @"%ProgramFiles(x86)%\ATI Technologies", FolderRole.Folder),
         new(Vendor.Amd, @"%SystemDrive%\AMD", FolderRole.Folder, Include: ["Radeon", "Adrenalin", "AMD-Software", "AMD Software", "WHQL-"], Exclude: ["Chipset", "Ryzen"]),
         new(Vendor.Amd, @"%LocalAppData%\AMD\DxCache", FolderRole.Cache, true),
         new(Vendor.Amd, @"%LocalAppData%\AMD\DxcCache", FolderRole.Cache, true),
         new(Vendor.Amd, @"%LocalAppData%\AMD\GLCache", FolderRole.Cache, true),
         new(Vendor.Amd, @"%LocalAppData%\AMD\VkCache", FolderRole.Cache, true),
         new(Vendor.Amd, @"%LocalAppData%\AMD\CN", FolderRole.Folder, true),
         new(Vendor.Amd, @"%LocalAppData%\RadeonInstaller", FolderRole.Cache, true)],
        [@"HKLM\SOFTWARE\AMD\CN", @"HKCU\SOFTWARE\AMD\CN", @"HKCU\SOFTWARE\AMD\DVR", @"HKLM\SOFTWARE\AMD\EEU", @"HKLM\SOFTWARE\ATI Technologies", @"HKLM\SOFTWARE\WOW6432Node\ATI Technologies", @"HKCU\SOFTWARE\ATI"],
        ["AdvancedMicroDevicesInc-2.AMDRadeonSoftware"],
        ["AMD Software", "AMD Radeon", "Radeon Software", "AMD Settings", "AMD Catalyst", "AMD Display", "AMD Link", "AMD Problem Report Wizard", "AMD Install Manager"],
        ["Chipset", "Ryzen", "uProf", "StoreMI", "RAID", "SATA", "PSP", "SMBus", "GPIO", "Master"],
        [@"\AMD\CNext\", @"\ATI Technologies\", @"\RadeonInstaller\", @"\AMD\CN\"], ["Advanced Micro Devices, Inc.", "Advanced Micro Devices, Inc", "Advanced Micro Devices", "AMD"],
        ["amd-vulkan", "amdvlk", "amdocl", "atiocl"]);

    public static readonly VendorProfile Intel = new(Vendor.Intel, [],
        ["igfx", "igfxn", "igfxCUIService2.0.0.0", "cplspcon", "cphs", "IGCCService", "IntelArcControlService", "IntelGraphicsSoftwareService"],
        [new(Vendor.Intel, @"%LocalAppData%\Intel\ShaderCache", FolderRole.Cache, true),
         new(Vendor.Intel, @"%UserProfile%\AppData\LocalLow\Intel\ShaderCache", FolderRole.Cache, true)],
        [@"HKCU\SOFTWARE\Intel\Display"],
        ["AppUp.IntelGraphicsExperience", "AppUp.IntelGraphicsControlPanel", "AppUp.IntelArcSoftware"],
        ["Intel(R) Graphics", "Intel® Graphics", "Intel Graphics", "Intel(R) Arc", "Intel® Arc", "Intel Arc"],
        ["Driver & Support Assistant", "Wireless", "Bluetooth", "Chipset", "Management Engine", "Rapid Storage", "Serial IO"],
        [@"\Intel\Intel(R) Arc", @"\Intel\Intel Graphics", @"\Intel\Intel(R) Graphics"], ["Intel Corporation", "Intel(R) Corporation", "Intel"],
        ["igvk", "igdrcl", "intelocl"]);

    // Shared vendor roots (AMD chipset software also writes under SOFTWARE\AMD): removed only when nothing else is left in them.
    public static readonly string[] EmptyParents = [@"HKLM\SOFTWARE\AMD", @"HKCU\SOFTWARE\AMD", @"HKLM\SOFTWARE\WOW6432Node\AMD"];
    // DirectX keeps compiled shaders for the previous adapter; Windows rebuilds the cache on demand.
    public static readonly FolderSpec DirectXCache = new(Vendor.Unknown, @"%LocalAppData%\D3DSCache", FolderRole.Cache, true);
    public static IReadOnlyList<VendorProfile> All { get; } = [Nvidia, Amd, Intel];
    public static VendorProfile For(Vendor vendor) => All.Single(p => p.Vendor == vendor);
    public static IEnumerable<FolderSpec> FolderSpecs => All.SelectMany(p => p.Folders).Append(DirectXCache);
}

public static partial class DeepCleanPlanner
{
    public const int MaximumItems = 5000;
    private static readonly string[] Classes = ["Display", "MEDIA", "SoftwareComponent", "SoftwareDevice", "Extension", "USB", "System", "AudioProcessingObject"];

    public static Vendor CpuVendor(string identifier) => identifier.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? Vendor.Intel :
        identifier.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? Vendor.Amd : Vendor.Unknown;

    [GeneratedRegex(@"^(?<number>\d{3,5})(?<suffix>[A-Z0-9]{0,4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModelPattern();
    // Conservative heuristic: when the model is unknown, assume the processor may contain integrated graphics.
    public static bool LikelyIntegratedGraphics(string cpuVendor, string cpuName)
    {
        var vendor = CpuVendor(cpuVendor);
        if (vendor == Vendor.Unknown) return false;
        var model = cpuName.Split([' ', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries).Select(t => ModelPattern().Match(t)).FirstOrDefault(m => m.Success);
        if (vendor == Vendor.Intel)
        {
            if (cpuName.Contains("Xeon", StringComparison.OrdinalIgnoreCase)) return model?.Groups["suffix"].Value.Contains('G', StringComparison.OrdinalIgnoreCase) == true;
            return model is null || !model.Groups["suffix"].Value.Contains('F', StringComparison.OrdinalIgnoreCase);
        }
        if (cpuName.Contains("Threadripper", StringComparison.OrdinalIgnoreCase) || cpuName.Contains("EPYC", StringComparison.OrdinalIgnoreCase)) return false;
        if (model is null) return true;
        var suffix = model.Groups["suffix"].Value; var number = model.Groups["number"].Value;
        if (suffix.Contains('G', StringComparison.OrdinalIgnoreCase) || suffix.Contains('U', StringComparison.OrdinalIgnoreCase) || suffix.Contains('H', StringComparison.OrdinalIgnoreCase)) return true;
        if (suffix.Contains('F', StringComparison.OrdinalIgnoreCase)) return false;
        // AM5 desktop processors (7000 and later) include a small Radeon GPU; earlier non-G desktop models do not.
        return number.Length == 4 && number[0] is '7' or '8' or '9';
    }

    public static bool InDomain(Vendor vendor, string id) => vendor != Vendor.Intel && VendorProfiles.For(vendor).IdPrefixes.Any(p => id.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    private static bool IntelDisplayAudio(string id) => id.StartsWith(@"HDAUDIO\FUNC_01&VEN_8086&DEV_28", StringComparison.OrdinalIgnoreCase);
    private static string PciModel(string id) => id.StartsWith(@"PCI\VEN_", StringComparison.OrdinalIgnoreCase) && id.Length >= 21 ? id[..21].ToUpperInvariant() : id.ToUpperInvariant();

    public static IReadOnlySet<Vendor> PresentGpuVendors(RemnantInventory inventory)
    {
        var present = new HashSet<Vendor>();
        var display = inventory.Packages.Where(p => p.ClassName.Equals("Display", StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.Inf, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => Vendors.FromProvider(g.First().Provider), StringComparer.OrdinalIgnoreCase);
        foreach (var device in inventory.Devices.Where(d => d.Present))
        {
            var vendor = Vendors.FromId(device.Id);
            if (device.Class.Equals("Display", StringComparison.OrdinalIgnoreCase) && vendor != Vendor.Unknown) present.Add(vendor);
            // A disabled or driverless GPU is still connected hardware.
            if (vendor is Vendor.Nvidia or Vendor.Amd && device.Id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)) present.Add(vendor);
            // Defense in depth when Windows omits the class: a connected device using a brand's Display package.
            if (display.TryGetValue(device.Inf, out var owner) && owner != Vendor.Unknown) present.Add(owner);
        }
        return present;
    }

    // Absent brands that still leave something behind. Integrated graphics of the processor brand stay opt-in.
    public static IReadOnlyList<Vendor> Candidates(RemnantInventory inventory)
    {
        if (!inventory.Admin || !inventory.Complete) return [];
        var present = PresentGpuVendors(inventory);
        return VendorProfiles.All.Select(p => p.Vendor).Where(v => !present.Contains(v) &&
            Create(inventory, [v]).Items.Any(i => i.Vendor == v && i.Kind is not (RemnantKind.Cache or RemnantKind.Process or RemnantKind.Uninstaller))).ToArray();
    }

    public static IReadOnlySet<string> IntelGraphicsIds(RemnantInventory inventory) =>
        inventory.Packages.Where(p => Vendors.FromProvider(p.Provider) == Vendor.Intel && p.ClassName.Equals("Display", StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.HardwareIds).Concat(inventory.Devices.Where(d => d.Class.Equals("Display", StringComparison.OrdinalIgnoreCase) && Vendors.FromId(d.Id) == Vendor.Intel).SelectMany(d => d.HardwareIds))
            .Where(id => id.StartsWith(@"PCI\VEN_8086&DEV_", StringComparison.OrdinalIgnoreCase)).Select(PciModel).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool OwnedDevice(Vendor vendor, DeviceNode device, IReadOnlySet<string> intelIds)
    {
        if (vendor == Vendor.Intel)
            return Vendors.FromId(device.Id) == Vendor.Intel && device.Class.Equals("Display", StringComparison.OrdinalIgnoreCase) ||
                device.HardwareIds.Any(IntelDisplayAudio) || device.Id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) && intelIds.Contains(PciModel(device.Id)) && device.Class.Equals("Display", StringComparison.OrdinalIgnoreCase);
        return InDomain(vendor, device.Id) || device.HardwareIds.Count > 0 && device.HardwareIds.Any(id => InDomain(vendor, id));
    }
    // A vendor's root-enumerated virtual device (for example NVIDIA Virtual Audio) remains "present" after the GPU is gone.
    private static bool VirtualDevice(Vendor vendor, DeviceNode device) => vendor != Vendor.Intel && device.Present &&
        device.Id.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase) && device.HardwareIds.Count > 0 && device.HardwareIds.All(id => InDomain(vendor, id));

    public static bool Within(string path, string root)
    {
        if (path.Length == 0 || root.Length == 0) return false;
        try
        {
            var full = Path.GetFullPath(path).TrimEnd('\\', '/') + "\\"; var parent = Path.GetFullPath(root).TrimEnd('\\', '/') + "\\";
            return full.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public static DeepCleanPlan Create(RemnantInventory inventory, IEnumerable<Vendor> requested)
    {
        if (!inventory.Admin || !inventory.Complete) throw new InvalidOperationException("Se necesita un inventario completo con permisos de administrador.");
        var present = PresentGpuVendors(inventory);
        var vendors = requested.Where(v => v is Vendor.Nvidia or Vendor.Amd or Vendor.Intel).Distinct().ToArray();
        var notes = new List<string>(); var items = new List<RemnantItem>(); var kept = new List<RemnantItem>();
        foreach (var vendor in vendors.Where(present.Contains)) notes.Add("vendorPresent:" + vendor);
        var targets = vendors.Where(v => !present.Contains(v)).ToArray();
        var intelIds = IntelGraphicsIds(inventory);
        var cpu = CpuVendor(inventory.CpuVendor);
        var physx = inventory.Apps.Any(a => a.Name.Contains("PhysX", StringComparison.OrdinalIgnoreCase));
        var contexts = new List<(Vendor Vendor, bool Select, List<StorePackage> Packages, string[] Roots)>();
        foreach (var vendor in targets)
        {
            var profile = VendorProfiles.For(vendor);
            // The brand's integrated GPU may only be disabled in firmware; keep its leftovers unless the person selects them.
            bool caution = vendor == cpu && LikelyIntegratedGraphics(inventory.CpuVendor, inventory.CpuName) || inventory.Laptop;
            if (caution) notes.Add((inventory.Laptop ? "laptopCaution:" : "integratedCaution:") + vendor);
            bool select = !caution;
            // Software components the brand's display packages create (AddComponent) carry IDs of their own, outside the PCI domain.
            var components = inventory.Packages.Where(p => Vendors.FromProvider(p.Provider) == vendor && p.ClassName.Equals("Display", StringComparison.OrdinalIgnoreCase))
                .SelectMany(p => p.ComponentIds ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool Component(string id) => components.Contains(id) || components.Any(c => id.StartsWith(c + "&", StringComparison.OrdinalIgnoreCase));
            var devices = inventory.Devices.Where(d => (!d.Present && (OwnedDevice(vendor, d, intelIds) || d.HardwareIds.Count > 0 && d.HardwareIds.All(Component))) || VirtualDevice(vendor, d)).ToArray();
            var deviceIds = devices.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            items.AddRange(devices.Select(d => new RemnantItem("device:" + d.Id, RemnantKind.Device, vendor, d.Id, string.IsNullOrWhiteSpace(d.Name) ? d.Id : d.Name,
                d.Present ? "virtualDevice" : "phantomDevice", select, 0, d.Id)));
            var packages = new List<StorePackage>();
            foreach (var package in inventory.Packages.Where(p => Vendors.FromProvider(p.Provider) == vendor && Planner.IsOem(p.Inf)))
            {
                if (!Classes.Contains(package.ClassName, StringComparer.OrdinalIgnoreCase)) continue;
                var users = inventory.Devices.Where(d => d.Inf.Equals(package.Inf, StringComparison.OrdinalIgnoreCase)).ToArray();
                string? reason = null; bool component = false;
                if (users.Any(d => !deviceIds.Contains(d.Id))) reason = users.Any(d => d.Present && !deviceIds.Contains(d.Id)) ? "packageActive" : "packageInUse";
                else if (package.HardwareIds.Count > 0 && package.HardwareIds.All(Component)) component = true;
                else
                {
                    var ids = package.HardwareIds;
                    bool evidence = vendor == Vendor.Intel
                        ? package.ClassName.Equals("Display", StringComparison.OrdinalIgnoreCase) && ids.Count > 0 && ids.All(id => id.StartsWith(@"PCI\VEN_8086&", StringComparison.OrdinalIgnoreCase)) ||
                          !package.ClassName.Equals("System", StringComparison.OrdinalIgnoreCase) && !package.ClassName.Equals("USB", StringComparison.OrdinalIgnoreCase) &&
                          ids.Count > 0 && ids.All(id => IntelDisplayAudio(id) || intelIds.Contains(PciModel(id)))
                        : ids.Count > 0 && ids.All(id => InDomain(vendor, id));
                    if (!evidence && ids.Count == 0 && users.Length > 0) evidence = true;
                    if (!evidence)
                    {
                        // Mixed or non-graphics hardware: a shared or platform package that must stay.
                        bool related = vendor == Vendor.Intel ? package.ClassName.Equals("Display", StringComparison.OrdinalIgnoreCase) || ids.Any(id => IntelDisplayAudio(id) || intelIds.Contains(PciModel(id))) : ids.Any(id => InDomain(vendor, id));
                        if (related) reason = "packageMixed"; else continue;
                    }
                }
                if (reason is not null) { kept.Add(new("package:" + package.Inf, RemnantKind.Package, vendor, package.Inf, Describe(package), reason, false, package.Bytes)); continue; }
                packages.Add(package);
                // Windows flags whole classes (for example USB) as boot-critical; an unused one is offered but left unselected.
                items.Add(new("package:" + package.Inf, RemnantKind.Package, vendor, package.Inf, Describe(package), package.BootCritical ? "bootCriticalUnused" : component ? "packageComponent" : "packageUnused",
                    select && !package.BootCritical, package.Bytes, package.Folder));
            }
            var folders = new List<(string Path, bool Selected)>();
            foreach (var entry in inventory.Folders)
            {
                var spec = profile.Folders.FirstOrDefault(s => s.Location.Equals(entry.Location, StringComparison.OrdinalIgnoreCase));
                if (spec is null) continue;
                var kind = spec.Role == FolderRole.Cache ? RemnantKind.Cache : RemnantKind.Folder;
                bool cacheSelect = select || spec.Role == FolderRole.Cache;
                var protectedChildren = entry.Children.Where(c => spec.Protect?.Any(p => c.Name.Equals(p, StringComparison.OrdinalIgnoreCase)) == true).ToArray();
                if (spec.Include is not null || protectedChildren.Length > 0)
                {
                    foreach (var child in entry.Children)
                    {
                        if (protectedChildren.Contains(child)) { kept.Add(new(kind + ":" + child.Path, kind, vendor, child.Path, child.Path, child.Name.Equals("PhysX", StringComparison.OrdinalIgnoreCase) ? "physxKept" : "folderProtected", false, child.Bytes)); continue; }
                        if (spec.Include is not null && !spec.Include.Any(i => child.Name.Contains(i, StringComparison.OrdinalIgnoreCase))) continue;
                        if (spec.Exclude?.Any(x => child.Name.Contains(x, StringComparison.OrdinalIgnoreCase)) == true) continue;
                        items.Add(new(kind + ":" + child.Path, kind, vendor, child.Path, child.Path, spec.Role == FolderRole.Cache ? "cacheRebuilt" : "folderVendor", cacheSelect, child.Bytes));
                        folders.Add((child.Path, cacheSelect));
                    }
                    continue;
                }
                items.Add(new(kind + ":" + entry.Path, kind, vendor, entry.Path, entry.Path, spec.Role == FolderRole.Cache ? "cacheRebuilt" : "folderVendor", cacheSelect, entry.Bytes));
                folders.Add((entry.Path, cacheSelect));
            }
            var removedRoots = folders.Select(f => f.Path).Concat(packages.Select(p => p.Folder)).Where(p => p.Length > 0).ToArray();
            bool Removed(string path) => removedRoots.Any(root => Within(path, root));
            bool Marked(string path) => profile.PathMarkers.Any(m => path.Contains(m, StringComparison.OrdinalIgnoreCase));
            foreach (var service in inventory.Services)
            {
                bool named = profile.Services.Contains(service.Name, StringComparer.OrdinalIgnoreCase);
                bool inside = Removed(service.Binary);
                if (!named && !inside && !(Marked(service.Binary) && !service.BinaryExists)) continue;
                if (inventory.ActiveServices.Contains(service.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (named) kept.Add(new("service:" + service.Name, RemnantKind.Service, vendor, service.Name, Label(service), "serviceActive", false));
                    continue;
                }
                items.Add(new("service:" + service.Name, RemnantKind.Service, vendor, service.Name, Label(service), inside || !service.BinaryExists ? "serviceOrphan" : "serviceUnused", select, 0, service.Binary));
            }
            foreach (var task in inventory.Tasks.Where(t => t.Files.Any(Removed) || !t.Exists && t.Files.Count > 0 && t.Files.All(Marked)))
                items.Add(new("task:" + task.Key, RemnantKind.Task, vendor, task.Key, task.Name, "commandOrphan", select, 0, string.Join(" · ", task.Files)));
            foreach (var entry in inventory.Startup.Where(t => t.Files.Any(Removed) || !t.Exists && t.Files.Count > 0 && t.Files.All(Marked)))
                items.Add(new("startup:" + entry.Key, RemnantKind.Startup, vendor, entry.Key, entry.Name, "commandOrphan", select, 0, string.Join(" · ", entry.Files)));
            foreach (var app in inventory.Apps.Where(a => profile.Publishers.Any(p => a.Publisher.Equals(p, StringComparison.OrdinalIgnoreCase))))
            {
                if (profile.AppExclusions.Any(x => app.Name.Contains(x, StringComparison.OrdinalIgnoreCase)))
                {
                    if (app.Name.Contains("PhysX", StringComparison.OrdinalIgnoreCase)) kept.Add(new("app:" + app.Key, RemnantKind.App, vendor, app.Key, app.Name, "physxKept", false));
                    continue;
                }
                // Names are translated by Windows' language ("NVIDIA Controlador de gráficos"); an entry whose uninstaller
                // lives only in folders this cleanup removes is dead afterwards whatever its name.
                if (!profile.AppNames.Any(n => app.Name.Contains(n, StringComparison.OrdinalIgnoreCase)) && !(app.Files.Count > 0 && app.Files.All(Removed))) continue;
                // Windows Installer products are removed through msiexec, never by deleting their registration.
                if (!app.Msi && app.Files.Count > 0 && (!app.Exists || app.Files.All(Removed)))
                    items.Add(new("app:" + app.Key, RemnantKind.App, vendor, app.Key, app.Name, "appOrphan", select, 0, app.Version));
                else if (app.Machine && (app.Msi || app.Exists))
                    items.Add(new("uninstall:" + app.Key, RemnantKind.Uninstaller, vendor, app.Key, app.Name, "appUninstaller", false, 0, app.Version));
                else kept.Add(new("app:" + app.Key, RemnantKind.App, vendor, app.Key, app.Name, "appManual", false));
            }
            foreach (var name in inventory.Appx.Where(a => profile.Appx.Contains(a, StringComparer.OrdinalIgnoreCase)))
                items.Add(new("appx:" + name, RemnantKind.Appx, vendor, name, name, "appxUnused", select));
            foreach (var key in inventory.RegistryKeys.Where(k => profile.RegistryKeys.Contains(k, StringComparer.OrdinalIgnoreCase)))
            {
                if (vendor == Vendor.Nvidia && physx) { kept.Add(new("registry:" + key, RemnantKind.Registry, vendor, key, key, "physxKept", false)); continue; }
                items.Add(new("registry:" + key, RemnantKind.Registry, vendor, key, key, "settingsKey", select));
            }
            foreach (var process in inventory.Processes.Where(p => Removed(p.Path)))
                items.Add(new("process:" + process.Id, RemnantKind.Process, vendor, process.Path, process.Name, "processStop", true, 0, process.Path));
            contexts.Add((vendor, select, packages, removedRoots));
        }
        RuntimeLeftovers(inventory, contexts, items, kept);
        if (targets.Length > 0)
            foreach (var entry in inventory.Folders.Where(f => f.Location.Equals(VendorProfiles.DirectXCache.Location, StringComparison.OrdinalIgnoreCase)))
                items.Add(new("Cache:" + entry.Path, RemnantKind.Cache, Vendor.Unknown, entry.Path, entry.Path, "cacheRebuilt", true, entry.Bytes));
        var unique = items.GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(i => i.Kind).ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase).ToArray();
        if (unique.Length > MaximumItems) throw new InvalidOperationException("Demasiados elementos para una limpieza segura; revisa el inventario.");
        return new(inventory.MachineId, targets, unique, kept, notes);
    }

    // Re-plan from a fresh inventory with the same intent; only items still eligible now may run.
    public static DeepCleanPlan Refresh(RemnantInventory inventory, DeepCleanPlan plan) => plan.Versions
        ? new(inventory.MachineId, PresentGpuVendors(inventory).ToArray(), OldVersions(inventory), [], [], true) : Create(inventory, plan.Vendors);

    // Older unused versions of a connected brand's graphics driver. The newest unused version stays for recovery.
    public static IReadOnlyList<RemnantItem> OldVersions(RemnantInventory inventory)
    {
        var present = PresentGpuVendors(inventory); var result = new List<RemnantItem>();
        var bound = inventory.Devices.Select(d => d.Inf).Where(i => i.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var vendor in present.Where(v => v is Vendor.Nvidia or Vendor.Amd or Vendor.Intel))
        {
            var candidates = inventory.Packages.Where(p => Vendors.FromProvider(p.Provider) == vendor && Planner.IsOem(p.Inf) && !p.BootCritical &&
                (p.ClassName.Equals("Display", StringComparison.OrdinalIgnoreCase) || vendor != Vendor.Intel && p.ClassName.Equals("MEDIA", StringComparison.OrdinalIgnoreCase)) &&
                p.HardwareIds.Count > 0 && p.HardwareIds.All(id => vendor == Vendor.Intel ? id.StartsWith(@"PCI\VEN_8086&", StringComparison.OrdinalIgnoreCase) : InDomain(vendor, id)));
            foreach (var group in candidates.GroupBy(p => p.ClassName.ToUpperInvariant() + "|" + p.OriginalName.ToUpperInvariant()))
            {
                if (!group.Any(p => bound.Contains(p.Inf))) continue;
                var unused = group.Where(p => !bound.Contains(p.Inf)).OrderByDescending(p => Version.TryParse(p.Version, out var v) ? v : new Version(0, 0)).ToArray();
                result.AddRange(unused.Skip(1).Select(p => new RemnantItem("package:" + p.Inf, RemnantKind.Package, vendor, p.Inf, Describe(p), "olderVersion", true, p.Bytes, p.Folder)));
            }
        }
        return result;
    }

    // Files a driver INF copied into System32/SysWOW64 and Khronos registrations stay behind when its package is deleted.
    // A file needs package evidence (the INF of a package being removed, or the setup log naming a Driver Store folder that no longer exists),
    // the brand as author when it has version information, and no remaining package that deploys the same file.
    private static void RuntimeLeftovers(RemnantInventory inventory, List<(Vendor Vendor, bool Select, List<StorePackage> Packages, string[] Roots)> contexts,
        List<RemnantItem> items, List<RemnantItem> kept)
    {
        if (contexts.Count == 0) return;
        var planned = contexts.SelectMany(c => c.Packages).Select(p => p.Inf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shared = inventory.Packages.Where(p => !planned.Contains(p.Inf)).SelectMany(p => p.SystemFiles ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stores = inventory.Packages.Select(p => Path.GetFileName(p.Folder.TrimEnd('\\'))).Where(f => f.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var registrations = inventory.Registrations ?? [];
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (vendor, select, packages, roots) in contexts)
        {
            var profile = VendorProfiles.For(vendor);
            var listed = packages.SelectMany(p => p.SystemFiles ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in inventory.SystemFiles ?? [])
            {
                var relative = SystemRelative(inventory.WindowsRoot, file.Path); if (relative is null) continue;
                var author = Vendors.FromCompany(file.Company);
                bool inf = listed.Contains(relative);
                bool log = !inf && file.Source.Length > 0 && !stores.Contains(file.Source) && author == vendor;
                if (!inf && !log) continue;
                string? keep = SharedRuntime(relative) ? "fileSharedRuntime" : shared.Contains(relative) ? "fileShared" : file.Company.Length > 0 && author != vendor ? "fileOtherCompany" : null;
                if (keep is not null) { kept.Add(new("file:" + file.Path, RemnantKind.File, vendor, file.Path, relative, keep, false, file.Bytes)); continue; }
                items.Add(new("file:" + file.Path, RemnantKind.File, vendor, file.Path, relative, inf ? "fileInf" : "fileLog", select, file.Bytes, file.Source));
                files.Add(file.Path);
            }
            foreach (var process in inventory.Processes.Where(p => files.Contains(p.Path)))
                items.Add(new("process:" + process.Id, RemnantKind.Process, vendor, process.Path, process.Name, "processStop", true, 0, process.Path));
            // COM classes served from what is being removed, with their Media Foundation registrations.
            foreach (var com in inventory.ComServers ?? [])
            {
                if (!roots.Any(r => Within(com.Server, r)) && !files.Contains(com.Server)) continue;
                foreach (var key in com.Transforms.Prepend(com.Key))
                    if (claimed.Add(key)) items.Add(new("registry:" + key, RemnantKind.Registry, vendor, key, key, "comServerRemoved", select, 0, com.Server));
            }
            // Protection entries of files inside a removed package folder; quarantined System32 files carry their own entry.
            foreach (var entry in (inventory.Lockdown ?? []).Where(l => roots.Any(r => Within(l.Path, r)) && !files.Contains(l.Path)))
                if (claimed.Add(entry.Key)) items.Add(new("registry:" + entry.Key, RemnantKind.Registry, vendor, entry.Key, entry.Path, "lockdownRemoved", select, 0, entry.Path));
            foreach (var registration in registrations)
            {
                bool removed = roots.Any(r => Within(registration.Name, r) || Within(registration.Library, r)) || files.Contains(registration.Name) || files.Contains(registration.Library);
                bool marked = profile.RuntimeMarkers?.Any(m => Path.GetFileName(registration.Name).Contains(m, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(registration.Library).Contains(m, StringComparison.OrdinalIgnoreCase)) == true;
                // Overlay and capture layers (Steam, OBS, RTSS…) are only touched when they point into what is being removed.
                bool driver = !registration.Key.Contains("Layers", StringComparison.OrdinalIgnoreCase);
                var reason = removed ? "registrationRemoved" : !registration.Exists && marked ? "registrationOrphan" :
                    driver && registration.Exists && Vendors.FromCompany(registration.Company) == vendor ? "registrationVendor" : null;
                if (reason is null || !claimed.Add(registration.Key + "|" + registration.Name)) continue;
                items.Add(new("registration:" + registration.Key + "|" + registration.Name, RemnantKind.Registration, vendor, registration.Key + "|" + registration.Name,
                    registration.Name, reason, select || reason == "registrationOrphan", 0, registration.Key));
            }
        }
        // A registration whose file vanished with a deleted Driver Store package only produces loader errors.
        foreach (var registration in registrations.Where(r => !r.Exists && r.Name.Contains(@"\DriverStore\FileRepository\", StringComparison.OrdinalIgnoreCase)))
            if (claimed.Add(registration.Key + "|" + registration.Name))
                items.Add(new("registration:" + registration.Key + "|" + registration.Name, RemnantKind.Registration, Vendor.Unknown, registration.Key + "|" + registration.Name,
                    registration.Name, "registrationOrphan", true, 0, registration.Key));
        foreach (var com in (inventory.ComServers ?? []).Where(c => !c.Exists && c.Server.Contains(@"\DriverStore\FileRepository\", StringComparison.OrdinalIgnoreCase)))
            foreach (var key in com.Transforms.Prepend(com.Key))
                if (claimed.Add(key)) items.Add(new("registry:" + key, RemnantKind.Registry, Vendor.Unknown, key, key, "comServerOrphan", true, 0, com.Server));
        foreach (var entry in (inventory.Lockdown ?? []).Where(l => !l.Exists && l.Path.Contains(@"\DriverStore\FileRepository\", StringComparison.OrdinalIgnoreCase)))
            if (claimed.Add(entry.Key)) items.Add(new("registry:" + entry.Key, RemnantKind.Registry, Vendor.Unknown, entry.Key, entry.Path, "lockdownOrphan", true, 0, entry.Path));
    }

    // Khronos loaders, Microsoft runtimes and PhysX are shared by every GPU brand and by games; never removed as leftovers.
    public static bool SharedRuntime(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        return name is "opencl.dll" || name.StartsWith("vulkan-1", StringComparison.Ordinal) || name.StartsWith("vulkaninfo", StringComparison.Ordinal) ||
            name.Contains("physx", StringComparison.Ordinal) || name.StartsWith("msvc", StringComparison.Ordinal) || name.StartsWith("vcruntime", StringComparison.Ordinal) ||
            name.StartsWith("ucrtbase", StringComparison.Ordinal) || name.StartsWith("api-ms-", StringComparison.Ordinal) || name.StartsWith("d3dcompiler", StringComparison.Ordinal) ||
            name.StartsWith("dxcompiler", StringComparison.Ordinal) || name.StartsWith("dxil", StringComparison.Ordinal);
    }

    // "System32\name" or "SysWOW64\name" relative to the Windows folder; kernel drivers and the Driver Store are out of scope.
    public static string? SystemRelative(string windowsRoot, string path)
    {
        if (windowsRoot.Length == 0) windowsRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (path.EndsWith(".sys", StringComparison.OrdinalIgnoreCase)) return null;
        var system = Path.Combine(windowsRoot, "System32");
        if (!Within(path, system) && !Within(path, Path.Combine(windowsRoot, "SysWOW64"))) return null;
        if (Within(path, Path.Combine(system, "drivers")) || Within(path, Path.Combine(system, "DriverStore"))) return null;
        return Path.GetRelativePath(windowsRoot, Path.GetFullPath(path));
    }

    private static string Describe(StorePackage p) => $"{p.OriginalName} · {p.ClassName} · {p.Version} · {p.Inf}";
    private static string Label(ServiceEntry s) => string.IsNullOrWhiteSpace(s.DisplayName) || s.DisplayName == s.Name ? s.Name : s.DisplayName + " (" + s.Name + ")";
}
