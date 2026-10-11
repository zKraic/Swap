using System.Text.Json;
using Swap.Core;
using Swap.Windows;

var results = new List<object>(); int failed = 0;
async Task Test(string name, Func<Task> action)
{
    try { await action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; results.Add(new { name, passed = false, error = e.Message }); Console.WriteLine("FAIL " + name + ": " + e.Message); }
}
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
async Task RejectAsync(Func<Task> action) { try { await action(); } catch (Exception e) when (e is InvalidOperationException or IOException or OperationCanceledException) { return; } throw new Exception("Expected rejection"); }
async Task RejectMessageAsync(Func<Task> action, string message) { try { await action(); } catch (IOException e) { Assert(e.Message.Contains(message), "Unexpected rejection: " + e.Message); return; } throw new Exception("Expected file validation rejection"); }
var root = Path.Combine(Path.GetTempPath(), "SwapTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
string Session() => Path.Combine(root, Guid.NewGuid().ToString("N"));
var basic = Fake.Standard();
Plan Plan(Fake f) => Planner.Create(f.Before, f.Source, f.Target, f.Package);

foreach (var direction in new[] { (Vendor.Nvidia, Vendor.Amd), (Vendor.Amd, Vendor.Nvidia), (Vendor.Nvidia, Vendor.Intel), (Vendor.Intel, Vendor.Nvidia), (Vendor.Amd, Vendor.Intel), (Vendor.Intel, Vendor.Amd) })
    await Test("End-to-end " + direction, async () =>
    {
        var f = new Fake(direction.Item1, direction.Item2); var path = Session(); var o = await new Engine(f).RunAsync(Plan(f), path, _ => { });
        Assert(o.Verified && !o.RebootRequired); Assert(f.Calls.IndexOf("install") > f.Calls.IndexOf("export:oem42.inf"));
        Assert(f.Calls.IndexOf("delete:oem42.inf") > f.Calls.IndexOf("install"));
        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "state.json"))); Assert(state.RootElement.GetProperty("completed").GetBoolean());
        Assert(File.Exists(Path.Combine(path, "after.json")) && File.ReadAllLines(Path.Combine(path, "journal.jsonl")).Length >= 7);
    });

var blocks = new Dictionary<string, Func<Snapshot, Snapshot>> {
 ["non-admin"] = s => s with { Admin = false }, ["incomplete inventory"] = s => s with { Complete = false },
 ["pending reboot"] = s => s with { PendingReboot = true }, ["remote session"] = s => s with { Remote = true },
 ["laptop hybrid"] = s => s with { Laptop = true }, ["low disk"] = s => s with { FreeBytes = 100 },
 ["missing destination GPU"] = s => s with { Adapters = [] },
 ["multiple destination GPUs"] = s => s with { Adapters = [s.Adapters[0], s.Adapters[0] with { Id = s.Adapters[0].Id + "2" }] },
};
foreach (var block in blocks) await Test("Preflight blocks " + block.Key, () => { Reject(() => Planner.Create(block.Value(basic.Before), basic.Source, basic.Target, basic.Package)); return Task.CompletedTask; });
foreach (var item in new[] { basic.Package with { Trusted = false }, basic.Package with { Vendor = Vendor.Nvidia }, basic.Package with { HardwareIds = [@"PCI\VEN_1002&DEV_FFFF"] }, basic.Package with { Version = "" } })
    await Test("Reject bad package " + JsonSerializer.Serialize(item), () => { Reject(() => Planner.Create(basic.Before, basic.Source, basic.Target, item)); return Task.CompletedTask; });
