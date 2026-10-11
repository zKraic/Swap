using System.Text.Json;
namespace Swap.Core;

public interface IPlatform
{
    Task<Snapshot> ScanAsync();
    Task<Package> InspectAsync(string path, Vendor target);
    Task<CommandResult> ExportAsync(string inf, string destination);
    Task<CommandResult> InstallAsync(Package package);
    Task<CommandResult> DeleteAsync(string inf);
    Task<string> RestorePointAsync();
}

public sealed class Engine(IPlatform platform)
{
    public async Task<Outcome> RunAsync(Plan plan, string session, Action<string> progress, CancellationToken token = default, Action<string>? stage = null)
    {
        Directory.CreateDirectory(session);
        using var sessionLock = new FileStream(Path.Combine(session, "session.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var messages = new List<string>();
        bool reboot = false;
        void Log(string text)
        {
            // Flush before the next system operation; a broken log stops the workflow.
            File.AppendAllText(Path.Combine(session, "journal.jsonl"), JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, text }) + "\n");
            messages.Add(text); progress(text);
        }
        void Save(string name, object value)
        {
            var file = Path.Combine(session, name + ".json");
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(file + ".tmp", file, true);
        }
        try
        {
            token.ThrowIfCancellationRequested();
            Log("Validando de nuevo el equipo y el paquete…");
            var current = await platform.ScanAsync();
            if (current.MachineId != plan.Before.MachineId) throw new InvalidOperationException("El plan corresponde a otro equipo.");
            var package = await platform.InspectAsync(plan.Package.Path, plan.Target);
            if (package.Sha256 != plan.Package.Sha256) throw new InvalidOperationException("El paquete cambió después de preparar el plan.");
            var fresh = Planner.Create(current, plan.Source, plan.Target, package, plan.TargetId, plan.CleanupAllAbsent);
            if (!fresh.Remove.SequenceEqual(plan.Remove)) throw new InvalidOperationException("El inventario cambió. Prepara un plan nuevo.");
            stage?.Invoke("backup");
            Save("before", current); Save("plan", fresh); Save("state", new { phase = "backup", completed = false });
            foreach (var driver in current.Drivers.Where(d => Planner.IsOem(d.Inf) && d.ClassGuid.Equals(Planner.DisplayGuid, StringComparison.OrdinalIgnoreCase)))
            {
                token.ThrowIfCancellationRequested();
                var dir = Path.Combine(session, "backup", driver.Inf);
                Directory.CreateDirectory(dir);
                Log("Respaldando " + driver.Inf);
                var result = await platform.ExportAsync(driver.Inf, dir);
                Log(result.Output);
                if (!result.Success || !Directory.EnumerateFiles(dir, "*.inf", SearchOption.AllDirectories).Any())
                    throw new InvalidOperationException("Falló el respaldo de " + driver.Inf + ". No se instalará ni eliminará ningún driver.");
            }
            Log(await platform.RestorePointAsync());
            token.ThrowIfCancellationRequested();
            Save("state", new { phase = "install", completed = false });
            stage?.Invoke("install");
            Log("Instalando el driver de destino…");
            var installed = await platform.InstallAsync(package);
            Log(installed.Output); reboot |= installed.Code == 3010;
            if (!installed.Success) throw new InvalidOperationException("La instalación falló. Código " + installed.Code + ". Los drivers antiguos se conservan.");
            stage?.Invoke("verify");
            var after = await platform.ScanAsync(); Save("after", after);
            reboot |= after.PendingReboot;
            var adapter = after.Adapters.SingleOrDefault(a => a.Id == fresh.TargetId && a.Vendor == plan.Target);
            bool verified = Continuation.VerifiedTarget(fresh, after) is not null;
            if (adapter is null || !verified || reboot || after.PendingReboot)
            {
                Log("La instalación requiere revisión o reinicio. Se conservan los drivers antiguos hasta verificar el nuevo driver.");
                Save("state", new { phase = "verify-after-reboot", completed = false, reboot = reboot || after.PendingReboot, waitBootId = after.BootId });
                return new(false, reboot || after.PendingReboot, session, messages);
            }
            stage?.Invoke("cleanup");
            Save("state", new { phase = "cleanup", completed = false });
            foreach (var driver in fresh.Remove)
            {
                token.ThrowIfCancellationRequested();
                // Refresh for every deletion: hot-plug / Windows Update may change device ownership.
                var check = await platform.ScanAsync();
                reboot |= check.PendingReboot;
                if (!check.Complete || check.MachineId != current.MachineId || check.PendingReboot ||
                    !check.Adapters.Any(a => a.Id == adapter.Id && a.Inf == adapter.Inf && a.Version == adapter.Version && a.Problem == 0 && a.Signed) ||
                    !Planner.EligibleCleanup(check, driver, plan.Source, plan.Target, plan.CleanupAllAbsent))
                    throw new InvalidOperationException("El estado de hardware cambió. Se detuvo la limpieza.");
                if (!check.Drivers.Contains(driver)) throw new InvalidOperationException("El paquete antiguo cambió. Revisa el inventario.");
                Log("Retirando paquete sin uso " + driver.Inf);
                var deleted = await platform.DeleteAsync(driver.Inf); Log(deleted.Output); reboot |= deleted.Code == 3010;
                if (!deleted.Success) throw new InvalidOperationException("Windows no permitió retirar " + driver.Inf + ". Se detuvo la limpieza.");
                if (reboot)
                {
                    Log("Windows solicita reiniciar. Se conservan los paquetes restantes.");
                    Save("state", new { phase = "verify-after-reboot", completed = false, reboot = true, waitBootId = check.BootId });
                    return new(false, true, session, messages);
                }
            }
            var final = await platform.ScanAsync(); Save("after", final);
            reboot |= final.PendingReboot;
            if (!final.Complete || final.MachineId != current.MachineId || !final.Adapters.Any(a => a.Vendor == plan.Target && a.Id == adapter.Id && a.Inf == adapter.Inf && a.Version == adapter.Version && a.Problem == 0 && a.Signed) || fresh.Remove.Any(d => final.Drivers.Contains(d)))
                throw new InvalidOperationException("La comprobación final necesita revisión. Consulta el registro.");
            if (reboot)
            {
                Log("Windows solicita reiniciar en la comprobación final. Se verificará de nuevo al iniciar sesión.");
                Save("state", new { phase = "verify-after-reboot", completed = false, reboot = true, waitBootId = final.BootId });
                return new(false, true, session, messages);
            }
            Log("Driver activo verificado y limpieza terminada. Falta comprobar tus juegos en el hardware real.");
            Save("state", new { phase = "complete", completed = true, reboot });
            return new(true, reboot, session, messages);
        }
        catch (Exception e)
        {
            Log(e is OperationCanceledException ? "Cancelado entre pasos. No se interrumpió un proceso de Windows." : "Detenido: " + e.Message);
            Save("state", new { phase = "stopped", completed = false, reboot, error = e.Message });
            throw;
        }
    }
}
