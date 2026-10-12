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
        return new(ps.MachineId, cpuVendor, cpuName, ps.Admin, ps.Laptop, devices, packages, Services(), active, tasks, Startup(), Apps(), ps.Appx ?? [],
            await Task.Run(Folders, token), RegistryKeys(), Processes(), ps.Complete && ps.Admin, warnings);
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
        var folder = Path.GetDirectoryName(p.Original) ?? ""; IReadOnlyList<string> ids = []; long bytes = 0; var provider = p.Provider; var cls = p.Class;
        try
        {
            var store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DriverStore", "FileRepository");
            if (folder.Length > 0 && DeepCleanPlanner.Within(folder, store) && File.Exists(p.Original) && new FileInfo(p.Original).Length < 32 * 1024 * 1024)
            {
                var summary = InfReader.Read(File.ReadAllText(p.Original));
                ids = summary.HardwareIds;
                if (provider.Length == 0) provider = summary.Provider;
                if (cls.Length == 0) cls = summary.Class;
                bytes = Measure(folder);
            }
            else folder = "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return new(p.Inf, Path.GetFileName(p.Original), provider, cls, p.Version, p.BootCritical, folder, bytes, ids);
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
                var files = CommandFiles.Parse(command);
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
    public async Task<CommandResult> ExportRegistryAsync(string key, string file) => RegistryPath().IsMatch(key) ? await Run("reg.exe", "export", key, file, "/y", "/reg:64") : Invalid("registry key");
    public async Task<CommandResult> ImportRegistryAsync(string file) => File.Exists(file) && file.EndsWith(".reg", StringComparison.OrdinalIgnoreCase) ? await Run("reg.exe", "import", file, "/reg:64") : Invalid("registry backup");
    public async Task<CommandResult> DeleteRegistryAsync(string key) => RegistryPath().IsMatch(key) ? await Run("reg.exe", "delete", key, "/f", "/reg:64") : Invalid("registry key");
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
            process.Kill(false);
            await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);
            return new(0, "Stopped " + Path.GetFileName(path));
        }
        catch (ArgumentException) { return new(0, "Process already exited."); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) { return new(1, e.Message); }
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
    public static string SecureRoot()
    {
        var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Swap");
        var root = Path.Combine(parent, "Cleanup");
        Directory.CreateDirectory(parent);
        if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked Swap data folder is not allowed.");
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { administrators, system })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var info = new DirectoryInfo(root);
        if (!info.Exists) info.Create(security);
        else
        {
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked cleanup folder is not allowed.");
            var owner = info.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
            if (owner != administrators && owner != system) throw new IOException("The cleanup backup folder has an unexpected owner. Review " + root + ".");
            info.SetAccessControl(security);
        }
        return root;
    }
}
