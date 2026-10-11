using System.Text.RegularExpressions;
namespace Swap.Core;

public static partial class Planner
{
    public const string DisplayGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    [GeneratedRegex("\\Aoem[0-9]+\\.inf\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OemPattern();
    public static bool IsOem(string inf) => OemPattern().IsMatch(inf);
    public static Plan Create(Snapshot s, Vendor source, Vendor target, Package package, string? targetId = null, bool cleanupAllAbsent = false)
    {
        if (source is not (Vendor.Amd or Vendor.Nvidia or Vendor.Intel) || target is not (Vendor.Amd or Vendor.Nvidia or Vendor.Intel))
            throw new InvalidOperationException("Selecciona NVIDIA, AMD o Intel como origen y destino.");
        if (!s.Admin || !s.Complete) throw new InvalidOperationException("Se necesita un inventario completo con permisos de administrador.");
        if (s.PendingReboot) throw new InvalidOperationException("Windows tiene un reinicio pendiente. Reinicia y vuelve a analizar.");
        if (s.Remote) throw new InvalidOperationException("Realiza el cambio desde una sesión local, no por escritorio remoto.");
        if (s.Laptop && !package.IsWindowsUpdate) throw new InvalidOperationException("Equipo portátil: usa el catálogo de Windows Update para preservar la compatibilidad OEM.");
        if (s.FreeBytes < 4L * 1024 * 1024 * 1024) throw new InvalidOperationException("Libera al menos 4 GB en la unidad de Windows.");
        if (!package.Trusted || package.Vendor != target) throw new InvalidOperationException("El paquete no tiene una firma válida o no pertenece al destino.");
        var targets = s.Adapters.Where(a => a.Vendor == target && (string.IsNullOrEmpty(targetId) || a.Id == targetId)).ToArray();
        if (targets.Length != 1) throw new InvalidOperationException("Debe haber exactamente una GPU de destino instalada. Los equipos con varias GPU requieren revisión manual.");
        if (!package.IsExe && !package.HardwareIds.Any(id => Vendors.Matches(id, targets[0].Id)))
            throw new InvalidOperationException("El INF no declara compatibilidad con la GPU detectada.");
        if (!package.IsExe && !package.IsWindowsUpdate && string.IsNullOrWhiteSpace(package.Version)) throw new InvalidOperationException("El INF no declara una versión verificable.");
        var remove = s.Drivers.Where(d => EligibleCleanup(s, d, source, target, cleanupAllAbsent)).ToArray();
        return new(s, source, target, package, remove,
            ["Se respalda antes de instalar.", "Se verifica el driver activo antes de retirar los antiguos.",
             "No se eliminan chipset, audio HDMI, servicios ni perfiles de juegos.",
             source == target ? "Upgrade: se conservan los paquetes anteriores para permitir recuperación." : "Solo se retiran paquetes Display del origen sin uso.",
             package.IsExe ? "El instalador oficial mostrará sus propias opciones. La firma no prueba compatibilidad; se verifica después." : "Instalación del driver de pantalla con Windows; el panel y funciones adicionales se instalan con el paquete oficial."], targets[0].Id, cleanupAllAbsent);
    }
    public static bool EligibleCleanup(Snapshot s, Driver d, Vendor source, Vendor target, bool allAbsent)
    {
        var vendor = Vendors.FromProvider(d.Provider);
        return !s.Laptop && IsOem(d.Inf) && !d.BootCritical && d.ClassGuid.Equals(DisplayGuid, StringComparison.OrdinalIgnoreCase) &&
            vendor is Vendor.Nvidia or Vendor.Amd or Vendor.Intel && vendor != target && (allAbsent || vendor == source) &&
            !s.Adapters.Any(a => a.Vendor == vendor || a.Inf.Equals(d.Inf, StringComparison.OrdinalIgnoreCase));
    }
}
