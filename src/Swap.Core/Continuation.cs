using System.Text.Json;
namespace Swap.Core;

// KraicDev. Resume verification and the remaining cleanup, never installation of a stored executable.
public sealed class Continuation(IPlatform platform)
{
    public static Adapter? VerifiedTarget(Plan plan, Snapshot current)
    {
        var original = plan.Before.Adapters.SingleOrDefault(a => a.Id == plan.TargetId && a.Vendor == plan.Target);
        var active = current.Adapters.SingleOrDefault(a => a.Id == plan.TargetId && a.Vendor == plan.Target);
        if (!current.Complete || current.MachineId != plan.Before.MachineId || original is null || active is not { Problem: 0, Signed: true } || !Planner.IsOem(active.Inf)) return null;
        bool matches = plan.Package.Path == "active" ? active.Inf == original.Inf && active.Version == original.Version :
            plan.Package.IsExe || plan.Package.IsWindowsUpdate && string.IsNullOrEmpty(plan.Package.Version)
                ? active.Inf != original.Inf || active.Version != original.Version : active.Version == plan.Package.Version;
        return matches ? active : null;
    }

    public async Task<Outcome> RunAsync(string session, Action<string> log, CancellationToken token = default, Action<string>? stage = null)
    {
        session = Path.GetFullPath(session);
        using var sessionLock = new FileStream(Path.Combine(session, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        T Read<T>(string name) { var path=Path.Combine(session,name+".json"); if(new FileInfo(path).Length>1024*1024) throw new IOException("Informe demasiado grande."); return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new IOException("Informe vacío."); }
        void Save(object state) { var file=Path.Combine(session,"state.json"); File.WriteAllText(file+".tmp",JsonSerializer.Serialize(state)); File.Move(file+".tmp",file,true); }
        var messages=new List<string>();
        void Record(string message) { File.AppendAllText(Path.Combine(session,"journal.jsonl"),JsonSerializer.Serialize(new {time=DateTimeOffset.UtcNow,text=message})+"\n"); messages.Add(message); log(message); }
        var state=Read<JsonElement>("state");
        if(state.GetProperty("phase").GetString()!="verify-after-reboot" || state.GetProperty("completed").GetBoolean() || !state.GetProperty("reboot").GetBoolean()) throw new InvalidOperationException("Esta sesión no espera un reinicio.");
        var plan=Read<Plan>("plan");
        var current=await platform.ScanAsync();
        if(current.MachineId!=plan.Before.MachineId) throw new InvalidOperationException("La sesión corresponde a otro equipo.");
        var waitBootId=state.TryGetProperty("waitBootId",out var wait)?wait.GetString():plan.Before.BootId;
        if(string.IsNullOrWhiteSpace(current.BootId)||string.IsNullOrWhiteSpace(waitBootId)||current.BootId==waitBootId||current.PendingReboot)
            return new(false,true,session,["Windows todavía necesita completar el reinicio."]);
        try
        {
            token.ThrowIfCancellationRequested(); stage?.Invoke("verify");
            var active=VerifiedTarget(plan,current) ?? throw new InvalidOperationException("El nuevo driver no está activo, firmado y verificado. No se limpia ningún paquete.");
            _ = Planner.Create(current,plan.Source,plan.Target,plan.Package,plan.TargetId,plan.CleanupAllAbsent);
            if (!await platform.VerifyGraphicsAsync(active)) throw new InvalidOperationException("Direct3D on the selected GPU could not be verified. Cleanup blocked.");
            await SessionBackups.VerifyAsync(plan.Before, session, token);
            async Task<Snapshot> Validate()
            {
                var snapshot=await platform.ScanAsync();
                _ = Planner.Create(snapshot,plan.Source,plan.Target,plan.Package,plan.TargetId,plan.CleanupAllAbsent);
                if(snapshot.MachineId!=plan.Before.MachineId || !snapshot.Adapters.Contains(active)) throw new InvalidOperationException("El hardware o el driver activo cambió.");
                return snapshot;
            }
            stage?.Invoke("cleanup");
            foreach(var driver in plan.Remove)
            {
                token.ThrowIfCancellationRequested(); current=await Validate();
                // The successful deletions before the restart need not be repeated.
                if(!current.Drivers.Any(d=>d.Inf.Equals(driver.Inf,StringComparison.OrdinalIgnoreCase))) continue;
                if(!current.Drivers.Contains(driver) || !Planner.EligibleCleanup(current,driver,plan.Source,plan.Target,plan.CleanupAllAbsent)) throw new InvalidOperationException("Un paquete antiguo cambió o volvió a estar en uso.");
                await BackupIntegrity.VerifyAsync(Path.Combine(session,"backup",driver.Inf), token);
                Record("Retirando paquete sin uso "+driver.Inf);
                var result=await platform.DeleteAsync(driver.Inf); Record(result.Output);
                if(!result.Success) throw new IOException("Windows no permitió retirar el paquete antiguo.");
                if(result.Code==3010) { Save(new {phase="verify-after-reboot",completed=false,reboot=true,waitBootId=current.BootId}); return new(false,true,session,messages); }
            }
            current=await Validate();
            if(plan.Remove.Any(d=>current.Drivers.Any(x=>x.Inf.Equals(d.Inf,StringComparison.OrdinalIgnoreCase)))) throw new IOException("Windows no confirmó la retirada.");
            File.WriteAllText(Path.Combine(session,"after.json"),JsonSerializer.Serialize(current));
            Record("Nuevo driver verificado. Limpieza de paquetes antiguos terminada después del reinicio.");
            Save(new {phase="complete",completed=true,reboot=false}); return new(true,false,session,messages);
        }
        catch(Exception e) { Save(new {phase="stopped",completed=false,reboot=false,error=e.Message}); Record("Continuación detenida: "+e.Message); throw; }
    }
}
