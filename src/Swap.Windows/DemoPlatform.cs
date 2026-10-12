using Swap.Core;
namespace Swap.Windows;

public sealed class DemoPlatform : IPlatform
{
    public Vendor Target { get; set; } = Vendor.Amd;
    public IReadOnlyList<Adapter> ExtraAdapters { get; set; } = [];
    private bool installed;
    private bool deleted;
    private Vendor Source => Target == Vendor.Amd ? Vendor.Nvidia : Vendor.Amd;
    private string Hardware => Target == Vendor.Amd ? @"PCI\VEN_1002&DEV_747E" : Target == Vendor.Intel ? @"PCI\VEN_8086&DEV_56A0" : @"PCI\VEN_10DE&DEV_2786";
    public Task<Snapshot> ScanAsync() => Task.FromResult(new Snapshot("Windows 11 · 24H2", "DEMO", true, false, false, false,
        80L * 1024 * 1024 * 1024,
        [new(Target == Vendor.Amd ? "AMD Radeon RX 7800 XT" : Target == Vendor.Intel ? "Intel Arc A770" : "NVIDIA GeForce RTX 4070", Hardware + @"\DEMO", Target,
            installed ? "oem99.inf" : "basicdisplay.inf", installed ? "32.0.1.100" : "10.0.1.0", 0, true), ..ExtraAdapters],
        deleted ? [] : [new("oem42.inf", Source == Vendor.Nvidia ? "NVIDIA" : "Advanced Micro Devices, Inc.", Planner.DisplayGuid, "31.0.1.0", false)], true,
        []));
    public Task<Package> InspectAsync(string path, Vendor target) => Task.FromResult(new Package("demo.inf", target, "32.0.1.100",
        [Hardware], "DEMO", true, false, true));
    public async Task<CommandResult> ExportAsync(string inf, string destination)
    { await Task.Delay(300); await File.WriteAllTextAsync(Path.Combine(destination, "demo.inf"), "; simulated backup only"); return new(0, "Respaldo simulado."); }
    public async Task<CommandResult> InstallAsync(Package package) { await Task.Delay(800); installed = true; return new(0, "Instalación simulada."); }
    public async Task<CommandResult> DeleteAsync(string inf) { await Task.Delay(300); deleted = true; return new(0, "Retirada simulada."); }
    public Task<string> RestorePointAsync() => Task.FromResult("Punto de restauración simulado.");
    public Task<bool> VerifyGraphicsAsync(Adapter adapter) => Task.FromResult(true);
}
