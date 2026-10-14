// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Swap.Core;

public interface ICleanupPlatform
{
    bool Simulated => false;
    Task<RemnantInventory> InventoryAsync(CancellationToken token = default);
    Task<string> RestorePointAsync();
    Task<CommandResult> StopProcessAsync(int id, string path);
    Task<CommandResult> RunUninstallerAsync(string key);
    Task<CommandResult> RemoveDeviceAsync(string instanceId);
    Task<CommandResult> ExportPackageAsync(string inf, string destination);
    Task<CommandResult> DeletePackageAsync(string inf);
    Task<CommandResult> AddPackageAsync(string infPath);
    Task<CommandResult> StopServiceAsync(string name);
    Task<CommandResult> DeleteServiceAsync(string name);
    Task<CommandResult> ExportRegistryAsync(string key, string file);
    Task<CommandResult> ImportRegistryAsync(string file);
    Task<CommandResult> DeleteRegistryAsync(string key);
    Task<CommandResult> ExportTaskAsync(string path, string file);
    Task<CommandResult> DeleteTaskAsync(string path);
    Task<CommandResult> RegisterTaskAsync(string path, string file);
    Task<CommandResult> ExportStartupAsync(string key, string file);
    Task<CommandResult> DeleteStartupAsync(string key);
    Task<CommandResult> RestoreStartupAsync(string file);
    Task<CommandResult> RemoveAppxAsync(string name);
    // Windows folder whose System32/SysWOW64 may hold vendor files; the demo uses its sandbox.
    string WindowsRoot => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    // Windows may already have removed a key together with the package or file it described.
    bool KeyExists(string key) => true;
    bool KeyEmpty(string key) => false;
    Task<CommandResult> QuarantineFileAsync(string path, string destination);
    Task<CommandResult> RestoreFileAsync(string backup, string origin);
    Task<CommandResult> ExportValueAsync(string key, string name, string file);
    Task<CommandResult> DeleteValueAsync(string key, string name);
    Task<CommandResult> RestoreValueAsync(string file);
    // Processes running right now, for vendor programs restarted after the plan was made.
    Task<IReadOnlyList<ProcessEntry>> RunningProcessesAsync() => Task.FromResult<IReadOnlyList<ProcessEntry>>([]);
}

public sealed record CleanupResult(string Key, RemnantKind Kind, Vendor Vendor, string Target, string Label, string Status, string Backup, string Message, long Bytes);
public sealed record CleanupOutcome(string Session, IReadOnlyList<CleanupResult> Results, bool RestartRecommended)
{
    public int Done => Results.Count(r => r.Status == "done");
    public int Failed => Results.Count(r => r.Status == "failed");
    public long Freed => Results.Where(r => r.Status == "done").Sum(r => r.Bytes);
}