await Test("Hardware boundaries reject partial IDs", () => { Assert(!Vendors.Matches(@"PCI\VEN_1002&DEV_747", @"PCI\VEN_1002&DEV_747E\1")); Assert(Vendors.Matches(@"PCI\VEN_1002&DEV_747E", @"PCI\VEN_1002&DEV_747E&SUBSYS_00112233\1")); return Task.CompletedTask; });
await Test("Upgrade never deletes previous package", async () => { var f = new Fake(Vendor.Amd, Vendor.Amd); var p = Plan(f); Assert(p.Remove.Count == 0); Assert((await new Engine(f).RunAsync(p, Session(), _ => { })).Verified); Assert(!f.Calls.Any(x => x.StartsWith("delete"))); });
await Test("Filters chipset/audio/boot-critical/unknown/active/malformed names", () =>
{
    var s = basic.Before with { Drivers = [basic.Before.Drivers[0],
        new("oem1.inf", "NVIDIA", "audio", "1", false), new("oem2.inf", "NVIDIA", "system", "1", false),
        new("oem3.inf", "NVIDIA", Planner.DisplayGuid, "1", true), new("oem4.inf", "Mystery NVIDIA Driver", Planner.DisplayGuid, "1", false),
        new("../../oem5.inf", "NVIDIA", Planner.DisplayGuid, "1", false), new("oem99.inf", "NVIDIA", Planner.DisplayGuid, "1", false),
        new("oem6.inf", "Intel", Planner.DisplayGuid, "1", false)] };
    Assert(Planner.Create(s, basic.Source, basic.Target, basic.Package).Remove.Select(d => d.Inf).SequenceEqual(["oem42.inf"])); return Task.CompletedTask;
});
await Test("Backup failure prevents every mutation", async () => { var f = Fake.Standard(); f.ExportCode = 5; await RejectAsync(() => new Engine(f).RunAsync(Plan(f), Session(), _ => { })); Assert(!f.Calls.Contains("install") && !f.Calls.Any(x => x.StartsWith("delete"))); });
await Test("Empty export success is rejected", async () => { var f = Fake.Standard(); f.EmptyExport = true; await RejectAsync(() => new Engine(f).RunAsync(Plan(f), Session(), _ => { })); Assert(!f.Calls.Contains("install")); });
await Test("Install failure preserves source", async () => { var f = Fake.Standard(); f.InstallCode = 1603; await RejectAsync(() => new Engine(f).RunAsync(Plan(f), Session(), _ => { })); Assert(!f.Calls.Any(x => x.StartsWith("delete"))); });
await Test("Success exit without active driver is not success", async () => { var f = Fake.Standard(); f.NoInstall = true; var o = await new Engine(f).RunAsync(Plan(f), Session(), _ => { }); Assert(!o.Verified && !f.Calls.Any(x => x.StartsWith("delete"))); });
await Test("3010 preserves source and persists required reboot", async () => { var f = Fake.Standard(); f.InstallCode = 3010; var session = Session(); var o = await new Engine(f).RunAsync(Plan(f), session, _ => { }); Assert(o.RebootRequired && !o.Verified && !f.Calls.Any(x => x.StartsWith("delete"))); using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(session,"state.json"))); Assert(state.RootElement.GetProperty("reboot").GetBoolean()); });
await Test("New pending reboot preserves source", async () => { var f = Fake.Standard(); f.PendingAfter = true; var o = await new Engine(f).RunAsync(Plan(f), Session(), _ => { }); Assert(o.RebootRequired && !o.Verified && !f.Calls.Any(x => x.StartsWith("delete"))); });
await Test("Final inventory restart remains pending and resumes verification", async () => {
    var f=Fake.Standard(); f.PendingFinal=true; var session=Session();
    var outcome=await new Engine(f).RunAsync(Plan(f),session,_=>{});
    Assert(outcome.RebootRequired && !outcome.Verified && f.Calls.Contains("delete:oem42.inf"));
    using(var state=JsonDocument.Parse(File.ReadAllText(Path.Combine(session,"state.json")))) {
        Assert(state.RootElement.GetProperty("phase").GetString()=="verify-after-reboot");
        Assert(!state.RootElement.GetProperty("completed").GetBoolean());
    }
    f.PendingFinal=false; f.BootId="BOOT-2";
    Assert((await new Continuation(f).RunAsync(session,_=>{})).Verified);
    Assert(f.Calls.Count(c=>c=="install")==1 && f.Calls.Count(c=>c=="delete:oem42.inf")==1);
});
await Test("Wrong installed version preserves source", async () => { var f = Fake.Standard(); f.VersionAfter = "9.9.9.9"; var o = await new Engine(f).RunAsync(Plan(f), Session(), _ => { }); Assert(!o.Verified && !f.Calls.Any(x => x.StartsWith("delete"))); });
await Test("Unsigned active driver preserves source", async () => { var f = Fake.Standard(); f.SignedAfter = false; var o = await new Engine(f).RunAsync(Plan(f), Session(), _ => { }); Assert(!o.Verified && !f.Calls.Any(x => x.StartsWith("delete"))); });
await Test("PnP device error preserves source", async () => { var f = Fake.Standard(); f.ProblemAfter = 43; var o = await new Engine(f).RunAsync(Plan(f), Session(), _ => { }); Assert(!o.Verified && !f.Calls.Any(x => x.StartsWith("delete"))); });
await Test("Changed computer rejected before writes", async () => { var f = Fake.Standard(); var p = Plan(f); f.Before = f.Before with { MachineId = "OTHER-PC" }; await RejectAsync(() => new Engine(f).RunAsync(p, Session(), _ => { })); Assert(!f.Calls.Contains("install")); });
await Test("Package hash change rejected before writes", async () => { var f = Fake.Standard(); var p = Plan(f); f.Package = f.Package with { Sha256 = "tampered" }; await RejectAsync(() => new Engine(f).RunAsync(p, Session(), _ => { })); Assert(!f.Calls.Contains("install")); });
await Test("Stale plan rejected before writes", async () => { var f = Fake.Standard(); var p = Plan(f); f.Before = f.Before with { Drivers = [] }; await RejectAsync(() => new Engine(f).RunAsync(p, Session(), _ => { })); Assert(!f.Calls.Contains("install")); });
await Test("Hot-plug source between install and deletion stops cleanup", async () => { var f = Fake.Standard(); f.HotPlug = true; await RejectAsync(() => new Engine(f).RunAsync(Plan(f), Session(), _ => { })); Assert(!f.Calls.Any(x => x.StartsWith("delete"))); });
await Test("Deletion failure marked stopped", async () => { var f = Fake.Standard(); f.DeleteCode = 5; var s = Session(); await RejectAsync(() => new Engine(f).RunAsync(Plan(f), s, _ => { })); Assert(File.ReadAllText(Path.Combine(s, "state.json")).Contains("stopped")); });
await Test("Cleanup restart stops before the next deletion and persists", async () =>
{
    var f = Fake.Standard(); f.DeleteCode = 3010; f.Before = f.Before with { Drivers = [.. f.Before.Drivers, f.Before.Drivers[0] with { Inf = "oem43.inf" }] }; var s = Session();
    var o = await new Engine(f).RunAsync(Plan(f), s, _ => { }); Assert(o.RebootRequired && !o.Verified && f.Calls.Count(c => c.StartsWith("delete")) == 1);
    using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(s, "state.json"))); Assert(state.RootElement.GetProperty("reboot").GetBoolean());
});
await Test("Restart survives a failed post-install inventory", async () =>
{
    var f = Fake.Standard(); f.InstallCode = 3010; f.ThrowAfterInstall = true; var s = Session(); await RejectAsync(() => new Engine(f).RunAsync(Plan(f), s, _ => { }));
    using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(s, "state.json"))); Assert(state.RootElement.GetProperty("reboot").GetBoolean()); Assert(!f.Calls.Any(c => c.StartsWith("delete")));
});
await Test("Missing destination GPU before cleanup preserves old packages", async () => { var f = Fake.Standard(); f.MissingTarget = true; await RejectAsync(() => new Engine(f).RunAsync(Plan(f), Session(), _ => { })); Assert(!f.Calls.Any(c => c.StartsWith("delete"))); });
await Test("Destination driver drift before cleanup preserves old packages", async () => { var f = Fake.Standard(); f.EarlyVersionDrift = true; await RejectAsync(() => new Engine(f).RunAsync(Plan(f), Session(), _ => { })); Assert(!f.Calls.Any(c => c.StartsWith("delete"))); });
await Test("False deletion success detected in final inventory", async () => { var f = Fake.Standard(); f.NoDelete = true; await RejectAsync(() => new Engine(f).RunAsync(Plan(f), Session(), _ => { })); });
await Test("Cancellation before install", async () => { var f = Fake.Standard(); using var c = new CancellationTokenSource(); c.Cancel(); await RejectAsync(() => new Engine(f).RunAsync(Plan(f), Session(), _ => { }, c.Token)); Assert(!f.Calls.Contains("install")); });
await Test("Cancellation after install never deletes", async () => { var f = Fake.Standard(); using var c = new CancellationTokenSource(); await RejectAsync(() => new Engine(f).RunAsync(Plan(f), Session(), text => { if (text == "installed") c.Cancel(); }, c.Token)); Assert(!f.Calls.Any(x => x.StartsWith("delete"))); });
await Test("OEM name adversarial inputs", () => { foreach (var inf in new[] { "*", "oem1.inf /force", "oem1.inf\n", "-oem1.inf", "oem.inf", @"C:\oem1.inf", "oem1.inf.exe", "oem١.inf" }) Assert(!Planner.IsOem(inf), inf); return Task.CompletedTask; });
await Test("1000 generated store entries protect non-display packages", () =>
{
    var random = new Random(42); var drivers = Enumerable.Range(1, 1000).Select(i => new Driver($"oem{i}.inf", random.Next(2) == 0 ? "NVIDIA" : "Intel", random.Next(3) == 0 ? Planner.DisplayGuid : "System", "1.0", random.Next(4) == 0)).ToArray();
    var p = Planner.Create(basic.Before with { Drivers = drivers }, basic.Source, basic.Target, basic.Package);
    Assert(p.Remove.All(d => !d.BootCritical && d.Provider == "NVIDIA" && d.ClassGuid == Planner.DisplayGuid && d.Inf != "oem99.inf")); return Task.CompletedTask;
});
await Test("Hash tree detects changed payload", async () => { var dir = Session(); Directory.CreateDirectory(dir); var p = Path.Combine(dir, "payload.sys"); await File.WriteAllTextAsync(p, "first"); var before = await WindowsPlatform.HashTreeAsync(dir); await File.WriteAllTextAsync(p, "second"); Assert(before != await WindowsPlatform.HashTreeAsync(dir)); });
await Test("Actual backend rejects unsigned EXE without running it", async () => { var p = Path.Combine(root, "unsigned.exe"); await File.WriteAllTextAsync(p, "not an executable"); await RejectMessageAsync(() => new WindowsPlatform().InspectAsync(p, Vendor.Amd), "Instalador sin firma válida"); });
await Test("Actual backend rejects non-display INF", async () => { var p = Path.Combine(root, "audio.inf"); await File.WriteAllTextAsync(p, "[Version]\nClass=Media\nClassGUID={fake}"); await RejectAsync(() => new WindowsPlatform().InspectAsync(p, Vendor.Nvidia)); });
await Test("Actual backend rejects catalog path traversal", async () => { var p = Path.Combine(root, "bad.inf"); await File.WriteAllTextAsync(p, "[Version]\nClass=Display\nClassGUID=" + Planner.DisplayGuid + "\nCatalogFile=../outside.cat"); await RejectAsync(() => new WindowsPlatform().InspectAsync(p, Vendor.Amd)); });
await Test("Actual backend rejects invalid OEM deletion without launching pnputil", async () => { await RejectAsync(() => new WindowsPlatform().DeleteAsync("* /force")); });
await Test("Unknown source/target vendors blocked", () => { Reject(() => Planner.Create(basic.Before, Vendor.Unknown, Vendor.Amd, basic.Package)); Reject(() => Planner.Create(basic.Before, Vendor.Nvidia, Vendor.Unknown, basic.Package)); return Task.CompletedTask; });
await Test("Changed EXE result verified without undocumented silent flags", async () => { var f = Fake.Standard(); f.Package = f.Package with { IsExe = true, Version = "" }; var o = await new Engine(f).RunAsync(Plan(f), Session(), _ => { }); Assert(o.Verified); });
await Test("Unchanged EXE result preserves source", async () => { var f = Fake.Standard(); f.Package = f.Package with { IsExe = true, Version = "" }; f.VersionAfter = "1.0.0.0"; var o = await new Engine(f).RunAsync(Plan(f), Session(), _ => { }); Assert(!o.Verified && !f.Calls.Any(c => c.StartsWith("delete"))); });
await Test("Final driver version drift rejected", async () => { var f = Fake.Standard(); f.FinalVersionDrift = true; await RejectAsync(() => new Engine(f).RunAsync(Plan(f), Session(), _ => { })); });
await Test("Locked session prevents concurrent run without system calls", async () => { var f = Fake.Standard(); var s = Session(); Directory.CreateDirectory(s); using var file = new FileStream(Path.Combine(s, "session.lock"), FileMode.Create, FileAccess.Write, FileShare.None); await RejectAsync(() => new Engine(f).RunAsync(Plan(f), s, _ => { })); Assert(f.Calls.Count == 0); });
foreach (var url in new[] { "http://www.amd.com/file.exe", "https://amd.com.evil.example/file.exe", "https://evilnvidia.com/file.exe", "https://amd.com:8443/file.exe", "https://user:pass@amd.com/file.exe", "file:///C:/file.exe", "https://127.0.0.1/file.exe" })
    await Test("Downloader rejects " + url, () => { Assert(!OfficialDownloader.Allowed(new Uri(url))); return Task.CompletedTask; });
