using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using Swap.Core;
using Swap.Windows;
namespace Swap.App;

public partial class MainWindow : Window
{
    private IPlatform platform;
    private Snapshot? snapshot;
    private bool busy, populating, dark, rebootRequested;
    private CancellationTokenSource? cancellation;
    private Preferences preferences;
    private string dataRoot;
    private readonly bool readOnly;
    private bool Demo => platform is DemoPlatform;
    private Adapter? TargetAdapter => GpuBox.SelectedItem as Adapter;
    private Vendor Target => TargetAdapter?.Vendor ?? Vendor.Unknown;
    private static string T(string key) => Localization.T(key);
    private sealed record FileEntry(string Label, string Path, Vendor Vendor, bool Demo = false) { public override string ToString() => Label; }
    private sealed record Choice(string Label, string Value) { public override string ToString() => Label; }
    public MainWindow()
    {
        var args = Environment.GetCommandLineArgs();
        platform = args.Contains("--demo") || args.Any(a => a.StartsWith("--ui-audit=")) ? new DemoPlatform() : new AutomaticPlatform();
        readOnly = args.Contains("--readonly");
        dataRoot = Demo ? Path.Combine(Preferences.DataRoot, "Demo") : Preferences.DataRoot;
        preferences = Preferences.Load();
        var language = args.FirstOrDefault(a => a.StartsWith("--lang="))?[7..];
        var theme = args.FirstOrDefault(a => a.StartsWith("--theme="))?[8..];
        if (language is not null) preferences = preferences with { Language = language };
        if (theme is not null) preferences = preferences with { Theme = theme };
        dark = preferences.Apply(); InitializeComponent(); PopulatePreferences(); StoragePath.Text = dataRoot;
        VersionLabel.Text = "KraicDev · v" + ApplicationVersion;
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Let Windows size the initial window to its work area on small or scaled displays.
        if (Width > SystemParameters.WorkArea.Width || Height > SystemParameters.WorkArea.Height)
            WindowState = WindowState.Maximized;
        await Guard(AnalyzeAsync);
        // A prepared card change decides whether the cleanup after a verified driver runs regardless of the preference.
        swapIntent = LoadIntent(); swapCleanup = swapIntent?.Phase == "installing";
        if (!Demo && !readOnly && snapshot is { Admin: true, Complete: true }) await Guard(ContinuePendingAsync);
        if (!readOnly && swapIntent is not null && snapshot is { Admin: true, Complete: true }) await Guard(ResumeSwapAsync);
        var audit = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--ui-audit="))?[11..];
        if (audit is not null) await UiAuditAsync(audit);
    }
    private async Task Guard(Func<Task> action)
    {
        if (busy) { Status.Text = T("wait"); return; }
        busy = true; RunButton.IsEnabled = DownloadButton.IsEnabled = DemoButton.IsEnabled = PrepareButton.IsEnabled = false;
        SwapPrimaryButton.IsEnabled = SwapSecondaryButton.IsEnabled = SwapCancelButton.IsEnabled = false;
        LanguageBox.IsEnabled = ThemeBox.IsEnabled = SourceBox.IsEnabled = GpuBox.IsEnabled = DeepCleanBox.IsEnabled = false;
        Progress.Visibility = Visibility.Visible; Details.IsExpanded = false;
        try { await action(); }
        catch (OperationCanceledException) { Status.Text = T("cancelled"); }
        catch (Exception e) { Status.Text = Localization.Translations.Keys.Any(k => T(k) == e.Message) ? e.Message : T("failed"); Log(e.ToString()); Details.IsExpanded = true; }
        finally
        {
            busy = false; LanguageBox.IsEnabled = ThemeBox.IsEnabled = SourceBox.IsEnabled = GpuBox.IsEnabled = DeepCleanBox.IsEnabled = true;
            DemoButton.IsEnabled = true; Progress.Visibility = CancelButton.Visibility = Visibility.Collapsed; UpdateButtons();
        }
    }
    private void Log(string text)
    {
        Details.Visibility = Visibility.Visible; LogBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine); LogBox.ScrollToEnd();
    }
    private void Stage(string phase)
    {
        int index = phase switch { "search" => 0, "download" => 1, "backup" => 2, "install" => 3, _ => 4 };
        var steps = new[] { Step1, Step2, Step3, Step4, Step5 };
        for (int i = 0; i < steps.Length; i++) { steps[i].Text = i < index ? "✓" : (i + 1).ToString("00"); steps[i].SetResourceReference(TextBlock.ForegroundProperty, i == index ? "Accent" : i < index ? "Good" : "Muted"); }
        Status.Text = T(phase == "cleanup" ? "verify" : phase) + "…";
    }
    private void FinishStages(bool installed = true)
    {
        var steps = new[] { Step1, Step2, Step3, Step4, Step5 };
        for (int i = 0; i < steps.Length; i++) { bool skipped = !installed && i is 1 or 3; steps[i].Text = skipped ? "—" : "✓"; steps[i].SetResourceReference(TextBlock.ForegroundProperty, skipped ? "Muted" : "Good"); }
    }
    private void ResetStages()
    {
        int index = 1; foreach (var step in new[] { Step1, Step2, Step3, Step4, Step5 }) { step.Text = (index++).ToString("00"); step.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); } OfferText.Visibility = Visibility.Collapsed;
    }
    private async Task AnalyzeAsync()
    {
        Status.Text = T("scanning"); var selectedId = TargetAdapter?.Id; snapshot = await platform.ScanAsync();
        var adapters = snapshot.Adapters.Where(a => a.Vendor is Vendor.Amd or Vendor.Nvidia or Vendor.Intel)
            .OrderBy(a => a is { Problem: 0, Signed: true } && Planner.IsOem(a.Inf)).ToArray();
        populating = true; GpuBox.ItemsSource = adapters; GpuBox.SelectedItem = TargetChoice.Select(adapters, selectedId); populating = false;
        GpuBox.Visibility = adapters.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        PaintSnapshot(); await InspectGraphicsAsync(); await ProbeApisAsync(); RefreshFiles(); Status.Text = T(TargetAdapter is not null ? "ready" : adapters.Length > 1 ? "selectGpu" : "noGpu"); UpdateButtons();
    }
    private void PaintSnapshot()
    {
        if (snapshot is null) return; var a = TargetAdapter;
        VersionLabel.Text = "KraicDev · v" + ApplicationVersion;
        GpuName.Text = a?.Name ?? T(snapshot.Adapters.Count(x => x.Vendor is Vendor.Amd or Vendor.Nvidia or Vendor.Intel) > 1 ? "selectGpu" : "noGpu"); GpuVendor.Text = a is null ? "NVIDIA · AMD · Intel" : VendorName(a.Vendor);
        DriverVersion.Text = a?.Version is { Length: > 0 } version ? version : "—";
        var healthy = a is { Problem: 0, Signed: true } && Planner.IsOem(a.Inf);
        DriverStatus.Text = T(healthy ? "signed" : "attention"); DriverStatus.SetResourceReference(TextBlock.ForegroundProperty, healthy ? "Good" : "Warning");
        SystemText.Text = snapshot.Os; DiskText.Text = $"{snapshot.FreeBytes / (1024L * 1024 * 1024)} GB";
        DemoBanner.Visibility = Demo ? Visibility.Visible : Visibility.Collapsed; ModeLabel.Text = T(Demo ? "demoMode" : "portable"); DemoButton.Content = T(Demo ? "live" : "demo");
        var alert = rebootRequested ? "reboot" : snapshot.PendingReboot ? "systemReboot" : !snapshot.Admin && !Demo ? "adminNeeded" : snapshot.Remote ? "remote" : snapshot.FreeBytes < 4L * 1024 * 1024 * 1024 ? "lowDisk" : snapshot.Laptop ? "oem" : null;
        AlertBorder.Visibility = alert is null ? Visibility.Collapsed : Visibility.Visible; AlertText.Text = alert is null ? "" : T(alert);
        RestartButton.Visibility = !Demo && (snapshot.PendingReboot || rebootRequested) ? Visibility.Visible : Visibility.Collapsed; AdminButton.Visibility = !Demo && !snapshot.Admin ? Visibility.Visible : Visibility.Collapsed;
        HealthText.Text = string.Join("\n\n", snapshot.Adapters.Select(x => $"{x.Name}\n{x.Version} · {x.Inf}\n{T(x.Signed ? "signed" : "unsigned")} · {(x.Problem == 0 ? T("healthy") : "PnP " + x.Problem)}")) + "\n\n" + T(snapshot.Admin ? "admin" : "readOnly") + (snapshot.PendingReboot ? " · " + T("reboot") : "");
        PaintGraphics();
        var source = PreviousVendor(snapshot);
        CleanupText.Text = TargetAdapter is null ? T("selectGpu") : string.Join("\n", snapshot.Drivers.Select(d => d.Inf + " · " + d.Provider + " · " + T(Planner.CleanupReason(snapshot, d, source, Target, SourceBox.SelectedIndex == 0))));
        CleanupPreview.Header = T("cleanupPreview") + " · " + (snapshot.Complete && TargetAdapter is not null ? snapshot.Drivers.Count(d => Planner.EligibleCleanup(snapshot, d, source, Target, SourceBox.SelectedIndex == 0)).ToString() : "—");
    }
    private void UpdateButtons()
    {
        bool enabled = !busy && !readOnly && !rebootRequested && snapshot is { Admin: true, Complete: true, PendingReboot: false, Remote: false } && TargetAdapter is not null && snapshot.FreeBytes >= 4L * 1024 * 1024 * 1024;
        RunButton.IsEnabled = DownloadButton.IsEnabled = enabled;
        PrepareButton.IsEnabled = !busy && !readOnly && snapshot is { Admin: true, Complete: true } && TargetAdapter is not null && swapIntent is null;
        SwapPrimaryButton.IsEnabled = SwapSecondaryButton.IsEnabled = SwapCancelButton.IsEnabled = !busy;
    }
    private static string VendorName(Vendor v) => v == Vendor.Nvidia ? "NVIDIA" : v == Vendor.Amd ? "AMD" : v == Vendor.Intel ? "Intel" : "—";
    private Vendor PreviousVendor(Snapshot current)
    {
        if (SourceBox.SelectedIndex > 0) return new[] { Vendor.Unknown, Vendor.Nvidia, Vendor.Amd, Vendor.Intel }[SourceBox.SelectedIndex];
        return Planner.DetectSource(current, Target);
    }
    private string NewSession() => Path.Combine(dataRoot, "Sessions", DateTime.Now.ToString("yyyyMMdd-HHmmss") + (Demo ? "-DEMO-" : "-LIVE-") + Guid.NewGuid().ToString("N")[..6]);
    private void Preflight(Snapshot current)
    {
        if (readOnly || !current.Admin || !current.Complete) throw new InvalidOperationException(T("adminNeeded"));
        if (current.PendingReboot || rebootRequested) throw new InvalidOperationException(T(rebootRequested?"reboot":"systemReboot"));
        if (current.Remote) throw new InvalidOperationException(T("remote"));
        if (current.FreeBytes < 4L * 1024 * 1024 * 1024) throw new InvalidOperationException(T("lowDisk"));
        if (Target == Vendor.Unknown) throw new InvalidOperationException(T("noGpu"));
        if (!current.Adapters.Any(a => a.Id == TargetAdapter?.Id && a.Vendor == Target)) throw new InvalidOperationException(T("noGpu")); EnsureWritable(dataRoot);
    }
    private async Task Operation(Func<CancellationToken, Task> action)
    {
        using var mutex = new Mutex(false, Demo ? @"Local\Swap-GPU-demo-operation" : @"Global\Swap-GPU-driver-operation");
        bool acquired; try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new InvalidOperationException(T("another"));
        try { cancellation = new CancellationTokenSource(); CancelButton.Visibility = Visibility.Visible; CancelButton.IsEnabled = true; LogBox.Clear(); Details.Visibility = Visibility.Collapsed; ResetStages(); await action(cancellation.Token); }
        finally { beforeOperation=null; cancellation?.Dispose(); cancellation = null; mutex.ReleaseMutex(); RefreshFiles(); }
    }
    private async Task AutomaticAsync(bool downloadOnly)
    {
        await Operation(async token =>
        {
            await AnalyzeAsync(); Preflight(snapshot!); var before = snapshot!; var source = PreviousVendor(before);
            if(!downloadOnly && graphicsDiagnostics is not null)beforeOperation=GraphicsHealth.Capture(before,graphicsDiagnostics,Demo);
            Stage("search"); Package? package; DriverOffer? offer = null;
            if (platform is AutomaticPlatform automatic) (package, offer) = await automatic.FindAsync(TargetAdapter!);
            else { await Task.Delay(650, token); package = await platform.InspectAsync("demo.inf", Target); }
            token.ThrowIfCancellationRequested();
            if (package is null)
            {
                if (downloadOnly) { Status.Text = T("noCompatible"); return; }
                if (TargetAdapter is not { Signed: true, Problem: 0 } || !Planner.IsOem(TargetAdapter.Inf)) { Status.Text = T("noCompatible"); return; }
                var outcome = await new Maintenance(platform).RunAsync(before, source, Target, NewSession(), Log, token, Stage, TargetAdapter!.Id, SourceBox.SelectedIndex == 0);
                rebootRequested = outcome.RebootRequired; lastVerified = outcome.Verified; await RegisterContinuationAsync(outcome);
                if (outcome.Verified) await AutoCleanupAsync(token);
                await AnalyzeAsync(); await SaveVerifiedCheckpointAsync(outcome.Verified); if (outcome.Verified) FinishStages(false);
                Status.Text = T(outcome.Verified ? "noOffer" : outcome.RebootRequired ? "reboot" : "review"); Log(outcome.SessionPath); return;
            }
            var plan = Planner.Create(before, source, Target, package, TargetAdapter!.Id, SourceBox.SelectedIndex == 0);
            if (offer is not null) { OfferText.Text = T("newDriver") + ": " + offer.Title + $" · {offer.Bytes / (1024 * 1024)} MB"; OfferText.Visibility = Visibility.Visible; Log(offer.Title); }
            Stage("download");
            if (platform is AutomaticPlatform downloader) { var result = await downloader.DownloadAsync(package); if (!result.Success) throw new IOException(result.Output); Log(result.Output); }
            else await Task.Delay(700, token);
            token.ThrowIfCancellationRequested();
            if (downloadOnly) { Step1.Text = Step2.Text = "✓"; Step1.SetResourceReference(TextBlock.ForegroundProperty, "Good"); Step2.SetResourceReference(TextBlock.ForegroundProperty, "Good"); Status.Text = T(Demo ? "demoDone" : "downloaded"); return; }
            var session = NewSession(); var resultOutcome = await new Engine(platform).RunAsync(plan, session, Log, token, Stage);
            rebootRequested = resultOutcome.RebootRequired; lastVerified = resultOutcome.Verified; await RegisterContinuationAsync(resultOutcome);
            if (resultOutcome.Verified) await AutoCleanupAsync(token);
            await AnalyzeAsync(); await SaveVerifiedCheckpointAsync(resultOutcome.Verified); if (resultOutcome.Verified) FinishStages();
            Status.Text = T(Demo ? "demoDone" : resultOutcome.Verified ? "done" : resultOutcome.RebootRequired ? "reboot" : "review");
            if (resultOutcome.RebootRequired && !Demo) { AlertBorder.Visibility = RestartButton.Visibility = Visibility.Visible; AlertText.Text = T("reboot"); } Log(session);
        });
    }
    private async Task RegisterContinuationAsync(Outcome outcome)
    {
        if (Demo || readOnly) return;
        if (outcome.RebootRequired)
        {
            if(beforeOperation is not null)
            {
                try {await File.WriteAllTextAsync(Path.Combine(outcome.SessionPath,"before-graphics.json"),JsonSerializer.Serialize(beforeOperation));}
                catch(Exception e){Log(T("checkpointReadError")+" · "+e.GetType().Name);}
            }
            await ResumeRegistration.ArmAsync(Environment.ProcessPath!, outcome.SessionPath);
        }
        else if (outcome.Verified) ResumeRegistration.Clear();
    }
    private async Task ContinuePendingAsync()
    {
        var pending = ResumeRegistration.Load(); if (pending is null) return;
        if (await WindowsPlatform.HashFileAsync(Environment.ProcessPath!) != pending.ExeSha256)
            throw new IOException("El EXE cambió. La continuación necesita revisión; no se modificaron drivers.");
        dataRoot = pending.DataRoot; StoragePath.Text = dataRoot;
        await Operation(async token =>
        {
            try
            {
                var graphicsPath=Path.Combine(pending.Session,"before-graphics.json");
                if(File.Exists(graphicsPath))
                {
                    try {if(new FileInfo(graphicsPath).Length>1024*1024)throw new IOException("Checkpoint too large.");beforeOperation=JsonSerializer.Deserialize<GraphicsCheckpoint>(await File.ReadAllTextAsync(graphicsPath));}
                    catch(Exception e){Log(T("checkpointReadError")+" · "+e.GetType().Name);}
                }
                var outcome = await new Continuation(platform).RunAsync(pending.Session, Log, token, Stage);
                rebootRequested = outcome.RebootRequired; lastVerified = outcome.Verified; await RegisterContinuationAsync(outcome);
                if (outcome.Verified) await AutoCleanupAsync(token);
                await AnalyzeAsync(); await SaveVerifiedCheckpointAsync(outcome.Verified);
                if (outcome.Verified) FinishStages(); Status.Text = T(outcome.Verified ? "done" : "reboot");
                Log(pending.Session);
            }
            catch { ResumeRegistration.Clear(); throw; }
        });
    }
    private async void ExecuteAutomatic(object sender, RoutedEventArgs e) => await Guard(() => AutomaticAsync(false));
    private async void DownloadOnly(object sender, RoutedEventArgs e) => await Guard(() => AutomaticAsync(true));
    private async void Analyze(object sender, RoutedEventArgs e) => await Guard(AnalyzeAsync);
    private void ToggleDemo(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true };
        if (!Demo) info.ArgumentList.Add("--demo");
        try { Process.Start(info); Close(); } catch (Exception ex) { Status.Text = T("failed"); Log(ex.Message); }
    }
    private void GpuChanged(object sender, SelectionChangedEventArgs e) { if (!IsLoaded || populating || busy) return; ResetStages(); PaintSnapshot(); UpdateButtons(); }
    private void Navigate(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return; var name = ((RadioButton)sender).Tag.ToString();
        foreach (var pair in new[] { (HomePage, "Home"), (CleanupPage, "Cleanup"), (LibraryPage, "Library"), (RecoveryPage, "Recovery"), (DiagnosticsPage, "Diagnostics"), (SettingsPage, "Settings") }) pair.Item1.Visibility = pair.Item2 == name ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = T(name!.ToLowerInvariant()); PageScroll.ScrollToTop(); RefreshFiles();
        if (name == "Cleanup" && cleanupInventory is null && !busy) _ = Guard(AnalyzeCleanupAsync);
    }
    private void PopulatePreferences()
    {
        populating = true;
        LanguageBox.ItemsSource = new[] { new Choice(T("systemLanguage"), "auto"), new Choice("Español", "es"), new Choice("English", "en"), new Choice("Português", "pt"), new Choice("Français", "fr"), new Choice("Deutsch", "de"), new Choice("Italiano", "it") };
        LanguageBox.DisplayMemberPath = ThemeBox.DisplayMemberPath = "Label";
        LanguageBox.SelectedIndex = Array.IndexOf(new[] { "auto", "es", "en", "pt", "fr", "de", "it" }, preferences.Language); if (LanguageBox.SelectedIndex < 0) LanguageBox.SelectedIndex = 0;
        ThemeBox.ItemsSource = new[] { new Choice(T("systemTheme"), "auto"), new Choice(T("dark"), "dark"), new Choice(T("light"), "light") };
        ThemeBox.SelectedIndex = Array.IndexOf(new[] { "auto", "dark", "light" }, preferences.Theme); if (ThemeBox.SelectedIndex < 0) ThemeBox.SelectedIndex = 0;
        SourceBox.ItemsSource = new[] { T("autoDetect"), "NVIDIA", "AMD", "Intel" }; SourceBox.SelectedIndex = Math.Clamp(preferences.Source, 0, 3);
        DeepCleanBox.IsChecked = preferences.DeepClean; populating = false;
    }
    private void DeepCleanChanged(object sender, RoutedEventArgs e) => PreferenceChanged(sender, null!);
    private void PreferenceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || populating || busy) { if (busy) DeepCleanBox.IsChecked = preferences.DeepClean; return; }
        preferences = new((LanguageBox.SelectedItem as Choice)?.Value ?? "auto", (ThemeBox.SelectedItem as Choice)?.Value ?? "auto", SourceBox.SelectedIndex, DeepCleanBox.IsChecked == true);
        dark = preferences.Apply(); PopulatePreferences(); PaintSnapshot(); RelabelCleanup(); RelabelSwap(); SetFrame(); RefreshFiles(); Status.Text = T("ready");
        PageTitle.Text = T(HomePage.Visibility == Visibility.Visible ? "home" : CleanupPage.Visibility == Visibility.Visible ? "cleanup" : LibraryPage.Visibility == Visibility.Visible ? "library" : RecoveryPage.Visibility == Visibility.Visible ? "recovery" : DiagnosticsPage.Visibility == Visibility.Visible ? "diagnostics" : "settings");
        try { if (!Demo) preferences.Save(); } catch (Exception ex) { Status.Text = T("failed"); Log(ex.Message); }
    }
    private void RefreshFiles()
    {
        var vault = Path.Combine(dataRoot, "Vault"); var files = new List<FileEntry>();
        if (Directory.Exists(vault)) foreach (var directory in Directory.GetDirectories(vault))
        {
            try { var package = JsonSerializer.Deserialize<Package>(File.ReadAllText(Path.Combine(directory, "package.json"))); if (package is not null && File.Exists(package.Path)) files.Add(new(VendorName(package.Vendor) + " · " + Path.GetFileName(package.Path) + (package.Version.Length == 0 ? "" : " · " + package.Version), package.Path, package.Vendor)); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { Log(ex.Message); }
        }
        VaultList.ItemsSource = files; VaultEmpty.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var sessions = Path.Combine(dataRoot, "Sessions"); var backups = new List<FileEntry>(); var history = new List<string>();
        if (Directory.Exists(sessions)) foreach (var directory in Directory.GetDirectories(sessions).OrderByDescending(Path.GetFileName).Take(20))
        {
            var simulated = Path.GetFileName(directory).Contains("-DEMO-"); var phase = "pending";
            try
            {
                using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "state.json"))); phase = state.RootElement.GetProperty("phase").GetString() ?? "pending";
                if (!simulated && snapshot is not null && snapshot.BootId.Length > 0 && !rebootRequested)
                {
                    var before = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(Path.Combine(directory, "before.json")));
                    // Only a session that actually requested reboot on this machine's current boot blocks a new run.
                    if (before?.MachineId == snapshot.MachineId && before.BootId == snapshot.BootId && state.RootElement.TryGetProperty("reboot", out var reboot) && reboot.GetBoolean()) rebootRequested = true;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException) { }
            var label = Path.GetFileName(directory); label = label[..Math.Min(15, label.Length)]; history.Add(label + " · " + T(phase == "complete" ? "complete" : phase == "stopped" ? "stopped" : "pending"));
            var backup = Path.Combine(directory, "backup"); if (!Directory.Exists(backup)) continue;
            foreach (var inf in Directory.GetFiles(backup, "*.inf", SearchOption.AllDirectories))
            {
                var vendor = Vendors.FromId(File.ReadAllText(inf)); backups.Add(new(simulated ? T("demoBackup") : label + " · " + VendorName(vendor) + " · " + Path.GetFileName(inf), inf, vendor, simulated));
            }
        }
        BackupList.ItemsSource = backups; HistoryEmpty.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed; HistoryText.Text = string.Join("\n", history.Take(8));
        if (rebootRequested && !Demo) { AlertBorder.Visibility = RestartButton.Visibility = Visibility.Visible; AlertText.Text = T("reboot"); }
    }
    private static void EnsureWritable(string directory) { Directory.CreateDirectory(directory); var path = Path.Combine(directory, ".write-" + Guid.NewGuid().ToString("N")); using (File.Create(path)) { } File.Delete(path); }
    private async void ChoosePackage(object sender, RoutedEventArgs e)
    {
        if (busy) return; if (Demo) { Status.Text = T("notDemo"); return; }
        var picker = new OpenFileDialog { Title = T("packagePicker"), Filter = "GPU (*.exe;*.inf)|*.exe;*.inf", CheckFileExists = true }; if (picker.ShowDialog(this) != true) return;
        await Guard(async () =>
        {
            if (Target == Vendor.Unknown) throw new IOException(T("noGpu")); var inspected = await platform.InspectAsync(picker.FileName, Target);
            var folder = Path.Combine(dataRoot, "Vault", inspected.Sha256[..16] + "-" + Guid.NewGuid().ToString("N")[..6]); EnsureWritable(folder);
            if (inspected.IsExe) File.Copy(inspected.Path, Path.Combine(folder, Path.GetFileName(inspected.Path)));
            else foreach (var file in WindowsPlatform.SafeFiles(Path.GetDirectoryName(inspected.Path)!)) { var path = Path.Combine(folder, Path.GetRelativePath(Path.GetDirectoryName(inspected.Path)!, file)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Copy(file, path); }
            var package = await platform.InspectAsync(Path.Combine(folder, Path.GetFileName(inspected.Path)), Target);
            if (package.Sha256 != inspected.Sha256) throw new IOException("Package changed during import.");
            await File.WriteAllTextAsync(Path.Combine(folder, "package.json"), JsonSerializer.Serialize(package)); RefreshFiles(); Status.Text = T("imported");
        });
    }
    private async Task InstallLocalAsync(FileEntry entry, bool restore)
    {
        await Operation(async token =>
        {
            await AnalyzeAsync(); Preflight(snapshot!); if (entry.Vendor != Target) throw new IOException(T("recoveryUnsupported"));
            if(graphicsDiagnostics is not null)beforeOperation=GraphicsHealth.Capture(snapshot!,graphicsDiagnostics,Demo);
            var package = await platform.InspectAsync(entry.Path, entry.Vendor); var plan = Planner.Create(snapshot!, restore ? Target : PreviousVendor(snapshot!), Target, package, TargetAdapter!.Id, !restore && SourceBox.SelectedIndex == 0);
            var outcome = await new Engine(platform).RunAsync(plan, NewSession(), Log, token, Stage);
            rebootRequested = outcome.RebootRequired; await RegisterContinuationAsync(outcome); await AnalyzeAsync(); await SaveVerifiedCheckpointAsync(outcome.Verified); if (outcome.Verified) FinishStages(); Status.Text = T(outcome.Verified ? "done" : outcome.RebootRequired ? "reboot" : "review");
        });
    }
    private async void InstallSaved(object sender, RoutedEventArgs e)
    {
        if (busy) return; if (VaultList.SelectedItem is not FileEntry entry) { Status.Text = T("selectPackage"); return; }
        if (MessageBox.Show(this, T("offlineConfirm"), "Swap", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) await Guard(() => InstallLocalAsync(entry, false));
    }
    private async void RestoreBackup(object sender, RoutedEventArgs e)
    {
        if (busy) return; if (BackupList.SelectedItem is not FileEntry entry) { Status.Text = T("selectBackup"); return; }
        if (entry.Demo || Demo) { Status.Text = T("demoBackup"); return; }
        if (MessageBox.Show(this, T("restoreConfirm"), "Swap", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) await Guard(async () =>
        {
            var sessions = Path.GetFullPath(Path.Combine(dataRoot, "Sessions"));
            var relative = Path.GetRelativePath(sessions, entry.Path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (relative.Length < 4 || relative[0] == ".." || relative[1] != "backup" || !Planner.IsOem(relative[2])) throw new IOException("Invalid backup location.");
            var session = Path.Combine(sessions, relative[0]); var backup = Path.Combine(session, "backup", relative[2]);
            using var state = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(session, "state.json")));
            var independent = state.RootElement.TryGetProperty("backupOnly", out var marker) && marker.GetBoolean();
            if (independent && (!state.RootElement.TryGetProperty("completed", out var completed) || !completed.GetBoolean())) throw new IOException("Incomplete backup. Restore blocked.");
            if (independent || File.Exists(Path.Combine(backup, BackupIntegrity.ManifestName))) await BackupIntegrity.VerifyAsync(backup);
            await InstallLocalAsync(entry, true);
        });
    }
    private async void CreateBackup(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        await Guard(async () => await Operation(async token =>
        {
            await AnalyzeAsync();
            if (readOnly || snapshot is not { Admin: true, Complete: true }) throw new InvalidOperationException(T("adminNeeded"));
            var path = await new GraphicsBackup(platform).RunAsync(snapshot, NewSession(), Log, token);
            RefreshFiles(); Status.Text = T("backupSaved"); Log(path);
        }));
    }
    private void Cancel(object sender, RoutedEventArgs e) { cancellation?.Cancel(); CancelButton.IsEnabled = false; Status.Text = T("stopping"); }
    private void OnClosing(object? sender, CancelEventArgs e) { if (busy) { e.Cancel = true; Status.Text = T("wait"); } }
    protected override void OnClosed(EventArgs e) { SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged; base.OnClosed(e); }
    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() => { if (preferences.Theme == "auto" || SystemParameters.HighContrast) { dark = preferences.Apply(); SetFrame(); } });
    private void Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void OnSourceInitialized(object? sender, EventArgs e) => SetFrame();
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private void SetFrame()
    {
        var handle = new WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero) return;
        int immersive = dark ? 1 : 0; int corners = 2; _ = DwmSetWindowAttribute(handle, 20, ref immersive, 4); _ = DwmSetWindowAttribute(handle, 33, ref corners, 4);
    }
    private void Elevate(object sender, RoutedEventArgs e)
    {
        if (busy || Demo) return;
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory }); Close(); }
        catch (Win32Exception ex) { Status.Text = T("adminNeeded"); Log(ex.Message); }
    }
    private void Restart(object sender, RoutedEventArgs e)
    {
        if (busy || Demo || !rebootRequested && snapshot?.PendingReboot != true) return;
        if (MessageBox.Show(this, T("restartConfirm"), "Swap", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe")) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "/r", "/t", "0" } });
    }
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private void OpenFolder(string name) { if (busy) return; try { var path = Path.Combine(dataRoot, name); Directory.CreateDirectory(path); Open(path); } catch (Exception e) { Status.Text = T("failed"); Log(e.Message); } }
    private void OpenData(object sender, RoutedEventArgs e) => OpenFolder("");
    private void OpenVault(object sender, RoutedEventArgs e) => OpenFolder("Vault");
    private void OpenSessions(object sender, RoutedEventArgs e) => OpenFolder("Sessions");
    private void OpenReports(object sender, RoutedEventArgs e) => OpenFolder("Reports");
    private void Official(object sender, RoutedEventArgs e) { if (!busy) Open(Target == Vendor.Intel ? "https://www.intel.com/content/www/us/en/download-center/home.html" : Target == Vendor.Nvidia ? "https://www.nvidia.com/en-us/drivers/" : "https://www.amd.com/en/support/download/drivers.html"); }
    private void OpenDocumentation(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try
        {
            var folder = Path.Combine(dataRoot, "Help"); Directory.CreateDirectory(folder); var assembly = Assembly.GetExecutingAssembly();
            foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Help/", StringComparison.Ordinal)))
            {
                var path = Path.Combine(folder, resource[5..].Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var stream = assembly.GetManifestResourceStream(resource)!; using var target = File.Create(path); stream.CopyTo(target);
            }
            Open(folder);
        }
        catch (Exception ex) { Status.Text = T("failed"); Log(ex.Message); }
    }
    private void OpenSources(object sender, RoutedEventArgs e) { if (!busy) Open("https://github.com/zKraic/Swap"); }
    private string ReportPath(string suffix) { var folder = Path.Combine(dataRoot, "Reports"); EnsureWritable(folder); return Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + suffix); }
    private static string ApplicationVersion => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
    private async void ExportReport(object sender, RoutedEventArgs e) => await Guard(async () => { await ExportReportAsync(); });
    private async Task<string> ExportReportAsync()
    {
        await AnalyzeAsync(); var current=snapshot!;
        var report = new { app = "Swap", author = "KraicDev", version = ApplicationVersion, generated = DateTimeOffset.Now, simulated = Demo, current.Os, current.Admin, current.Complete, current.PendingReboot, current.Laptop, current.FreeBytes,
            adapters = current.Adapters.Select(a => new { a.Name, a.Vendor, a.Version, a.Inf, a.Problem, a.Signed }), current.Drivers,
            cleanup = current.Drivers.Select(d => new { d.Inf, d.Provider, reason = TargetAdapter is null ? "selectGpu" : Planner.CleanupReason(current, d, PreviousVendor(current), Target, SourceBox.SelectedIndex == 0), eligible = TargetAdapter is not null && Planner.EligibleCleanup(current, d, PreviousVendor(current), Target, SourceBox.SelectedIndex == 0) }),
            preparation = Preparation.Assess(current, TargetAdapter, PreviousVendor(current), Target, SourceBox.SelectedIndex == 0,
                graphicsDiagnostics is not null && GraphicsHealth.Evaluate(TargetAdapter, graphicsDiagnostics).Any(f => f.Code == "direct3dOk")),
            displays=graphicsDiagnostics?.Displays.Select(d=>new {d.Name,d.Width,d.Height,d.Hz,d.MaximumHz}),
            probes=graphicsDiagnostics?.Probes.Select(p=>new {p.Name,p.DedicatedBytes,p.Hardware,p.Direct3D,p.FeatureLevel,p.Error}),
            games=GameFindings(TargetAdapter), apis=SwapReport.Summarize(apiReport,TargetAdapter),
            incidents=graphicsDiagnostics?.Incidents, graphicsDiagnostics?.DisplaysComplete, graphicsDiagnostics?.EventsComplete,
            changes=checkpoint is not null && graphicsDiagnostics is not null && checkpoint.MachineId==current.MachineId && checkpoint.Simulated==Demo ? GraphicsHealth.Compare(checkpoint,current,graphicsDiagnostics,Demo) : null,
            scope = "Windows PnP, driver, Direct3D and display observations. No electrical, cabling, FPS or game certification." };
        var path = ReportPath("-report.json"); await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); Status.Text = T("saved"); Log(path); return path;
    }
    private async void Dxdiag(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (Demo) { Status.Text = T("notDemo"); return; } var path = ReportPath("-dxdiag.xml"); var result = await WindowsPlatform.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "dxdiag.exe"), ["/whql:off", "/x", path], 180);
        if (!result.Success || !File.Exists(path)) throw new IOException(result.Output); Status.Text = T("saved"); Log(path);
    });
    private async void Events(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (Demo) { Status.Text = T("notDemo"); return; }
        const string script = "$events=@(Get-WinEvent -FilterHashtable @{LogName='System';StartTime=(Get-Date).AddDays(-3);Level=1,2,3} -MaxEvents 200 -ErrorAction SilentlyContinue | Where-Object {$_.ProviderName -match '^(Display|nvlddmkm|amdwddmg|amdkmdag|igfx|Microsoft-Windows-WHEA-Logger)$'} | Select-Object TimeCreated,ProviderName,Id,LevelDisplayName,Message); ConvertTo-Json -InputObject @($events) -Depth 4";
        var json = await WindowsPlatform.PowerShellAsync(script); var path = ReportPath("-events.json"); await File.WriteAllTextAsync(path, json); Status.Text = T("saved"); Log(path);
    });
}