// KraicDev. Every reversible change is backed up first in an administrator-only folder. A stopped item never blocks the rest.
public sealed partial class DeepCleanEngine(ICleanupPlatform platform)
{
    [GeneratedRegex(@"\A\d{8}-\d{6}-CLEAN-[0-9a-f]{6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SessionName();
    public static bool ValidSession(string name) => SessionName().IsMatch(name);
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public async Task<CleanupOutcome> RunAsync(DeepCleanPlan plan, IReadOnlySet<string> selected, string session, string secureRoot,
        Action<string> log, CancellationToken token = default, Action<string>? stage = null, Action<int, int, string>? progress = null)
    {
        var name = Path.GetFileName(Path.GetFullPath(session).TrimEnd('\\'));
        if (!ValidSession(name)) throw new IOException("Invalid cleanup session name.");
        if (Directory.Exists(session)) throw new IOException("A new session directory is required.");
        var secure = Path.Combine(secureRoot, name);
        if (Directory.Exists(secure)) throw new IOException("Cleanup backup destination already exists.");
        Directory.CreateDirectory(session); Directory.CreateDirectory(secure);
        using var sessionLock = new FileStream(Path.Combine(session, "session.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        var results = new List<CleanupResult>(); bool restart = false;
        void Log(string text) { File.AppendAllText(Path.Combine(session, "journal.jsonl"), JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, text }) + "\n"); log(text); }
        void Save(string folder, string file, object value) { var path = Path.Combine(folder, file); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, Indented)); File.Move(path + ".tmp", path, true); }
        void Record(RemnantItem item, string status, string backup, string message, long bytes = 0)
        {
            results.Add(new(item.Key, item.Kind, item.Vendor, item.Target, item.Label, status, backup, message.Length > 600 ? message[..600] : message, bytes));
            // The undo manifest lives with the backups; the user-writable copy is only informational.
            Save(secure, "undo.json", results); Save(session, "cleanup-results.json", results);
        }
        try
        {
            stage?.Invoke("backup"); token.ThrowIfCancellationRequested();
            Save(session, "state.json", new { phase = "cleanup-backup", completed = false, cleanup = true });
            Log("Validando de nuevo el inventario antes de limpiar…");
            var inventory = await platform.InventoryAsync(token);
            if (inventory.MachineId != plan.MachineId) throw new InvalidOperationException("El plan corresponde a otro equipo.");
            var fresh = DeepCleanPlanner.Refresh(inventory, plan);
            if (!plan.Versions && fresh.Vendors.Count != plan.Vendors.Count) throw new InvalidOperationException("Una marca de la limpieza volvió a estar conectada. No se modificó nada.");
            // Only items that are still eligible right now run. Processes and devices follow the work that depends on them.
            var chosen = fresh.Items.Where(i => selected.Contains(i.Key)).ToList();
            var packageInfs = chosen.Where(i => i.Kind == RemnantKind.Package).Select(i => i.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var device in fresh.Items.Where(i => i.Kind == RemnantKind.Device && !selected.Contains(i.Key)))
                if (inventory.Devices.Any(d => d.Id.Equals(device.Target, StringComparison.OrdinalIgnoreCase) && packageInfs.Contains(d.Inf))) chosen.Add(device with { Reason = "packageDependency" });
            var roots = chosen.Where(i => i.Kind is RemnantKind.Folder or RemnantKind.Package).Select(i => i.Kind == RemnantKind.Package ? i.Detail : i.Target).Where(r => r.Length > 0).ToArray();
            var files = chosen.Where(i => i.Kind == RemnantKind.File).Select(i => i.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
            chosen.RemoveAll(i => i.Kind == RemnantKind.Process);
            chosen.AddRange(fresh.Items.Where(i => i.Kind == RemnantKind.Process && (roots.Any(r => DeepCleanPlanner.Within(i.Detail, r)) || files.Contains(i.Detail))));
            Save(session, "cleanup-plan.json", new { author = "KraicDev", simulated = platform.Simulated, plan = fresh, selected = chosen.Select(i => i.Key) });
            Save(secure, "plan.json", fresh);
            Log(await platform.RestorePointAsync());
            Save(session, "state.json", new { phase = "cleanup", completed = false, cleanup = true });
            stage?.Invoke("cleanup");
            var order = new[] { RemnantKind.Process, RemnantKind.Uninstaller, RemnantKind.Device, RemnantKind.Package, RemnantKind.Service, RemnantKind.Task,
                RemnantKind.Startup, RemnantKind.Appx, RemnantKind.Folder, RemnantKind.File, RemnantKind.Cache, RemnantKind.Registration, RemnantKind.Registry, RemnantKind.App };
            var failedDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase); int index = 0;
            // Stop services before ending processes: a killed service process could be restarted by its recovery policy.
            foreach (var service in chosen.Where(i => i.Kind == RemnantKind.Service)) await platform.StopServiceAsync(service.Target);
            var ordered = chosen.OrderBy(i => Array.IndexOf(order, i.Kind)).ToArray(); int position = 0;
            foreach (var item in ordered)
            {
                token.ThrowIfCancellationRequested();
                progress?.Invoke(position++, ordered.Length, item.Label);
                var backup = Path.Combine(secure, "items", (index++).ToString("D4"));
                try
                {
                    switch (item.Kind)
                    {
                        case RemnantKind.Process:
                        {
                            var r = await platform.StopProcessAsync(int.Parse(item.Key[(item.Key.IndexOf(':') + 1)..]), item.Detail);
                            Record(item, r.Success ? "done" : "failed", "", r.Output); break;
                        }
                        case RemnantKind.Uninstaller:
                        {
                            Log("Abriendo el desinstalador oficial: " + item.Label);
                            var r = await platform.RunUninstallerAsync(item.Target); restart |= r.Code == 3010;
                            Record(item, r.Success ? "done" : "failed", "", r.Output); break;
                        }
                        case RemnantKind.Device:
                        {
                            Log("Retirando dispositivo sin hardware: " + item.Label);
                            var r = await platform.RemoveDeviceAsync(item.Target); restart |= r.Code == 3010;
                            if (!r.Success) failedDevices.Add(item.Target);
                            Record(item, r.Success ? "done" : "failed", "", r.Output); break;
                        }
                        case RemnantKind.Package:
                        {
                            if (inventory.Devices.Any(d => d.Inf.Equals(item.Target, StringComparison.OrdinalIgnoreCase) && failedDevices.Contains(d.Id)))
                            { Record(item, "skipped", "", "dependentDeviceFailed"); break; }
                            Directory.CreateDirectory(backup);
                            var export = await platform.ExportPackageAsync(item.Target, backup);
                            if (!export.Success || !Directory.EnumerateFiles(backup, "*.inf", SearchOption.AllDirectories).Any()) { Record(item, "skipped", "", "backupFailed " + export.Output); break; }
                            await BackupIntegrity.SealAsync(backup, token);
                            Log("Retirando paquete " + item.Label);
                            var r = await platform.DeletePackageAsync(item.Target); restart |= r.Code == 3010;
                            Record(item, r.Success ? "done" : "failed", Rel(secure, backup), r.Output, r.Success ? item.Bytes : 0); break;
                        }
                        case RemnantKind.Service:
                        {
                            var key = @"HKLM\SYSTEM\CurrentControlSet\Services\" + item.Target;
                            Directory.CreateDirectory(backup); var file = Path.Combine(backup, "service.reg");
                            var export = await platform.ExportRegistryAsync(key, file);
                            if (!export.Success || !File.Exists(file)) { Record(item, "skipped", "", "backupFailed " + export.Output); break; }
                            await platform.StopServiceAsync(item.Target);
                            var r = await platform.DeleteServiceAsync(item.Target); restart |= r.Code is 3010 or 1072;
                            Record(item, r.Success || r.Code == 1072 ? "done" : "failed", Rel(secure, file), r.Output); break;
                        }
                        case RemnantKind.Task:
                        {
                            Directory.CreateDirectory(backup); var file = Path.Combine(backup, "task.xml");
                            var export = await platform.ExportTaskAsync(item.Target, file);
                            if (!export.Success || !File.Exists(file)) { Record(item, "skipped", "", "backupFailed " + export.Output); break; }
                            var r = await platform.DeleteTaskAsync(item.Target);
                            Record(item, r.Success ? "done" : "failed", Rel(secure, file), r.Output); break;
                        }
                        case RemnantKind.Startup:
                        {
                            Directory.CreateDirectory(backup); var file = Path.Combine(backup, "startup.json");
                            var export = await platform.ExportStartupAsync(item.Target, file);
                            if (!export.Success || !File.Exists(file)) { Record(item, "skipped", "", "backupFailed " + export.Output); break; }
                            var r = await platform.DeleteStartupAsync(item.Target);
                            Record(item, r.Success ? "done" : "failed", Rel(secure, file), r.Output); break;
                        }
                        case RemnantKind.Appx:
                        {
                            var r = await platform.RemoveAppxAsync(item.Target);
                            Record(item, r.Success ? "done" : "failed", "", r.Output); break;
                        }
                        case RemnantKind.Folder:
                        {
                            var target = Path.Combine(backup, "content");
                            if (!SafeFolder(item.Target)) { Record(item, "skipped", "", "unsafePath"); break; }
                            if (!string.Equals(Path.GetPathRoot(Path.GetFullPath(item.Target)), Path.GetPathRoot(Path.GetFullPath(secure)), StringComparison.OrdinalIgnoreCase))
                            { Record(item, "skipped", "", "otherVolume"); break; }
                            Directory.CreateDirectory(backup);
                            Log("Moviendo a cuarentena " + item.Target);
                            var (moved, locked) = await MoveFolderAsync(item.Target, target, roots.Append(secure).ToArray(), Log, token);
                            if (moved == 0 && locked.Count > 0) { Record(item, "failed", "", "inUse " + string.Join(", ", locked.Take(8))); break; }
                            await File.WriteAllTextAsync(Path.Combine(backup, "origin.txt"), item.Target, token);
                            // The move already happened: a sealing problem must not hide the folder from undo.
                            string seal = "";
                            try { await BackupIntegrity.SealAsync(target, token, false); }
                            catch (IOException e) { seal = "unsealed " + e.Message; }
                            // Whatever stayed open is listed; undo puts the moved part back next to it.
                            if (locked.Count > 0) Record(item, "partial", Rel(secure, target), ("inUse " + string.Join(", ", locked.Take(8)) + " " + seal).Trim(), 0);
                            else Record(item, "done", Rel(secure, target), seal, item.Bytes);
                            break;
                        }
                        case RemnantKind.File:
                        {
                            if (DeepCleanPlanner.SystemRelative(platform.WindowsRoot, item.Target) is null || DeepCleanPlanner.SharedRuntime(item.Target)) { Record(item, "skipped", "", "unsafePath"); break; }
                            // Windows deletes the System32 links of a package it removes; only what it leaves needs the quarantine.
                            if (!File.Exists(item.Target)) { Record(item, "done", "", "alreadyRemoved"); break; }
                            if (!string.Equals(Path.GetPathRoot(Path.GetFullPath(item.Target)), Path.GetPathRoot(Path.GetFullPath(secure)), StringComparison.OrdinalIgnoreCase))
                            { Record(item, "skipped", "", "otherVolume"); break; }
                            var folder = Path.Combine(backup, "file"); Directory.CreateDirectory(folder);
                            var target = Path.Combine(folder, Path.GetFileName(item.Target));
                            Log("Moviendo a cuarentena " + item.Target);
                            var r = await platform.QuarantineFileAsync(item.Target, target);
                            if (!r.Success) { Record(item, "failed", "", r.Output); break; }
                            await File.WriteAllTextAsync(Path.Combine(backup, "origin.txt"), item.Target, token);
                            string seal = "";
                            try { await BackupIntegrity.SealAsync(folder, token, false); }
                            catch (IOException e) { seal = "unsealed " + e.Message; }
                            Record(item, "done", Rel(secure, target), (r.Output + " " + seal).Trim(), item.Bytes); break;
                        }
                        case RemnantKind.Registration:
                        {
                            var split = item.Target.IndexOf('|');
                            if (split < 0) { Record(item, "skipped", "", "invalidTarget"); break; }
                            Directory.CreateDirectory(backup); var file = Path.Combine(backup, "value.json");
                            var export = await platform.ExportValueAsync(item.Target[..split], item.Target[(split + 1)..], file);
                            if (!export.Success || !File.Exists(file)) { Record(item, "skipped", "", "backupFailed " + export.Output); break; }
                            var r = await platform.DeleteValueAsync(item.Target[..split], item.Target[(split + 1)..]);
                            Record(item, r.Success ? "done" : "failed", Rel(secure, file), r.Output); break;
                        }
                        case RemnantKind.Cache:
                        {
                            if (!SafeFolder(item.Target)) { Record(item, "skipped", "", "unsafePath"); break; }
                            var (freed, locked) = DeleteTree(item.Target);
                            Record(item, locked == 0 ? "done" : "partial", "", locked == 0 ? "" : locked + " inUse", freed); break;
                        }
                        case RemnantKind.Registry:
                        case RemnantKind.App:
                        {
                            if (!platform.KeyExists(item.Target)) { Record(item, "done", "", "alreadyRemoved"); break; }
                            Directory.CreateDirectory(backup); var file = Path.Combine(backup, "registry.reg");
                            var export = await platform.ExportRegistryAsync(item.Target, file);
                            if (!export.Success || !File.Exists(file)) { Record(item, "skipped", "", "backupFailed " + export.Output); break; }
                            var r = await platform.DeleteRegistryAsync(item.Target);
                            // An empty shared vendor root goes too; importing the child backup recreates it on undo.
                            var parent = item.Target[..Math.Max(0, item.Target.LastIndexOf('\\'))];
                            if (r.Success && VendorProfiles.EmptyParents.Contains(parent, StringComparer.OrdinalIgnoreCase) && platform.KeyEmpty(parent) && (await platform.DeleteRegistryAsync(parent)).Success)
                                r = r with { Output = r.Output + " parentRemoved" };
                            Record(item, r.Success ? "done" : "failed", Rel(secure, file), r.Output); break;
                        }
                    }
                }
                // A Windows step that ran past its time limit is reported on that item; the others still run.
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or TimeoutException)
                { Record(item, "failed", "", e.Message); }
            }
            stage?.Invoke("verify");
            var after = await platform.InventoryAsync(token);
            File.WriteAllText(Path.Combine(session, "after.json"), JsonSerializer.Serialize(after));
            var remaining = DeepCleanPlanner.Refresh(after, plan).Items.Select(i => i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Status != "done" || !remaining.Contains(results[i].Key)) continue;
                // Windows deletes a service marked for deletion at the next restart.
                if (results[i].Kind == RemnantKind.Service) { results[i] = results[i] with { Message = "pendingRestart " + results[i].Message }; restart = true; }
                else if (results[i].Kind is RemnantKind.Device or RemnantKind.Package or RemnantKind.Task or RemnantKind.Startup or RemnantKind.Folder or RemnantKind.Registry or RemnantKind.App
                    or RemnantKind.File or RemnantKind.Registration)
                    results[i] = results[i] with { Status = "failed", Message = "stillPresent " + results[i].Message };
            }
            Save(secure, "undo.json", results); Save(session, "cleanup-results.json", results);
            var outcome = new CleanupOutcome(session, results, restart);
            Log($"Limpieza terminada: {outcome.Done} retirados, {outcome.Failed} con revisión. Respaldo y cuarentena en {secure}.");
            Save(session, "state.json", new { phase = "complete", completed = true, cleanup = true, restart, done = outcome.Done, failed = outcome.Failed, freed = outcome.Freed });
            return outcome;
        }
        catch (Exception e)
        {
            Log(e is OperationCanceledException ? "Cancelado entre pasos. No se interrumpió un proceso de Windows." : "Detenido: " + e.Message);
            Save(session, "state.json", new { phase = "stopped", completed = false, cleanup = true, error = e.Message });
            throw;
        }
    }

    // Restores what has a backup, newest change first. Removed phantom devices, Store apps and caches cannot return this way.
    public async Task<CleanupOutcome> UndoAsync(string session, string secureRoot, Action<string> log, CancellationToken token = default)
    {
        var name = Path.GetFileName(Path.GetFullPath(session).TrimEnd('\\'));
        if (!ValidSession(name)) throw new IOException("Invalid cleanup session name.");
        var secure = Path.Combine(secureRoot, name);
        var manifest = Path.Combine(secure, "undo.json");
        if (!File.Exists(manifest) || new FileInfo(manifest).Length > 16 * 1024 * 1024) throw new IOException("No hay respaldo de esta limpieza.");
        var done = JsonSerializer.Deserialize<CleanupResult[]>(await File.ReadAllTextAsync(manifest, token)) ?? [];
        var results = new List<CleanupResult>(); bool restart = false;
        void Log(string text) { File.AppendAllText(Path.Combine(session, "journal.jsonl"), JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, text = "undo: " + text }) + "\n"); log(text); }
        foreach (var item in done.Where(r => r.Status is "done" || r.Status == "partial" && r.Kind == RemnantKind.Folder).Reverse())
        {
            token.ThrowIfCancellationRequested();
            // Backup locations are derived from the protected folder; never from paths a user could rewrite.
            var backup = item.Backup.Length == 0 ? "" : Path.GetFullPath(Path.Combine(secure, item.Backup));
            if (backup.Length > 0 && !DeepCleanPlanner.Within(backup, secure)) { results.Add(item with { Status = "failed", Message = "invalidBackup" }); continue; }
            // Something Windows had already removed by itself left nothing to restore.
            if (backup.Length == 0 && item.Kind is RemnantKind.Registry or RemnantKind.App or RemnantKind.File) { results.Add(item with { Status = "skipped", Message = "nothingToRestore" }); continue; }
            try
            {
                CommandResult? r = null;
                switch (item.Kind)
                {
                    case RemnantKind.Registry: case RemnantKind.App: case RemnantKind.Service:
                        r = await platform.ImportRegistryAsync(backup); restart |= item.Kind == RemnantKind.Service; break;
                    case RemnantKind.Task: r = await platform.RegisterTaskAsync(item.Target, backup); break;
                    case RemnantKind.Startup: r = await platform.RestoreStartupAsync(backup); break;
                    case RemnantKind.Package:
                    {
                        await BackupIntegrity.VerifyAsync(backup, token);
                        var inf = Directory.EnumerateFiles(backup, "*.inf", SearchOption.AllDirectories).FirstOrDefault() ?? throw new IOException("Backup has no INF.");
                        r = await platform.AddPackageAsync(inf); break;
                    }
                    case RemnantKind.Folder:
                    {
                        var origin = (await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(backup)!, "origin.txt"), token)).Trim();
                        // A partial move left the open entries in place: the moved ones go back beside them, never over them.
                        bool merge = item.Status == "partial" && Directory.Exists(origin);
                        if (!SafeFolder(origin) || !merge && (Directory.Exists(origin) || File.Exists(origin))) { results.Add(item with { Status = "failed", Message = "destinationExists" }); continue; }
                        var sealPath = Path.Combine(backup, BackupIntegrity.ManifestName);
                        if (File.Exists(sealPath)) await BackupIntegrity.VerifyAsync(backup, token, false);
                        // The seal belongs to the quarantine, not to the vendor folder that comes back.
                        if (File.Exists(sealPath)) File.Delete(sealPath);
                        if (merge)
                        {
                            var entries = Directory.EnumerateFileSystemEntries(backup).ToArray();
                            if (entries.Any(e => Path.Exists(Path.Combine(origin, Path.GetFileName(e))))) { results.Add(item with { Status = "failed", Message = "destinationExists" }); continue; }
                            foreach (var entry in entries)
                                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0) Directory.Move(entry, Path.Combine(origin, Path.GetFileName(entry))); else File.Move(entry, Path.Combine(origin, Path.GetFileName(entry)));
                            r = new(0, "restored beside the entries that stayed " + origin); break;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(origin)!); Directory.Move(backup, origin);
                        r = new(0, "restored " + origin); break;
                    }
                    case RemnantKind.File:
                    {
                        var folder = Path.GetDirectoryName(backup)!;
                        var origin = (await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(folder)!, "origin.txt"), token)).Trim();
                        if (DeepCleanPlanner.SystemRelative(platform.WindowsRoot, origin) is null || File.Exists(origin) || Directory.Exists(origin))
                        { results.Add(item with { Status = "failed", Message = "destinationExists" }); continue; }
                        if (File.Exists(Path.Combine(folder, BackupIntegrity.ManifestName))) await BackupIntegrity.VerifyAsync(folder, token, false);
                        r = await platform.RestoreFileAsync(backup, origin); break;
                    }
                    case RemnantKind.Registration: r = await platform.RestoreValueAsync(backup); break;
                    default: results.Add(item with { Status = "skipped", Message = "notReversible" }); continue;
                }
                Log(item.Label + " · " + r.Output);
                results.Add(item with { Status = r.Success ? "restored" : "failed", Message = r.Output });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or TimeoutException)
            { results.Add(item with { Status = "failed", Message = e.Message }); }
        }
        File.WriteAllText(Path.Combine(session, "undo-results.json"), JsonSerializer.Serialize(results, Indented));
        File.WriteAllText(Path.Combine(session, "state.json"), JsonSerializer.Serialize(new { phase = "undone", completed = true, cleanup = true, restart }));
        return new(session, results, restart);
    }

    private static string Rel(string root, string path) => Path.GetRelativePath(root, path);

    // A vendor program restarted after the plan (a service watchdog, a tray helper) keeps logs open and blocks the move.
    // Stop what runs from the folders being removed or from this quarantine, wait, and try again; if something is still
    // open, move every entry that can move and report the rest. Returns how many entries moved and which ones stayed.
    private async Task<(int Moved, List<string> Locked)> MoveFolderAsync(string source, string target, string[] roots, Action<string> log, CancellationToken token)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { Directory.Move(source, target); return (1, []); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { if (attempt == 2) log("inUse " + source + " · " + e.Message); }
            foreach (var process in await platform.RunningProcessesAsync())
                if (process.Path.Length > 0 && (DeepCleanPlanner.Within(process.Path, source) || roots.Any(r => DeepCleanPlanner.Within(process.Path, r))))
                    log((await platform.StopProcessAsync(process.Id, process.Path)).Output + " · " + process.Path);
            await Task.Delay(TimeSpan.FromSeconds(attempt + 1.5), token);
        }
        int moved = 0; var locked = new List<string>();
        Directory.CreateDirectory(target);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source).ToArray())
        {
            var destination = Path.Combine(target, Path.GetFileName(entry));
            try
            {
                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0) Directory.Move(entry, destination); else File.Move(entry, destination);
                moved++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { locked.Add(Path.GetFileName(entry)); }
        }
        if (locked.Count == 0) { try { Directory.Delete(source); } catch (IOException) { locked.Add("."); } }
        if (moved == 0 && Directory.Exists(target) && !Directory.EnumerateFileSystemEntries(target).Any()) Directory.Delete(target);
        return (moved, locked);
    }

    // Refuse drive roots, Windows, profile roots and links. Targets come from fixed vendor locations.
    public static bool SafeFolder(string path)
    {
        try
        {
            var full = Path.GetFullPath(path).TrimEnd('\\');
            if (full.Length < 4 || full.Count(c => c == '\\') < 2) return false;
            foreach (var special in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.UserProfile })
            {
                // Never a protected root itself, nor a folder that contains one.
                var root = Environment.GetFolderPath(special);
                if (root.Length > 0 && DeepCleanPlanner.Within(root, full)) return false;
            }
            if (DeepCleanPlanner.Within(full, Environment.GetFolderPath(Environment.SpecialFolder.Windows))) return false;
            return !Directory.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException) { return false; }
    }

    // Caches regenerate. Locked files stay; links are removed without following them.
    public static (long Freed, int Locked) DeleteTree(string root)
    {
        long freed = 0; int locked = 0;
        if (!Directory.Exists(root)) return (0, 0);
        void Walk(string directory)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { locked++; continue; }
                try
                {
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(entry, false); else File.Delete(entry); continue; }
                    if ((attributes & FileAttributes.Directory) != 0) { Walk(entry); Directory.Delete(entry, false); continue; }
                    var length = new FileInfo(entry).Length;
                    if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
                    File.Delete(entry); freed += length;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { locked++; }
            }
        }
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(root, false); return (0, 0); }
        Walk(root);
        try { if (locked == 0) Directory.Delete(root, false); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { locked++; }
        return (freed, locked);
    }
}
