using System.Text.Json;
namespace Swap.Core;

public sealed class Maintenance(IPlatform platform)
{
    // No catalog offer is not proof of health. Require a signed, healthy OEM driver.
    public async Task<Outcome> RunAsync(Snapshot before, Vendor source, Vendor target, string session, Action<string> log,
        CancellationToken token = default, Action<string>? stage = null, string? targetId = null, bool cleanupAllAbsent = false)
    {
        var adapter = before.Adapters.SingleOrDefault(a => a.Vendor == target && (string.IsNullOrEmpty(targetId) || a.Id == targetId));
        if (adapter is not { Signed: true, Problem: 0 } || !Planner.IsOem(adapter.Inf))
            throw new InvalidOperationException("No hay un driver compatible en el catálogo y el driver activo necesita atención. No se modificó el equipo.");
        var proof = new Package("active", target, adapter.Version, [adapter.Id[..adapter.Id.LastIndexOf('\\')]], "active", true, false, true);
        var plan = Planner.Create(before, source, target, proof, adapter.Id, cleanupAllAbsent);
        if (Directory.Exists(session)) throw new IOException("A new session directory is required.");
        Directory.CreateDirectory(session);
        using var fileLock = new FileStream(Path.Combine(session, "session.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        var messages = new List<string>();
        bool reboot = false;
        void Record(string message) { File.AppendAllText(Path.Combine(session, "journal.jsonl"), JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, text = message }) + "\n"); messages.Add(message); log(message); }
        void Save(string name, object value) { var path = Path.Combine(session, name + ".json"); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value)); File.Move(path + ".tmp", path, true); }
        try
        {
            Save("before", before); Save("plan", plan); Save("state", new { phase = "backup", completed = false }); stage?.Invoke("backup");
            await SessionBackups.ExportAsync(platform, before, session, Record, token);
            Record(await platform.RestorePointAsync()); token.ThrowIfCancellationRequested(); stage?.Invoke("verify");
            async Task<Snapshot> Validate()
            {
                var s = await platform.ScanAsync();
                reboot |= s.PendingReboot;
                if (s.MachineId != before.MachineId) throw new InvalidOperationException("El equipo cambió.");
                _ = Planner.Create(s, source, target, proof, adapter.Id, cleanupAllAbsent);
                if (!s.Adapters.Contains(adapter)) throw new InvalidOperationException("El driver activo cambió; vuelve a analizar.");
                return s;
            }
            await Validate();
            if (!await platform.VerifyGraphicsAsync(adapter)) throw new InvalidOperationException("Direct3D on the selected GPU could not be verified. Cleanup blocked.");
            await SessionBackups.VerifyAsync(before, session, token);
            Save("state", new { phase = "cleanup", completed = false }); stage?.Invoke("cleanup");
            foreach (var d in plan.Remove)
            {
                token.ThrowIfCancellationRequested(); var current = await Validate();
                if (!Planner.EligibleCleanup(current, d, source, target, cleanupAllAbsent) || !current.Drivers.Contains(d)) throw new InvalidOperationException("El inventario cambió; limpieza detenida.");
                await BackupIntegrity.VerifyAsync(Path.Combine(session, "backup", d.Inf), token);
                Record("Retirando paquete sin uso " + d.Inf); var deleted = await platform.DeleteAsync(d.Inf);
                if (!deleted.Success) throw new IOException("La retirada necesita revisión.");
                if (deleted.Code == 3010)
                {
                    Record("Windows solicita reiniciar. Se conservan los paquetes restantes.");
                    Save("state", new { phase = "verify-after-reboot", completed = false, reboot = true, installed = false, waitBootId = current.BootId });
                    return new(false, true, session, messages);
                }
            }
            var final = await Validate(); Save("after", final);
            if (plan.Remove.Any(d => final.Drivers.Any(x => x.Inf.Equals(d.Inf, StringComparison.OrdinalIgnoreCase)))) throw new IOException("Windows no confirmó la retirada del paquete.");
            await Preparation.SaveVerifiedAsync(platform, final, plan, adapter, session, token);
            Record("Driver activo firmado y Direct3D verificado al terminar. Resultado guardado en preparation.json.");
            Save("state", new { phase = "complete", completed = true, installed = false }); return new(true, false, session, messages);
        }
        catch (Exception e) { Save("state", new { phase = "stopped", completed = false, reboot, error = e.Message }); Record(e.Message); throw; }
    }
}