await Test("Downloader accepts official domain boundaries", () => { Assert(OfficialDownloader.Allowed(new Uri("https://drivers.amd.com/file.exe"))); Assert(OfficialDownloader.Allowed(new Uri("https://international.download.nvidia.com/file.exe"))); Assert(OfficialDownloader.Allowed(new Uri("https://downloadmirror.intel.com/file.exe"))); Assert(!OfficialDownloader.Allowed(new Uri("https://intel.com.evil.example/file.exe"))); return Task.CompletedTask; });
await Test("Downloader rejects invalid URL without network", async () => { await RejectAsync(() => OfficialDownloader.DownloadAsync("not a url", root, _ => { })); });

if (args.Contains("--windows-readonly")) await Test("Real Windows inventory READ ONLY", async () => { var s = await new WindowsPlatform().ScanAsync(); Assert(!string.IsNullOrWhiteSpace(s.Os)); Console.WriteLine($"Inventory: adapters={s.Adapters.Count}; admin={s.Admin}; complete={s.Complete}. No driver modifications."); });
await Test("Present source GPU keeps its driver and can update destination", async () =>
{
    var f = Fake.Standard(); f.Before = f.Before with { Adapters = [f.Before.Adapters[0], new("integrated", @"PCI\VEN_10DE&DEV_0001\1", Vendor.Nvidia, "oem42.inf", "1.0.0.0", 0, true)] };
    Assert(Plan(f).Remove.Count == 0); Assert((await new Engine(f).RunAsync(Plan(f), Session(), _ => { })).Verified); Assert(!f.Calls.Any(c => c.StartsWith("delete")));
});
await Test("Windows Update path permits laptops and preserves all old packages", async () =>
{
    var f = Fake.Standard(); f.Before = f.Before with { Laptop = true }; f.Package = f.Package with { IsWindowsUpdate = true };
    Assert(Plan(f).Remove.Count == 0); Assert((await new Engine(f).RunAsync(Plan(f), Session(), _ => { })).Verified); Assert(!f.Calls.Any(c => c.StartsWith("delete")));
});
await Test("Intel providers recognized exactly", () => { Assert(Vendors.FromProvider("Intel Corporation") == Vendor.Intel); Assert(Vendors.FromProvider("Intel(R) Corporation") == Vendor.Intel); Assert(Vendors.FromProvider("Fake Intel Corporation") == Vendor.Unknown); return Task.CompletedTask; });
await Test("Catalog selects matching GPU, never another vendor or partial ID", () =>
{
    var id = @"PCI\VEN_8086&DEV_56A0"; var good = new DriverOffer(Guid.NewGuid().ToString(), 1, "Intel - Display - 32.0.101.6734", id, "Intel Corporation", DateTime.UtcNow, 400000000);
    var wrong = good with { Id = Guid.NewGuid().ToString(), HardwareId = @"PCI\VEN_8086&DEV_56A", Date = DateTime.UtcNow.AddDays(1) };
    Assert(DriverSelection.Select([wrong, good], [id], Vendor.Intel) == good); Assert(DriverSelection.Select([good], [id], Vendor.Amd) is null);
    Assert(DriverSelection.VersionOf(good.Title).ToString() == "32.0.101.6734"); return Task.CompletedTask;
});
await Test("Catalog prefers newest date then version, not response order", () =>
{
    var id = @"PCI\VEN_1002&DEV_747E"; var old = new DriverOffer(Guid.NewGuid().ToString(), 1, "AMD - Display - 32.0.1.5", id, "AMD", DateTime.UtcNow.AddYears(-1), 1);
    var newer = old with { Id = Guid.NewGuid().ToString(), Title = "AMD - Display - 32.0.1.100", Date = DateTime.UtcNow }; var latest = newer with { Id = Guid.NewGuid().ToString(), Title = "AMD - Display - 32.0.1.200" };
    Assert(DriverSelection.Select([latest, old, newer], [id], Vendor.Amd) == latest); return Task.CompletedTask;
});
await Test("Catalog never downgrades or reinstalls a healthy driver", () =>
{
    var id = @"PCI\VEN_8086&DEV_56A0"; var offer = new DriverOffer(Guid.NewGuid().ToString(), 1, "Intel - Display - 32.0.101.1000", id, "Intel", DateTime.UtcNow, 1);
    Assert(DriverSelection.Select([offer], [id], Vendor.Intel, new Version(32,0,101,2000)) is null);
    Assert(DriverSelection.Select([offer], [id], Vendor.Intel, new Version(32,0,101,1000)) is null);
    Assert(DriverSelection.Select([offer], [id], Vendor.Intel) == offer); return Task.CompletedTask;
});
await Test("Catalog rejects corrupt identity, revision, size and absent hardware", () =>
{
    var id = @"PCI\VEN_10DE&DEV_2786"; var offer = new DriverOffer(Guid.NewGuid().ToString(), 1, "NVIDIA - Display", id, "NVIDIA", DateTime.UtcNow, 1);
    foreach (var bad in new[] { offer with { Id = "'; exploit" }, offer with { Revision = 0 }, offer with { Bytes = -1 }, offer with { Bytes = 5L * 1024 * 1024 * 1024 } }) Assert(DriverSelection.Select([bad], [id], Vendor.Nvidia) is null);
    Assert(DriverSelection.Select([offer], [], Vendor.Nvidia) is null); return Task.CompletedTask;
});
await Test("Windows Update unknown version requires an actual active change", async () =>
{
    var f = Fake.Standard(); f.Package = f.Package with { IsWindowsUpdate = true, Version = "" }; f.NoInstall = true;
    Assert(!(await new Engine(f).RunAsync(Plan(f), Session(), _ => { })).Verified); Assert(!f.Calls.Any(c => c.StartsWith("delete")));
});
await Test("No-offer maintenance backs up before removing, never installs", async () =>
{
    var f = Fake.Standard(); var o = await new Maintenance(f).RunAsync(f.Before, f.Source, f.Target, Session(), _ => { }); Assert(o.Verified);
    Assert(!f.Calls.Contains("install")); Assert(f.Calls.IndexOf("delete:oem42.inf") > f.Calls.IndexOf("export:oem42.inf"));
});
await Test("No-offer maintenance rejects generic, unsigned and broken active drivers", async () =>
{
    foreach (var adapter in new[] { basic.Before.Adapters[0] with { Inf = "basicdisplay.inf" }, basic.Before.Adapters[0] with { Signed = false }, basic.Before.Adapters[0] with { Problem = 43 } })
    { var f = Fake.Standard(); f.Before = f.Before with { Adapters = [adapter] }; await RejectAsync(() => new Maintenance(f).RunAsync(f.Before, f.Source, f.Target, Session(), _ => { })); Assert(!f.Calls.Any(c => c.StartsWith("delete"))); }
});
await Test("No-offer maintenance stops on failed backup", async () => { var f = Fake.Standard(); f.ExportCode = 5; await RejectAsync(() => new Maintenance(f).RunAsync(f.Before, f.Source, f.Target, Session(), _ => { })); Assert(!f.Calls.Any(c => c.StartsWith("delete"))); });
await Test("No-offer maintenance detects false deletion success", async () => { var f = Fake.Standard(); f.NoDelete = true; await RejectAsync(() => new Maintenance(f).RunAsync(f.Before, f.Source, f.Target, Session(), _ => { })); });
await Test("No-offer cleanup restart stops and persists", async () =>
{
    var f = Fake.Standard(); f.DeleteCode = 3010; f.Before = f.Before with { Drivers = [.. f.Before.Drivers, f.Before.Drivers[0] with { Inf = "oem43.inf" }] }; var s = Session();
    var o = await new Maintenance(f).RunAsync(f.Before, f.Source, f.Target, s, _ => { }); Assert(o.RebootRequired && !o.Verified && f.Calls.Count(c => c.StartsWith("delete")) == 1);
    using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(s, "state.json"))); Assert(state.RootElement.GetProperty("reboot").GetBoolean());
});
await Test("No-offer maintenance can be cancelled before cleanup", async () => { var f = Fake.Standard(); using var c = new CancellationTokenSource(); c.Cancel(); await RejectAsync(() => new Maintenance(f).RunAsync(f.Before, f.Source, f.Target, Session(), _ => { }, c.Token)); Assert(!f.Calls.Any(x => x.StartsWith("delete"))); });
await Test("Automatic backend cannot execute an undiscovered update", async () => { var p = basic.Package with { IsWindowsUpdate = true }; await RejectAsync(() => new AutomaticPlatform().InstallAsync(p)); });
if (args.Contains("--windows-update-readonly")) await Test("Real Windows Update discovery READ ONLY", async () =>
{
    var platform = new AutomaticPlatform(); var snapshot = await platform.ScanAsync(); var adapter = snapshot.Adapters.First(a => a.Vendor is Vendor.Nvidia or Vendor.Amd or Vendor.Intel);
    var result = await platform.FindAsync(adapter); Console.WriteLine(result.Offer is null ? "No compatible offer in configured catalog. No downloads or installations." : "Compatible offer: " + result.Offer.Title + ". No downloads or installations.");
});
var installedInf = args.FirstOrDefault(a => a.StartsWith("--inspect-inf="))?[14..];
if (installedInf is not null) await Test("Real signed NVIDIA INF inspection READ ONLY", async () => { var p = await new WindowsPlatform().InspectAsync(installedInf, Vendor.Nvidia); Assert(p.Trusted && !p.IsExe && p.HardwareIds.Count > 0); Console.WriteLine($"Signed INF: version={p.Version}; PCI IDs={p.HardwareIds.Count}; SHA-256={p.Sha256[..16]}. No installation/export/removal."); });
var output = args.FirstOrDefault(a => a.StartsWith("--report="))?[9..];
await Test("Independent graphics backup exports only Display OEM packages without driver writes", async () => {
    var f=Fake.Standard(); f.Before=f.Before with {Drivers=[..f.Before.Drivers,new("oem77.inf","Intel","other-class","1",false)]};
    var path=await new GraphicsBackup(f).RunAsync(f.Before,Session(),_=>{});
    Assert(f.Calls.Count(c=>c.StartsWith("export:"))==2 && !f.Calls.Any(c=>c=="install"||c.StartsWith("delete:")||c=="restore-point"));
    await BackupIntegrity.VerifyAsync(Path.Combine(path,"backup","oem42.inf"));
    Assert(File.ReadAllText(Path.Combine(path,"state.json")).Contains("\"backupOnly\":true"));
});
await Test("Backup detects altered bytes before restore", async () => {var p=await new GraphicsBackup(Fake.Standard()).RunAsync(basic.Before,Session(),_=>{});var b=Path.Combine(p,"backup","oem42.inf");await File.WriteAllTextAsync(Path.Combine(b,"original.inf"),"tampered");await RejectAsync(()=>BackupIntegrity.VerifyAsync(b));});
await Test("Backup detects added files including nested manifest names", async () => {var p=await new GraphicsBackup(Fake.Standard()).RunAsync(basic.Before,Session(),_=>{});var b=Path.Combine(p,"backup","oem42.inf");Directory.CreateDirectory(Path.Combine(b,"extra"));await File.WriteAllTextAsync(Path.Combine(b,"extra","integrity.json"),"added");await RejectAsync(()=>BackupIntegrity.VerifyAsync(b));});
await Test("Backup detects deleted files", async () => {var p=await new GraphicsBackup(Fake.Standard()).RunAsync(basic.Before,Session(),_=>{});var b=Path.Combine(p,"backup","oem42.inf");File.Delete(Path.Combine(b,"original.inf"));await RejectAsync(()=>BackupIntegrity.VerifyAsync(b));});
await Test("Backup never follows manifest traversal paths", async () => {var p=await new GraphicsBackup(Fake.Standard()).RunAsync(basic.Before,Session(),_=>{});var b=Path.Combine(p,"backup","oem42.inf");await File.WriteAllTextAsync(Path.Combine(b,"integrity.json"),"{\"Schema\":1,\"Files\":[{\"Path\":\"../../outside.inf\",\"Bytes\":1,\"Sha256\":\"x\"}]}");await RejectAsync(()=>BackupIntegrity.VerifyAsync(b));});
await Test("Failed or empty export cannot become completed backup", async () => {foreach(var f in new[]{new Fake(Vendor.Nvidia,Vendor.Amd){ExportCode=5},new Fake(Vendor.Nvidia,Vendor.Amd){EmptyExport=true}}){var s=Session();await RejectAsync(()=>new GraphicsBackup(f).RunAsync(f.Before,s,_=>{}));var state=File.ReadAllText(Path.Combine(s,"state.json"));Assert(!state.Contains("\"completed\":true")&&state.Contains("\"backupOnly\":true"));}});
await Test("Backup rejects incomplete non-admin or duplicate inventory", async () => {foreach(var s in new[]{basic.Before with{Complete=false},basic.Before with{Admin=false},basic.Before with{Drivers=[..basic.Before.Drivers,basic.Before.Drivers[0]]}})await RejectAsync(()=>new GraphicsBackup(Fake.Standard()).RunAsync(s,Session(),_=>{}));});
await Test("Cancelled backup never exports", async () => {var f=Fake.Standard();using var c=new CancellationTokenSource();c.Cancel();await RejectAsync(()=>new GraphicsBackup(f).RunAsync(f.Before,Session(),_=>{},c.Token));Assert(!f.Calls.Any(x=>x.StartsWith("export:")));});
await Test("Intel Arc destination preserves an Intel integrated GPU", async () => {
    var f=new Fake(Vendor.Nvidia,Vendor.Intel); var arc=f.Before.Adapters[0];
    var integrated=new Adapter("Intel UHD",@"PCI\VEN_8086&DEV_1234\2",Vendor.Intel,"oem44.inf","1.0.0.0",0,true);
    f.Before=f.Before with {Adapters=[arc,integrated],Drivers=[..f.Before.Drivers,new("oem44.inf","Intel",Planner.DisplayGuid,"1.0.0.0",false)]};
    var plan=Planner.Create(f.Before,f.Source,f.Target,f.Package,arc.Id,true);
    var outcome=await new Engine(f).RunAsync(plan,Session(),_=>{}); Assert(outcome.Verified); Assert((await f.ScanAsync()).Adapters.Contains(integrated)); Assert(!f.Calls.Contains("delete:oem44.inf"));
});
await Test("Automatic cleanup covers all absent GPU brands", async () => {
    var f=Fake.Standard();f.Before=f.Before with {Drivers=[..f.Before.Drivers,new("oem44.inf","Intel",Planner.DisplayGuid,"1.0.0.0",false)]};
    var plan=Planner.Create(f.Before,f.Source,f.Target,f.Package,f.Before.Adapters[0].Id,true);
    Assert(plan.Remove.Count==2); Assert((await new Engine(f).RunAsync(plan,Session(),_=>{})).Verified);Assert(f.Calls.Contains("delete:oem44.inf"));
});
await Test("Automatic cleanup preserves every still-present brand", () => {
    var f=Fake.Standard();f.Before=f.Before with {Adapters=[..f.Before.Adapters,new("Intel UHD",@"PCI\VEN_8086&DEV_1234\2",Vendor.Intel,"oem44.inf","1",0,true)],Drivers=[..f.Before.Drivers,new("oem44.inf","Intel",Planner.DisplayGuid,"1",false),new("oem45.inf","Intel",Planner.DisplayGuid,"0",false)]};
    var plan=Planner.Create(f.Before,f.Source,f.Target,f.Package,f.Before.Adapters[0].Id,true);Assert(plan.Remove.Count==1&&plan.Remove[0].Inf=="oem42.inf");return Task.CompletedTask;
});
async Task<(Fake Fake,string Path)> WaitingForRestart() { var f=Fake.Standard();f.InstallCode=3010;var path=Session();var o=await new Engine(f).RunAsync(Plan(f),path,_=>{});Assert(o.RebootRequired&&!o.Verified);return(f,path); }
await Test("Continuation verifies after reboot and finishes without reinstalling", async () => {
    var(f,path)=await WaitingForRestart();f.BootId="BOOT-2";var o=await new Continuation(f).RunAsync(path,_=>{});Assert(o.Verified&&!o.RebootRequired);Assert(f.Calls.Count(c=>c=="install")==1&&f.Calls.Contains("delete:oem42.inf"));
});
await Test("Continuation waits for an actual new boot", async () => { var(f,path)=await WaitingForRestart();var o=await new Continuation(f).RunAsync(path,_=>{});Assert(o.RebootRequired&&!f.Calls.Any(c=>c.StartsWith("delete"))); });
await Test("Continuation rejects wrong hardware, signature and version", async () => {
    foreach(var kind in new[]{"hardware","signature","version","machine"}) {var(f,path)=await WaitingForRestart();f.BootId="BOOT-2";if(kind=="hardware")f.Before=f.Before with {Adapters=[f.Before.Adapters[0] with {Id=@"PCI\VEN_1002&DEV_747E\OTHER"}]};if(kind=="signature")f.SignedAfter=false;if(kind=="version")f.VersionAfter="9.9.9.9";if(kind=="machine")f.Before=f.Before with {MachineId="OTHER"};await RejectAsync(()=>new Continuation(f).RunAsync(path,_=>{}));Assert(!f.Calls.Any(c=>c.StartsWith("delete")));}
});
await Test("Continuation refuses cleanup without the saved backup", async () => {var(f,path)=await WaitingForRestart();f.BootId="BOOT-2";File.Delete(Path.Combine(path,"backup","oem42.inf","original.inf"));await RejectAsync(()=>new Continuation(f).RunAsync(path,_=>{}));Assert(!f.Calls.Any(c=>c.StartsWith("delete")));});
await Test("Continuation tolerates packages already removed before restart", async () => {var(f,path)=await WaitingForRestart();f.BootId="BOOT-2";f.Before=f.Before with {Drivers=f.Before.Drivers.Where(d=>d.Inf!="oem42.inf").ToArray()};Assert((await new Continuation(f).RunAsync(path,_=>{})).Verified);Assert(!f.Calls.Any(c=>c.StartsWith("delete")));});
await Test("Continuation cancellation never removes a package", async () => {var(f,path)=await WaitingForRestart();f.BootId="BOOT-2";using var cts=new CancellationTokenSource();cts.Cancel();await RejectAsync(()=>new Continuation(f).RunAsync(path,_=>{},cts.Token));Assert(!f.Calls.Any(c=>c.StartsWith("delete")));});
await Test("Continuation refuses completed sessions", async () => {var f=Fake.Standard();var path=Session();await new Engine(f).RunAsync(Plan(f),path,_=>{});f.BootId="BOOT-2";await RejectAsync(()=>new Continuation(f).RunAsync(path,_=>{}));});
await Test("Continuation cleanup restart waits for another boot", async () => {var(f,path)=await WaitingForRestart();f.BootId="BOOT-2";f.DeleteCode=3010;Assert((await new Continuation(f).RunAsync(path,_=>{})).RebootRequired);var count=f.Calls.Count(c=>c.StartsWith("delete"));Assert((await new Continuation(f).RunAsync(path,_=>{})).RebootRequired);Assert(f.Calls.Count(c=>c.StartsWith("delete"))==count);f.BootId="BOOT-3";f.DeleteCode=0;Assert((await new Continuation(f).RunAsync(path,_=>{})).Verified);});
await Test("Continuation stops if an old GPU returns after restart", async () => {var(f,path)=await WaitingForRestart();f.BootId="BOOT-2";f.Before=f.Before with {Adapters=[..f.Before.Adapters,new("NVIDIA reconnected",@"PCI\VEN_10DE&DEV_1111\1",Vendor.Nvidia,"oem42.inf","1",0,true)]};await RejectAsync(()=>new Continuation(f).RunAsync(path,_=>{}));Assert(!f.Calls.Any(c=>c.StartsWith("delete")));});
var registrationExe=args.FirstOrDefault(a=>a.StartsWith("--resume-registration="))?[22..];
if(registrationExe is not null) await Test("Real isolated guest RunOnce registration and cleanup",async()=>{
    if(Environment.MachineName!="SWAPTESTVM" && Environment.UserName!="WDAGUtilityAccount")throw new InvalidOperationException("Registration testing is restricted to the private guest.");
    if(Path.GetFullPath(registrationExe)!=@"C:\SwapInput\Swap.exe")throw new InvalidOperationException("Unexpected guest app path.");
    Assert(ResumeRegistration.Load() is null,"A real continuation is already pending.");
    using var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce");
    Assert(key.GetValue("Swap-KraicDev-Continue") is null);
    var prior=key.GetValueNames().OrderBy(x=>x).ToArray();
    var session=Path.Combine(root,"Sessions",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(session);await File.WriteAllTextAsync(Path.Combine(session,"state.json"),"{}");
    try {await ResumeRegistration.ArmAsync(registrationExe,session);var pending=ResumeRegistration.Load();Assert(pending?.Session==session);Assert(pending!.ExeSha256==await WindowsPlatform.HashFileAsync(registrationExe));Assert(key.GetValue("Swap-KraicDev-Continue") is string command&&command.EndsWith(" --continue"));}
    finally {ResumeRegistration.Clear();}
    Assert(ResumeRegistration.Load() is null&&key.GetValue("Swap-KraicDev-Continue") is null);Assert(key.GetValueNames().OrderBy(x=>x).SequenceEqual(prior));
});
var healthAdapter=basic.Before.Adapters[0];
GraphicsDiagnostics HealthData() => new(DateTimeOffset.UtcNow,[new("DISPLAY1","Monitor",healthAdapter.Id,2560,1440,60,144)],
    [new(@"PCI\VEN_1002&DEV_747E","GPU",8L*1024*1024*1024,true,true,0xB000,"")],[],true,true,[]);
await Test("Health reports absent GPU without certifying a connection",()=>{Assert(GraphicsHealth.Evaluate(null,HealthData()).Single().Code=="gpuAbsent");return Task.CompletedTask;});
await Test("Health detects signed OEM and hardware Direct3D separately",()=>{var a=healthAdapter with {Inf="oem99.inf"};var h=GraphicsHealth.Evaluate(a,HealthData());Assert(h.Any(f=>f.Code=="driverOk")&&h.Any(f=>f.Code=="direct3dOk"));return Task.CompletedTask;});
await Test("Basic display is not a healthy vendor driver",()=>{Assert(GraphicsHealth.Evaluate(healthAdapter with {Inf="display.inf"},HealthData()).Any(f=>f.Code=="driverReview"&&f.Attention));return Task.CompletedTask;});
await Test("PnP 43 and unsigned drivers need attention",()=>{var h=GraphicsHealth.Evaluate(healthAdapter with {Problem=43,Signed=false},HealthData());Assert(h.Any(f=>f.Code=="pnpError"&&f.Attention)&&h.Any(f=>f.Code=="driverReview"));return Task.CompletedTask;});
await Test("Software renderer cannot certify GPU Direct3D",()=>{var d=HealthData();Assert(GraphicsHealth.Evaluate(healthAdapter,d with {Probes=[d.Probes[0] with {Hardware=false}]}).Any(f=>f.Code=="direct3dUnknown"));return Task.CompletedTask;});
await Test("Identical PCI models do not receive individual certification",()=>{var d=HealthData();Assert(GraphicsHealth.Evaluate(healthAdapter,d with {Probes=[d.Probes[0],d.Probes[0]]}).Any(f=>f.Code=="direct3dUnknown"));return Task.CompletedTask;});
await Test("Failed hardware Direct3D is visible",()=>{var d=HealthData();Assert(GraphicsHealth.Evaluate(healthAdapter,d with {Probes=[d.Probes[0] with {Direct3D=false,Error="0x887A0004"}]}).Any(f=>f.Code=="direct3dFailed"&&f.Attention));return Task.CompletedTask;});
await Test("Display frequency compares only available current-resolution modes",()=>{Assert(GraphicsHealth.Evaluate(healthAdapter,HealthData()).Any(f=>f.Code=="refreshAvailable"));var d=HealthData();Assert(!GraphicsHealth.Evaluate(healthAdapter,d with {Displays=[d.Displays[0] with {Hz=1}]}).Any(f=>f.Code=="refreshAvailable"));return Task.CompletedTask;});
await Test("Unknown diagnostics cannot claim verified displays or errors",()=>{var h=GraphicsHealth.Evaluate(healthAdapter,HealthData() with {DisplaysComplete=false,EventsComplete=false});Assert(h.Any(f=>f.Code=="displayUnknown")&&h.Any(f=>f.Code=="eventsUnknown")&&!h.Any(f=>f.Code=="noDisplayErrors"));return Task.CompletedTask;});
await Test("Checkpoint rejects other machines and simulated environments",()=>{var d=HealthData();var c=GraphicsHealth.Capture(basic.Before,d,true);Reject(()=>GraphicsHealth.Compare(c,basic.Before,d,false));Reject(()=>GraphicsHealth.Compare(c,basic.Before with {MachineId="OTHER"},d,true));return Task.CompletedTask;});
await Test("Checkpoint notices driver replacement without labeling it failure",()=>{var d=HealthData();var c=GraphicsHealth.Capture(basic.Before,d,false);var s=basic.Before with {Adapters=[healthAdapter with {Version="3.0",Inf="oem50.inf"}]};var result=GraphicsHealth.Compare(c,s,d,false);Assert(result.Any(f=>f.Code=="driverChanged"&&!f.Attention)&&!result.Any(f=>f.Code=="driverRegressed"));return Task.CompletedTask;});
await Test("Checkpoint finds Basic display regression and missing GPU",()=>{var d=HealthData();var c=GraphicsHealth.Capture(basic.Before,d,false);Assert(GraphicsHealth.Compare(c,basic.Before with {Adapters=[healthAdapter with {Inf="display.inf"}]},d,false).Any(f=>f.Code=="driverRegressed"&&f.Attention));Assert(GraphicsHealth.Compare(c,basic.Before with {Adapters=[]},d,false).Any(f=>f.Code=="gpuRemoved"));return Task.CompletedTask;});
await Test("Checkpoint detects resolution, refresh and display loss",()=>{var d=HealthData();var c=GraphicsHealth.Capture(basic.Before,d,false);Assert(GraphicsHealth.Compare(c,basic.Before,d with {Displays=[d.Displays[0] with {Hz=30}]},false).Any(f=>f.Code=="displayReduced"));Assert(GraphicsHealth.Compare(c,basic.Before,d with {Displays=[]},false).Any(f=>f.Code=="displayRemoved"));return Task.CompletedTask;});
await Test("Checkpoint distinguishes new incidents from historical incidents",()=>{var d=HealthData();var c=GraphicsHealth.Capture(basic.Before,d,false);Assert(!GraphicsHealth.Compare(c,basic.Before,d with {Incidents=[new(c.Generated.AddHours(-1),"Display",4101,"TDR")]},false).Any(f=>f.Code=="newDisplayErrors"));Assert(GraphicsHealth.Compare(c,basic.Before,d with {Incidents=[new(c.Generated.AddSeconds(1),"Display",4101,"TDR")]},false).Any(f=>f.Code=="newDisplayErrors"));return Task.CompletedTask;});
await Test("Checkpoint preserves unknown event status",()=>{var d=HealthData();var c=GraphicsHealth.Capture(basic.Before,d,false);var result=GraphicsHealth.Compare(c,basic.Before,d with {EventsComplete=false},false);Assert(result.Any(f=>f.Code=="eventsUnknown")&&!result.Any(f=>f.Code=="checkpointUnchanged"));return Task.CompletedTask;});
await Test("Malformed checkpoint collections are rejected",()=>{var d=HealthData();var c=GraphicsHealth.Capture(basic.Before,d,false);Reject(()=>GraphicsHealth.Compare(c with {Adapters=null!},basic.Before,d,false));Reject(()=>GraphicsHealth.Compare(c with {Displays=null!},basic.Before,d,false));Reject(()=>GraphicsHealth.Compare(c with {Adapters=[healthAdapter with {Inf=null!}]},basic.Before,d,false));return Task.CompletedTask;});
if(args.Contains("--graphics-native")) await Test("Real isolated guest display, Direct3D enumeration and recent events",async()=>{
    if(Environment.MachineName!="SWAPTESTVM" && Environment.UserName!="WDAGUtilityAccount")throw new InvalidOperationException("Native graphics testing belongs in the private guest.");
    var d=await GraphicsInspector.ReadAsync();Assert(d.DisplaysComplete&&d.Displays.Count>0&&d.Probes.Count>0&&d.EventsComplete);
    Assert(d.Displays.All(x=>x.Width>0&&x.Height>0));
    Console.WriteLine($"Native graphics: displays={d.Displays.Count}; probes={d.Probes.Count}; eventsComplete={d.EventsComplete}. No mode changes.");
});
if (output is not null) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, passed = results.Count - failed, failed, results, environment = "simulated workflow; explicitly requested native guest observations recorded individually", temporaryDirectory = root }, new JsonSerializerOptions { WriteIndented = true })); }
Console.WriteLine($"RESULT {results.Count - failed}/{results.Count} passed; {failed} failed.");
return failed == 0 ? 0 : 1;

