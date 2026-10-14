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
            // Hidden sub-components without an uninstall command are judged by their icon module.
            Assert(CommandFiles.Parse(@"C:\Program Files\NVIDIA Corporation\Installer2\InstallerCore\NVI2.dll,0").Single() == @"C:\Program Files\NVIDIA Corporation\Installer2\InstallerCore\NVI2.dll");
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
            Assert(plan.Kept.Any(k => k.Kind == RemnantKind.Registry && k.Reason == "physxKept") && !plan.Items.Any(i => i.Kind == RemnantKind.Registry && i.Reason == "settingsKey"));
            Assert(plan.Kept.Any(k => k.Kind == RemnantKind.App && k.Label.Contains("PhysX")));
            Assert(plan.Items.Any(i => i.Kind == RemnantKind.Folder && i.Target.EndsWith("NVIDIA App")) && !plan.Items.Any(i => i.Target.EndsWith("PhysX")));
        });
        await test("Vendor applications orphaned by the quarantine are removed from Programs", async () =>
        {
            var plan = DeepCleanPlanner.Create(await Demo(), [Vendor.Nvidia]);
            Assert(plan.Items.Count(i => i.Kind == RemnantKind.App && i.Reason == "appOrphan") == 2);
            Assert(plan.Items.Any(i => i.Kind == RemnantKind.Task && i.Reason == "commandOrphan"));
        });
        await test("Translated Programs entries follow their uninstaller's folder, not their name; exclusions and unrelated tools stay", async () =>
        {
            var inventory = await Demo();
            var first = DeepCleanPlanner.Create(inventory, [Vendor.Nvidia]);
            var orphan = inventory.Apps.First(a => first.Items.Any(i => i.Key == "app:" + a.Key && i.Reason == "appOrphan"));
            inventory = inventory with { Apps = [.. inventory.Apps,
                orphan with { Key = orphan.Key + "-es", Name = "NVIDIA Controlador de gráficos 582.78" },
                orphan with { Key = orphan.Key + "-de", Name = "NVIDIA Grafiktreiber 582.78" },
                orphan with { Key = orphan.Key + "-cuda", Name = "NVIDIA CUDA Runtime 12.4" },
                orphan with { Key = orphan.Key + "-physx", Name = "NVIDIA Software del sistema PhysX 9.23" },
                orphan with { Key = orphan.Key + "-tool", Name = "NVIDIA Herramienta sin relación", Files = [@"C:\Program Files\Other Vendor\tool.exe"], Exists = true }] };
            var plan = DeepCleanPlanner.Create(inventory, [Vendor.Nvidia]);
            foreach (var suffix in new[] { "-es", "-de" }) Assert(plan.Items.Any(i => i.Key == "app:" + orphan.Key + suffix && i.Reason == "appOrphan"), "translated entry " + suffix);
            Assert(!plan.Items.Any(i => i.Key.EndsWith("-cuda") || i.Key.EndsWith("-physx") || i.Key.EndsWith("-tool")), "excluded or unrelated entry planned");
            Assert(plan.Kept.Any(k => k.Key.EndsWith("-physx") && k.Reason == "physxKept"));
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
        await test("A Windows step that finishes late is reported on its item; the rest of the cleanup continues", async () =>
        {
            var sandbox = Sandbox(); var platform = new DemoCleanupPlatform(Path.Combine(sandbox, "System")) { SlowTasks = true };
            var plan = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]);
            var outcome = await new DeepCleanEngine(platform).RunAsync(plan, All(plan), Session(sandbox), Secure(sandbox), _ => { });
            Assert(outcome.Results.Single(r => r.Kind == RemnantKind.Task) is { Status: "failed" } task && task.Message.Contains("late"));
            Assert(!platform.Calls.Any(c => c.StartsWith("delete-task")) && platform.Calls.Contains(@"delete-registry:HKLM\SOFTWARE\NVIDIA Corporation") && outcome.Done >= 15,
                "cleanup stopped at the late step");
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

        // ---- 0.6: files outside the Driver Store, Khronos registrations, game APIs and the guided card change ----
        await test("INF reader lists System32/SysWOW64 copies and ignores other destinations", () =>
        {
            var inf = """
                [Version]
                Class=Display
                Provider=%ATI%
                [DestinationDirs]
                DefaultDestDir = 11
                System32Files = 11
                Wow64Files = 10, SysWOW64
                Wow64Dirid = 16425
                LxssFiles = 11, lxss\lib
                StoreFiles = 13
                DriverFiles = 12
                Escape = 11, ..\Temp
                [System32Files]
                atiadlxx.dll,,,0x00004000
                amdxc64.dll
                ..\evil.dll
                [Wow64Files]
                atiadlxy.dll
                [Wow64Dirid]
                amdxc32.dll
                [LxssFiles]
                libdxcore.so
                [StoreFiles]
                amdkmdag.sys
                [DriverFiles]
                amdkmdag.sys
                [Escape]
                payload.dll
                [Strings]
                ATI = "Advanced Micro Devices, Inc."
                """;
            var files = InfReader.Read(inf).SystemFiles!;
            Assert(files.SequenceEqual([@"System32\atiadlxx.dll", @"System32\amdxc64.dll", @"SysWOW64\atiadlxy.dll", @"SysWOW64\amdxc32.dll", @"System32\lxss\lib\libdxcore.so"]), string.Join(",", files));
            return Done();
        });
        await test("Software components of a removed display driver are planned through its AddComponent IDs", () =>
        {
            var parent = InfReader.Read("""
                [Version]
                Class=Display
                Provider=%AMD%
                [ati2mtag_Cezanne.Components]
                AddComponent = AMDOCL,,AMDOCLComponent
                AddComponent = AMDWIN,,AMDWINComponent
                AddComponent = AMDUWP,,AMDUWPComponent
                [AMDOCLComponent]
                ComponentIDs = AMDOCL-23.19
                [AMDWINComponent]
                ComponentIDs = AMDWIN-23.19
                [AMDUWPComponent]
                ComponentIDs=VID1002&PID0001
                [Strings]
                AMD = "Advanced Micro Devices, Inc."
                """);
            Assert(parent.ComponentIds!.SequenceEqual([@"SWC\AMDOCL-23.19", @"SWC\AMDWIN-23.19", @"SWC\VID1002&PID0001"]), string.Join(",", parent.ComponentIds!));
            var inventory = Inventory() with
            {
                Devices = [new(@"PCI\VEN_10DE&DEV_2786\NEW", "Display", "oem99.inf", "nvlddmkm", true, [@"PCI\VEN_10DE&DEV_2786"]),
                    new(@"SWC\AMDOCL-23.19\OLD", "SoftwareComponent", "oem6.inf", "", false, [@"SWC\AMDOCL-23.19"])],
                Packages = [new("oem4.inf", "u0202828.inf", "Advanced Micro Devices, Inc.", "Display", "31", false, @"C:\Store\u0", 9, [@"PCI\VEN_1002&DEV_1638"], null, parent.ComponentIds),
                    new("oem6.inf", "amdocl.inf", "Advanced Micro Devices, Inc.", "SoftwareComponent", "31", false, @"C:\Store\ocl", 9, [@"SWC\AMDOCL-23.19"]),
                    new("oem7.inf", "amdwin-u0202828.inf", "Advanced Micro Devices, Inc.", "SoftwareComponent", "31", false, @"C:\Store\win", 9, [@"SWC\AMDWIN-23.19"]),
                    new("oem8.inf", "other.inf", "Advanced Micro Devices, Inc.", "SoftwareComponent", "1", false, @"C:\Store\other", 9, [@"SWC\RYZENMASTER"])]
            };
            var plan = DeepCleanPlanner.Create(inventory, [Vendor.Amd]);
            Assert(plan.Items.Any(i => i.Key == "package:oem6.inf" && i.Reason == "packageComponent") && plan.Items.Any(i => i.Key == "package:oem7.inf" && i.Reason == "packageComponent"));
            Assert(plan.Items.Any(i => i.Key == @"device:SWC\AMDOCL-23.19\OLD") && !plan.Items.Any(i => i.Target == "oem8.inf"), "unrelated component planned");
            return Done();
        });
        await test("Setup log copies keep the Driver Store folder of packages that are already gone", () =>
        {
            var lines = new[] {
                @"     flq:      Copying 'C:\Windows\System32\DriverStore\FileRepository\u0202828.inf_amd64_4c7e1a\B026278\atiadlxx.dll' to 'C:\Windows\system32\atiadlxx.dll'.",
                @"     flq:      Copying 'C:\Users\Someone\Downloads\x.dll' to 'C:\Windows\System32\x.dll'.",
                @"     flq:      Copying 'C:\Windows\System32\DriverStore\FileRepository\..\evil\a.dll' to 'C:\Windows\System32\a.dll'.",
                @"     flq:      Copying 'C:\Windows\System32\DriverStore\FileRepository\nv_dispi.inf_amd64_9a\nvapi.dll' to 'C:\Windows\SysWOW64\nvapi.dll'.",
                // Windows 10 2004+ links System32 files to the store instead of copying them (observed in the comparison lab).
                @"     flq:                     Hardlinking 'C:\Windows\System32\DriverStore\FileRepository\u0202828.inf_amd64_9cfe79f0f12c413e\.\B026278\amdadlx64.dll' to 'C:\Windows\system32\amdadlx64.dll'.",
                @"     flq:                Copying 'C:\Windows\TEMP\{1446fd92}\B026278\amdadlx64.dll' to 'C:\Windows\System32\DriverStore\Temp\{642f}\B026278\amdadlx64.dll'." };
            var copies = SetupLog.Copies(lines).ToArray();
            Assert(copies.Length == 3 && copies[0] == (@"C:\Windows\system32\atiadlxx.dll", "u0202828.inf_amd64_4c7e1a") && copies[1].Source == "nv_dispi.inf_amd64_9a" &&
                copies[2] == (@"C:\Windows\system32\amdadlx64.dll", "u0202828.inf_amd64_9cfe79f0f12c413e"), string.Join(";", copies));
            return Done();
        });
        await test("File authors map to brands exactly, including the ATI era", () =>
        {
            Assert(Vendors.FromCompany("Advanced Micro Devices, Inc.") == Vendor.Amd && Vendors.FromCompany("ATI Technologies Inc.") == Vendor.Amd && Vendors.FromCompany("NVIDIA Corporation") == Vendor.Nvidia);
            Assert(Vendors.FromCompany("Intel(R) Corporation") == Vendor.Intel && Vendors.FromCompany("LunarG, Inc.") == Vendor.Unknown && Vendors.FromCompany("Not NVIDIA") == Vendor.Unknown && Vendors.FromCompany("") == Vendor.Unknown);
            return Done();
        });
        await test("System32 files need package evidence, the brand as author and no remaining user; loaders always stay", async () =>
        {
            var platform = new DemoCleanupPlatform(Sandbox()); var inventory = await platform.InventoryAsync();
            var plan = DeepCleanPlanner.Create(inventory, [Vendor.Nvidia]);
            string File(string relative) => "file:" + Path.Combine(platform.WindowsRoot, relative);
            foreach (var (key, reason) in new[] { (File(@"System32\nvapi64.dll"), "fileInf"), (File(@"System32\nvcuda.dll"), "fileInf"), (File(@"SysWOW64\nvapi.dll"), "fileInf"), (File(@"System32\nvml.dll"), "fileLog") })
                Assert(plan.Items.Any(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && i.Reason == reason && i.Selected), "missing " + key);
            Assert(plan.Kept.Any(k => k.Key.Equals(File(@"System32\vulkan-1.dll"), StringComparison.OrdinalIgnoreCase) && k.Reason == "fileSharedRuntime"));
            Assert(plan.Kept.Any(k => k.Key.Equals(File(@"System32\OpenCL.dll"), StringComparison.OrdinalIgnoreCase) && k.Reason == "fileSharedRuntime"));
            Assert(!plan.Items.Any(i => i.Target.Contains("amdxc64", StringComparison.OrdinalIgnoreCase) || i.Target.Contains("vulkan-1", StringComparison.OrdinalIgnoreCase)), "connected or shared file planned");
            // Another package that stays deploys the same file; a different author; log evidence of a package still in the store.
            var nvapi = Path.Combine(platform.WindowsRoot, @"System32\nvapi64.dll");
            var shared = inventory with { Packages = [.. inventory.Packages, new("oem80.inf", "nvmixed.inf", "NVIDIA", "MEDIA", "1", false, @"C:\Store\mixed", 1,
                [@"HDAUDIO\FUNC_01&VEN_10DE&DEV_0001", @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0887"], [@"System32\nvapi64.dll"])] };
            var sharedPlan = DeepCleanPlanner.Create(shared, [Vendor.Nvidia]);
            Assert(sharedPlan.Kept.Any(k => k.Key == "package:oem80.inf") && sharedPlan.Kept.Any(k => k.Target == nvapi && k.Reason == "fileShared"));
            var foreign = inventory with { SystemFiles = inventory.SystemFiles!.Select(f => f.Path == nvapi ? f with { Company = "Microsoft Corporation" } : f).ToArray() };
            Assert(DeepCleanPlanner.Create(foreign, [Vendor.Nvidia]).Kept.Any(k => k.Target == nvapi && k.Reason == "fileOtherCompany"));
            var living = inventory with { SystemFiles = inventory.SystemFiles!.Select(f => f.Source.Length > 0 ? f with { Source = "nv_dispi.inf_amd64_demo" } : f).ToArray() };
            Assert(!DeepCleanPlanner.Create(living, [Vendor.Nvidia]).Items.Any(i => i.Target.EndsWith("nvml.dll")), "log evidence of a package still in the store");
            var unsigned = inventory with { SystemFiles = inventory.SystemFiles!.Select(f => f.Source.Length > 0 ? f with { Company = "" } : f).ToArray() };
            Assert(!DeepCleanPlanner.Create(unsigned, [Vendor.Nvidia]).Items.Any(i => i.Target.EndsWith("nvml.dll")), "log evidence without vendor authorship");
            Assert(DeepCleanPlanner.SystemRelative(@"C:\Windows", @"C:\Windows\System32\drivers\nvlddmkm.sys") is null && DeepCleanPlanner.SystemRelative(@"C:\Windows", @"C:\Windows\System32\DriverStore\x.dll") is null &&
                DeepCleanPlanner.SystemRelative(@"C:\Windows", @"C:\Windows\Temp\x.dll") is null && DeepCleanPlanner.SystemRelative(@"C:\Windows", @"C:\Windows\SysWOW64\nvapi.dll") == @"SysWOW64\nvapi.dll");
        });
        await test("Khronos registrations: removed or missing targets go, overlay layers stay", async () =>
        {
            var platform = new DemoCleanupPlatform(Sandbox()); var inventory = await platform.InventoryAsync();
            var plan = DeepCleanPlanner.Create(inventory, [Vendor.Nvidia]);
            var registrations = plan.Items.Where(i => i.Kind == RemnantKind.Registration).ToArray();
            Assert(registrations.Any(i => i.Label == "nvopencl64.dll" && i.Reason == "registrationOrphan" && i.Selected));
            Assert(registrations.Any(i => i.Label.EndsWith("nv-vk64.json") && i.Reason == "registrationRemoved"));
            Assert(!registrations.Any(i => i.Label.Contains("Steam", StringComparison.OrdinalIgnoreCase)), "overlay layer planned");
            // A missing file inside the Driver Store is only loader noise, whatever its brand.
            var stale = inventory with { Registrations = [.. inventory.Registrations!, new(@"HKLM\SOFTWARE\Khronos\Vulkan\Drivers", @"C:\Windows\System32\DriverStore\FileRepository\gone_amd64_1\x.json", "", false, "")] };
            Assert(DeepCleanPlanner.Create(stale, [Vendor.Nvidia]).Items.Any(i => i.Vendor == Vendor.Unknown && i.Reason == "registrationOrphan"));
            // An existing ICD of the absent brand is unregistered; a layer of the same brand that is not being removed is not.
            var vendor = inventory with { Registrations = [new(@"HKLM\SOFTWARE\Khronos\Vulkan\Drivers", @"C:\Program Files\X\nv.json", @"C:\Program Files\X\nvoglv64.dll", true, "NVIDIA Corporation"),
                new(@"HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers", @"C:\Program Files\X\nvlayer.json", @"C:\Program Files\X\nvlayer.dll", true, "NVIDIA Corporation")] };
            var items = DeepCleanPlanner.Create(vendor, [Vendor.Nvidia]).Items.Where(i => i.Kind == RemnantKind.Registration).ToArray();
            Assert(items.Length == 1 && items[0].Reason == "registrationVendor", string.Join(",", items.Select(i => i.Key)));
            // Nothing is planned when no brand is targeted.
            Assert(!DeepCleanPlanner.Create(stale, []).Items.Any());
        });
        await test("COM codecs served from a removed package go with their Media Foundation entries; other classes stay", async () =>
        {
            var platform = new DemoCleanupPlatform(Sandbox()); var inventory = await platform.InventoryAsync();
            var clsid = @"HKLM\SOFTWARE\Classes\CLSID\{60F44560-5A20-4857-BFEF-D29773CB8040}";
            var plan = DeepCleanPlanner.Create(inventory, [Vendor.Nvidia]);
            Assert(plan.Items.Any(i => i.Key == "registry:" + clsid && i.Reason == "comServerRemoved" && i.Selected) &&
                plan.Items.Any(i => i.Target.StartsWith(@"HKLM\SOFTWARE\Microsoft\Windows Media Foundation\Transforms\") && i.Reason == "comServerRemoved"));
            var windows = inventory with { ComServers = [new(@"HKLM\SOFTWARE\Classes\CLSID\{11111111-2222-3333-4444-555555555555}", "{11111111-2222-3333-4444-555555555555}", @"C:\Windows\System32\mfplat.dll", true, []),
                new(@"HKLM\SOFTWARE\Classes\CLSID\{22222222-2222-3333-4444-555555555555}", "{22222222-2222-3333-4444-555555555555}", @"C:\Windows\System32\DriverStore\FileRepository\gone_amd64_1\enc.dll", false, [])] };
            var items = DeepCleanPlanner.Create(windows, [Vendor.Nvidia]).Items.Where(i => i.Reason.StartsWith("comServer")).ToArray();
            Assert(items.Length == 1 && items[0].Reason == "comServerOrphan" && items[0].Vendor == Vendor.Unknown, string.Join(",", items.Select(i => i.Key)));
            var sandbox = Sandbox(); var real = new DemoCleanupPlatform(Path.Combine(sandbox, "System")); var secure = Secure(sandbox); var session = Session(sandbox);
            var run = DeepCleanPlanner.Create(await real.InventoryAsync(), [Vendor.Nvidia]);
            var outcome = await new DeepCleanEngine(real).RunAsync(run, All(run), session, secure, _ => { });
            Assert(outcome.Results.Any(r => r.Key == "registry:" + clsid && r.Status == "done") && (await real.InventoryAsync()).ComServers!.Count == 0);
            await new DeepCleanEngine(real).UndoAsync(session, secure, _ => { });
            Assert((await real.InventoryAsync()).ComServers!.Count == 1, "COM class not restored");
        });
        await test("PnP protection entries of a removed package go; entries Windows already dropped count as done", async () =>
        {
            var platform = new DemoCleanupPlatform(Sandbox()); var inventory = await platform.InventoryAsync();
            var key = inventory.Lockdown!.Single().Key;
            var plan = DeepCleanPlanner.Create(inventory, [Vendor.Nvidia]);
            Assert(plan.Items.Any(i => i.Key == "registry:" + key && i.Reason == "lockdownRemoved" && i.Selected));
            var orphan = inventory with { Lockdown = [new(@"HKLM\X\PnpLockdownFiles\gone", @"C:\Windows\System32\DriverStore\FileRepository\gone_amd64_1\a.dll", false),
                new(@"HKLM\X\PnpLockdownFiles\inbox", @"C:\Windows\System32\DriverStore\FileRepository\keep_amd64_1\b.dll", true)] };
            var items = DeepCleanPlanner.Create(orphan, [Vendor.Nvidia]).Items.Where(i => i.Reason.StartsWith("lockdown")).ToArray();
            Assert(items.Length == 1 && items[0].Reason == "lockdownOrphan", string.Join(",", items.Select(i => i.Key)));
            // Removed by Windows between planning and execution: done, nothing exported, nothing to restore.
            var sandbox = Sandbox(); var real = new DemoCleanupPlatform(Path.Combine(sandbox, "System")) { PackageDropsLockdown = true }; var secure = Secure(sandbox); var session = Session(sandbox);
            var run = DeepCleanPlanner.Create(await real.InventoryAsync(), [Vendor.Nvidia]);
            var outcome = await new DeepCleanEngine(real).RunAsync(run, All(run), session, secure, _ => { });
            Assert(outcome.Results.Any(r => r.Key == "registry:" + key && r.Status == "done" && r.Message == "alreadyRemoved" && r.Backup.Length == 0));
            var undo = await new DeepCleanEngine(real).UndoAsync(session, secure, _ => { });
            Assert(undo.Results.Any(r => r.Key == "registry:" + key && r.Status == "skipped") && !undo.Results.Any(r => r.Status == "failed"));
        });
        await test("System32 files are quarantined with integrity and restored by undo; registrations too", async () =>
        {
            var sandbox = Sandbox(); var platform = new DemoCleanupPlatform(Path.Combine(sandbox, "System")); var secure = Secure(sandbox);
            var plan = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]); var session = Session(sandbox);
            var nvapi = Path.Combine(platform.WindowsRoot, @"System32\nvapi64.dll");
            var outcome = await new DeepCleanEngine(platform).RunAsync(plan, All(plan), session, secure, _ => { });
            var file = outcome.Results.Single(r => r.Target == nvapi);
            Assert(file.Status == "done" && !File.Exists(nvapi) && File.Exists(Path.Combine(secure, Path.GetFileName(session), file.Backup)), file.Status + " " + file.Message);
            Assert(File.Exists(Path.Combine(platform.WindowsRoot, @"System32\vulkan-1.dll")) && File.Exists(Path.Combine(platform.WindowsRoot, @"System32\amdxc64.dll")), "shared or connected file touched");
            Assert(outcome.Results.Count(r => r.Kind == RemnantKind.Registration && r.Status == "done") == 2 && platform.Calls.Count(c => c.StartsWith("export-value:")) == 2);
            var undo = await new DeepCleanEngine(platform).UndoAsync(session, secure, _ => { });
            Assert(File.Exists(nvapi) && undo.Results.Count(r => r.Kind == RemnantKind.File && r.Status == "restored") == 4, string.Join(",", undo.Results.Where(r => r.Kind == RemnantKind.File).Select(r => r.Status + " " + r.Message)));
            Assert(undo.Results.Count(r => r.Kind == RemnantKind.Registration && r.Status == "restored") == 2 && (await platform.InventoryAsync()).Registrations!.Count == 3);
        });
        await test("A System32 file Windows already removed with its package counts as done and has nothing to restore", async () =>
        {
            var sandbox = Sandbox(); var platform = new DemoCleanupPlatform(Path.Combine(sandbox, "System")) { PackageDropsSystemFiles = true }; var secure = Secure(sandbox); var session = Session(sandbox);
            var plan = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]);
            var nvcuda = Path.Combine(platform.WindowsRoot, @"System32\nvcuda.dll");
            var outcome = await new DeepCleanEngine(platform).RunAsync(plan, All(plan), session, secure, _ => { });
            Assert(outcome.Results.Single(r => r.Target == nvcuda) is { Status: "done", Message: "alreadyRemoved", Backup.Length: 0 } && outcome.Failed == 0);
            Assert(outcome.Results.Count(r => r.Kind == RemnantKind.File && r.Message == "alreadyRemoved") == 3 && outcome.Results.Any(r => r.Target.EndsWith("nvml.dll") && r.Backup.Length > 0), "log-evidence file not quarantined");
            var undo = await new DeepCleanEngine(platform).UndoAsync(session, secure, _ => { });
            Assert(undo.Results.Single(r => r.Target == nvcuda).Status == "skipped" && !File.Exists(nvcuda) && !undo.Results.Any(r => r.Status == "failed"));
        });
        await test("A tampered quarantined file is not put back into System32", async () =>
        {
            var sandbox = Sandbox(); var platform = new DemoCleanupPlatform(Path.Combine(sandbox, "System")); var secure = Secure(sandbox);
            var plan = DeepCleanPlanner.Create(await platform.InventoryAsync(), [Vendor.Nvidia]); var session = Session(sandbox);
            var outcome = await new DeepCleanEngine(platform).RunAsync(plan, All(plan), session, secure, _ => { });
            var file = outcome.Results.First(r => r.Kind == RemnantKind.File && r.Status == "done");
            File.WriteAllText(Path.Combine(secure, Path.GetFileName(session), file.Backup), "tampered");
            var undo = await new DeepCleanEngine(platform).UndoAsync(session, secure, _ => { });
            Assert(undo.Results.Any(r => r.Key == file.Key && r.Status == "failed") && !File.Exists(file.Target));
        });
        await test("Game readiness judges the selected GPU and flags runtimes of disconnected brands", () =>
        {
            var target = new Adapter("GeForce", @"PCI\VEN_10DE&DEV_2786&SUBSYS_1\4&1", Vendor.Nvidia, "oem99.inf", "32.0", 0, true);
            var good = new ApiReport(DateTimeOffset.UtcNow, true, [new("D3D12", "GeForce", 0x10DE, 0x2786, "12_2")], "ok", [new("Vulkan", "GeForce", 0x10DE, 0x2786, "1.3.280")],
                "ok", [new("OpenCL", "NVIDIA CUDA · NVIDIA Corporation", 0x10DE, 1, "OpenCL 3.0")], []);
            var findings = GameReadiness.Evaluate(target, good, new HashSet<Vendor> { Vendor.Nvidia });
            Assert(findings.Select(f => f.Code).SequenceEqual(["d3d12Ok", "vulkanOk", "openclOk"]) && !findings.Any(f => f.Attention), string.Join(",", findings.Select(f => f.Code)));
            var stale = good with { VulkanDevices = [.. good.VulkanDevices, new("Radeon", "Radeon", 0x1002, 0x73BF, "1.3")], OpenClPlatforms = [.. good.OpenClPlatforms, new("OpenCL", "AMD APP · Advanced Micro Devices, Inc.", 0x1002, 0, "2.1")] };
            var flagged = GameReadiness.Evaluate(target, stale, new HashSet<Vendor> { Vendor.Nvidia });
            Assert(flagged.Any(f => f.Code == "vulkanStale" && f.Attention) && flagged.Any(f => f.Code == "openclStale" && f.Attention));
            var broken = GameReadiness.Evaluate(target, good with { D3D12 = [], Vulkan = "ok", VulkanDevices = [] }, new HashSet<Vendor> { Vendor.Nvidia });
            Assert(broken.Any(f => f.Code == "d3d12Missing" && f.Attention) && broken.Any(f => f.Code == "vulkanNoTarget" && f.Attention));
            Assert(GameReadiness.Evaluate(target, good with { Completed = false, Errors = ["timeout"] }, new HashSet<Vendor>()).Single().Code == "apiProbeFailed");
            Assert(GameReadiness.FeatureLevel(0xc100) == "12_1" && GameReadiness.VulkanVersion((1u << 22) | (3u << 12) | 280) == "1.3.280");
            return Done();
        });
        await test("Card change detection: waiting, swapped, no GPU, several new GPUs and another computer", () =>
        {
            Adapter Gpu(string id, Vendor vendor) => new("GPU " + id, id, vendor, "oem1.inf", "1", 0, true);
            var old = Gpu(@"PCI\VEN_1002&DEV_73BF\OLD", Vendor.Amd); var igpu = Gpu(@"PCI\VEN_8086&DEV_4680\IGPU", Vendor.Intel);
            var intent = new SwapIntent(SwapDetection.Schema, "PC", Vendor.Amd, old.Id, old.Name, "31", DateTimeOffset.UtcNow, 0, "", null, [old.Id, igpu.Id]);
            Snapshot With(params Adapter[] adapters) => new("W", "PC", true, false, false, false, 1L << 40, adapters, [], true, []);
            Assert(SwapDetection.State(intent, With(old, igpu)).State == "waiting");
            var (state, newcomer) = SwapDetection.State(intent, With(igpu, Gpu(@"PCI\VEN_10DE&DEV_2786\NEW", Vendor.Nvidia)));
            Assert(state == "swapped" && newcomer!.Vendor == Vendor.Nvidia);
            Assert(SwapDetection.State(intent, With(igpu)).State == "noGpu");
            Assert(SwapDetection.State(intent, With(Gpu(@"PCI\VEN_10DE&DEV_2786\A", Vendor.Nvidia), Gpu(@"PCI\VEN_10DE&DEV_2786\B", Vendor.Nvidia))).State == "ambiguous");
            Assert(SwapDetection.State(intent, With(igpu) with { MachineId = "OTHER" }).State == "otherMachine");
            Assert(SwapDetection.Valid(intent) && !SwapDetection.Valid(intent with { Schema = 9 }) && !SwapDetection.Valid(intent with { Phase = "x" }) && !SwapDetection.Valid(null));
            var path = Path.Combine(Sandbox(), "intent.json"); SwapRegistration.Save(path, intent with { Starts = 3 });
            Assert(SwapRegistration.Load(path) is { Starts: 3, FromVendor: Vendor.Amd });
            File.WriteAllText(path, "{\"Schema\":1}");
            try { SwapRegistration.Load(path); throw new Exception("accepted"); } catch (IOException) { }
            return Done();
        });
        await test("Validation report is English, attention-aware and carries no account or computer name", () =>
        {
            var apis = SwapReport.Summarize(new ApiReport(DateTimeOffset.UtcNow, true, [new("D3D12", "Radeon RX 7800 XT", 0x1002, 0x747E, "12_2"), new("D3D12", "Intel UHD", 0x8086, 1, "12_1")],
                "ok", [new("Vulkan", "AMD Radeon RX 7800 XT", 0x1002, 0x747E, "1.3.280")], "missing", [], []), new("Radeon", @"PCI\VEN_1002&DEV_747E\1", Vendor.Amd, "oem9.inf", "32", 0, true));
            Assert(apis.D3D12 == "Radeon RX 7800 XT · FL 12_2" && apis.Vulkan.Contains("1.3.280") && apis.OpenCl == "missing", apis.ToString());
            var validation = new SwapValidation(1, "Swap", "0.6.0", DateTimeOffset.UtcNow, false, "Windows 11 · " + Environment.MachineName, "NVIDIA", "RTX 3070", "32.0", "AMD", "RX 7800 XT", "32.1",
                true, 0, true, 23, 0, 1L << 31, 0, [new("pnpOk", "")], [], [new("d3d12Ok", "FL 12_2")], apis, ["Display · 2560×1440 · 144 Hz (max 165 Hz)"], [], SwapReport.Scope);
            var markdown = SwapReport.Markdown(validation, code => code == "d3d12Ok" ? "Direct3D 12 available on the GPU" : code);
            Assert(markdown.Contains("### Swap validation report") && markdown.Contains("23 removed") && markdown.Contains("2.0 GB") && markdown.Contains("Direct3D 12 available"), markdown);
            Assert(!markdown.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase) || Environment.MachineName.Length < 3, "computer name leaked");
            Assert(!validation.Attention && (validation with { LeftoversAfter = 2 }).Attention && (validation with { GamesAfter = [new("vulkanNoTarget", "", true)] }).Attention);
            return Done();
        });
    }

    private static StorePackage Pkg(string inf, string name, string provider, string cls, string version, string id = @"PCI\VEN_10DE&DEV_2786") =>
        new(inf, name, provider, cls, version, false, @"C:\Store\" + inf, 100, [id]);
    private static RemnantInventory Inventory(string cpu = "AuthenticAMD", string cpuName = "AMD Ryzen 7 5800X") =>
        new("TEST", cpu, cpuName, true, false, [new(@"PCI\VEN_1002&DEV_747E\NEW", "Display", "oem99.inf", "amdkmdag", true, [@"PCI\VEN_1002&DEV_747E"])], [], [], [], [], [], [], [], [], [], [], true, []);
}
