// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Swap.Core;
namespace Swap.Windows;

// KraicDev. Simulated leftovers of a removed card. Folders are real but live in the demo sandbox; nothing else is touched.
public sealed class DemoCleanupPlatform : ICleanupPlatform
{
    public bool Simulated => true;
    public Vendor Source { get; init; } = Vendor.Nvidia;
    public Vendor Target { get; init; } = Vendor.Amd;
    public string Sandbox { get; }
    public string CpuVendor { get; init; } = "AuthenticAMD";
    public string CpuName { get; init; } = "AMD Ryzen 7 5800X 8-Core Processor";
    public bool Physx { get; init; }
    // Failure injection for tests.
    public HashSet<string> FailDevices { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> FailPackages { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool FailExports { get; set; }
    public bool SourceReturns { get; set; }
    public List<string> Calls { get; } = [];
    private readonly HashSet<string> removed = new(StringComparer.OrdinalIgnoreCase);

    public DemoCleanupPlatform(string sandbox)
    {
        Sandbox = Path.GetFullPath(sandbox);
        void File(string path, int bytes) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); if (!System.IO.File.Exists(path)) System.IO.File.WriteAllBytes(path, new byte[bytes]); }
        File(Path.Combine(ProgramFiles, "NVIDIA App", "CEF", "NVIDIA app.exe"), 4096);
        File(Path.Combine(ProgramFiles, "NvContainer", "nvcontainer.exe"), 2048);
        File(Path.Combine(ProgramFiles, "Installer2", "InstallerCore", "NVI2.DLL"), 1024);
        File(Path.Combine(ProgramFiles, "PhysX", "Common", "PhysXLoader.dll"), 1024);
        File(Path.Combine(Local, "NVIDIA", "DXCache", "shader.bin"), 65536);
        File(Path.Combine(Local, "D3DSCache", "cache.idx"), 32768);
        foreach (var (relative, bytes) in SystemSeed) File(Path.Combine(WindowsRoot, relative), bytes);
    }
    public string WindowsRoot => Path.Combine(Sandbox, "Windows");
    // What nv_dispi.inf copies to System32/SysWOW64 in the simulated PC, plus one file only the setup log still ties to an old package.
    private static readonly (string Relative, int Bytes)[] SystemSeed = [(@"System32\nvapi64.dll", 8192), (@"System32\nvcuda.dll", 16384), (@"SysWOW64\nvapi.dll", 4096),
        (@"System32\vulkan-1.dll", 2048), (@"System32\OpenCL.dll", 2048), (@"System32\nvml.dll", 4096), (@"System32\amdxc64.dll", 4096)];
    private string ProgramFiles => Path.Combine(Sandbox, "Program Files", "NVIDIA Corporation");
    private string Local => Path.Combine(Sandbox, "Users", "Demo", "AppData", "Local");
    private string Store(string name) => Path.Combine(Sandbox, "DriverStore", name);
    private static string Hardware(Vendor v) => v == Vendor.Amd ? @"PCI\VEN_1002&DEV_747E" : v == Vendor.Intel ? @"PCI\VEN_8086&DEV_56A0" : @"PCI\VEN_10DE&DEV_2786";

