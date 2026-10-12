using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Swap.Core;
namespace Swap.Windows;

public sealed record DriverOffer(string Id, int Revision, string Title, string HardwareId, string Manufacturer, DateTime Date, long Bytes);
public static class DriverSelection
{
    // WUA already checks applicability. Also require the exact hardware/compatible ID from the selected device.
    public static DriverOffer? Select(IEnumerable<DriverOffer> offers, IEnumerable<string> hardwareIds, Vendor vendor, Version? installedVersion = null) =>
        offers.Where(o => Guid.TryParse(o.Id, out _) && o.Revision > 0 && o.Bytes >= 0 && o.Bytes <= 4L * 1024 * 1024 * 1024 &&
            Vendors.FromId(o.HardwareId) == vendor && hardwareIds.Contains(o.HardwareId, StringComparer.OrdinalIgnoreCase) &&
            (installedVersion is null || VersionOf(o.Title) > installedVersion))
        .OrderByDescending(o => o.Date).ThenByDescending(o => VersionOf(o.Title)).FirstOrDefault();
    public static Version VersionOf(string title)
    {
        var matches = Regex.Matches(title, @"\b\d+\.\d+\.\d+\.\d+\b");
        return matches.Count > 0 && Version.TryParse(matches[^1].Value, out var version) ? version : new Version(0, 0, 0, 0);
    }
}
public sealed class AutomaticPlatform : IPlatform
{
    private readonly WindowsPlatform windows = new();
    private DriverOffer? selected;
    private Adapter? adapter;
    private Package? selectedPackage;
    public async Task<(Package? Package, DriverOffer? Offer)> FindAsync(Adapter target)
    {
        selected = null; selectedPackage = null; adapter = null;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(target.Id));
        var ids = JsonSerializer.Deserialize<string[]>(await WindowsPlatform.PowerShellAsync(
            "$id=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded + "')); $ids=@((Get-PnpDeviceProperty -InstanceId $id -KeyName DEVPKEY_Device_HardwareIds).Data); $ids+=@((Get-PnpDeviceProperty -InstanceId $id -KeyName DEVPKEY_Device_CompatibleIds).Data); ConvertTo-Json -InputObject @($ids) -Compress")) ?? [];
        var offers = JsonSerializer.Deserialize<DriverOffer[]>(await WindowsPlatform.PowerShellAsync(WindowsPlatform.Script("Updates"), 1800)) ?? [];
        var healthy = target is { Problem: 0, Signed: true } && Planner.IsOem(target.Inf);
        selected = DriverSelection.Select(offers, ids, target.Vendor, healthy && Version.TryParse(target.Version, out var active) ? active : null);
        if (selected is null) return (null, null);
        adapter = target;
        var version = DriverSelection.VersionOf(selected.Title);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(selected) + target.Id)));
        selectedPackage = new("wu:" + selected.Id + ":" + selected.Revision, target.Vendor,
            version.Major == 0 ? "" : version.ToString(), [target.Id[..target.Id.LastIndexOf('\\')]], hash, true, false, true);
        return (selectedPackage, selected);
    }
    public Task<CommandResult> DownloadAsync(Package package) => ApplyAsync(package, true);
    private async Task<CommandResult> ApplyAsync(Package package, bool downloadOnly)
    {
        if (package != selectedPackage || selected is null || adapter is null) throw new IOException("Busca un driver compatible antes de continuar.");
        var request = new { selected.Id, selected.Revision, selected.Title, selected.HardwareId, selected.Manufacturer, AdapterId = adapter.Id, DownloadOnly = downloadOnly };
        var script = WindowsPlatform.Script("ApplyUpdate").Replace("__REQUEST__", Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        return JsonSerializer.Deserialize<CommandResult>(await WindowsPlatform.PowerShellAsync(script, 7200)) ?? throw new IOException("Windows Update no devolvió un resultado.");
    }
    public Task<Snapshot> ScanAsync() => windows.ScanAsync();
    public Task<bool> VerifyGraphicsAsync(Adapter adapter) => windows.VerifyGraphicsAsync(adapter);
    public Task<Package> InspectAsync(string path, Vendor target) => selectedPackage is not null && selectedPackage.Path == path && selectedPackage.Vendor == target
        ? Task.FromResult(selectedPackage) : windows.InspectAsync(path, target);
    public Task<CommandResult> ExportAsync(string inf, string destination) => windows.ExportAsync(inf, destination);
    public Task<CommandResult> InstallAsync(Package package) => package.IsWindowsUpdate ? ApplyAsync(package, false) : windows.InstallAsync(package);
    public Task<CommandResult> DeleteAsync(string inf) => windows.DeleteAsync(inf);
    public Task<CommandResult> RemoveDeviceAsync(string instanceId) => windows.RemoveDeviceAsync(instanceId);
    public Task<string> RestorePointAsync() => windows.RestorePointAsync();
}
