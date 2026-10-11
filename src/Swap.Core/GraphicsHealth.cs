namespace Swap.Core;

// KraicDev. Evidence from Windows, not a certification of cabling, power or game stability.
public sealed record DisplayOutput(string Key, string Name, string AdapterId, int Width, int Height, int Hz, int MaximumHz);
public sealed record GraphicsProbe(string HardwareId, string Name, long DedicatedBytes, bool Hardware, bool Direct3D, int FeatureLevel, string Error);
public sealed record DisplayIncident(DateTimeOffset Time, string Provider, int Id, string Kind);
public sealed record GraphicsDiagnostics(DateTimeOffset Generated, IReadOnlyList<DisplayOutput> Displays,
    IReadOnlyList<GraphicsProbe> Probes, IReadOnlyList<DisplayIncident> Incidents, bool DisplaysComplete, bool EventsComplete, IReadOnlyList<string> Warnings);
public sealed record HealthFinding(string Code, string Detail, bool Attention = false);
public sealed record GraphicsCheckpoint(int Schema, string MachineId, bool Simulated, DateTimeOffset Generated,
    IReadOnlyList<Adapter> Adapters, IReadOnlyList<DisplayOutput> Displays);

public static class GraphicsHealth
{
    public static IReadOnlyList<HealthFinding> Evaluate(Adapter? adapter, GraphicsDiagnostics diagnostics)
    {
        var items = new List<HealthFinding>();
        if (adapter is null) { items.Add(new("gpuAbsent", "", true)); return items; }
        items.Add(new("gpuPresent", adapter.Name));
        items.Add(new(adapter.Problem == 0 ? "pnpOk" : "pnpError", adapter.Problem == 0 ? "" : adapter.Problem.ToString(), adapter.Problem != 0));
        items.Add(new(adapter.Signed && Planner.IsOem(adapter.Inf) ? "driverOk" : "driverReview", adapter.Version, !adapter.Signed || !Planner.IsOem(adapter.Inf)));
        var probes = diagnostics.Probes.Where(p => p.Hardware && Vendors.Matches(p.HardwareId, adapter.Id)).ToArray();
        // A PCI model is not an instance identity. Identical cards cannot be certified individually this way.
        items.Add(probes.Length != 1 ? new("direct3dUnknown", "") : probes[0].Direct3D ? new("direct3dOk", $"0x{probes[0].FeatureLevel:X}") : new("direct3dFailed", probes[0].Error, true));
        if (!diagnostics.DisplaysComplete) items.Add(new("displayUnknown", ""));
        else
        {
            var matching = diagnostics.Displays.Where(d => d.AdapterId.Equals(adapter.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            items.Add(new(matching.Length == 0 ? "noDirectDisplay" : "displayConnected", matching.Length.ToString()));
            foreach (var display in diagnostics.Displays.Where(d => d.Hz > 1 && d.MaximumHz > d.Hz + 1))
                items.Add(new("refreshAvailable", $"{display.Name}: {display.Hz} → {display.MaximumHz} Hz"));
        }
        if (!diagnostics.EventsComplete) items.Add(new("eventsUnknown", ""));
        else items.Add(new(diagnostics.Incidents.Count == 0 ? "noDisplayErrors" : "displayErrors", diagnostics.Incidents.Count.ToString(), diagnostics.Incidents.Count > 0));
        return items;
    }

    public static GraphicsCheckpoint Capture(Snapshot snapshot, GraphicsDiagnostics diagnostics, bool simulated) =>
        new(1, snapshot.MachineId, simulated, diagnostics.Generated, snapshot.Adapters.ToArray(), diagnostics.Displays.ToArray());

    public static IReadOnlyList<HealthFinding> Compare(GraphicsCheckpoint checkpoint, Snapshot current, GraphicsDiagnostics diagnostics, bool simulated)
    {
        if (checkpoint.Schema != 1 || checkpoint.MachineId != current.MachineId || checkpoint.Simulated != simulated ||
            checkpoint.Adapters is null || checkpoint.Displays is null ||
            checkpoint.Adapters.Any(a => a is null || string.IsNullOrWhiteSpace(a.Id) || a.Inf is null) ||
            checkpoint.Displays.Any(d => d is null || string.IsNullOrWhiteSpace(d.Key) || d.AdapterId is null))
            throw new InvalidOperationException("El punto de control corresponde a otro equipo o entorno.");
        var findings = new List<HealthFinding>();
        foreach (var old in checkpoint.Adapters)
        {
            var now = current.Adapters.SingleOrDefault(a => a.Id == old.Id);
            if (now is null) { findings.Add(new("gpuRemoved", old.Name, true)); continue; }
            if (now.Version != old.Version || !now.Inf.Equals(old.Inf, StringComparison.OrdinalIgnoreCase))
                findings.Add(new("driverChanged", $"{now.Name}: {old.Version} → {now.Version}"));
            if (now.Problem != 0 || old.Signed && !now.Signed || Planner.IsOem(old.Inf) && !Planner.IsOem(now.Inf))
                findings.Add(new("driverRegressed", now.Name, true));
        }
        foreach (var now in current.Adapters.Where(a => !checkpoint.Adapters.Any(x => x.Id == a.Id))) findings.Add(new("gpuAdded", now.Name));
        if (!diagnostics.DisplaysComplete) findings.Add(new("displayUnknown", ""));
        else foreach (var old in checkpoint.Displays)
        {
            var now = diagnostics.Displays.FirstOrDefault(d => d.Key == old.Key);
            if (now is null) { findings.Add(new("displayRemoved", old.Name, true)); continue; }
            if (now.Width < old.Width || now.Height < old.Height || now.Hz > 1 && old.Hz > now.Hz + 1)
                findings.Add(new("displayReduced", $"{now.Name}: {old.Width}×{old.Height} {old.Hz} Hz → {now.Width}×{now.Height} {now.Hz} Hz", true));
            if (!now.AdapterId.Equals(old.AdapterId, StringComparison.OrdinalIgnoreCase)) findings.Add(new("displayRouteChanged", now.Name));
        }
        if (!diagnostics.EventsComplete) findings.Add(new("eventsUnknown", ""));
        else if (diagnostics.Incidents.Any(i => i.Time > checkpoint.Generated)) findings.Add(new("newDisplayErrors", diagnostics.Incidents.Count(i => i.Time > checkpoint.Generated).ToString(), true));
        if (findings.Count == 0) findings.Add(new("checkpointUnchanged", ""));
        return findings;
    }
}