sealed class Fake(Vendor source, Vendor target) : IPlatform
{
    public Vendor Source => source; public Vendor Target => target;
    public Snapshot Before { get; set; } = new("Windows test", "TEST-PC", true, false, false, false, 20L * 1024 * 1024 * 1024,
        [new("GPU", Hardware(target) + @"\1", target, "oem99.inf", "1.0.0.0", 0, true)],
        [new("oem42.inf", Provider(source), Planner.DisplayGuid, "1.0.0.0", false),
         new("oem99.inf", Provider(target), Planner.DisplayGuid, "1.0.0.0", false)], true, []);
    private static string Hardware(Vendor v) => v == Vendor.Amd ? @"PCI\VEN_1002&DEV_747E" : v == Vendor.Intel ? @"PCI\VEN_8086&DEV_56A0" : @"PCI\VEN_10DE&DEV_2786";
    private static string Provider(Vendor v) => v == Vendor.Nvidia ? "NVIDIA" : v == Vendor.Intel ? "Intel Corporation" : "Advanced Micro Devices, Inc.";
    public Package Package { get; set; } = new("test.inf", target, "2.0.0.0", [Hardware(target)], "HASH", true, false);
    public List<string> Calls { get; } = [];
    public int ExportCode, InstallCode, DeleteCode, ProblemAfter;
    public bool EmptyExport, NoInstall, NoDelete, PendingAfter, PendingFinal, HotPlug, FinalVersionDrift, ThrowAfterInstall, MissingTarget, EarlyVersionDrift;
    public bool SignedAfter = true;
    public string BootId = "BOOT-1";
    public string VersionAfter = "2.0.0.0";
    private bool installed; private int scansAfter;
    private readonly HashSet<string> deletedInfs=[];
    public static Fake Standard() => new(Vendor.Nvidia, Vendor.Amd);
    public Task<Snapshot> ScanAsync()
    {
        Calls.Add("scan"); if (!installed) return Task.FromResult(Before with {BootId=BootId, Drivers=Before.Drivers.Where(d=>!deletedInfs.Contains(d.Inf)).ToArray()});
        scansAfter++;
        if (ThrowAfterInstall) throw new IOException("Simulated failed inventory");
        var adapters = Before.Adapters.Select(a => a.Id==Before.Adapters[0].Id ? a with { Version = FinalVersionDrift && scansAfter >= 3 || EarlyVersionDrift && scansAfter >= 2 ? "3.3.3.3" : VersionAfter, Signed = SignedAfter, Problem = ProblemAfter } : a).ToList();
        if (MissingTarget && scansAfter >= 2) adapters.Clear();
        if (HotPlug && scansAfter >= 2) adapters.Add(new("source hotplug", @"PCI\VEN_10DE&DEV_1234\1", source, "oem42.inf", "1", 0, true));
        return Task.FromResult(Before with { BootId=BootId, PendingReboot = PendingAfter || PendingFinal && scansAfter >= 3, Adapters = adapters, Drivers = Before.Drivers.Where(d=>!deletedInfs.Contains(d.Inf)).ToArray() });
    }
    public Task<Package> InspectAsync(string path, Vendor v) { Calls.Add("inspect"); return Task.FromResult(Package); }
    public async Task<CommandResult> ExportAsync(string inf, string destination) { Calls.Add("export:" + inf); if (!EmptyExport) await File.WriteAllTextAsync(Path.Combine(destination, "original.inf"), "fake backup"); return new(ExportCode, "exported"); }
    public Task<CommandResult> InstallAsync(Package p) { Calls.Add("install"); installed = !NoInstall && InstallCode is 0 or 3010; return Task.FromResult(new CommandResult(InstallCode, "installed")); }
    public Task<CommandResult> DeleteAsync(string inf) { Calls.Add("delete:" + inf); if(!NoDelete&&DeleteCode==0)deletedInfs.Add(inf); return Task.FromResult(new CommandResult(DeleteCode, "deleted")); }
    public Task<string> RestorePointAsync() { Calls.Add("restore-point"); return Task.FromResult("simulated restore point"); }
}
