// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Text.Json;
using Swap.Core;
using Swap.Windows;

// KraicDev. Deep cleanup, disconnected-device handling and new diagnostics. Simulated platforms only; no system changes.
static class DeepCleanTests
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> test, Action<bool, string> assert, string root)
    {
        void Assert(bool value, string message = "Assertion failed") => assert(value, message);
        int counter = 0;
        // Cleanup refuses anything inside Windows; under SYSTEM the temporary folder is there, so use the system drive instead.
        var sandboxRoot = DeepCleanPlanner.Within(root, Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ? Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "SwapTestSandbox") : root;
        string Sandbox() => Path.Combine(sandboxRoot, "clean-" + Guid.NewGuid().ToString("N"));
        string Session(string sandbox) => Path.Combine(sandbox, "Sessions", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-CLEAN-" + (++counter).ToString("x6"));
        string Secure(string sandbox) { var path = Path.Combine(sandbox, "Secure"); Directory.CreateDirectory(path); return path; }
        static HashSet<string> All(DeepCleanPlan plan) => plan.Items.Where(i => i.Selected).Select(i => i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        static Task Done() => Task.CompletedTask;

        await test("INF reader resolves decorated model sections, strings, quotes and continuations", () =>
        {
            var inf = """
                [Version]
                Signature="$Windows NT$"
                Class=MEDIA ; comment
                Provider=%Vendor%
                [Manufacturer]
                %Vendor% = Devices, NTamd64.10.0...16299, NTarm64
                [Devices.NTamd64.10.0...16299]
                %Desc% = Install, HDAUDIO\FUNC_01&VEN_10DE&DEV_0094 , "HDAUDIO\FUNC_01&VEN_10DE&DEV_0095"
                %Desc2% = Install, \
                  PCI\VEN_10DE&DEV_1AD7
                [Devices.NTarm64]
                %Desc% = Install, USB\VID_0955&PID_9000 ; arm
                [Install]
                CopyFiles = PCI\VEN_FFFF&DEV_FFFF
                [Strings]
                Vendor = "NVIDIA Corporation"
                """;
            var summary = InfReader.Read(inf);
            Assert(summary.Class == "MEDIA" && summary.Provider == "NVIDIA Corporation", summary.Class + "/" + summary.Provider);
            Assert(summary.HardwareIds.SequenceEqual([@"HDAUDIO\FUNC_01&VEN_10DE&DEV_0094", @"HDAUDIO\FUNC_01&VEN_10DE&DEV_0095", @"PCI\VEN_10DE&DEV_1AD7", @"USB\VID_0955&PID_9000"]), string.Join(",", summary.HardwareIds));
            return Done();
        });
        await test("Command parser judges hosted modules, quoted and unquoted paths", () =>
        {
            Assert(CommandFiles.Parse(@"C:\WINDOWS\SysWOW64\RunDll32.EXE ""C:\Program Files\NVIDIA Corporation\Installer2\InstallerCore\NVI2.DLL"",UninstallPackage Display.Driver").Single() == @"C:\Program Files\NVIDIA Corporation\Installer2\InstallerCore\NVI2.DLL");
            Assert(CommandFiles.Parse(@"C:\Program Files\NVIDIA Corporation\Display.NvContainer\NVDisplay.Container.exe -s NVDisplay.ContainerLocalSystem").Single().EndsWith(@"Display.NvContainer\NVDisplay.Container.exe"));
            Assert(CommandFiles.Parse(@"""C:\Program Files\AMD\CNext\CNext\RadeonSoftware.exe"" -minimized").Single() == @"C:\Program Files\AMD\CNext\CNext\RadeonSoftware.exe");
            Assert(CommandFiles.Parse(@"\SystemRoot\System32\drivers\nvlddmkm.sys").Single().EndsWith(@"System32\drivers\nvlddmkm.sys", StringComparison.OrdinalIgnoreCase));
            Assert(CommandFiles.Parse(@"MsiExec.exe /X{F5C4D1F8-5B2A-4D17-9E7E-2A0C3A2F8E3B}").Count == 0 && CommandFiles.Parse("").Count == 0);
            return Done();
        });
        await test("Integrated-graphics heuristic follows processor model suffixes", () =>
        {
            foreach (var (vendor, name, expected) in new[] {
                ("GenuineIntel", "Intel(R) Core(TM) i5-12400F CPU @ 2.50GHz", false), ("GenuineIntel", "Intel(R) Core(TM) i7-13700KF", false),
                ("GenuineIntel", "Intel(R) Core(TM) i7-13700K", true), ("GenuineIntel", "Intel(R) Core(TM) Ultra 7 265KF", false),
                ("GenuineIntel", "Intel(R) Xeon(R) W-2245 CPU @ 3.90GHz", false), ("GenuineIntel", "Intel processor", true),
                ("AuthenticAMD", "AMD Ryzen 7 5800X 8-Core Processor", false), ("AuthenticAMD", "AMD Ryzen 5 5600G with Radeon Graphics", true),
                ("AuthenticAMD", "AMD Ryzen 5 7600 6-Core Processor", true), ("AuthenticAMD", "AMD Ryzen 5 7500F 6-Core Processor", false),
                ("AuthenticAMD", "AMD Ryzen 7 9800X3D 8-Core Processor", true), ("AuthenticAMD", "AMD Ryzen Threadripper 3970X", false),
                ("AuthenticAMD", "AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", true), ("CentaurHauls", "Other", false) })
                Assert(DeepCleanPlanner.LikelyIntegratedGraphics(vendor, name) == expected, name);
            return Done();
        });
        await test("Provider identity is exact and recognizes common vendor spellings", () =>
        {
            Assert(Vendors.FromProvider("AMD") == Vendor.Amd && Vendors.FromProvider(" NVIDIA Corporation ") == Vendor.Nvidia && Vendors.FromProvider("INTEL") == Vendor.Intel);
            Assert(Vendors.FromProvider("Mystery NVIDIA Driver") == Vendor.Unknown && Vendors.FromProvider("AMD Inc") == Vendor.Unknown);
            return Done();
        });

        // ---- Planner ----
        async Task<RemnantInventory> Demo(Action<DemoCleanupPlatform>? setup = null, bool physx = false, string cpu = "AuthenticAMD", string cpuName = "AMD Ryzen 7 5800X 8-Core Processor")
        {
            var platform = new DemoCleanupPlatform(Sandbox()) { Physx = physx, CpuVendor = cpu, CpuName = cpuName };
            setup?.Invoke(platform);
            return await platform.InventoryAsync();
        }
        await test("NVIDIA to AMD plan removes owned devices, packages and software without touching AMD chipset", async () =>
        {
            var inventory = await Demo();
            Assert(DeepCleanPlanner.Candidates(inventory).SequenceEqual([Vendor.Nvidia]));
            var plan = DeepCleanPlanner.Create(inventory, [Vendor.Nvidia]);
            var keys = plan.Items.Select(i => i.Key).ToArray();
            foreach (var expected in new[] { @"device:PCI\VEN_10DE&DEV_2484\DEMO", @"device:HDAUDIO\FUNC_01&VEN_10DE&DEV_0094\DEMO", @"device:PCI\VEN_10DE&DEV_228B\DEMO",
                @"device:ROOT\UNNAMED_DEVICE\0000", "package:oem42.inf", "package:oem43.inf", "package:oem44.inf", "package:oem45.inf",
                "service:nvlddmkm", "service:NVDisplay.ContainerLocalSystem", "service:NvContainerLocalSystem", "appx:NVIDIACorp.NVIDIAControlPanel",
                @"registry:HKLM\SOFTWARE\NVIDIA Corporation", "process:424242" })
                Assert(keys.Contains(expected, StringComparer.OrdinalIgnoreCase), "missing " + expected);
            Assert(!keys.Any(k => k.Contains("oem60") || k.Contains("oem99") || k.Contains("VEN_1022") || k.Contains("VEN_1002") || k.Contains("amdsmbus")), "AMD item planned");
            Assert(plan.Items.Single(i => i.Key == @"device:ROOT\UNNAMED_DEVICE\0000").Reason == "virtualDevice");
            Assert(plan.Items.Where(i => i.Kind != RemnantKind.Uninstaller).All(i => i.Selected));
            var usb = inventory with { Packages = inventory.Packages.Select(p => p.Inf == "oem45.inf" ? p with { BootCritical = true } : p).ToArray() };
            Assert(DeepCleanPlanner.Create(usb, [Vendor.Nvidia]).Items.Single(i => i.Key == "package:oem45.inf") is { Selected: false, Reason: "bootCriticalUnused" }, "boot-critical class package must be opt-in");
        });
        await test("A connected brand is never cleaned and is reported", async () =>
        {
            var inventory = await Demo(p => p.SourceReturns = true);
            var plan = DeepCleanPlanner.Create(inventory, [Vendor.Nvidia, Vendor.Amd]);
            Assert(plan.Items.Count == 0 && plan.Notes.Contains("vendorPresent:Nvidia") && plan.Notes.Contains("vendorPresent:Amd"));
            Assert(DeepCleanPlanner.Candidates(inventory).Count == 0);
        });
        await test("Packages used by a connected device or mixed hardware are kept", async () =>
        {
            var inventory = await Demo();
            inventory = inventory with { Devices = [.. inventory.Devices, new(@"USB\VID_0955&PID_7020\X", "USB", "oem45.inf", "", true, [@"USB\VID_0955&PID_7020"], "Shield")],
                Packages = [.. inventory.Packages, new("oem70.inf", "nvmixed.inf", "NVIDIA", "MEDIA", "1", false, @"C:\Store\mixed", 10, [@"HDAUDIO\FUNC_01&VEN_10DE&DEV_0001", @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0887"])] };
            var plan = DeepCleanPlanner.Create(inventory, [Vendor.Nvidia]);
            Assert(plan.Kept.Any(k => k.Key == "package:oem45.inf" && k.Reason == "packageActive") && !plan.Items.Any(i => i.Key == "package:oem45.inf"));
            Assert(plan.Kept.Any(k => k.Key == "package:oem70.inf" && k.Reason == "packageMixed"));
        });
        await test("PhysX stays: folder, registry settings and its Windows Installer entry are kept", async () =>
        {
            var plan = DeepCleanPlanner.Create(await Demo(physx: true), [Vendor.Nvidia]);
            Assert(plan.Kept.Any(k => k.Reason == "physxKept" && k.Target.EndsWith("PhysX")));
            Assert(plan.Kept.Any(k => k.Kind == RemnantKind.Registry && k.Reason == "physxKept") && !plan.Items.Any(i => i.Kind == RemnantKind.Registry));
            Assert(plan.Kept.Any(k => k.Kind == RemnantKind.App && k.Label.Contains("PhysX")));
            Assert(plan.Items.Any(i => i.Kind == RemnantKind.Folder && i.Target.EndsWith("NVIDIA App")) && !plan.Items.Any(i => i.Target.EndsWith("PhysX")));
        });
        await test("Vendor applications orphaned by the quarantine are removed from Programs", async () =>
        {
            var plan = DeepCleanPlanner.Create(await Demo(), [Vendor.Nvidia]);
            Assert(plan.Items.Count(i => i.Kind == RemnantKind.App && i.Reason == "appOrphan") == 2);
            Assert(plan.Items.Any(i => i.Kind == RemnantKind.Task && i.Reason == "commandOrphan"));
        });
        await test("Services: active and unrelated services stay; orphaned vendor services go", () =>
        {
            var inventory = Inventory() with
            {
                Services = [new("nvlddmkm", "", @"C:\Windows\System32\drivers\nvlddmkm.sys", true), new("NvContainerLocalSystem", "", @"C:\Gone\nvcontainer.exe", false),
                    new("RandomVendorService", "", @"C:\Program Files\Other\svc.exe", true), new("OrphanUnnamed", "", @"C:\Program Files\NVIDIA Corporation\Missing\x.exe", false)],
                ActiveServices = ["nvlddmkm"]
            };
            var plan = DeepCleanPlanner.Create(inventory, [Vendor.Nvidia]);
            Assert(plan.Kept.Any(k => k.Key == "service:nvlddmkm" && k.Reason == "serviceActive"));
            Assert(plan.Items.Any(i => i.Key == "service:NvContainerLocalSystem" && i.Reason == "serviceOrphan") && plan.Items.Any(i => i.Key == "service:OrphanUnnamed"));
            Assert(!plan.Items.Any(i => i.Key == "service:RandomVendorService"));
            return Done();
        });
        await test("Startup and tasks: only removed or missing vendor programs are planned", () =>
        {
            var inventory = Inventory() with
            {
                Startup = [new(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run|NvBackend", "NvBackend", [@"C:\Program Files (x86)\NVIDIA Corporation\Update Core\NvBackend.exe"], false),
                    new(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run|Other", "Other", [@"C:\Tools\missing.exe"], false),
                    new(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run|Alive", "Alive", [@"C:\Program Files\NVIDIA Corporation\alive.exe"], true)]
            };
            var plan = DeepCleanPlanner.Create(inventory, [Vendor.Nvidia]);
            Assert(plan.Items.Count(i => i.Kind == RemnantKind.Startup) == 1 && plan.Items.Single(i => i.Kind == RemnantKind.Startup).Label == "NvBackend");
            return Done();
        });
        await test("Intel cleanup on an AMD processor keeps network, chipset and unrelated Intel packages", () =>
        {
            var inventory = Inventory(cpu: "AuthenticAMD", cpuName: "AMD Ryzen 7 5800X") with
            {
                Devices = [new(@"PCI\VEN_10DE&DEV_2786\NEW", "Display", "oem99.inf", "nvlddmkm", true, [@"PCI\VEN_10DE&DEV_2786"]),
                    new(@"PCI\VEN_8086&DEV_56A0\OLD", "Display", "oem20.inf", "igfxn", false, [@"PCI\VEN_8086&DEV_56A0"], "Intel Arc A770"),
                    new(@"HDAUDIO\FUNC_01&VEN_8086&DEV_2812\OLD", "MEDIA", "oem21.inf", "IntcDAud", false, [@"HDAUDIO\FUNC_01&VEN_8086&DEV_2812"]),
                    new(@"PCI\VEN_8086&DEV_15F3\NIC", "Net", "oem30.inf", "e2f", true, [@"PCI\VEN_8086&DEV_15F3"], "Intel I225-V")],
                Packages = [new("oem20.inf", "iigd_dch_d.inf", "Intel Corporation", "Display", "31.0.101.5522", false, @"C:\Store\igd", 900, [@"PCI\VEN_8086&DEV_56A0"]),
                    new("oem21.inf", "intcdaud.inf", "Intel(R) Corporation", "MEDIA", "10.29.0.1", false, @"C:\Store\aud", 9, [@"HDAUDIO\FUNC_01&VEN_8086&DEV_2812"]),
                    new("oem22.inf", "igcc_dch.inf", "Intel Corporation", "Extension", "31.0.101.5522", false, @"C:\Store\ext", 9, [@"PCI\VEN_8086&DEV_56A0&SUBSYS_10208086"]),
                    new("oem30.inf", "e2f.inf", "Intel", "Net", "2.1", false, @"C:\Store\net", 9, [@"PCI\VEN_8086&DEV_15F3"]),
                    new("oem31.inf", "heci.inf", "Intel", "System", "2433", false, @"C:\Store\heci", 9, [@"PCI\VEN_8086&DEV_7AE8"]),
                    new("oem32.inf", "iastor.inf", "Intel Corporation", "MEDIA", "1", false, @"C:\Store\x", 9, [@"PCI\VEN_8086&DEV_7AD0"])]
            };
            var plan = DeepCleanPlanner.Create(inventory, [Vendor.Intel]);
            var planned = plan.Items.Where(i => i.Kind == RemnantKind.Package).Select(i => i.Target).Order().ToArray();
            Assert(planned.SequenceEqual(["oem20.inf", "oem21.inf", "oem22.inf"]), string.Join(",", planned));
            Assert(!plan.Items.Any(i => i.Target.Contains("15F3")) && plan.Items.Any(i => i.Target == @"PCI\VEN_8086&DEV_56A0\OLD"));
            return Done();
        });
        await test("Integrated GPU of the processor brand and laptops are left unselected", () =>
        {
            var intel = Inventory(cpu: "GenuineIntel", cpuName: "Intel(R) Core(TM) i7-12700K") with
            {
                Devices = [new(@"PCI\VEN_1002&DEV_747E\NEW", "Display", "oem99.inf", "amdkmdag", true, [@"PCI\VEN_1002&DEV_747E"]), new(@"PCI\VEN_8086&DEV_4680\IGPU", "Display", "oem20.inf", "igfxn", false, [@"PCI\VEN_8086&DEV_4680"])],
                Packages = [new("oem20.inf", "iigd_dch.inf", "Intel Corporation", "Display", "31", false, @"C:\Store\igd", 9, [@"PCI\VEN_8086&DEV_4680"])]
            };
            var plan = DeepCleanPlanner.Create(intel, [Vendor.Intel]);
            Assert(plan.Items.Count > 0 && plan.Items.All(i => !i.Selected) && plan.Notes.Contains("integratedCaution:Intel"));
            var laptop = DeepCleanPlanner.Create(Inventory() with { Laptop = true, Packages = [new("oem42.inf", "nv_dispi.inf", "NVIDIA", "Display", "1", false, @"C:\S", 9, [@"PCI\VEN_10DE&DEV_2484"])] }, [Vendor.Nvidia]);
            Assert(laptop.Items.Count == 1 && !laptop.Items[0].Selected && laptop.Notes.Contains("laptopCaution:Nvidia"));
            return Done();
        });
        await test("AMD installer leftovers keep chipset folders", () =>
        {
            var inventory = Inventory() with
            {
                Devices = [new(@"PCI\VEN_10DE&DEV_2786\NEW", "Display", "oem99.inf", "nvlddmkm", true, [@"PCI\VEN_10DE&DEV_2786"])],
                Folders = [new(@"%SystemDrive%\AMD", @"C:\AMD", 30, [new("Chipset_Software", @"C:\AMD\Chipset_Software", 10), new("AMD-Software-Installer", @"C:\AMD\AMD-Software-Installer", 10), new("Ryzen Master", @"C:\AMD\Ryzen Master", 10)])]
            };
            var plan = DeepCleanPlanner.Create(inventory, [Vendor.Amd]);
            Assert(plan.Items.Where(i => i.Kind == RemnantKind.Folder).Select(i => i.Target).SequenceEqual([@"C:\AMD\AMD-Software-Installer"]));
            return Done();
        });
        await test("A connected Intel GPU is recognized even when Windows omits the device class", () =>
        {
            var inventory = Inventory(cpu: "AuthenticAMD", cpuName: "AMD Ryzen 7 5800X") with
            {
                Devices = [new(@"PCI\VEN_1002&DEV_747E\NEW", "Display", "oem99.inf", "amdkmdag", true, [@"PCI\VEN_1002&DEV_747E"]), new(@"PCI\VEN_8086&DEV_56A0\ARC", "", "oem20.inf", "igfxn", true, [@"PCI\VEN_8086&DEV_56A0"])],
                Packages = [Pkg("oem20.inf", "iigd_dch_d.inf", "Intel Corporation", "Display", "31.0.101.5522", @"PCI\VEN_8086&DEV_56A0"), Pkg("oem21.inf", "iigd_dch_d.inf", "Intel Corporation", "Display", "31.0.101.4000", @"PCI\VEN_8086&DEV_56A0")]
            };
            Assert(DeepCleanPlanner.PresentGpuVendors(inventory).Contains(Vendor.Intel));
            var plan = DeepCleanPlanner.Create(inventory, [Vendor.Intel]);
            Assert(plan.Items.Count == 0 && plan.Notes.Contains("vendorPresent:Intel") && !DeepCleanPlanner.Candidates(inventory).Contains(Vendor.Intel));
            return Done();
        });
        await test("Incomplete or non-administrator inventory cannot produce a plan", () =>
        {
            try { DeepCleanPlanner.Create(Inventory() with { Complete = false }, [Vendor.Nvidia]); throw new Exception("accepted"); } catch (InvalidOperationException) { }
            try { DeepCleanPlanner.Create(Inventory() with { Admin = false }, [Vendor.Nvidia]); throw new Exception("accepted"); } catch (InvalidOperationException) { }
            Assert(DeepCleanPlanner.Candidates(Inventory() with { Admin = false }).Count == 0);
            return Done();
        });
        await test("Older driver versions keep the active and newest unused package", () =>
        {
            var inventory = Inventory() with
            {
                Devices = [new(@"PCI\VEN_10DE&DEV_2786\NEW", "Display", "oem50.inf", "nvlddmkm", true, [@"PCI\VEN_10DE&DEV_2786"])],
                Packages = [Pkg("oem48.inf", "nv_dispi.inf", "NVIDIA", "Display", "32.0.15.5000"), Pkg("oem49.inf", "nv_dispi.inf", "NVIDIA", "Display", "32.0.15.6000"),
                    Pkg("oem47.inf", "nv_dispi.inf", "NVIDIA", "Display", "31.0.15.1000"), Pkg("oem50.inf", "nv_dispi.inf", "NVIDIA", "Display", "32.0.15.7000"),
                    Pkg("oem51.inf", "u0400000.inf", "Advanced Micro Devices, Inc.", "Display", "31.0.1", @"PCI\VEN_1002&DEV_747E")]
            };
            var old = DeepCleanPlanner.OldVersions(inventory).Select(i => i.Target).Order().ToArray();
            Assert(old.SequenceEqual(["oem47.inf", "oem48.inf"]), string.Join(",", old));
            return Done();
        });

        // ---- Engine ----
        await test("Deep cleanup backs up before every change, quarantines folders and completes", async () =>
        {
            var sandbox = Sandbox(); var platform = new DemoCleanupPlatform(Path.Combine(sandbox, "System")); var secure = Secure(sandbox);
            var plan = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]);
            var session = Session(sandbox);
            var outcome = await new DeepCleanEngine(platform).RunAsync(plan, All(plan), session, secure, _ => { });
            Assert(outcome.Failed == 0 && outcome.Done >= 15, $"done={outcome.Done} failed={outcome.Failed}");
            int I(string call) => platform.Calls.FindIndex(c => c.StartsWith(call, StringComparison.OrdinalIgnoreCase));
            Assert(I("export:oem42.inf") < I("delete:oem42.inf") && I("remove-device:PCI\\VEN_10DE&DEV_2484") < I("delete:oem42.inf"));
            Assert(I("export-registry:HKLM\\SYSTEM\\CurrentControlSet\\Services\\nvlddmkm") < I("delete-service:nvlddmkm"));
            Assert(I("stop:424242") >= 0 && I("stop:424242") < I("delete:oem42.inf"));
            Assert(!Directory.Exists(Path.Combine(sandbox, "System", "Program Files", "NVIDIA Corporation", "NVIDIA App")) && Directory.Exists(Path.Combine(sandbox, "System", "Program Files", "NVIDIA Corporation", "PhysX")));
            var name = Path.GetFileName(session);
            Assert(Directory.EnumerateFiles(Path.Combine(secure, name), BackupIntegrity.ManifestName, SearchOption.AllDirectories).Count() >= 3, "integrity manifests");
            Assert(File.Exists(Path.Combine(secure, name, "undo.json")) && File.ReadAllText(Path.Combine(session, "state.json")).Contains("\"complete\""));
            var left = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]).Items;
            Assert(left.Count == 0, "leftovers remain: " + string.Join(", ", left.Select(i => i.Key)));
        });
        await test("Undo restores backed-up registry, services, tasks, packages and quarantined folders", async () =>
        {
            var sandbox = Sandbox(); var platform = new DemoCleanupPlatform(Path.Combine(sandbox, "System")); var secure = Secure(sandbox);
            var plan = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]); var session = Session(sandbox);
            await new DeepCleanEngine(platform).RunAsync(plan, All(plan), session, secure, _ => { });
            var undo = await new DeepCleanEngine(platform).UndoAsync(session, secure, _ => { });
            Assert(undo.Results.Count(r => r.Status == "restored") >= 8 && undo.Results.All(r => r.Status is "restored" or "skipped"), string.Join(",", undo.Results.Where(r => r.Status == "failed").Select(r => r.Key + " " + r.Message)));
            Assert(Directory.Exists(Path.Combine(sandbox, "System", "Program Files", "NVIDIA Corporation", "NVIDIA App", "CEF")));
            var after = await platform.InventoryAsync();
            Assert(after.Services.Any(s => s.Name == "nvlddmkm") && after.Packages.Any(p => p.Inf == "oem43.inf") && after.Tasks.Count == 1 && after.RegistryKeys.Count == 2);
            Assert(undo.Results.Any(r => r.Kind == RemnantKind.Device && r.Status == "skipped") && undo.Results.Any(r => r.Kind == RemnantKind.Appx && r.Status == "skipped"));
        });
        await test("Unselected items are never touched", async () =>
        {
            var sandbox = Sandbox(); var platform = new DemoCleanupPlatform(Path.Combine(sandbox, "System"));
            var plan = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]);
            await new DeepCleanEngine(platform).RunAsync(plan, new HashSet<string> { "registry:HKCU\\SOFTWARE\\NVIDIA Corporation" }, Session(sandbox), Secure(sandbox), _ => { });
            Assert(platform.Calls.Count(c => c.StartsWith("delete") || c.StartsWith("remove")) == 1 && platform.Calls.Contains(@"delete-registry:HKCU\SOFTWARE\NVIDIA Corporation"));
            Assert(Directory.Exists(Path.Combine(sandbox, "System", "Program Files", "NVIDIA Corporation", "NVIDIA App")));
        });
        await test("A selected package removes its disconnected device first, even if that device was unselected", async () =>
        {
            var sandbox = Sandbox(); var platform = new DemoCleanupPlatform(Path.Combine(sandbox, "System"));
            var plan = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]);
            await new DeepCleanEngine(platform).RunAsync(plan, new HashSet<string> { "package:oem43.inf" }, Session(sandbox), Secure(sandbox), _ => { });
            var remove = platform.Calls.IndexOf(@"remove-device:HDAUDIO\FUNC_01&VEN_10DE&DEV_0094\DEMO");
            Assert(remove >= 0 && remove < platform.Calls.IndexOf("delete:oem43.inf"));
        });
        await test("A device that cannot be removed keeps its package; other work continues", async () =>
        {
            var sandbox = Sandbox(); var platform = new DemoCleanupPlatform(Path.Combine(sandbox, "System"));
            platform.FailDevices.Add(@"PCI\VEN_10DE&DEV_2484\DEMO"); platform.FailPackages.Add("oem44.inf");
            var plan = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]);
            var outcome = await new DeepCleanEngine(platform).RunAsync(plan, All(plan), Session(sandbox), Secure(sandbox), _ => { });
            Assert(!platform.Calls.Contains("delete:oem42.inf") && outcome.Results.Any(r => r.Key == "package:oem42.inf" && r.Status == "skipped" && r.Message == "dependentDeviceFailed"));
            Assert(outcome.Results.Any(r => r.Key == "package:oem44.inf" && r.Status == "failed") && platform.Calls.Contains("delete:oem43.inf") && outcome.Failed >= 2);
        });
        await test("Failed backups prevent deletion of that item", async () =>
        {
            var sandbox = Sandbox(); var platform = new DemoCleanupPlatform(Path.Combine(sandbox, "System")) { FailExports = true };
            var plan = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]);
            var outcome = await new DeepCleanEngine(platform).RunAsync(plan, All(plan), Session(sandbox), Secure(sandbox), _ => { });
            Assert(!platform.Calls.Any(c => c.StartsWith("delete:") || c.StartsWith("delete-service") || c.StartsWith("delete-registry") || c.StartsWith("delete-task")));
            Assert(outcome.Results.Where(r => r.Kind is RemnantKind.Package or RemnantKind.Service or RemnantKind.Registry or RemnantKind.Task).All(r => r.Status == "skipped"));
        });
        await test("A reconnected brand or another machine stops cleanup before any change", async () =>
        {
            foreach (var drift in new[] { "vendor", "machine" })
            {
                var sandbox = Sandbox(); var platform = new DemoCleanupPlatform(Path.Combine(sandbox, "System"));
                var plan = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]);
                if (drift == "vendor") platform.SourceReturns = true; else plan = plan with { MachineId = "OTHER" };
                try { await new DeepCleanEngine(platform).RunAsync(plan, All(plan), Session(sandbox), Secure(sandbox), _ => { }); throw new Exception("accepted " + drift); }
                catch (InvalidOperationException) { }
                Assert(!platform.Calls.Any(c => c.StartsWith("delete") || c.StartsWith("remove") || c.StartsWith("stop:")), drift);
            }
        });
        await test("Cleanup sessions require a fresh validated name", async () =>
        {
            var sandbox = Sandbox(); var platform = new DemoCleanupPlatform(Path.Combine(sandbox, "System"));
            var plan = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]);
            foreach (var bad in new[] { Path.Combine(sandbox, "Sessions", "..\\..\\evil"), Path.Combine(sandbox, "Sessions", "20260101-000000-LIVE-abcdef") })
                try { await new DeepCleanEngine(platform).RunAsync(plan, All(plan), bad, Secure(sandbox), _ => { }); throw new Exception("accepted"); } catch (IOException) { }
            var existing = Session(sandbox); Directory.CreateDirectory(existing);
            try { await new DeepCleanEngine(platform).RunAsync(plan, All(plan), existing, Secure(sandbox), _ => { }); throw new Exception("accepted"); } catch (IOException) { }
            Assert(platform.Calls.Count == 0);
        });
        await test("Undo trusts only the protected manifest and backup paths inside it", async () =>
        {
            var sandbox = Sandbox(); var platform = new DemoCleanupPlatform(Path.Combine(sandbox, "System")); var secure = Secure(sandbox);
            var plan = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]); var session = Session(sandbox);
            await new DeepCleanEngine(platform).RunAsync(plan, All(plan), session, secure, _ => { });
            File.WriteAllText(Path.Combine(session, "cleanup-results.json"), "[]");
            var manifest = Path.Combine(secure, Path.GetFileName(session), "undo.json");
            var results = JsonSerializer.Deserialize<List<CleanupResult>>(File.ReadAllText(manifest))!;
            var index = results.FindIndex(r => r.Kind == RemnantKind.Registry);
            results[index] = results[index] with { Backup = @"..\..\..\evil.reg" };
            File.WriteAllText(manifest, JsonSerializer.Serialize(results));
            var undo = await new DeepCleanEngine(platform).UndoAsync(session, secure, _ => { });
            Assert(undo.Results.Any(r => r.Key == results[index].Key && r.Message == "invalidBackup") && undo.Results.Count(r => r.Status == "restored") >= 7);
        });
        await test("Tampered quarantine is not restored", async () =>
        {
            var sandbox = Sandbox(); var platform = new DemoCleanupPlatform(Path.Combine(sandbox, "System")); var secure = Secure(sandbox);
            var plan = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]); var session = Session(sandbox);
            var outcome = await new DeepCleanEngine(platform).RunAsync(plan, All(plan), session, secure, _ => { });
            var folder = outcome.Results.First(r => r.Kind == RemnantKind.Folder && r.Status == "done");
            File.WriteAllText(Path.Combine(secure, Path.GetFileName(session), folder.Backup, "planted.dll"), "x");
            var undo = await new DeepCleanEngine(platform).UndoAsync(session, secure, _ => { });
            Assert(undo.Results.Any(r => r.Key == folder.Key && r.Status == "failed") && !Directory.Exists(folder.Target));
        });
        await test("Cancellation before cleanup changes nothing", async () =>
        {
            var sandbox = Sandbox(); var platform = new DemoCleanupPlatform(Path.Combine(sandbox, "System"));
            var plan = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]);
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { await new DeepCleanEngine(platform).RunAsync(plan, All(plan), Session(sandbox), Secure(sandbox), _ => { }, cancel.Token); throw new Exception("accepted"); } catch (OperationCanceledException) { }
            Assert(platform.Calls.Count == 0);
        });
        await test("Folder safety refuses system roots, profile roots and links", () =>
        {
            foreach (var path in new[] { @"C:\", Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "DriverStore"), Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))! })
                Assert(!DeepCleanEngine.SafeFolder(path), path);
            Assert(DeepCleanEngine.SafeFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation")));
            Assert(DeepCleanEngine.SafeFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NVIDIA Corporation", "Downloader")));
            return Done();
        });
        await test("Cache removal deletes links without following them", async () =>
        {
            var sandbox = Sandbox(); var outside = Path.Combine(sandbox, "outside"); var cache = Path.Combine(sandbox, "cache");
            Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
            Directory.CreateDirectory(cache); File.WriteAllText(Path.Combine(cache, "shader.bin"), "x");
            using (var link = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { ArgumentList = { "/c", "mklink", "/J", Path.Combine(cache, "junction"), outside }, CreateNoWindow = true, UseShellExecute = false })!) await link.WaitForExitAsync();
            Assert(Directory.Exists(Path.Combine(cache, "junction")), "junction not created");
            var (freed, locked) = DeepCleanEngine.DeleteTree(cache);
            Assert(locked == 0 && freed == 1 && !Directory.Exists(cache) && File.Exists(Path.Combine(outside, "keep.txt")));
        });

        // ---- Disconnected GPU before Display-package deletion (the 0.4.2 real-hardware failure) ----
        await test("Disconnected-device rules: same brand only, never a connected device", () =>
        {
            var snapshot = new Snapshot("W", "M", true, false, false, false, 1L << 40, [new("New", @"PCI\VEN_1002&DEV_747E\1", Vendor.Amd, "oem99.inf", "1", 0, true)],
                [new("oem42.inf", "NVIDIA", Planner.DisplayGuid, "1", false)], true, [], "B", [new("Old", @"PCI\VEN_10DE&DEV_2484\1", Vendor.Nvidia, "oem42.inf", "", 0, false)]);
            Assert(Planner.EligibleCleanup(snapshot, snapshot.Drivers[0], Vendor.Nvidia, Vendor.Amd, true));
            var foreign = snapshot with { Phantoms = [.. snapshot.Disconnected, new("Odd", @"PCI\VEN_8086&DEV_0001\1", Vendor.Intel, "oem42.inf", "", 0, false)] };
            Assert(!Planner.EligibleCleanup(foreign, snapshot.Drivers[0], Vendor.Nvidia, Vendor.Amd, true));
            return Done();
        });
        await test("Integrated caution keeps processor-brand display packages in automatic mode", () =>
        {
            var snapshot = new Snapshot("W", "M", true, false, false, false, 1L << 40, [new("New", @"PCI\VEN_1002&DEV_747E\1", Vendor.Amd, "oem99.inf", "1", 0, true)],
                [new("oem42.inf", "NVIDIA", Planner.DisplayGuid, "1", false), new("oem20.inf", "Intel Corporation", Planner.DisplayGuid, "1", false)], true, [], "B", null, "GenuineIntel", "Intel(R) Core(TM) i7-12700K");
            Assert(Planner.EligibleCleanup(snapshot, snapshot.Drivers[0], Vendor.Nvidia, Vendor.Amd, true) && !Planner.EligibleCleanup(snapshot, snapshot.Drivers[1], Vendor.Nvidia, Vendor.Amd, true));
            Assert(Planner.CleanupReason(snapshot, snapshot.Drivers[1], Vendor.Nvidia, Vendor.Amd, true) == "cleanupIntegrated");
            Assert(Planner.EligibleCleanup(snapshot, snapshot.Drivers[1], Vendor.Intel, Vendor.Amd, false), "explicit choice");
            Assert(Planner.EligibleCleanup(snapshot with { CpuName = "Intel(R) Core(TM) i5-12400F" }, snapshot.Drivers[1], Vendor.Nvidia, Vendor.Amd, true), "F model has no iGPU");
            Assert(Planner.DetectSource(snapshot with { Drivers = [snapshot.Drivers[1], snapshot.Drivers[0]] }, Vendor.Amd) == Vendor.Nvidia, "detected the iGPU brand");
            Assert(Planner.DetectSource(snapshot with { Drivers = [snapshot.Drivers[1]] }, Vendor.Amd) == Vendor.Amd, "only iGPU leftovers must mean no cleanup");
            return Done();
        });

        // ---- Diagnostics ----
        await test("PCIe width loss is flagged while idle link speed is not", () =>
        {
            var adapter = new Adapter("GPU", @"PCI\VEN_10DE&DEV_2786&SUBSYS_1\4&1", Vendor.Nvidia, "oem99.inf", "1", 0, true);
            GraphicsDiagnostics D(int width) => new(DateTimeOffset.UtcNow, [], [], [], true, true, [], [new(adapter.Id, width, 16, 1, 4)]);
            Assert(GraphicsHealth.Evaluate(adapter, D(16)).Any(f => f.Code == "pcieLink" && !f.Attention && f.Detail.Contains("Gen 1 / Gen 4")));
            Assert(GraphicsHealth.Evaluate(adapter, D(4)).Any(f => f.Code == "pcieReduced" && f.Attention && f.Detail == "x4 / x16"));
            Assert(!GraphicsHealth.Evaluate(adapter, D(16) with { Links = null }).Any(f => f.Code.StartsWith("pcie")));
            return Done();
        });
    }

    private static StorePackage Pkg(string inf, string name, string provider, string cls, string version, string id = @"PCI\VEN_10DE&DEV_2786") =>
        new(inf, name, provider, cls, version, false, @"C:\Store\" + inf, 100, [id]);
    private static RemnantInventory Inventory(string cpu = "AuthenticAMD", string cpuName = "AMD Ryzen 7 5800X") =>
        new("TEST", cpu, cpuName, true, false, [new(@"PCI\VEN_1002&DEV_747E\NEW", "Display", "oem99.inf", "amdkmdag", true, [@"PCI\VEN_1002&DEV_747E"])], [], [], [], [], [], [], [], [], [], [], true, []);
}
