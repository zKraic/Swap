using System.Windows;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
namespace Swap.App;
public partial class App : Application
{
    private static Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        // Child process of the graphics API check: no window, no elevation, no preferences.
        if (e.Args.Length == 1 && e.Args[0] == Swap.Windows.ApiProbe.Argument) { Shutdown(Swap.Windows.ApiProbe.WriteReport()); return; }
        Preferences.Load().Apply();
        bool demo = e.Args.Contains("--demo") || e.Args.Any(a => a.StartsWith("--ui-audit="));
        bool readonlyMode = e.Args.Contains("--readonly");
        using var identity = WindowsIdentity.GetCurrent();
        bool admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        // After a sign-in the task opens Swap elevated; its RunOnce fallback steps aside, and only one continuation window opens.
        bool continuation = e.Args.Contains(Swap.Windows.SwapRegistration.Argument) || e.Args.Contains(Swap.Windows.ResumeRegistration.Argument);
        if (continuation && admin)
        {
            instance = new Mutex(true, Swap.Windows.LogonTask.InstanceName, out var created);
            if (!created) { Shutdown(); return; }
        }
        if (continuation && !admin && e.Args.Contains(Swap.Windows.LogonTask.FallbackArgument) && Swap.Windows.LogonTask.WaitForInstance(TimeSpan.FromSeconds(60))) { Shutdown(); return; }
        // Keep the demo and diagnostic entry points available without elevation.
        if (!demo && !readonlyMode && !admin)
        {
            try
            {
                var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory };
                foreach (var argument in e.Args) info.ArgumentList.Add(argument);
                Process.Start(info); Shutdown(); return;
            }
            catch (Win32Exception) { /* User declined UAC: inventory remains available, all writes are gated. */ }
        }
        DispatcherUnhandledException += (_, args) => { MessageBox.Show(Localization.T("failed") + "\n\n" + args.Exception.Message, "Swap"); args.Handled = true; };
        base.OnStartup(e);
        var window = new MainWindow(); MainWindow = window; window.Show();
    }
}
