using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Swap.Core;
namespace Swap.Windows;

public sealed partial class WindowsPlatform : IPlatform
{
    private static string SystemExe(string name) => Path.Combine(Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative") : Environment.GetFolderPath(Environment.SpecialFolder.System), name);
    public static async Task<CommandResult> RunAsync(string exe, IEnumerable<string> args, int timeoutSeconds = 180)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileName(exe).Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            // PowerShell 7 can leak Core-only modules into a Windows PowerShell 5.1 child.
            // Resolve only the Windows module locations, independently of the launching shell.
            info.Environment["PSModulePath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\Modules") + ";" +
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"WindowsPowerShell\Modules");
        }
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("No se pudo iniciar " + exe);
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            // Never kill an installer / servicing operation. Wait until Windows finishes; no second operation may start.
            await process.WaitForExitAsync();
            throw new TimeoutException("La operación excedió el tiempo previsto; terminó con código " + process.ExitCode + ". Revisa el estado antes de continuar.");
        }
        return new(process.ExitCode, (await output + "\n" + await error).Trim());
    }
    public static async Task<string> PowerShellAsync(string script, int timeout = 180)
    {
        var preamble = "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; try { ";
        var result = await RunAsync(SystemExe("WindowsPowerShell\\v1.0\\powershell.exe"),
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(preamble + script + "\n} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }"))], timeout);
        if (result.Code != 0) throw new InvalidOperationException(result.Output);
        return result.Output;
    }
    internal static string Script(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Swap.Windows.Scripts." + name + ".ps1")!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    public static bool IsKnownRemoteTool(string name) => name.StartsWith("TeamViewer", StringComparison.OrdinalIgnoreCase) ||
        new[] { "AnyDesk", "rustdesk", "remoting_host", "parsecd" }.Contains(name, StringComparer.OrdinalIgnoreCase);
    public async Task<Snapshot> ScanAsync()
    {
        // Read-only: a slow PC may need minutes; a late but complete inventory is still valid.
        var snapshot = JsonSerializer.Deserialize<Snapshot>(await PowerShellAsync(Script("Scan"), 600)) ?? throw new IOException("Inventario vacío.");
        var processes = Process.GetProcesses();
        try
        {
            var remote = false;
            foreach (var process in processes)
            {
                try { remote |= IsKnownRemoteTool(process.ProcessName); }
                catch (InvalidOperationException) { /* An exited process is no longer a remote-control tool. */ }
            }
            return snapshot with { Remote = snapshot.Remote || remote };
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    public async Task<bool> VerifyGraphicsAsync(Adapter adapter)
    {
        var diagnostics = await GraphicsInspector.ReadAsync();
        return GraphicsHealth.Evaluate(adapter, diagnostics).Any(f => f.Code == "direct3dOk");
    }
    private sealed record Signature(bool Valid, string Subject, string Status);
    private static async Task<Signature> SignatureAsync(string path) =>
        JsonSerializer.Deserialize<Signature>(await PowerShellAsync(Script("Signature").Replace("__PATH__", Convert.ToBase64String(Encoding.UTF8.GetBytes(path)))))!;
    [GeneratedRegex(@"^\s*(?<key>[A-Za-z0-9_.]+)\s*=\s*(?<value>[^;\r\n]+)", RegexOptions.Multiline)]
    private static partial Regex Entries();
    [GeneratedRegex(@"PCI\\VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}(?:&(?:SUBSYS_[0-9A-F]{8}|REV_[0-9A-F]{2}))*", RegexOptions.IgnoreCase)]
    private static partial Regex Hardware();
    public async Task<Package> InspectAsync(string path, Vendor target)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Elige un archivo local existente, sin enlaces simbólicos.");
        bool exe = Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
        if (!exe && !Path.GetExtension(path).Equals(".inf", StringComparison.OrdinalIgnoreCase)) throw new IOException("Selecciona un instalador EXE oficial o un INF extraído.");
        var vendorName = target == Vendor.Nvidia ? "NVIDIA" : target == Vendor.Intel ? "Intel" : "Advanced Micro Devices";
        if (exe)
        {
            var sig = await SignatureAsync(path);
            if (!sig.Valid || !sig.Subject.Contains(vendorName, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Instalador sin firma válida del fabricante seleccionado: " + sig.Status);
            var metadata = FileVersionInfo.GetVersionInfo(path);
            var product = metadata.ProductName + " " + metadata.FileDescription;
            bool known = target == Vendor.Intel ? product.Contains("Graphics", StringComparison.OrdinalIgnoreCase) : target == Vendor.Nvidia ? product.Contains("NVIDIA Package Launcher", StringComparison.OrdinalIgnoreCase) || product.Contains("NVIDIA Installer", StringComparison.OrdinalIgnoreCase) :
                product.Contains("AMD Software", StringComparison.OrdinalIgnoreCase) || product.Contains("Radeon Software", StringComparison.OrdinalIgnoreCase);
            if (!known || new[] { "Cleanup", "Chipset", "Ryzen", "CUDA" }.Any(word => product.Contains(word, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Los metadatos no identifican un instalador de gráficos reconocido. Usa el paquete oficial de GPU o un INF extraído.");
            return new(path, target, "", [], await HashFileAsync(path), true, true);
        }
        var text = await File.ReadAllTextAsync(path);
        var entries = Entries().Matches(text).Cast<Match>().ToArray();
        string Value(string name) => entries.FirstOrDefault(m => m.Groups["key"].Value.Equals(name, StringComparison.OrdinalIgnoreCase))?.Groups["value"].Value.Trim().Trim('"') ?? "";
        if (!Value("ClassGUID").Equals(Planner.DisplayGuid, StringComparison.OrdinalIgnoreCase) || !Value("Class").Equals("Display", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Solo se permiten paquetes de clase Display.");
        var catalogName = entries.FirstOrDefault(m => m.Groups["key"].Value.StartsWith("CatalogFile", StringComparison.OrdinalIgnoreCase))?.Groups["value"].Value.Trim().Trim('"') ?? "";
        if (catalogName.Length == 0 || catalogName != Path.GetFileName(catalogName) || catalogName.Contains('/') || catalogName.Contains('\\'))
            throw new IOException("El INF no declara un catálogo local válido.");
        var catalog = Path.Combine(Path.GetDirectoryName(path)!, catalogName);
        var signature = await SignatureAsync(catalog);
        if (!signature.Valid || !(signature.Subject.Contains("Microsoft Windows Hardware Compatibility", StringComparison.OrdinalIgnoreCase) || signature.Subject.Contains(vendorName, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Catálogo sin firma de confianza del fabricante o Microsoft: " + signature.Status);
        var ids = Hardware().Matches(text).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length == 0 || ids.Any(id => Vendors.FromId(id) != target)) throw new IOException("El INF mezcla fabricantes o no identifica la GPU de destino.");
        var version = Value("DriverVer").Split(',').Last().Trim();
        if (!Version.TryParse(version, out _)) throw new IOException("Versión de driver no válida.");
        return new(path, target, version, ids, await HashTreeAsync(Path.GetDirectoryName(path)!), true, false);
    }
    public static async Task<string> HashFileAsync(string path)
    {
        using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
    public static async Task<string> HashTreeAsync(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in SafeFiles(directory).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, file).Replace('\\','/') + "\0"));
            hash.AppendData(Convert.FromHexString(await HashFileAsync(file)));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static IEnumerable<string> SafeFiles(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("No se aceptan carpetas enlazadas.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("El paquete contiene un enlace: " + Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0) { foreach (var file in SafeFiles(entry)) yield return file; }
            else yield return entry;
        }
    }
    public Task<CommandResult> ExportAsync(string inf, string destination)
    {
        if (!Planner.IsOem(inf)) throw new IOException("Nombre OEM inválido.");
        return RunAsync(SystemExe("pnputil.exe"), ["/export-driver", inf, destination], 600);
    }
    public async Task<CommandResult> InstallAsync(Package package)
    {
        // Deny writes/deletion of all package files, then revalidate immediately before use.
        var locks = new List<FileStream>();
        try
        {
            foreach (var file in package.IsExe ? [package.Path] : SafeFiles(Path.GetDirectoryName(package.Path)!))
                locks.Add(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read));
            var current = await InspectAsync(package.Path, package.Vendor);
            if (current.Sha256 != package.Sha256) throw new IOException("El paquete cambió antes de instalar. Se detuvo la operación.");
            if (!package.IsExe) return await RunAsync(SystemExe("pnputil.exe"), ["/add-driver", package.Path, "/install"], 1800);
            using var process = Process.Start(new ProcessStartInfo(package.Path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(package.Path)! })
                ?? throw new IOException("No se pudo abrir el instalador.");
            await process.WaitForExitAsync();
            return new(process.ExitCode, "Instalador oficial cerrado. Código " + process.ExitCode + ". Se comprobará el driver activo; los instaladores que lanzan otro proceso pueden requerir una revisión posterior.");
        }
        finally { foreach (var stream in locks) stream.Dispose(); }
    }
    public Task<CommandResult> DeleteAsync(string inf)
    {
        if (!Planner.IsOem(inf)) throw new IOException("Nombre OEM inválido.");
        // No /force, /uninstall, /reboot or wildcards. A package still used by a device is refused by Windows.
        return RunAsync(SystemExe("pnputil.exe"), ["/delete-driver", inf], 600);
    }
    // Only disconnected devices are passed here; WindowsCleanupPlatform validates the instance ID format.
    public Task<CommandResult> RemoveDeviceAsync(string instanceId) => WindowsCleanupPlatform.Present(instanceId)
        ? Task.FromResult(new CommandResult(1, "The device is connected; it is not removed."))
        : new WindowsCleanupPlatform().RemoveDeviceAsync(instanceId);
    public async Task<string> RestorePointAsync()
    {
        try
        {
            await PowerShellAsync("$ErrorActionPreference='Stop'; Checkpoint-Computer -Description 'Swap GPU - before driver change' -RestorePointType MODIFY_SETTINGS -WarningAction Stop", 300);
            return "Windows aceptó la solicitud de punto de restauración. El respaldo de paquetes es independiente.";
        }
        catch (Exception e) { return "Punto de restauración no disponible: " + e.Message + ". Se conserva el respaldo de drivers; no equivale a una copia del sistema."; }
    }
}
