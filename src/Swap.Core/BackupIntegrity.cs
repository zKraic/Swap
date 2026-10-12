// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
namespace Swap.Core;

public sealed record BackupFile(string Path, long Bytes, string Sha256);
public sealed record BackupManifest(int Schema, IReadOnlyList<BackupFile> Files);

// A driver change must retain the same verified recovery material as an independent backup.
public static class SessionBackups
{
    public static Driver[] Drivers(Snapshot snapshot)
    {
        var drivers = snapshot.Drivers.Where(d => Planner.IsOem(d.Inf) && d.ClassGuid.Equals(Planner.DisplayGuid, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (drivers.Select(d => d.Inf).Distinct(StringComparer.OrdinalIgnoreCase).Count() != drivers.Length)
            throw new IOException("Duplicate Display packages in inventory; backup blocked.");
        return drivers;
    }
    public static async Task ExportAsync(IPlatform platform, Snapshot snapshot, string session, Action<string> log, CancellationToken token)
    {
        foreach (var driver in Drivers(snapshot))
        {
            token.ThrowIfCancellationRequested();
            var destination = Path.Combine(session, "backup", driver.Inf);
            Directory.CreateDirectory(destination);
            log("Respaldando " + driver.Inf);
            var result = await platform.ExportAsync(driver.Inf, destination);
            log(result.Output);
            if (!result.Success) throw new IOException("Driver backup failed. Installation and cleanup blocked.");
            await BackupIntegrity.SealAsync(destination, token);
            await BackupIntegrity.VerifyAsync(destination, token);
        }
    }
    public static async Task VerifyAsync(Snapshot snapshot, string session, CancellationToken token)
    {
        var drivers = Drivers(snapshot);
        if (drivers.Length == 0) return;
        var root = Path.Combine(session, "backup");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked backup directory is not allowed.");
        foreach (var driver in drivers)
            await BackupIntegrity.VerifyAsync(Path.Combine(root, driver.Inf), token);
    }
}

public static class BackupIntegrity
{
    public const string ManifestName = "integrity.json";
    private static IEnumerable<string> Files(string root, string manifestPath, int depth = 0)
    {
        if (depth > 32) throw new IOException("Backup directory depth exceeds its limit.");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked backup paths are not allowed.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked backup paths are not allowed.");
            if ((attributes & FileAttributes.Directory) != 0) { foreach (var file in Files(entry, manifestPath, depth + 1)) yield return file; }
            else if (!entry.Equals(manifestPath, StringComparison.OrdinalIgnoreCase)) yield return entry;
        }
    }
    private static async Task<BackupFile[]> Describe(string root, CancellationToken token)
    {
        root = Path.GetFullPath(root);
        var files = Files(root, Path.Combine(root, ManifestName)).Take(20001).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length is 0 or > 20000 || !files.Any(f => Path.GetExtension(f).Equals(".inf", StringComparison.OrdinalIgnoreCase))) throw new IOException("Backup has no INF or exceeds its file limit.");
        var result = new List<BackupFile>();
        foreach (var path in files)
        {
            token.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            result.Add(new(Path.GetRelativePath(root, path).Replace('\\', '/'), stream.Length, Convert.ToHexString(await SHA256.HashDataAsync(stream, token))));
        }
        return result.ToArray();
    }
    public static async Task SealAsync(string root, CancellationToken token = default)
    {
        var description = await Describe(root, token);
        // CreateNew never replaces an existing manifest. This checksum is not a signature.
        using var stream = new FileStream(Path.Combine(root, ManifestName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, new BackupManifest(1, description), cancellationToken: token);
    }
    public static async Task VerifyAsync(string root, CancellationToken token = default)
    {
        var path = Path.Combine(root, ManifestName);
        if (new FileInfo(path).Length > 8 * 1024 * 1024 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Invalid backup manifest.");
        using var stream = File.OpenRead(path);
        BackupManifest? manifest;
        try { manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, cancellationToken: token); }
        catch (JsonException e) { throw new IOException("Invalid backup manifest.", e); }
        if (manifest is not { Schema: 1, Files: not null } || manifest.Files.Count is 0 or > 20000) throw new IOException("Invalid backup manifest.");
        var actual = await Describe(root, token);
        // Only compare relative names; never open paths supplied by an untrusted manifest.
        if (!actual.SequenceEqual(manifest.Files)) throw new IOException("Backup files changed. Restore blocked.");
    }
}

public sealed class GraphicsBackup(IPlatform platform)
{
    public async Task<string> RunAsync(Snapshot before, string session, Action<string> log, CancellationToken token = default)
    {
        if (!before.Admin || !before.Complete) throw new InvalidOperationException("Complete administrator inventory is required.");
        var drivers = before.Drivers.Where(d => Planner.IsOem(d.Inf) && d.ClassGuid.Equals(Planner.DisplayGuid, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (drivers.Length == 0 || drivers.Select(d => d.Inf).Distinct(StringComparer.OrdinalIgnoreCase).Count() != drivers.Length) throw new InvalidOperationException("No unique OEM Display packages to back up.");
        if (Directory.Exists(session)) throw new IOException("Backup destination already exists.");
        Directory.CreateDirectory(session);
        using var sessionLock = new FileStream(Path.Combine(session, "session.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        await File.WriteAllTextAsync(Path.Combine(session, "before.json"), JsonSerializer.Serialize(before), token);
        await File.WriteAllTextAsync(Path.Combine(session, "state.json"), "{\"phase\":\"backup\",\"completed\":false,\"backupOnly\":true}", token);
        foreach (var driver in drivers)
        {
            token.ThrowIfCancellationRequested();
            var destination = Path.Combine(session, "backup", driver.Inf); Directory.CreateDirectory(destination);
            log("Export: " + driver.Inf);
            if (!(await platform.ExportAsync(driver.Inf, destination)).Success) throw new IOException("Driver export failed.");
            await BackupIntegrity.SealAsync(destination, token); await BackupIntegrity.VerifyAsync(destination, token);
        }
        var after = await platform.ScanAsync();
        if (!after.Complete || after.MachineId != before.MachineId || !after.Adapters.SequenceEqual(before.Adapters) || !after.Drivers.SequenceEqual(before.Drivers)) throw new IOException("Inventory changed during backup; review required.");
        await File.WriteAllTextAsync(Path.Combine(session, "state.json"), "{\"phase\":\"complete\",\"completed\":true,\"backupOnly\":true}", token);
        return session;
    }
}
