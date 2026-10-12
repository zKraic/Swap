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
        // Automatic mode never assumes that the processor brand's (possibly firmware-disabled) integrated GPU is gone.
        return !s.Laptop && IsOem(d.Inf) && !d.BootCritical && d.ClassGuid.Equals(DisplayGuid, StringComparison.OrdinalIgnoreCase) &&
            vendor is Vendor.Nvidia or Vendor.Amd or Vendor.Intel && vendor != target && (vendor == source || allAbsent && !s.IntegratedCaution(vendor)) &&
            !s.Adapters.Any(a => a.Vendor == vendor || a.Inf.Equals(d.Inf, StringComparison.OrdinalIgnoreCase)) &&
            s.Disconnected.Where(p => p.Inf.Equals(d.Inf, StringComparison.OrdinalIgnoreCase)).All(p => p.Vendor == vendor);
    }

    // The removed card stays registered as a disconnected device that still uses its driver package; Windows refuses
    // to delete a package in use, so that device entry is removed first. Nothing connected is ever touched.
    public static async Task RemoveDisconnectedAsync(IPlatform platform, Snapshot current, Driver driver, Action<string> log)
    {
        var vendor = Vendors.FromProvider(driver.Provider);
        foreach (var phantom in current.Disconnected.Where(p => p.Inf.Equals(driver.Inf, StringComparison.OrdinalIgnoreCase)))
        {
            if (phantom.Vendor != vendor || current.Adapters.Any(a => a.Id.Equals(phantom.Id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Un dispositivo inesperado usa " + driver.Inf + ". Se detuvo la limpieza.");
            log("Retirando el registro de la tarjeta desconectada " + phantom.Name);
            var removed = await platform.RemoveDeviceAsync(phantom.Id);
            log(removed.Output);
            if (!removed.Success) throw new InvalidOperationException("Windows no permitió retirar la tarjeta desconectada " + phantom.Name + ". Se conservan sus paquetes.");
        }
    }

    // The brand of unused Display packages whose hardware is gone. Never guesses the processor brand's integrated GPU;
    // without a candidate the target is returned, which means an upgrade without cleanup.
    public static Vendor DetectSource(Snapshot s, Vendor target) =>
        s.Drivers.Where(d => IsOem(d.Inf) && !d.BootCritical && d.ClassGuid.Equals(DisplayGuid, StringComparison.OrdinalIgnoreCase) && !s.Adapters.Any(a => a.Inf.Equals(d.Inf, StringComparison.OrdinalIgnoreCase)))
            .Select(d => Vendors.FromProvider(d.Provider)).FirstOrDefault(v => v != Vendor.Unknown && v != target && !s.Adapters.Any(a => a.Vendor == v) && !s.IntegratedCaution(v), target);

    public static string CleanupReason(Snapshot s, Driver d, Vendor source, Vendor target, bool allAbsent)
    {
        if (EligibleCleanup(s, d, source, target, allAbsent)) return "cleanupUnused";
        if (s.Laptop) return "cleanupOem";
        if (d.BootCritical || !IsOem(d.Inf) || !d.ClassGuid.Equals(DisplayGuid, StringComparison.OrdinalIgnoreCase)) return "cleanupProtected";
        var vendor = Vendors.FromProvider(d.Provider);
        if (vendor == Vendor.Unknown) return "cleanupUnknown";
        if (s.Adapters.Any(a => a.Inf.Equals(d.Inf, StringComparison.OrdinalIgnoreCase))) return "cleanupActive";
        if (vendor == target) return "cleanupRecovery";
        if (s.Adapters.Any(a => a.Vendor == vendor)) return "cleanupPresent";
        if (allAbsent && vendor != source && s.IntegratedCaution(vendor)) return "cleanupIntegrated";
        return "cleanupSelection";
    }
}
