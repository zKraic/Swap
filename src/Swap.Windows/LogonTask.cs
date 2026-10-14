// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
namespace Swap.Windows;

// KraicDev. A sign-in task opens Swap elevated for the person who prepared a card change or a driver restart, so it continues
// without a UAC prompt. It runs a protected copy (administrators write, users read) and Swap deletes the task when it is done.
// The HKCU RunOnce entry stays as a fallback and steps aside once the task has opened Swap.
public static class LogonTask
{
    public const string AfterSwap = @"\KraicDev\Swap after card change";
    public const string Continue = @"\KraicDev\Swap continuation";
    public const string FallbackArgument = "--task-fallback";
    public const string InstanceName = @"Global\Swap-KraicDev-Continuation";

    private static string E(string value) => SecurityElement.Escape(value);
    public static string Xml(string userSid, string executable, string argument) => $$"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Author>KraicDev Swap</Author>
            <Description>Opens Swap after sign-in to continue a graphics card or driver change. Swap deletes this task when it finishes.</Description>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
              <UserId>{{E(userSid)}}</UserId>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{{E(userSid)}}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>false</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <RunOnlyIfIdle>false</RunOnlyIfIdle>
            <WakeToRun>false</WakeToRun>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>4</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{{E(executable)}}</Command>
              <Arguments>{{E(argument)}}</Arguments>
              <WorkingDirectory>{{E(Path.GetDirectoryName(executable) ?? "")}}</WorkingDirectory>
            </Exec>
          </Actions>
        </Task>
        """;

    // Returns false when the task could not be registered; the RunOnce fallback then asks for UAC as before.
    public static async Task<bool> RegisterAsync(string name, string executable, string argument, string? userSid = null)
    {
        try
        {
            var cached = await ProtectedCopyAsync(executable);
            userSid ??= WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("No user identity.");
            var file = Path.Combine(WindowsCleanupPlatform.SecureFolder("Continuation", true), "task-" + Guid.NewGuid().ToString("N") + ".xml");
            await File.WriteAllTextAsync(file, Xml(userSid, cached, argument), Encoding.Unicode);
            try { return (await WindowsPlatform.RunAsync(WindowsPlatform.SystemExe("schtasks.exe"), ["/Create", "/TN", name, "/XML", file, "/F"], 60)).Code == 0; }
            finally { File.Delete(file); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException) { return false; }
    }

    public static void Delete(string name) => Schtasks("/Delete", "/TN", name, "/F");
    public static bool Exists(string name) => Schtasks("/Query", "/TN", name) == 0;
    private static int Schtasks(params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(WindowsPlatform.SystemExe("schtasks.exe")) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info);
            if (process is null || !process.WaitForExit(30000)) return -1;
            return process.ExitCode;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return -1; }
    }

    // The task runs elevated, so the program it starts must not be replaceable without administrator rights.
    private static async Task<string> ProtectedCopyAsync(string executable)
    {
        executable = Path.GetFullPath(executable);
        if (FileVersionInfo.GetVersionInfo(executable).CompanyName != "KraicDev") throw new IOException("The executable is not Swap.");
        var cached = Path.Combine(WindowsCleanupPlatform.SecureFolder("Continuation", true), "Swap.exe");
        byte[] hash;
        await using (var source = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            hash = await SHA256.HashDataAsync(source); source.Position = 0;
            if (!executable.Equals(cached, StringComparison.OrdinalIgnoreCase))
            {
                await using (var output = new FileStream(cached + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) await source.CopyToAsync(output);
                File.Move(cached + ".tmp", cached, true);
            }
        }
        byte[] copied;
        await using (var check = new FileStream(cached, FileMode.Open, FileAccess.Read, FileShare.Read)) copied = await SHA256.HashDataAsync(check);
        if (!CryptographicOperations.FixedTimeEquals(hash, copied)) throw new IOException("The protected copy of Swap differs from the original.");
        return cached;
    }

    // Started by the RunOnce fallback: wait briefly for the elevated instance opened by the task.
    public static bool WaitForInstance(TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (true)
        {
            try { if (Mutex.TryOpenExisting(InstanceName, out var existing)) { existing.Dispose(); return true; } }
            catch (UnauthorizedAccessException) { return true; }
            if (DateTime.UtcNow >= until) return false;
            Thread.Sleep(2000);
        }
    }
}
