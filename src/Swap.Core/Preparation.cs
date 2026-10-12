using System.Text.Json;
namespace Swap.Core;

// KraicDev. A bounded driver-readiness result, never a hardware or gaming certificate.
public sealed record PreparationResult(int Schema, DateTimeOffset Generated, bool Ready,
    bool InventoryComplete, bool DriverHealthy, bool HardwareDirect3D, bool RestartRequired,
    IReadOnlyList<string> RemainingUnusedPackages, IReadOnlyList<object> PreservedPackages, string Scope);

public static class Preparation
{
    public static PreparationResult Assess(Snapshot snapshot, Adapter? adapter, Vendor source, Vendor target,
        bool allAbsent, bool hardwareDirect3D)
    {
        bool healthy = adapter is { Problem: 0, Signed: true } && adapter.Vendor == target && Planner.IsOem(adapter.Inf) && snapshot.Adapters.Contains(adapter);
        var unused = adapter is null ? [] : snapshot.Drivers.Where(d => Planner.EligibleCleanup(snapshot, d, source, target, allAbsent)).Select(d => d.Inf).ToArray();
        var preserved = snapshot.Drivers.Where(d => !unused.Contains(d.Inf, StringComparer.OrdinalIgnoreCase))
            .Select(d => (object)new { d.Inf, d.Provider, reason = adapter is null ? "selectGpu" : Planner.CleanupReason(snapshot, d, source, target, allAbsent) }).ToArray();
        return new(1, DateTimeOffset.UtcNow, snapshot.Complete && healthy && hardwareDirect3D && !snapshot.PendingReboot && unused.Length == 0,
            snapshot.Complete, healthy, hardwareDirect3D, snapshot.PendingReboot, unused, preserved,
            "Compatible Display driver and unused-package cleanup only. Preserved packages are intentional. Vendor applications, electrical connections and game stability are not certified.");
    }

    public static async Task SaveVerifiedAsync(IPlatform platform, Snapshot final, Plan plan, Adapter active, string session, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _ = Planner.Create(final, plan.Source, plan.Target, plan.Package, plan.TargetId, plan.CleanupAllAbsent);
        if (final.MachineId != plan.Before.MachineId || !final.Adapters.Contains(active))
            throw new InvalidOperationException("The selected driver or machine changed during final verification.");
        var result = Assess(final, active, plan.Source, plan.Target, plan.CleanupAllAbsent, await platform.VerifyGraphicsAsync(active));
        if (!result.Ready) throw new InvalidOperationException("Final driver readiness could not be verified. Review Direct3D and remaining unused packages.");
        await SessionBackups.VerifyAsync(plan.Before, session, token);
        var path = Path.Combine(session, "preparation.json");
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(new { author = "KraicDev", simulated = platform.Simulated, result, backupIntegrityVerified = true,
            driver = new { active.Name, active.Vendor, active.Inf, active.Version } }, new JsonSerializerOptions { WriteIndented = true }), token);
        File.Move(path + ".tmp", path, true);
    }
}
