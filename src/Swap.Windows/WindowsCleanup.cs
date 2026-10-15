// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Swap.Core;
namespace Swap.Windows;

// KraicDev. Native remnant inventory and the Windows operations behind deep cleanup. Each operation receives a
// validated identifier and uses an absolute system tool path with separate arguments; never a composed shell command.
public sealed partial class WindowsCleanupPlatform : ICleanupPlatform
{
    private const string Enum = @"SYSTEM\CurrentControlSet\Enum";
    private static readonly string[] RunKeys = [@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"];
    private static readonly string[] UninstallKeys = [@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"];
    private static string SystemExe(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Locate_DevNodeW")] private static extern int LocateDevNode(out uint devInst, string id, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")] private static extern bool QueryImage(IntPtr process, int flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    public static bool Present(string instanceId) => LocateDevNode(out _, instanceId, 0) == 0;

    private sealed record PsPackage(string Inf, string Original, string Provider, string Class, string Version, bool BootCritical);
    private sealed record PsTask(string Path, string[]? Actions);
    private sealed record PsInventory(bool Admin, bool Complete, string MachineId, bool Laptop, PsPackage[]? Packages, PsTask[]? Tasks, string[]? Appx, string[]? Warnings);

    public async Task<RemnantInventory> InventoryAsync(CancellationToken token = default)
    {
        var names = JsonSerializer.Serialize(VendorProfiles.All.SelectMany(p => p.Appx).ToArray()).Replace("'", "''");
        var ps = JsonSerializer.Deserialize<PsInventory>(await WindowsPlatform.PowerShellAsync(WindowsPlatform.Script("Remnants").Replace("__APPX__", names), 900))
            ?? throw new IOException("Inventario vacío.");
        token.ThrowIfCancellationRequested();
        var warnings = (ps.Warnings ?? []).ToList();
        var (devices, active) = await Task.Run(() => Devices(warnings), token);
        var packages = await Task.Run(() => (ps.Packages ?? []).Where(p => Planner.IsOem(p.Inf)).Select(Package).ToArray(), token);
        var tasks = (ps.Tasks ?? []).Where(t => t.Path.StartsWith('\\')).Select(t =>
        {
            var files = (t.Actions ?? []).SelectMany(a => CommandFiles.Parse(a)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new CommandEntry(t.Path, t.Path, files, files.Length > 0 && files.All(File.Exists));
        }).ToArray();
        var (cpuVendor, cpuName) = Cpu();
        var systemFiles = await Task.Run(() => SystemFiles(packages, warnings), token);
        var deployed = systemFiles.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new(ps.MachineId, cpuVendor, cpuName, ps.Admin, ps.Laptop, devices, packages, Services(), active, tasks, Startup(), Apps(), ps.Appx ?? [],
            await Task.Run(Folders, token), RegistryKeys(), Processes(), ps.Complete && ps.Admin, warnings, systemFiles, Registrations(),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), await Task.Run(() => ComServers(deployed), token), await Task.Run(() => LockdownEntries(deployed), token));
    }

    // Protection entries of driver-deployed files: Driver Store files and System32 files a package deployed.
    private static LockdownEntry[] LockdownEntries(IReadOnlySet<string> deployed)
    {
        var result = new List<LockdownEntry>();
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.OpenSubKey(Lockdown); if (key is null) return [];
            foreach (var name in key.GetSubKeyNames().Take(200000))
            {
                var path = Environment.ExpandEnvironmentVariables(name.Replace('/', '\\'));
                if (!path.Contains(@"\DriverStore\FileRepository\", StringComparison.OrdinalIgnoreCase) && !deployed.Contains(path)) continue;
                result.Add(new(@"HKLM\" + Lockdown + @"\" + name, path, File.Exists(path)));
            }
        }
        catch (Exception e) when (e is IOException or System.Security.SecurityException or UnauthorizedAccessException) { }
        return result.ToArray();
    }
    public bool KeyExists(string key)
    {
        try { using var registry = OpenPath(key); return registry is not null; }
        catch (Exception e) when (e is IOException or System.Security.SecurityException or UnauthorizedAccessException) { return true; }
    }
    public bool KeyEmpty(string key)
    {
        try { using var registry = OpenPath(key); return registry is { SubKeyCount: 0, ValueCount: 0 }; }
        catch (Exception e) when (e is IOException or System.Security.SecurityException or UnauthorizedAccessException) { return false; }
    }

    // COM classes whose server is a Driver Store file or a System32 file a package deployed (vendor encoders and decoders), with their
    // Media Foundation transform registrations and the copies Windows keeps of driver-installed registry data (Setup\PnpResources).
    private const string PnpResources = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\PnpResources\Registry\HKCR";
    private const string PnpResources32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Setup\PnpResources\Registry\HKCR\Wow6432Node";
    private static ComServer[] ComServers(IReadOnlySet<string> deployed)
    {
        var result = new List<ComServer>();
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        var related = new Dictionary<Guid, List<string>>();
        void Add(Guid id, string key) { if (!related.TryGetValue(id, out var list)) related[id] = list = []; list.Add(@"HKLM\" + key); }
        bool Exists(string key)
        {
            try { using var found = root.OpenSubKey(key); return found is not null; }
            catch (Exception e) when (e is IOException or System.Security.SecurityException or UnauthorizedAccessException) { return false; }
        }
        // Transform names are GUIDs without braces; CLSID keys have them.
        foreach (var (classes, mirror) in new[] { (@"SOFTWARE\Classes", PnpResources), (@"SOFTWARE\Classes\WOW6432Node", PnpResources32) })
        {
            try
            {
                var transforms = classes + @"\MediaFoundation\Transforms";
                using var mft = root.OpenSubKey(transforms); if (mft is null) continue;
                foreach (var name in mft.GetSubKeyNames())
                {
                    if (!Guid.TryParse(name, out var id)) continue;
                    Add(id, transforms + @"\" + name);
                    if (Exists(mirror + @"\MediaFoundation\Transforms\" + name)) Add(id, mirror + @"\MediaFoundation\Transforms\" + name);
                }
                using var categories = mft.OpenSubKey("Categories");
                foreach (var category in categories?.GetSubKeyNames() ?? [])
                {
                    using var members = categories!.OpenSubKey(category);
                    foreach (var name in members?.GetSubKeyNames() ?? [])
                    {
                        if (!Guid.TryParse(name, out var id)) continue;
                        var path = @"\MediaFoundation\Transforms\Categories\" + category + @"\" + name;
                        Add(id, transforms + @"\Categories\" + category + @"\" + name);
                        if (Exists(mirror + path)) Add(id, mirror + path);
                    }
                }
            }
            catch (Exception e) when (e is IOException or System.Security.SecurityException or UnauthorizedAccessException) { }
        }
        foreach (var (classes, mirror) in new[] { (@"SOFTWARE\Classes\CLSID", PnpResources + @"\CLSID"), (@"SOFTWARE\WOW6432Node\Classes\CLSID", PnpResources32 + @"\CLSID") })
        {
            try
            {
                using var clsids = root.OpenSubKey(classes); if (clsids is null) continue;
                foreach (var clsid in clsids.GetSubKeyNames().Take(50000))
                {
                    if (!Guid.TryParse(clsid, out var id)) continue;
                    foreach (var kind in new[] { "InprocServer32", "LocalServer32" })
                    {
                        using var server = clsids.OpenSubKey(clsid + @"\" + kind);
                        if (server?.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string value || value.Length == 0) continue;
                        var file = CommandFiles.Parse(value).FirstOrDefault() ?? Environment.ExpandEnvironmentVariables(value.Trim('"'));
                        // System32 servers count only when a driver package deployed that file.
                        if (!file.Contains(@"\DriverStore\FileRepository\", StringComparison.OrdinalIgnoreCase) && !deployed.Contains(file)) continue;
                        var keys = related.TryGetValue(id, out var transforms) ? new List<string>(transforms) : [];
                        if (Exists(mirror + @"\" + clsid)) keys.Add(@"HKLM\" + mirror + @"\" + clsid);
                        result.Add(new(@"HKLM\" + classes + @"\" + clsid, clsid, file, File.Exists(file), keys.ToArray()));
                        break;
                    }
                }
            }
            catch (Exception e) when (e is IOException or System.Security.SecurityException or UnauthorizedAccessException) { }
        }
        return result.ToArray();
    }

    // Candidates come only from package evidence: the INF of every package in the store and the copies recorded in the setup log.
    private static SystemFileEntry[] SystemFiles(IEnumerable<StorePackage> packages, List<string> warnings)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in packages.SelectMany(p => p.SystemFiles ?? [])) candidates.TryAdd(Path.GetFullPath(Path.Combine(windows, relative)), "");
        // Known modules a vendor's own installer (not its INF) copies into System32/SysWOW64; the planner still requires the brand as author.
        foreach (var module in VendorProfiles.All.SelectMany(p => p.SystemModules ?? []))
            foreach (var folder in new[] { "System32", "SysWOW64" }) candidates.TryAdd(Path.GetFullPath(Path.Combine(windows, folder, module)), "");
        try
        {
            foreach (var (target, source) in SetupLog.Copies(SetupLogLines(windows)))
                if (DeepCleanPlanner.SystemRelative(windows, target) is not null) candidates[Path.GetFullPath(target)] = source;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { warnings.Add("Setup log unavailable: " + e.GetType().Name); }
        var result = new List<SystemFileEntry>();
        foreach (var (path, source) in candidates.Take(20000))
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                result.Add(new(info.FullName, info.Length, Company(info.FullName), source));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
        return result.ToArray();
    }
    // Current and archived setupapi.dev logs, read while Windows keeps them open; bounded in total size.
    private static IEnumerable<string> SetupLogLines(string windows)
    {
        long budget = 256L * 1024 * 1024;
        var folder = Path.Combine(windows, "INF");
        foreach (var log in Directory.EnumerateFiles(folder, "setupapi.dev*.log").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(log);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || (budget -= info.Length) < 0) yield break;
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            while (reader.ReadLine() is { } line) yield return line;
        }
    }
    private static string Company(string path)
    {
        try { return FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim() ?? ""; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return ""; }
    }

    // Vulkan drivers and layers and OpenCL vendors registered for the Khronos loaders, native and 32-bit views.
    private static readonly string[] KhronosKeys = [@"HKLM\SOFTWARE\Khronos\Vulkan\Drivers", @"HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers", @"HKLM\SOFTWARE\Khronos\Vulkan\ExplicitLayers",
        @"HKLM\SOFTWARE\Khronos\OpenCL\Vendors", @"HKLM\SOFTWARE\WOW6432Node\Khronos\Vulkan\Drivers", @"HKLM\SOFTWARE\WOW6432Node\Khronos\Vulkan\ImplicitLayers",
        @"HKLM\SOFTWARE\WOW6432Node\Khronos\Vulkan\ExplicitLayers", @"HKLM\SOFTWARE\WOW6432Node\Khronos\OpenCL\Vendors"];
    private static RegistrationEntry[] Registrations()
    {
        var result = new List<RegistrationEntry>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var path in KhronosKeys)
        {
            try
            {
                using var key = OpenPath(path); if (key is null) continue;
                var system = Path.Combine(windows, path.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase) ? "SysWOW64" : "System32");
                foreach (var name in key.GetValueNames().Where(n => n.Length > 0).Take(500))
                {
                    // OpenCL vendors may be bare DLL names that the loader finds through the system folder.
                    var file = Path.IsPathRooted(name) ? name : name.IndexOfAny(['\\', '/']) < 0 ? Path.Combine(system, name) : "";
                    bool exists = file.Length > 0 && File.Exists(file); var library = file;
                    if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) { library = exists ? ManifestLibrary(file, system) : ""; exists = exists && library.Length > 0 && File.Exists(library); }
                    result.Add(new(path, name, library, exists, exists ? Company(library) : ""));
                }
            }
            catch (Exception e) when (e is IOException or System.Security.SecurityException or UnauthorizedAccessException) { }
        }
        return result.ToArray();
    }
    private static string ManifestLibrary(string manifest, string system)
    {
        try
        {
            if (new FileInfo(manifest).Length > 1024 * 1024) return "";
            using var json = JsonDocument.Parse(File.ReadAllText(manifest));
            var root = json.RootElement; JsonElement section;
            if (!root.TryGetProperty("ICD", out section) && !root.TryGetProperty("layer", out section))
                section = root.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array && layers.GetArrayLength() > 0 ? layers[0] : default;
            if (section.ValueKind != JsonValueKind.Object || !section.TryGetProperty("library_path", out var value) || value.GetString() is not { Length: > 0 } library) return "";
            library = library.Replace('/', '\\');
            return Path.IsPathRooted(library) ? library : library.Contains('\\') ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest)!, library)) : Path.Combine(system, library);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException) { return ""; }
    }

    private static (DeviceNode[] Devices, string[] Active) Devices(List<string> warnings)
    {
        var result = new List<DeviceNode>(); var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var classFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var classNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var enumKey = root.OpenSubKey(Enum) ?? throw new IOException("Device registry unavailable.");
        int denied = 0;
        foreach (var bus in enumKey.GetSubKeyNames())
        {
            using var busKey = Open(enumKey, bus, ref denied); if (busKey is null) continue;
            foreach (var model in busKey.GetSubKeyNames())
            {
                using var modelKey = Open(busKey, model, ref denied); if (modelKey is null) continue;
                foreach (var instance in modelKey.GetSubKeyNames())
                {
                    using var key = Open(modelKey, instance, ref denied); if (key is null) continue;
                    var id = bus + "\\" + model + "\\" + instance;
                    var hardware = key.GetValue("HardwareID") as string[] ?? [];
                    var driver = key.GetValue("Driver") as string ?? "";
                    var service = key.GetValue("Service") as string ?? "";
                    var inf = "";
                    if (driver.Length > 0)
                    {
                        using var driverKey = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\" + driver);
                        inf = driverKey?.GetValue("InfPath") as string ?? "";
                    }
                    var name = Indirect(key.GetValue("FriendlyName") as string ?? key.GetValue("DeviceDesc") as string ?? "");
                    bool present = Present(id);
                    if (present)
                    {
                        if (service.Length > 0) active.Add(service);
                        foreach (var filter in (key.GetValue("UpperFilters") as string[] ?? []).Concat(key.GetValue("LowerFilters") as string[] ?? [])) active.Add(filter);
                        if (key.GetValue("ClassGUID") is string guid) classFilters.Add(guid);
                    }
                    result.Add(new(id, ClassName(root, key, classNames), inf, service, present, hardware, name));
                }
            }
        }
        foreach (var guid in classFilters)
        {
            using var classKey = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\" + guid);
            foreach (var filter in (classKey?.GetValue("UpperFilters") as string[] ?? []).Concat(classKey?.GetValue("LowerFilters") as string[] ?? [])) active.Add(filter);
        }
        if (denied > 0) warnings.Add("Device registry entries without read access: " + denied);
        return (result.ToArray(), active.ToArray());
    }
    // Current Windows versions store only ClassGUID on the device; the class name lives under Control\Class.
    private static string ClassName(RegistryKey root, RegistryKey device, Dictionary<string, string> cache)
    {
        if (device.GetValue("Class") is string { Length: > 0 } direct) return direct;
        if (device.GetValue("ClassGUID") is not string guid || !Guid.TryParse(guid, out _)) return "";
        if (!cache.TryGetValue(guid, out var name))
        {
            using var classKey = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\" + guid);
            cache[guid] = name = classKey?.GetValue("Class") as string ?? "";
        }
        return name;
    }
    private static RegistryKey? Open(RegistryKey parent, string name, ref int denied)
    {
        try { return parent.OpenSubKey(name); }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { denied++; return null; }
    }
    private static string Indirect(string value) => value.StartsWith('@') && value.Contains(';') ? value[(value.LastIndexOf(';') + 1)..] : value;

    private static StorePackage Package(PsPackage p)
    {
        var folder = Path.GetDirectoryName(p.Original) ?? ""; IReadOnlyList<string> ids = [], files = [], components = []; long bytes = 0; var provider = p.Provider; var cls = p.Class;
        try
        {
            var store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DriverStore", "FileRepository");
            if (folder.Length > 0 && DeepCleanPlanner.Within(folder, store) && File.Exists(p.Original) && new FileInfo(p.Original).Length < 32 * 1024 * 1024)
            {
                var summary = InfReader.Read(File.ReadAllText(p.Original));
                ids = summary.HardwareIds; files = summary.SystemFiles ?? []; components = summary.ComponentIds ?? [];
                if (provider.Length == 0) provider = summary.Provider;
                if (cls.Length == 0) cls = summary.Class;
                bytes = Measure(folder);
            }
            else folder = "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return new(p.Inf, Path.GetFileName(p.Original), provider, cls, p.Version, p.BootCritical, folder, bytes, ids, files, components);
    }

    private static ServiceEntry[] Services()
    {
        var result = new List<ServiceEntry>();
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var services = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (services is null) return [];
        foreach (var name in services.GetSubKeyNames())
        {
            try
            {
                using var key = services.OpenSubKey(name); if (key is null) continue;
                var image = key.GetValue("ImagePath", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
                if (key.GetValue("Type") is not int type) continue;
                string binary;
                if (image.Length == 0) binary = (type & 3) != 0 ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", name + ".sys") : "";
                else if (image.Contains("svchost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    using var parameters = key.OpenSubKey("Parameters");
                    binary = CommandFiles.Parse(parameters?.GetValue("ServiceDll", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "").FirstOrDefault() ?? "";
                }
                else binary = CommandFiles.Parse(image).FirstOrDefault() ?? "";
                result.Add(new(name, Indirect(key.GetValue("DisplayName") as string ?? ""), binary, binary.Length > 0 && File.Exists(binary)));
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return result.ToArray();
    }

    private static RegistryKey? OpenPath(string path, bool writable = false)
    {
        var hive = path.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase) ? RegistryHive.LocalMachine : path.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase) ? RegistryHive.CurrentUser : throw new IOException("Unsupported registry hive.");
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        return root.OpenSubKey(path[5..], writable);
    }
    private static CommandEntry[] Startup()
    {
        var result = new List<CommandEntry>();
        foreach (var path in RunKeys)
        {
            using var key = OpenPath(path); if (key is null) continue;
            foreach (var name in key.GetValueNames().Where(n => n.Length > 0))
            {
                var files = CommandFiles.Parse(key.GetValue(name) as string ?? "");
                result.Add(new(path + "|" + name, name, files, files.Count > 0 && files.All(File.Exists)));
            }
        }
        return result.ToArray();
    }
    private static AppEntry[] Apps()
    {
        var result = new List<AppEntry>();
        foreach (var path in UninstallKeys)
        {
            using var key = OpenPath(path); if (key is null) continue;
            foreach (var name in key.GetSubKeyNames())
            {
                using var app = key.OpenSubKey(name); if (app?.GetValue("DisplayName") is not string display) continue;
                var command = app.GetValue("UninstallString") as string ?? "";
                bool msi = app.GetValue("WindowsInstaller") is int installer && installer == 1 && Guid.TryParse(name, out _);
                // Hidden vendor sub-components often have no uninstall command; their icon module is the only file evidence.
                var files = CommandFiles.Parse(command);
                if (files.Count == 0 && command.Length == 0 && app.GetValue("DisplayIcon") is string icon) files = CommandFiles.Parse(icon);
                result.Add(new(path + "\\" + name, display, app.GetValue("Publisher") as string ?? "", app.GetValue("DisplayVersion") as string ?? "", files,
                    files.Count > 0 && files.All(File.Exists), path.StartsWith("HKLM", StringComparison.Ordinal), msi, command));
            }
        }
        return result.ToArray();
    }

    // Per-user locations are expanded for every local profile; caches of other users are rebuilt by the GPU driver too.
    private static string[] Profiles()
    {
        var result = new List<string>();
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var list = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        // The profiles directory comes from Windows, not from the current account (which may be SYSTEM or another administrator).
        var users = Environment.ExpandEnvironmentVariables(list?.GetValue("ProfilesDirectory", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? @"%SystemDrive%\Users");
        foreach (var sid in list?.GetSubKeyNames() ?? [])
        {
            if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal)) continue;
            using var profile = list!.OpenSubKey(sid);
            if (profile?.GetValue("ProfileImagePath") is string image && Directory.Exists(image) && DeepCleanPlanner.Within(image, users)) result.Add(Environment.ExpandEnvironmentVariables(image));
        }
        var current = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!result.Contains(current, StringComparer.OrdinalIgnoreCase) && DeepCleanPlanner.Within(current, users)) result.Add(current);
        return result.ToArray();
    }
    public static IEnumerable<string> Expand(FolderSpec spec, IReadOnlyList<string> profiles)
    {
        if (!spec.PerUser) { yield return Environment.ExpandEnvironmentVariables(spec.Location); yield break; }
        foreach (var profile in profiles)
            yield return spec.Location.Replace("%LocalAppData%", Path.Combine(profile, "AppData", "Local"), StringComparison.OrdinalIgnoreCase)
                .Replace("%AppData%", Path.Combine(profile, "AppData", "Roaming"), StringComparison.OrdinalIgnoreCase)
                .Replace("%UserProfile%", profile, StringComparison.OrdinalIgnoreCase);
    }
    private static FolderEntry[] Folders()
    {
        var profiles = Profiles(); var result = new List<FolderEntry>();
        foreach (var spec in VendorProfiles.FolderSpecs)
            foreach (var path in Expand(spec, profiles).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (path.Contains('%') || !Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                    var children = spec.Include is null && spec.Protect is null ? [] :
                        Directory.EnumerateFileSystemEntries(path).Select(c => new FolderChild(Path.GetFileName(c), c, Directory.Exists(c) ? Measure(c) : new FileInfo(c).Length)).ToArray();
                    result.Add(new(spec.Location, path, Measure(path), children));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        return result.ToArray();
    }
    // Bounded size estimate; links are not followed.
    public static long Measure(string root)
    {
        long total = 0; int count = 0; var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0 && count < 300000)
        {
            var directory = pending.Pop();
            try
            {
                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                {
                    if (++count > 300000) break;
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (entry is DirectoryInfo) pending.Push(entry.FullName); else total += ((FileInfo)entry).Length;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return total;
    }
    private static string[] RegistryKeys() => VendorProfiles.All.SelectMany(p => p.RegistryKeys).Where(path =>
    {
        try { using var key = OpenPath(path); return key is not null; } catch (Exception e) when (e is IOException or System.Security.SecurityException or UnauthorizedAccessException) { return false; }
    }).ToArray();
    public Task<IReadOnlyList<ProcessEntry>> RunningProcessesAsync() => Task.Run<IReadOnlyList<ProcessEntry>>(Processes);
    private static ProcessEntry[] Processes()
    {
        var result = new List<ProcessEntry>();
        foreach (var process in Process.GetProcesses())
            using (process)
            {
                var path = ImagePath(process.Id);
                if (path.Length > 0) result.Add(new(process.Id, process.ProcessName, path));
            }
        return result.ToArray();
    }
    private static string ImagePath(int id)
    {
        var handle = OpenProcess(0x1000, false, id); if (handle == IntPtr.Zero) return "";
        try { var buffer = new StringBuilder(1024); int size = buffer.Capacity; return QueryImage(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : ""; }
        finally { CloseHandle(handle); }
    }
    private static (string Vendor, string Name) Cpu()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var cpu = root.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return ((cpu?.GetValue("VendorIdentifier") as string ?? "").Trim(), (cpu?.GetValue("ProcessorNameString") as string ?? "").Trim());
    }

    // ---- Operations ----
    [GeneratedRegex(@"\A[A-Za-z0-9_]{1,32}\\[^\\""\r\n]{1,200}\\[^\\""\r\n]{1,200}\z", RegexOptions.CultureInvariant)] private static partial Regex InstanceId();
    [GeneratedRegex(@"\A[A-Za-z0-9_.\- ]{1,200}\z", RegexOptions.CultureInvariant)] private static partial Regex ServiceName();
    [GeneratedRegex(@"\AHK(LM|CU)\\[^""\r\n*?]{3,400}\z", RegexOptions.CultureInvariant)] private static partial Regex RegistryPath();
    [GeneratedRegex(@"\A\\[^""\r\n*?]{1,400}\z", RegexOptions.CultureInvariant)] private static partial Regex TaskPath();
    private static Task<CommandResult> Run(string exe, params string[] args) => WindowsPlatform.RunAsync(SystemExe(exe), args, 600);
    private static CommandResult Invalid(string what) => new(87, "Invalid " + what);

    public Task<string> RestorePointAsync() => new WindowsPlatform().RestorePointAsync();
    public Task<CommandResult> RemoveDeviceAsync(string instanceId)
    {
        if (!InstanceId().IsMatch(instanceId)) return Task.FromResult(Invalid("device"));
        // pnputil /remove-device exists from Windows 10 2004; older builds keep the device and report it.
        if (Environment.OSVersion.Version.Build < 19041) return Task.FromResult(new CommandResult(50, "pnputil /remove-device requires Windows 10 2004 or later."));
        return Run("pnputil.exe", "/remove-device", instanceId);
    }
    public Task<CommandResult> ExportPackageAsync(string inf, string destination) => new WindowsPlatform().ExportAsync(inf, destination);
    public Task<CommandResult> DeletePackageAsync(string inf) => new WindowsPlatform().DeleteAsync(inf);
    public async Task<CommandResult> AddPackageAsync(string infPath)
    {
        if (!infPath.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) || !File.Exists(infPath)) return Invalid("package");
        return await Run("pnputil.exe", "/add-driver", Path.GetFullPath(infPath));
    }
    public async Task<CommandResult> StopServiceAsync(string name) => ServiceName().IsMatch(name) ? await Run("sc.exe", "stop", name) : Invalid("service");
    public async Task<CommandResult> DeleteServiceAsync(string name)
    {
        if (!ServiceName().IsMatch(name)) return Invalid("service");
        var result = await Run("sc.exe", "delete", name);
        return result.Code == 1060 ? new(0, "Service no longer exists.") : result;
    }
    public async Task<CommandResult> ExportRegistryAsync(string key, string file)
    {
        if (!RegistryPath().IsMatch(key)) return Invalid("registry key");
        var export = await Run("reg.exe", "export", key, file, "/y", "/reg:64");
        // A protected key is also saved as a hive: values, subkeys and its TrustedInstaller permissions come back exactly on undo.
        return export.Success && ProtectedKey(key) ? SaveHive(key, file + ".hive") is { Success: false } hive ? hive : export : export;
    }
    public async Task<CommandResult> ImportRegistryAsync(string file)
    {
        if (!File.Exists(file) || !file.EndsWith(".reg", StringComparison.OrdinalIgnoreCase)) return Invalid("registry backup");
        if (File.Exists(file + ".hive") && FirstKey(file) is { } key && ProtectedKey(key)) return RestoreHive(key, file + ".hive");
        return await Run("reg.exe", "import", file, "/reg:64");
    }
    public async Task<CommandResult> DeleteRegistryAsync(string key)
    {
        if (!RegistryPath().IsMatch(key)) return Invalid("registry key");
        return ProtectedKey(key) ? DeleteProtected(key) : await Run("reg.exe", "delete", key, "/f", "/reg:64");
    }

    // ---- PnP lockdown entries ----
    // Windows 11 reserves PnpLockdownFiles for TrustedInstaller: administrators can read an entry but not delete it. Only keys there are
    // opened with the backup/restore privileges (the key is never re-permissioned): saved as a hive, deleted, and restored from that hive.
    internal static bool ProtectedKey(string key) => key.StartsWith(@"HKLM\" + Lockdown + @"\", StringComparison.OrdinalIgnoreCase) && key.Length > Lockdown.Length + 6;
    private static readonly UIntPtr LocalMachine = unchecked((UIntPtr)0x80000002u);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegCreateKeyExW")]
    private static extern int RegCreateKeyEx(UIntPtr key, string subKey, int reserved, string? className, int options, int access, IntPtr security, out UIntPtr result, out int disposition);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegOpenKeyExW")]
    private static extern int RegOpenKeyEx(UIntPtr key, string subKey, int options, int access, out UIntPtr result);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegSaveKeyExW")] private static extern int RegSaveKeyEx(UIntPtr key, string file, IntPtr security, int format);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegRestoreKeyW")] private static extern int RegRestoreKey(UIntPtr key, string file, int flags);
    [DllImport("advapi32.dll")] private static extern int RegCloseKey(UIntPtr key);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegEnumKeyExW")]
    private static extern int RegEnumKeyEx(UIntPtr key, int index, StringBuilder name, ref int length, IntPtr reserved, IntPtr className, IntPtr classLength, IntPtr lastWrite);
    [DllImport("ntdll.dll")] private static extern int NtDeleteKey(UIntPtr key);
    private const int BackupRestore = 0x4, Wow64Key = 0x0100;
    private static string SubKey(string key) => key[5..];
    private static bool Privileged() => EnablePrivilege("SeRestorePrivilege") && EnablePrivilege("SeBackupPrivilege");
    // RegCreateKeyEx documents that REG_OPTION_BACKUP_RESTORE grants the backup/restore access (read, write, delete) from the
    // privileges alone. A read-only probe first makes sure an existing key is opened, never a new one created by mistake.
    private static int OpenPrivileged(string key, bool create, out UIntPtr handle)
    {
        handle = UIntPtr.Zero;
        if (!create)
        {
            var probe = RegOpenKeyEx(LocalMachine, SubKey(key), 0, 0x20019 | Wow64Key, out var readable);
            if (probe == 0) RegCloseKey(readable); else if (probe != 5) return probe;
        }
        return RegCreateKeyEx(LocalMachine, SubKey(key), 0, null, BackupRestore, Wow64Key, IntPtr.Zero, out handle, out _);
    }
    private static CommandResult SaveHive(string key, string file)
    {
        if (!Privileged()) return new(5, "accessDenied");
        if (File.Exists(file)) File.Delete(file);
        if (OpenPrivileged(key, false, out var handle) is var open && open != 0) return new(open, "hiveOpenFailed");
        try { var saved = RegSaveKeyEx(handle, file, IntPtr.Zero, 2); return saved == 0 ? new(0, "hiveSaved") : new(saved, "hiveSaveFailed"); }
        finally { RegCloseKey(handle); }
    }
    private static CommandResult DeleteProtected(string key)
    {
        if (!Privileged()) return new(5, "accessDenied");
        if (OpenPrivileged(key, false, out var handle) is var open && open != 0)
            return open == 2 ? new(0, "alreadyRemoved") : new(open, "openFailed");
        try
        {
            // Entries are leaf keys; any child is removed first, the same way.
            var children = new List<string>(); var name = new StringBuilder(512);
            for (int i = 0; ; i++) { int length = name.Capacity; name.Clear(); if (RegEnumKeyEx(handle, i, name, ref length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0) break; children.Add(name.ToString()); }
            foreach (var child in children) if (DeleteProtected(key + "\\" + child) is { Success: false } failed) return failed;
            var status = NtDeleteKey(handle);
            return status == 0 ? new(0, "removed privileged") : new(5, "deleteFailed 0x" + status.ToString("X8"));
        }
        finally { RegCloseKey(handle); }
    }
    private static CommandResult RestoreHive(string key, string file)
    {
        if (!Privileged()) return new(5, "accessDenied");
        if (OpenPrivileged(key, true, out var handle) is var created && created != 0) return new(created, "hiveCreateFailed");
        try { var restored = RegRestoreKey(handle, file, 8); return restored == 0 ? new(0, "restored privileged") : new(restored, "hiveRestoreFailed"); }
        finally { RegCloseKey(handle); }
    }
    // The first key of a reg.exe export, as HKLM\… ; the backup lives in the administrator-only folder.
    private static string? FirstKey(string file)
    {
        foreach (var line in File.ReadLines(file))
            if (line.StartsWith("[HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase) && line.EndsWith(']')) return "HKLM\\" + line[20..^1];
        return null;
    }
    public async Task<CommandResult> ExportTaskAsync(string path, string file)
    {
        if (!TaskPath().IsMatch(path)) return Invalid("task");
        var split = path.LastIndexOf('\\');
        var request = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Path = path[..(split + 1)], Name = path[(split + 1)..], File = file })));
        await WindowsPlatform.PowerShellAsync("$r=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + request + "'))|ConvertFrom-Json; Export-ScheduledTask -TaskPath $r.Path -TaskName $r.Name | Set-Content -LiteralPath $r.File -Encoding Unicode");
        return File.Exists(file) ? new(0, "Task exported.") : new(1, "Task export failed.");
    }
    public async Task<CommandResult> DeleteTaskAsync(string path) => TaskPath().IsMatch(path) ? await Run("schtasks.exe", "/delete", "/tn", path, "/f") : Invalid("task");
    public async Task<CommandResult> RegisterTaskAsync(string path, string file) => TaskPath().IsMatch(path) && File.Exists(file) ? await Run("schtasks.exe", "/create", "/tn", path, "/xml", file, "/f") : Invalid("task");

    private sealed record StartupValue(string Key, string Name, string Data, bool Expand);
    public async Task<CommandResult> ExportStartupAsync(string key, string file)
    {
        var (path, name) = StartupKey(key); if (path is null) return Invalid("startup entry");
        using var run = OpenPath(path); if (run?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string data) return new(2, "Startup entry not found.");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new StartupValue(path, name, data, run.GetValueKind(name) == RegistryValueKind.ExpandString)));
        return new(0, "Startup entry exported.");
    }
    public Task<CommandResult> DeleteStartupAsync(string key)
    {
        var (path, name) = StartupKey(key); if (path is null) return Task.FromResult(Invalid("startup entry"));
        using var run = OpenPath(path, true); run?.DeleteValue(name, false);
        return Task.FromResult(new CommandResult(0, "Startup entry removed."));
    }
    public async Task<CommandResult> RestoreStartupAsync(string file)
    {
        var value = JsonSerializer.Deserialize<StartupValue>(await File.ReadAllTextAsync(file));
        if (value is null || !RunKeys.Contains(value.Key, StringComparer.OrdinalIgnoreCase) || value.Name.Length == 0) return Invalid("startup backup");
        using var run = OpenPath(value.Key, true) ?? throw new IOException("Startup key unavailable.");
        run.SetValue(value.Name, value.Data, value.Expand ? RegistryValueKind.ExpandString : RegistryValueKind.String);
        return new(0, "Startup entry restored.");
    }
    private static (string? Path, string Name) StartupKey(string key)
    {
        var split = key.IndexOf('|'); if (split < 0) return (null, "");
        var path = key[..split]; return RunKeys.Contains(path, StringComparer.OrdinalIgnoreCase) ? (path, key[(split + 1)..]) : (null, "");
    }

    public async Task<CommandResult> RemoveAppxAsync(string name)
    {
        if (!VendorProfiles.All.SelectMany(p => p.Appx).Contains(name, StringComparer.Ordinal)) return Invalid("Store app");
        await WindowsPlatform.PowerShellAsync("$n='" + name + "'; Get-AppxPackage -AllUsers -Name $n | Remove-AppxPackage -AllUsers; Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq $n | Remove-AppxProvisionedPackage -Online | Out-Null", 600);
        return new(0, "Store app removed for all users.");
    }

    public async Task<CommandResult> StopProcessAsync(int id, string path)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            if (!ImagePath(id).Equals(path, StringComparison.OrdinalIgnoreCase)) return new(0, "Process already exited.");
            // A program with a window is asked to close first, the way the person would close it; only what stays is ended.
            if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow())
            {
                using var polite = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await process.WaitForExitAsync(polite.Token); return new(0, "Closed " + Path.GetFileName(path)); }
                catch (OperationCanceledException) { }
            }
            process.Kill(false);
            await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);
            return new(0, "Stopped " + Path.GetFileName(path));
        }
        catch (ArgumentException) { return new(0, "Process already exited."); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) { return new(1, e.Message); }
    }

    // ---- Vendor files in System32/SysWOW64 ----
    // A plain move keeps the file's own permissions. Files that Windows protects for the PnP manager are renamed with the
    // administrator's restore privilege instead; their ACL is never rewritten. The PnP lockdown entry is saved and removed with the file.
    private const string Lockdown = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\PnpLockdownFiles";
    public async Task<CommandResult> QuarantineFileAsync(string path, string destination)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (DeepCleanPlanner.SystemRelative(windows, path) is null || !File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || File.Exists(destination))
            return Invalid("file");
        var moved = MoveFile(path, destination);
        if (!moved.Success) return moved;
        var entry = FindLockdown(windows, path); var note = "";
        if (entry is not null)
        {
            var file = Path.Combine(Path.GetDirectoryName(destination)!, "lockdown.reg"); var key = @"HKLM\" + Lockdown + @"\" + entry;
            var export = await ExportRegistryAsync(key, file);
            note = export.Success && File.Exists(file) && (await DeleteRegistryAsync(key)).Success ? " lockdownSaved" : " lockdownKept";
        }
        return new(0, moved.Output + note);
    }
    public async Task<CommandResult> RestoreFileAsync(string backup, string origin)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (DeepCleanPlanner.SystemRelative(windows, origin) is null || File.Exists(origin) || !File.Exists(backup)) return Invalid("file backup");
        var moved = MoveFile(backup, origin);
        var lockdown = Path.Combine(Path.GetDirectoryName(backup)!, "lockdown.reg");
        if (moved.Success && File.Exists(lockdown)) await ImportRegistryAsync(lockdown);
        return moved;
    }
    // Lockdown entries are subkeys named after the file, for example "%SystemRoot%/System32/name.dll".
    private static string? FindLockdown(string windows, string path)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.OpenSubKey(Lockdown); if (key is null) return null;
            var full = Path.GetFullPath(path);
            return key.GetSubKeyNames().FirstOrDefault(n => string.Equals(Environment.ExpandEnvironmentVariables(n.Replace('/', '\\')), full, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is IOException or System.Security.SecurityException or UnauthorizedAccessException) { return null; }
    }
    private static CommandResult MoveFile(string source, string destination)
    {
        try { File.Move(source, destination); return new(0, "moved"); }
        catch (UnauthorizedAccessException) { return PrivilegedRename(source, destination); }
        catch (IOException e) { return new(1, "inUse " + e.Message); }
    }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LookupPrivilegeValueW")] private static extern bool LookupPrivilege(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivilege state, int length, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, int infoClass, IntPtr info, int size);
    [StructLayout(LayoutKind.Sequential, Pack = 4)] private struct TokenPrivilege { public int Count; public long Luid; public int Attributes; }
    private static bool EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out var token)) return false;
        try { if (!LookupPrivilege(null, name, out var luid)) return false; var state = new TokenPrivilege { Count = 1, Luid = luid, Attributes = 2 }; return AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0; }
        finally { CloseHandle(token); }
    }
    private static CommandResult PrivilegedRename(string source, string destination)
    {
        if (!EnablePrivilege("SeRestorePrivilege") || !EnablePrivilege("SeBackupPrivilege")) return new(5, "accessDenied");
        // DELETE | SYNCHRONIZE with backup semantics; the link is renamed, never followed.
        using var handle = CreateFile(source, 0x00010000 | 0x00100000, 7, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) return new(Marshal.GetLastWin32Error(), "accessDenied");
        var name = Encoding.Unicode.GetBytes(Path.GetFullPath(destination));
        int size = 20 + name.Length + 2; var buffer = Marshal.AllocHGlobal(size);
        try
        {
            for (int i = 0; i < size; i++) Marshal.WriteByte(buffer, i, 0);
            Marshal.WriteInt32(buffer, 16, name.Length); Marshal.Copy(name, 0, buffer + 20, name.Length);
            return SetFileInformationByHandle(handle, 3, buffer, size) ? new(0, "moved privileged") : new(Marshal.GetLastWin32Error(), "renameFailed");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    // ---- Khronos registrations ----
    private sealed record ValueBackup(string Key, string Name, RegistryValueKind Kind, string Data);
    private static bool Khronos(string key) => KhronosKeys.Contains(key, StringComparer.OrdinalIgnoreCase);
    public async Task<CommandResult> ExportValueAsync(string key, string name, string file)
    {
        if (!Khronos(key) || name.Length == 0) return Invalid("registration");
        using var registry = OpenPath(key); if (registry?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } value) return new(2, "Registration not found.");
        var data = value switch { byte[] bytes => Convert.ToBase64String(bytes), string[] lines => JsonSerializer.Serialize(lines), _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "" };
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new ValueBackup(key, name, registry.GetValueKind(name), data)));
        return new(0, "Registration exported.");
    }
    public Task<CommandResult> DeleteValueAsync(string key, string name)
    {
        if (!Khronos(key) || name.Length == 0) return Task.FromResult(Invalid("registration"));
        using var registry = OpenPath(key, true); registry?.DeleteValue(name, false);
        return Task.FromResult(new CommandResult(0, "Registration removed."));
    }
    public async Task<CommandResult> RestoreValueAsync(string file)
    {
        var value = JsonSerializer.Deserialize<ValueBackup>(await File.ReadAllTextAsync(file));
        if (value is null || !Khronos(value.Key) || value.Name.Length == 0) return Invalid("registration backup");
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var registry = root.CreateSubKey(value.Key[5..], true);
        object data = value.Kind switch
        {
            RegistryValueKind.DWord => int.Parse(value.Data, System.Globalization.CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => long.Parse(value.Data, System.Globalization.CultureInfo.InvariantCulture),
            RegistryValueKind.Binary => Convert.FromBase64String(value.Data),
            RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(value.Data) ?? [],
            _ => value.Data
        };
        registry.SetValue(value.Name, data, value.Kind);
        return new(0, "Registration restored.");
    }

    // Official uninstallers only from machine-wide registrations whose program lives in an administrator-only folder.
    public async Task<CommandResult> RunUninstallerAsync(string key)
    {
        if (!UninstallKeys.Take(2).Any(k => key.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase))) return Invalid("uninstaller");
        using var app = OpenPath(key); if (app is null) return new(2, "Application not found.");
        var name = key[(key.LastIndexOf('\\') + 1)..];
        ProcessStartInfo info;
        if (app.GetValue("WindowsInstaller") is int msi && msi == 1 && Guid.TryParse(name, out var product))
        {
            info = new(SystemExe("msiexec.exe")) { UseShellExecute = false };
            info.ArgumentList.Add("/x"); info.ArgumentList.Add(product.ToString("B")); info.ArgumentList.Add("/norestart");
        }
        else
        {
            var command = app.GetValue("UninstallString") as string ?? "";
            var files = CommandFiles.Parse(command);
            var exe = Regex.Match(Environment.ExpandEnvironmentVariables(command).Trim(), @"\A(""(?<p>[^""]+)""|(?<p>\S+\.exe))(?<a>.*)\z", RegexOptions.IgnoreCase);
            var program = exe.Success ? exe.Groups["p"].Value : "";
            var trusted = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.Windows }.Select(Environment.GetFolderPath);
            if (program.Length == 0 || !File.Exists(program) || !trusted.Any(t => DeepCleanPlanner.Within(program, t)) || files.Count == 0) return new(5, "Uninstaller outside a protected folder; use Windows Settings.");
            info = new(program, exe.Groups["a"].Value.Trim()) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(program)! };
        }
        using var process = Process.Start(info) ?? throw new IOException("Uninstaller did not start.");
        await process.WaitForExitAsync();
        return new(process.ExitCode, "Official uninstaller closed. Code " + process.ExitCode);
    }

    // Backups that can restore services or registry data are written only to an administrator-only folder.
    public static string SecureRoot() => SecureFolder("Cleanup", false);

    // ---- Releasing cleanup backups ----
    // Backups and quarantine stay until the person releases them; afterwards those cleanups can no longer be undone.
    // Only session folders are touched, links are deleted as links, and files moved there with the restore privilege
    // (for example a protected System32 module) are deleted with that privilege, never re-permissioned.
    public static long BackupBytes(string root) => Sessions(root).Sum(Measure);
    public static (int Sessions, long Bytes, int Locked) DiscardBackups(string root)
    {
        int sessions = 0, locked = 0; long bytes = 0;
        foreach (var session in Sessions(root))
        {
            var size = Measure(session); var failed = DeleteTree(session);
            if (failed == 0) { sessions++; bytes += size; } else locked += failed;
        }
        return (sessions, bytes, locked);
    }
    private static string[] Sessions(string root) => Directory.Exists(root)
        ? Directory.GetDirectories(root).Where(d => DeepCleanEngine.ValidSession(Path.GetFileName(d)) && (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0).ToArray() : [];
    private static int DeleteTree(string folder)
    {
        int failed = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder).ToArray())
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0) failed += DeleteTree(entry);
            else if (!DeleteEntry(entry, attributes)) failed++;
        }
        return failed + (DeleteEntry(folder, File.GetAttributes(folder)) ? 0 : 1);
    }
    private static bool DeleteEntry(string path, FileAttributes attributes)
    {
        try
        {
            if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(path, false); else File.Delete(path);
            return true;
        }
        catch (UnauthorizedAccessException) { return PrivilegedDelete(path); }
        catch (IOException) { return false; }
    }
    private static bool PrivilegedDelete(string path)
    {
        if (!Privileged()) return false;
        // DELETE with backup semantics; a link is opened as itself, never followed.
        using var handle = CreateFile(path, 0x00010000, 7, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) return false;
        var flag = Marshal.AllocHGlobal(4);
        try { Marshal.WriteInt32(flag, 1); return SetFileInformationByHandle(handle, 4, flag, 4); }
        finally { Marshal.FreeHGlobal(flag); }
    }

    // Folders under %ProgramData%\Swap that only administrators and SYSTEM can change; users may get read access.
    internal static string SecureFolder(string name, bool usersRead)
    {
        var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Swap");
        var root = Path.Combine(parent, name);
        Directory.CreateDirectory(parent);
        if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked Swap data folder is not allowed.");
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { administrators, system })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        if (usersRead)
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var info = new DirectoryInfo(root);
        if (!info.Exists) info.Create(security);
        else
        {
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked Swap folder is not allowed.");
            var owner = info.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
            if (owner != administrators && owner != system) throw new IOException("A Swap folder has an unexpected owner. Review " + root + ".");
            info.SetAccessControl(security);
        }
        return root;
    }
}
