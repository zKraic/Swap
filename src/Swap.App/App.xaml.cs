using System.Windows;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
namespace Swap.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Preferences.Load().Apply();
        bool demo = e.Args.Contains("--demo") || e.Args.Any(a => a.StartsWith("--ui-audit="));
        bool readonlyMode = e.Args.Contains("--readonly");
        using var identity = WindowsIdentity.GetCurrent();
        bool admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
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