    public Task<RemnantInventory> InventoryAsync(CancellationToken token = default)
    {
        bool Keep(string key) => !removed.Contains(key);
        var devices = new List<DeviceNode>
        {
            new(Hardware(Target) + @"\DEMO", "Display", "oem99.inf", Target == Vendor.Amd ? "amdkmdag" : "nvlddmkm", true, [Hardware(Target)], "Demo GPU"),
            new(@"PCI\VEN_1022&DEV_790B\DEMO", "System", "oem60.inf", "amdsmbus", true, [@"PCI\VEN_1022&DEV_790B"], "AMD SMBus"),
            new(@"PCI\VEN_10DE&DEV_2484\DEMO", "Display", "oem42.inf", "nvlddmkm", SourceReturns, [@"PCI\VEN_10DE&DEV_2484"], "NVIDIA GeForce RTX 3070"),
            new(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_0094\DEMO", "MEDIA", "oem43.inf", "NVHDA", false, [@"HDAUDIO\FUNC_01&VEN_10DE&DEV_0094"], "NVIDIA High Definition Audio"),
            new(@"PCI\VEN_10DE&DEV_228B\DEMO", "System", "hdaudbus.inf", "HDAudBus", false, [@"PCI\VEN_10DE&DEV_228B"], "High Definition Audio Controller"),
            new(@"ROOT\UNNAMED_DEVICE\0000", "MEDIA", "oem44.inf", "nvvad_WaveExtensible", true, [@"USB\VID_0955&PID_9000"], "NVIDIA Virtual Audio Device (Wave Extensible) (WDM)"),
        };
        var packages = new List<StorePackage>
        {
            new("oem42.inf", "nv_dispi.inf", "NVIDIA", "Display", "32.0.15.6094", false, Store("nv_dispi.inf_amd64_demo"), 1_850_000_000, [@"PCI\VEN_10DE&DEV_2484"],
                [@"System32\nvapi64.dll", @"System32\nvcuda.dll", @"SysWOW64\nvapi.dll", @"System32\vulkan-1.dll", @"System32\OpenCL.dll"]),
            new("oem43.inf", "nvhda.inf", "NVIDIA Corporation", "MEDIA", "1.4.2.6", false, Store("nvhda.inf_amd64_demo"), 280_000, [@"HDAUDIO\FUNC_01&VEN_10DE&DEV_0094"]),
            new("oem44.inf", "nvvad.inf", "NVIDIA", "MEDIA", "4.65.0.3", false, Store("nvvad.inf_amd64_demo"), 120_000, [@"USB\VID_0955&PID_9000"]),
            new("oem45.inf", "nvppc.inf", "NVIDIA", "USB", "1.52.831.832", false, Store("nvppc.inf_amd64_demo"), 700_000, [@"PCI\VEN_10DE&DEV_1AD7"]),
            new("oem60.inf", "amdsmbus.inf", "Advanced Micro Devices, Inc", "System", "5.12.0.38", false, Store("amdsmbus.inf_amd64_demo"), 90_000, [@"PCI\VEN_1022&DEV_790B"]),
            new("oem99.inf", "u0400000.inf", Target == Vendor.Amd ? "Advanced Micro Devices, Inc." : "NVIDIA", "Display", "32.0.1.100", false, Store("target_demo"), 1_200_000_000, [Hardware(Target)],
                [@"System32\amdxc64.dll", @"System32\vulkan-1.dll"]),
        };
        var companies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["vulkan-1.dll"] = "LunarG, Inc.", ["OpenCL.dll"] = "Khronos Group", ["amdxc64.dll"] = "Advanced Micro Devices, Inc." };
        var systemFiles = SystemSeed.Select(s => Path.Combine(WindowsRoot, s.Relative)).Where(System.IO.File.Exists).Select(p => new SystemFileEntry(p, new FileInfo(p).Length,
            companies.TryGetValue(Path.GetFileName(p), out var company) ? company : "NVIDIA Corporation", Path.GetFileName(p) == "nvml.dll" ? "nv_dispi.inf_amd64_previous" : "")).ToArray();
        var registrations = new List<RegistrationEntry>
        {
            new(@"HKLM\SOFTWARE\Khronos\OpenCL\Vendors", "nvopencl64.dll", Path.Combine(WindowsRoot, "System32", "nvopencl64.dll"), false, ""),
            new(@"HKLM\SOFTWARE\Khronos\Vulkan\Drivers", Path.Combine(Store("nv_dispi.inf_amd64_demo"), "nv-vk64.json"), Path.Combine(Store("nv_dispi.inf_amd64_demo"), "nvoglv64.dll"), true, "NVIDIA Corporation"),
            new(@"HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers", @"C:\Program Files (x86)\Steam\SteamOverlayVulkanLayer64.json", @"C:\Program Files (x86)\Steam\SteamOverlayVulkanLayer64.dll", true, "Valve Corporation"),
        };
        var services = new List<ServiceEntry>
        {
            new("nvlddmkm", "nvlddmkm", Path.Combine(Store("nv_dispi.inf_amd64_demo"), "nvlddmkm.sys"), true),
            new("NVDisplay.ContainerLocalSystem", "NVIDIA Display Container LS", Path.Combine(Store("nv_dispi.inf_amd64_demo"), "Display.NvContainer", "NVDisplay.Container.exe"), true),
            new("NvContainerLocalSystem", "NVIDIA LocalSystem Container", Path.Combine(ProgramFiles, "NvContainer", "nvcontainer.exe"), true),
            new("amdsmbus", "AMD SMBus", @"C:\Windows\System32\drivers\amdsmbus.sys", true),
        };
        var tasks = new List<CommandEntry> { new(@"\NvTmRep_CrashReport1_{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}", "NvTmRep_CrashReport1", [Path.Combine(ProgramFiles, "NvContainer", "nvcontainer.exe")], true) };
        var apps = new List<AppEntry>
        {
            new(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}_Display.Driver", "NVIDIA Graphics Driver 560.94", "NVIDIA Corporation", "560.94",
                [Path.Combine(ProgramFiles, "Installer2", "InstallerCore", "NVI2.DLL")], true, true, false, "rundll32 NVI2.DLL,UninstallPackage Display.Driver"),
            new(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}_NvApp", "NVIDIA app 11.0", "NVIDIA Corporation", "11.0",
                [Path.Combine(ProgramFiles, "NVIDIA App", "CEF", "NVIDIA app.exe")], true, true, false, "NVIDIA app.exe -uninstall"),
        };
        if (Physx) apps.Add(new(@"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{F5C4D1F8-5B2A-4D17-9E7E-2A0C3A2F8E3B}", "NVIDIA PhysX System Software 9.23.1019", "NVIDIA Corporation", "9.23.1019", [], false, true, true, "MsiExec.exe /X{F5C4D1F8-5B2A-4D17-9E7E-2A0C3A2F8E3B}"));
        var folders = new List<FolderEntry>();
        if (Directory.Exists(ProgramFiles))
            folders.Add(new(@"%ProgramFiles%\NVIDIA Corporation", ProgramFiles, WindowsCleanupPlatform.Measure(ProgramFiles),
                Directory.EnumerateDirectories(ProgramFiles).Select(c => new FolderChild(Path.GetFileName(c), c, WindowsCleanupPlatform.Measure(c))).ToArray()));
        if (Directory.Exists(Path.Combine(Local, "NVIDIA"))) folders.Add(new(@"%LocalAppData%\NVIDIA", Path.Combine(Local, "NVIDIA"), WindowsCleanupPlatform.Measure(Path.Combine(Local, "NVIDIA")), []));
        if (Directory.Exists(Path.Combine(Local, "D3DSCache"))) folders.Add(new(VendorProfiles.DirectXCache.Location, Path.Combine(Local, "D3DSCache"), WindowsCleanupPlatform.Measure(Path.Combine(Local, "D3DSCache")), []));
        var processes = new List<ProcessEntry> { new(424242, "NVIDIA app", Path.Combine(ProgramFiles, "NVIDIA App", "CEF", "NVIDIA app.exe")) };
        return Task.FromResult(new RemnantInventory("DEMO", CpuVendor, CpuName, true, false,
            devices.Where(d => Keep("device:" + d.Id)).ToArray(), packages.Where(p => Keep("package:" + p.Inf)).ToArray(),
            services.Where(s => Keep("service:" + s.Name)).ToArray(), ["amdkmdag", "amdsmbus", "HDAudBus"],
            tasks.Where(t => Keep("task:" + t.Key)).ToArray(), [], apps.Where(a => Keep("app:" + a.Key)).ToArray(),
            new[] { "NVIDIACorp.NVIDIAControlPanel" }.Where(a => Keep("appx:" + a)).ToArray(), folders,
            new[] { @"HKLM\SOFTWARE\NVIDIA Corporation", @"HKCU\SOFTWARE\NVIDIA Corporation" }.Where(k => Keep("registry:" + k)).ToArray(),
            processes.Where(p => Keep("process:" + p.Id)).ToArray(), true, [], systemFiles,
            registrations.Where(r => Keep("registration:" + r.Key + "|" + r.Name)).ToArray(), WindowsRoot,
            new[] { new ComServer(@"HKLM\SOFTWARE\Classes\CLSID\{60F44560-5A20-4857-BFEF-D29773CB8040}", "{60F44560-5A20-4857-BFEF-D29773CB8040}",
                Path.Combine(Store("nv_dispi.inf_amd64_demo"), "nvEncMFTH264x.dll"), true, [@"HKLM\SOFTWARE\Microsoft\Windows Media Foundation\Transforms\{60F44560-5A20-4857-BFEF-D29773CB8040}"]) }
                .Where(c => Keep("registry:" + c.Key)).ToArray(),
            new[] { new LockdownEntry(LockdownKey,
                Path.Combine(Store("nv_dispi.inf_amd64_demo"), "nvEncMFTH264x.dll"), true) }.Where(l => Keep("registry:" + l.Key)).ToArray()));
    }
    public bool KeyExists(string key) => !removed.Contains("registry:" + key) && !removed.Contains("app:" + key);

    private async Task<CommandResult> Step(string call, string key, bool fail = false)
    {
        Calls.Add(call); await Task.Delay(40);
        if (fail) return new(1, "Simulated failure: " + call);
        removed.Add(key); return new(0, "Simulated: " + call);
    }
    private async Task<CommandResult> Backup(string call, string file, string content)
    {
        Calls.Add(call); if (FailExports) return new(1, "Simulated export failure");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllTextAsync(file, content); return new(0, "Simulated backup");
    }
    public Task<string> RestorePointAsync() { Calls.Add("restore-point"); return Task.FromResult("Punto de restauración simulado."); }
    public Task<CommandResult> StopProcessAsync(int id, string path) => Step("stop:" + id, "process:" + id);
    public Task<CommandResult> RunUninstallerAsync(string key) => Step("uninstall:" + key, "app:" + key);
    public Task<CommandResult> RemoveDeviceAsync(string instanceId) => Step("remove-device:" + instanceId, "device:" + instanceId, FailDevices.Contains(instanceId));
    public async Task<CommandResult> ExportPackageAsync(string inf, string destination) => await Backup("export:" + inf, Path.Combine(destination, inf), "; simulated " + inf);
    // Test hook: Windows dropping the package's protection entry together with the package.
    public bool PackageDropsLockdown { get; set; }
    public bool PackageDropsSystemFiles { get; set; }
    public const string LockdownKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\PnpLockdownFiles\%SystemRoot%/System32/DriverStore/FileRepository/nv_dispi.inf_amd64_demo/nvEncMFTH264x.dll";
    public async Task<CommandResult> DeletePackageAsync(string inf)
    {
        var result = await Step("delete:" + inf, "package:" + inf, FailPackages.Contains(inf));
        if (result.Success && PackageDropsLockdown && inf == "oem42.inf") removed.Add("registry:" + LockdownKey);
        // Current Windows deletes the System32 links of the package it removes, shared loaders included.
        if (result.Success && PackageDropsSystemFiles && inf == "oem42.inf")
            foreach (var relative in new[] { @"System32\nvapi64.dll", @"System32\nvcuda.dll", @"SysWOW64\nvapi.dll", @"System32\vulkan-1.dll", @"System32\OpenCL.dll" }) File.Delete(Path.Combine(WindowsRoot, relative));
        return result;
    }
    public Task<CommandResult> AddPackageAsync(string infPath) { Calls.Add("add:" + Path.GetFileName(infPath)); removed.Remove("package:" + Path.GetFileName(infPath)); return Task.FromResult(new CommandResult(0, "Simulated staging")); }
    public Task<CommandResult> StopServiceAsync(string name) { Calls.Add("stop-service:" + name); return Task.FromResult(new CommandResult(0, "")); }
    public Task<CommandResult> DeleteServiceAsync(string name) => Step("delete-service:" + name, "service:" + name);
    public Task<CommandResult> ExportRegistryAsync(string key, string file) => Backup("export-registry:" + key, file, "Windows Registry Editor Version 5.00\r\n; " + key);
    public async Task<CommandResult> ImportRegistryAsync(string file)
    {
        var key = (await File.ReadAllLinesAsync(file)).Last()[2..]; Calls.Add("import:" + key);
        foreach (var prefix in new[] { "registry:", "app:" }) removed.Remove(prefix + key);
        removed.RemoveWhere(r => r.StartsWith("service:", StringComparison.Ordinal) && key.EndsWith("\\" + r[8..], StringComparison.OrdinalIgnoreCase));
        return new(0, "Simulated import");
    }
    public Task<CommandResult> DeleteRegistryAsync(string key) => Step("delete-registry:" + key, (key.Contains(@"\Uninstall\", StringComparison.OrdinalIgnoreCase) ? "app:" : "registry:") + key);
    // Test hook: a Windows step that finishes after its time limit.
    public bool SlowTasks { get; set; }
    public Task<CommandResult> ExportTaskAsync(string path, string file) => SlowTasks ? throw new TimeoutException("Simulated late Windows step")
        : Backup("export-task:" + path, file, "<Task>" + path + "</Task>");
    public Task<CommandResult> DeleteTaskAsync(string path) => Step("delete-task:" + path, "task:" + path);
    public Task<CommandResult> RegisterTaskAsync(string path, string file) { Calls.Add("register-task:" + path); removed.Remove("task:" + path); return Task.FromResult(new CommandResult(0, "Simulated task")); }
    public Task<CommandResult> ExportStartupAsync(string key, string file) => Backup("export-startup:" + key, file, JsonSerializer.Serialize(new { key }));
    public Task<CommandResult> DeleteStartupAsync(string key) => Step("delete-startup:" + key, "startup:" + key);
    public Task<CommandResult> RestoreStartupAsync(string file) { Calls.Add("restore-startup"); return Task.FromResult(new CommandResult(0, "Simulated startup")); }
    public Task<CommandResult> RemoveAppxAsync(string name) => Step("remove-appx:" + name, "appx:" + name);
    // Files are really moved inside the sandbox; registrations are simulated.
    public Task<CommandResult> QuarantineFileAsync(string path, string destination)
    {
        Calls.Add("quarantine:" + Path.GetFileName(path));
        if (DeepCleanPlanner.SystemRelative(WindowsRoot, path) is null) return Task.FromResult(new CommandResult(87, "Outside the simulated Windows folder"));
        File.Move(path, destination); return Task.FromResult(new CommandResult(0, "Simulated quarantine"));
    }
    public Task<CommandResult> RestoreFileAsync(string backup, string origin) { Calls.Add("restore-file:" + Path.GetFileName(origin)); File.Move(backup, origin); return Task.FromResult(new CommandResult(0, "Simulated restore")); }
    public Task<CommandResult> ExportValueAsync(string key, string name, string file) => Backup("export-value:" + name, file, JsonSerializer.Serialize(new { key, name }));
    public Task<CommandResult> DeleteValueAsync(string key, string name) => Step("delete-value:" + name, "registration:" + key + "|" + name);
    public async Task<CommandResult> RestoreValueAsync(string file)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(file));
        var key = json.RootElement.GetProperty("key").GetString(); var name = json.RootElement.GetProperty("name").GetString();
        Calls.Add("restore-value:" + name); removed.Remove("registration:" + key + "|" + name);
        return new(0, "Simulated registration");
    }
}
