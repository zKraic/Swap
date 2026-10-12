// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using Swap.Core;
using Swap.Windows;

// KraicDev. Destructive lab check for the isolated SWAPTESTVM guest only: real signed NVIDIA driver packages staged without
// NVIDIA hardware, plus simulated vendor services, tasks, startup, uninstall entry, folders, settings and a running process.
static class LabTests
{
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}_Display.Driver";
    public static async Task RunAsync(Func<string, Func<Task>, Task> test, Action<bool, string> assert, string payload, string root)
    {
        void Assert(bool value, string message = "Assertion failed") => assert(value, message);
        await test("Real guest deep cleanup of NVIDIA leftovers with backup, quarantine and undo", async () =>
        {
            if (Environment.MachineName != "SWAPTESTVM") throw new InvalidOperationException("Deep cleanup lab belongs only in the isolated guest.");
            var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation");
            var system = Environment.SystemDirectory;
            async Task<string> Run(string exe, params string[] args) { var r = await WindowsPlatform.RunAsync(Path.Combine(system, exe), args, 600); return r.Code + " " + r.Output; }
            foreach (var inf in Directory.GetFiles(payload, "nv*.inf", SearchOption.AllDirectories)) Console.WriteLine("stage " + Path.GetFileName(inf) + ": " + await Run("pnputil.exe", "/add-driver", inf));
            void Copy(string source, string target) { Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target, true); }
            Copy(Path.Combine(system, "PING.EXE"), Path.Combine(pf, "NVIDIA App", "CEF", "NVIDIA app.exe"));
            Copy(Path.Combine(system, "cmd.exe"), Path.Combine(pf, "NvContainer", "nvcontainer.exe"));
            Copy(Path.Combine(system, "version.dll"), Path.Combine(pf, "Installer2", "InstallerCore", "NVI2.DLL"));
            Copy(Path.Combine(system, "version.dll"), Path.Combine(pf, "PhysX", "Common", "PhysXLoader.dll"));
            Copy(Path.Combine(system, "version.dll"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NVIDIA Corporation", "Drs", "nvdrsdb0.bin"));
            var users = Directory.GetDirectories(@"C:\Users").Where(d => File.Exists(Path.Combine(d, "NTUSER.DAT")) && !Path.GetFileName(d).Equals("Default", StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var user in users) Copy(Path.Combine(system, "version.dll"), Path.Combine(user, "AppData", "Local", "NVIDIA", "DXCache", "lab.bin"));
            Console.WriteLine(await Run("sc.exe", "create", "NvContainerLocalSystem", "binPath=", "\"" + Path.Combine(pf, "NvContainer", "nvcontainer.exe") + "\" -s", "start=", "demand"));
            Console.WriteLine(await Run("sc.exe", "create", "NVDisplay.ContainerLocalSystem", "binPath=", Path.Combine(system, @"DriverStore\FileRepository\nv_dispi.inf_amd64_lab\Display.NvContainer\NVDisplay.Container.exe"), "start=", "demand"));
            Console.WriteLine(await Run("schtasks.exe", "/create", "/tn", "NvTmRep_CrashReport1_{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}", "/tr", "\"" + Path.Combine(pf, "NvContainer", "nvcontainer.exe") + "\" /c exit", "/sc", "once", "/st", "23:59", "/ru", "SYSTEM", "/f"));
            using (var run = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")) run.SetValue("NvBackend", "\"C:\\Program Files (x86)\\NVIDIA Corporation\\Update Core\\NvBackend.exe\"");
            using (var settings = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\NVIDIA Corporation\Global")) settings.SetValue("Lab", 1);
            using (var app = Registry.LocalMachine.CreateSubKey(Uninstall))
            { app.SetValue("DisplayName", "NVIDIA Graphics Driver 560.94"); app.SetValue("Publisher", "NVIDIA Corporation"); app.SetValue("UninstallString", "C:\\WINDOWS\\SysWOW64\\RunDll32.EXE \"" + Path.Combine(pf, "Installer2", "InstallerCore", "NVI2.DLL") + "\",UninstallPackage Display.Driver"); }
            using var running = Process.Start(new ProcessStartInfo(Path.Combine(pf, "NVIDIA App", "CEF", "NVIDIA app.exe"), "-n 900 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;

            var platform = new WindowsCleanupPlatform();
            var inventory = await platform.InventoryAsync();
            Assert(inventory.Admin && inventory.Complete, "inventory " + string.Join("; ", inventory.Warnings));
            Assert(DeepCleanPlanner.Candidates(inventory).Contains(Vendor.Nvidia), "NVIDIA not detected as an absent brand");
            var plan = DeepCleanPlanner.Create(inventory, [Vendor.Nvidia]);
            foreach (var item in plan.Items) Console.WriteLine($"plan {item.Kind} {item.Target} [{item.Reason}] selected={item.Selected}");
            foreach (var item in plan.Kept) Console.WriteLine($"kept {item.Kind} {item.Target} [{item.Reason}]");
            var packages = plan.Items.Where(i => i.Kind == RemnantKind.Package).Select(i => i.Label.Split(' ')[0]).Order().ToArray();
            Assert(packages.SequenceEqual(["nvhda.inf", "nvpcf.inf", "nvppc.inf", "nvvad.inf"]), "packages " + string.Join(",", packages));
            foreach (var key in new[] { "service:NvContainerLocalSystem", "service:NVDisplay.ContainerLocalSystem", "task:\\NvTmRep_CrashReport1_{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}",
                @"startup:HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run|NvBackend", "app:HKLM\\" + Uninstall, @"registry:HKLM\SOFTWARE\NVIDIA Corporation",
                "Folder:" + Path.Combine(pf, "NVIDIA App"), "Folder:" + Path.Combine(pf, "NvContainer"), "Folder:" + Path.Combine(pf, "Installer2"), "process:" + running.Id })
                Assert(plan.Items.Any(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase)), "missing " + key);
            Assert(plan.Kept.Any(k => k.Target.Equals(Path.Combine(pf, "PhysX"), StringComparison.OrdinalIgnoreCase)), "PhysX not kept");
            Assert(plan.Items.All(i => i.Vendor is Vendor.Nvidia or Vendor.Unknown), "foreign item");
            Assert(!plan.Items.Any(i => i.Target.Contains("ftdi", StringComparison.OrdinalIgnoreCase) || i.Target.Contains("prnms", StringComparison.OrdinalIgnoreCase)), "unrelated package planned");

            var secure = WindowsCleanupPlatform.SecureRoot();
            var rules = new DirectoryInfo(secure).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Select(r => r.IdentityReference.Value).ToArray();
            Assert(rules.All(sid => sid is "S-1-5-32-544" or "S-1-5-18"), "secure root ACL " + string.Join(",", rules));
            var session = Path.Combine(root, "Sessions", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-CLEAN-" + Guid.NewGuid().ToString("N")[..6]);
            // Opt in to every optional item except official uninstallers, including the boot-critical-class USB-C package.
            var selected = plan.Items.Where(i => i.Kind != RemnantKind.Uninstaller).Select(i => i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var outcome = await new DeepCleanEngine(platform).RunAsync(plan, selected, session, secure, text => Console.WriteLine("clean: " + text));
            foreach (var result in outcome.Results) Console.WriteLine($"result {result.Status} {result.Kind} {result.Target} {result.Message}");
            Assert(outcome.Failed == 0 && outcome.Done >= 15, $"done={outcome.Done} failed={outcome.Failed}");
            Assert(running.WaitForExit(5000), "vendor process still running");
            var store = await WindowsPlatform.PowerShellAsync("@(Get-WindowsDriver -Online | Where-Object { $_.OriginalFileName -match '\\\\nv(hda|vad|pcf|ppc)\\.inf' }).Count");
            Assert(store.Trim() == "0", "packages remain: " + store);
            Assert(Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\NvContainerLocalSystem") is null, "service remains");
            Assert(Registry.LocalMachine.OpenSubKey(Uninstall) is null && Registry.LocalMachine.OpenSubKey(@"SOFTWARE\NVIDIA Corporation") is null, "registry remains");
            Assert(!Directory.Exists(Path.Combine(pf, "NVIDIA App")) && Directory.Exists(Path.Combine(pf, "PhysX")), "folders");
            Assert(!(await Run("schtasks.exe", "/query", "/tn", "NvTmRep_CrashReport1_{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}")).StartsWith("0 "), "task remains");
            // Only profiles Windows registers are cleaned; a stray folder under C:\Users is not a profile.
            var caches = plan.Items.Where(i => i.Kind == RemnantKind.Cache && i.Vendor == Vendor.Nvidia).Select(i => i.Target).ToArray();
            Assert(caches.Length > 0 && caches.All(c => !Directory.Exists(c)), "cache remains: " + string.Join(", ", caches.Where(Directory.Exists)));

            var undo = await new DeepCleanEngine(platform).UndoAsync(session, secure, text => Console.WriteLine("undo: " + text));
            foreach (var result in undo.Results) Console.WriteLine($"undo {result.Status} {result.Kind} {result.Target} {result.Message}");
            Assert(!undo.Results.Any(r => r.Status == "failed"), "undo failures");
            store = await WindowsPlatform.PowerShellAsync("@(Get-WindowsDriver -Online | Where-Object { $_.OriginalFileName -match '\\\\nv(hda|vad|pcf|ppc)\\.inf' }).Count");
            Assert(store.Trim() == "4", "packages not restored: " + store);
            Assert(Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\NvContainerLocalSystem") is not null && Registry.LocalMachine.OpenSubKey(Uninstall) is not null, "registry not restored");
            Assert(Registry.LocalMachine.OpenSubKey(@"SOFTWARE\NVIDIA Corporation\Global")?.GetValue("Lab") is 1, "settings not restored");
            Assert(File.Exists(Path.Combine(pf, "NVIDIA App", "CEF", "NVIDIA app.exe")) && File.Exists(Path.Combine(pf, "Installer2", "InstallerCore", "NVI2.DLL")), "folders not restored");
            Assert((await Run("schtasks.exe", "/query", "/tn", "NvTmRep_CrashReport1_{B2FE1952-0186-46C3-BAEC-A80AA35AC5B8}")).StartsWith("0 "), "task not restored");
            using (var run = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")) Assert(run?.GetValue("NvBackend") is string, "startup not restored");
            Console.WriteLine($"Deep cleanup lab: removed={outcome.Done}; restored={undo.Results.Count(r => r.Status == "restored")}; freed={outcome.Freed} bytes.");
        });
    }
}
