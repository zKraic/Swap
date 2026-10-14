// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Swap.Core;
using Swap.Windows;
namespace Swap.App;

// KraicDev. "I'm changing my card": record the state before, open again after the swap, install and clean up on its own,
// and finish with a validation report. Every step keeps the normal checks; the preparation only remembers the person's choice.
public partial class MainWindow
{
    private SwapIntent? swapIntent;
    private bool swapCleanup, lastVerified;
    private CleanupOutcome? lastCleanup;
    private string swapState = "";
    private Adapter? swapNewcomer;
    private SwapValidation? lastValidation;
    private string? lastValidationPath;
    private DispatcherTimer? swapTimer; private int swapSeconds;
    private const string GithubForm = "https://github.com/zKraic/Swap/issues/new?template=hardware_validation.yml";
    private string IntentFile => Demo ? Path.Combine(dataRoot, "swap-intent.json") : SwapRegistration.IntentPath;

    private SwapIntent? LoadIntent()
    {
        try { return SwapRegistration.Load(IntentFile); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        { Log(e.Message); try { SwapRegistration.Clear(IntentFile); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } return null; }
    }
    private void SaveIntent(SwapIntent intent) { SwapRegistration.Save(IntentFile, intent); swapIntent = intent; }
    private void ClearIntent()
    {
        swapTimer?.Stop(); swapIntent = null; swapCleanup = false;
        try { if (Demo) { if (File.Exists(IntentFile)) File.Delete(IntentFile); } else SwapRegistration.Clear(IntentFile); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log(e.Message); }
        UpdateButtons();
    }

    // ---- Before the swap ----
    private async void PrepareSwap(object sender, RoutedEventArgs e)
    {
        if (busy || TargetAdapter is not { } adapter) return;
        if (MessageBox.Show(this, string.Format(T("swapPrepareConfirm"), adapter.Name), "Swap", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await Guard(() => Operation(PrepareSwapCoreAsync));
    }
    private async Task PrepareSwapCoreAsync(CancellationToken token)
    {
        var selected = TargetAdapter?.Id; await AnalyzeAsync();
        if (readOnly || snapshot is not { Admin: true, Complete: true }) throw new InvalidOperationException(T("adminNeeded"));
        var from = snapshot.Adapters.FirstOrDefault(a => a.Id == selected) ?? throw new InvalidOperationException(T("noGpu"));
        var backup = "";
        try { backup = await new GraphicsBackup(platform).RunAsync(snapshot, NewSession(), Log, token); }
        catch (InvalidOperationException e) { Log(T("swapNoBackup") + " · " + e.Message); }
        var graphics = graphicsDiagnostics is null ? null : GraphicsHealth.Capture(snapshot, graphicsDiagnostics, Demo);
        SaveIntent(new(SwapDetection.Schema, snapshot.MachineId, from.Vendor, from.Id, from.Name, from.Version, DateTimeOffset.Now, 0, backup, apiReport,
            snapshot.Adapters.Where(a => a.Vendor != Vendor.Unknown).Select(a => a.Id).ToArray(), dataRoot, "prepared", graphics, "", Demo));
        if (!Demo) await SwapRegistration.ArmAsync(Environment.ProcessPath!);
        RefreshFiles(); ShowSwap("prepared", null); Status.Text = T("swapReadyTitle");
    }

    // ---- After the swap: runs on every start while a preparation exists ----
    private async Task ResumeSwapAsync()
    {
        var intent = swapIntent ?? LoadIntent();
        if (intent is null || snapshot is null) return;
        if (intent.MachineId != snapshot.MachineId || intent.Simulated != Demo || intent.Phase == "done") { ClearIntent(); return; }
        intent = intent with { Starts = intent.Starts + 1 };
        if (intent.Starts > SwapDetection.MaximumStarts) { ClearIntent(); Status.Text = T("swapExpired"); return; }
        SaveIntent(intent);
        if (intent.DataRoot.Length > 0 && Directory.Exists(intent.DataRoot) && !Demo) { dataRoot = intent.DataRoot; StoragePath.Text = dataRoot; }
        if (intent.Phase == "installing")
        {
            swapCleanup = true;
            // A driver change that needed a restart continues first; finish once its result is verified.
            if (!Demo && ResumeRegistration.Load() is not null) { ShowSwap("continuing", null); return; }
            var target = snapshot.Adapters.FirstOrDefault(a => a.Id.Equals(intent.ToAdapterId, StringComparison.OrdinalIgnoreCase));
            if (lastVerified || target is { Problem: 0, Signed: true } && Planner.IsOem(target.Inf)) await FinishSwapCoreAsync();
            else ShowSwap("review", target);
            return;
        }
        var (state, newcomer) = SwapDetection.State(intent, snapshot);
        // Keep the next start armed; a failure here must not hide what Swap found.
        if (!Demo && state is "waiting" or "noGpu" or "ambiguous")
        {
            try { await SwapRegistration.ArmAsync(Environment.ProcessPath!); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log(e.Message); }
        }
        swapNewcomer = newcomer;
        ShowSwap(state, newcomer);
        if (state == "swapped" && newcomer is not null) StartSwapCountdown(newcomer);
    }
    private void StartSwapCountdown(Adapter newcomer)
    {
        SelectAdapter(newcomer.Id);
        swapSeconds = 20; ShowSwap("swapped", newcomer);
        swapTimer?.Stop(); swapTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        swapTimer.Tick += async (_, _) =>
        {
            if (busy) return;
            if (--swapSeconds > 0) { SwapText.Text = string.Format(T("swapCountdown"), swapSeconds); return; }
            swapTimer?.Stop(); await StartSwapInstallAsync();
        };
        if (!IsAuditRun) swapTimer.Start();
    }
    private void SelectAdapter(string id)
    {
        if (GpuBox.ItemsSource is not IEnumerable<Adapter> adapters || adapters.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is not { } adapter) return;
        populating = true; GpuBox.SelectedItem = adapter; populating = false; PaintSnapshot(); UpdateButtons();
    }
    private async Task StartSwapInstallAsync()
    {
        swapTimer?.Stop();
        if (busy || swapIntent is not { } intent) return;
        var target = TargetAdapter;
        if (target is null || target.Id.Equals(intent.FromAdapterId, StringComparison.OrdinalIgnoreCase)) { Status.Text = T("selectGpu"); return; }
        SaveIntent(intent with { Phase = "installing", ToAdapterId = target.Id });
        // From here a restart is handled by the driver continuation, which opens Swap itself.
        if (!Demo) SwapRegistration.Disarm();
        swapCleanup = true; lastVerified = false; lastCleanup = null;
        // Right after sign-in the network may not be up yet, or Windows may be installing the same driver on its own.
        // Each attempt re-plans from a fresh inventory; a driver Windows installed meanwhile is verified and kept.
        for (int attempt = 0; ; attempt++)
        {
            ShowSwap("installing", target); swapAttempts = attempt + 1;
            await Guard(() => AutomaticAsync(false));
            if (lastVerified || rebootRequested || swapIntent is null || attempt >= SwapDetection.InstallRetries.Count) break;
            Log(T("swapRetryLog") + " · " + Status.Text);
            if (!await WaitForRetryAsync(Demo ? 1 : SwapDetection.InstallRetries[attempt], target)) return;
            SelectAdapter(target.Id);
        }
        if (lastVerified) await Guard(FinishSwapCoreAsync);
        else ShowSwap(rebootRequested ? "continuing" : "review", target);
    }
    private CancellationTokenSource? swapWait;
    private int swapAttempts;
    private async Task<bool> WaitForRetryAsync(int seconds, Adapter target)
    {
        using var wait = swapWait = new CancellationTokenSource();
        try
        {
            ShowSwap("retry", target);
            for (int left = seconds; left > 0; left--) { SwapText.Text = string.Format(T("swapRetry"), left); await Task.Delay(1000, wait.Token); }
            return swapIntent is not null;
        }
        catch (OperationCanceledException) { return false; }
        finally { swapWait = null; }
    }
    private async Task FinishSwapCoreAsync()
    {
        if (swapIntent is not { } intent) return;
        await AnalyzeAsync();
        var target = snapshot!.Adapters.FirstOrDefault(a => a.Id.Equals(intent.ToAdapterId, StringComparison.OrdinalIgnoreCase)) ?? TargetAdapter;
        if (target is not null) SelectAdapter(target.Id);
        var validation = await BuildValidationAsync(intent, target);
        lastValidationPath = await SaveValidationAsync(validation);
        lastValidation = validation;
        ClearIntent();
        ShowSwap("done", target);
        Status.Text = T("swapDoneTitle");
    }

    // ---- Validation report ----
    private async Task<SwapValidation> BuildValidationAsync(SwapIntent? intent, Adapter? target)
    {
        var current = snapshot!;
        int leftovers = 0;
        try
        {
            var inventory = await Cleanup.InventoryAsync();
            if (inventory.Admin && inventory.Complete && DeepCleanPlanner.Candidates(inventory) is { Count: > 0 } vendors)
                leftovers = DeepCleanPlanner.Create(inventory, vendors).Items.Count(i => i.Selected && i.Kind is not (RemnantKind.Cache or RemnantKind.Process or RemnantKind.Uninstaller));
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or JsonException) { Log(e.Message); leftovers = -1; }
        finally { cleanupInventory = null; }
        IReadOnlyList<HealthFinding> before = [];
        if (intent?.Before is { } previous)
        {
            var old = new Adapter(intent.FromName, intent.FromAdapterId, intent.FromVendor, "", intent.FromDriver, 0, true);
            before = GameReadiness.Evaluate(old, previous, (intent.BeforeAdapters ?? []).Select(Vendors.FromId).Where(v => v != Vendor.Unknown).ToHashSet());
        }
        IReadOnlyList<HealthFinding> changes = [];
        if (intent?.Graphics is { } checkpoint && graphicsDiagnostics is not null)
        {
            try { changes = GraphicsHealth.Compare(checkpoint, current, graphicsDiagnostics, Demo); }
            catch (InvalidOperationException) { changes = []; }
        }
        var source = intent?.FromVendor ?? PreviousVendor(current);
        return new(1, "Swap", ApplicationVersion, DateTimeOffset.Now, Demo, current.Os,
            SwapReport.VendorName(source), intent?.FromName ?? "not recorded", intent?.FromDriver ?? "",
            SwapReport.VendorName(target?.Vendor ?? Vendor.Unknown), target?.Name ?? "not detected", target?.Version ?? "", target?.Signed == true && Planner.IsOem(target.Inf), target?.Problem ?? -1,
            target is { Problem: 0, Signed: true } && Planner.IsOem(target.Inf) && (intent is null || lastVerified || intent.Phase == "installing"),
            lastCleanup?.Done ?? 0, lastCleanup?.Failed ?? 0, lastCleanup?.Freed ?? 0, leftovers,
            graphicsDiagnostics is null ? [] : GraphicsHealth.Evaluate(target, graphicsDiagnostics), before, GameFindings(target), SwapReport.Summarize(apiReport, target),
            graphicsDiagnostics?.Displays.Select(d => $"{d.Name} · {d.Width}×{d.Height} · {d.Hz} Hz (max {d.MaximumHz} Hz)").ToArray() ?? [], changes, SwapReport.Scope);
    }
    private async Task<string> SaveValidationAsync(SwapValidation validation)
    {
        var markdown = SwapReport.Markdown(validation, Localization.English);
        var path = ReportPath("-validation.md");
        await File.WriteAllTextAsync(path, markdown);
        await File.WriteAllTextAsync(Path.ChangeExtension(path, ".json"), SwapReport.Sanitize(JsonSerializer.Serialize(validation, new JsonSerializerOptions { WriteIndented = true })));
        Log(path); return path;
    }
    private async void ExportValidation(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        await AnalyzeAsync();
        var validation = await BuildValidationAsync(null, TargetAdapter);
        var path = await SaveValidationAsync(validation);
        lastValidation = validation; lastValidationPath = path;
        CopyText(await File.ReadAllTextAsync(path));
        Status.Text = T("validationSaved");
    });
    private void CopyText(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException e) { Log(e.Message); }
    }
    private void OpenGithubForm(object sender, RoutedEventArgs e) { if (!busy) Open(GithubForm); }

    // ---- Banner ----
    private void ShowSwap(string state, Adapter? adapter)
    {
        swapState = state; SwapBanner.Visibility = Visibility.Visible;
        SwapPrimaryButton.Visibility = SwapSecondaryButton.Visibility = SwapExtraButton.Visibility = Visibility.Collapsed;
        // The status bar says the same as the banner, not a generic "ready to start".
        if (state is "prepared" or "waiting") Status.Text = T("swapReadyTitle");
        else if (state == "noGpu") Status.Text = T("swapNoGpuTitle");
        SwapCancelButton.Visibility = Visibility.Visible; SwapCancelButton.Content = T("swapCancel");
        void Primary(string key) { SwapPrimaryButton.Content = T(key); SwapPrimaryButton.Visibility = Visibility.Visible; }
        switch (state)
        {
            case "prepared": case "waiting":
                SwapTitle.Text = T("swapReadyTitle"); SwapText.Text = string.Format(T("swapReadyText"), swapIntent?.FromName ?? "GPU"); Primary("swapShutdown"); break;
            case "noGpu": SwapTitle.Text = T("swapNoGpuTitle"); SwapText.Text = T("swapNoGpuText"); break;
            case "swapped":
                SwapTitle.Text = string.Format(T("swapDetectedTitle"), adapter?.Name ?? "GPU"); SwapText.Text = string.Format(T("swapCountdown"), swapSeconds); Primary("swapStartNow"); break;
            case "ambiguous": SwapTitle.Text = T("swapAmbiguousTitle"); SwapText.Text = T("swapAmbiguousText"); Primary("swapStartNow"); break;
            case "installing": SwapTitle.Text = T("swapInstallingTitle"); SwapText.Text = adapter?.Name ?? ""; SwapCancelButton.Visibility = Visibility.Collapsed; break;
            case "retry": SwapTitle.Text = T("swapInstallingTitle"); SwapText.Text = ""; break;
            case "continuing": SwapTitle.Text = T("swapContinuingTitle"); SwapText.Text = T("reboot"); break;
            case "review": SwapTitle.Text = T("swapReviewTitle"); SwapText.Text = T("review"); break;
            case "done":
                SwapTitle.Text = T("swapDoneTitle");
                var games = lastValidation is null ? "" : T(lastValidation.GamesAfter.Any(f => f.Attention) ? "gamesAttention" : "gamesReady");
                SwapText.Text = string.Format(T("swapDoneText"), lastValidation?.CleanupRemoved ?? 0, games);
                Primary("swapOpenReport"); SwapSecondaryButton.Content = T("swapShare"); SwapSecondaryButton.Visibility = Visibility.Visible; SwapCancelButton.Content = T("swapDismiss");
                // Windows often starts the new card at 60 Hz: one click applies the highest rate, with the usual keep-or-revert.
                // Otherwise, a Windows Update driver comes without the manufacturer's control panel: point to it, optional.
                if (graphicsDiagnostics?.Displays.FirstOrDefault(d => d.Hz > 1 && d.MaximumHz > d.Hz + 1) is { } slow)
                { SwapExtraButton.Content = string.Format(T("swapRefreshButton"), slow.MaximumHz); SwapExtraButton.Tag = slow; SwapExtraButton.Visibility = Visibility.Visible; }
                else if (adapter is { Vendor: not Vendor.Unknown } card && VendorApp(card.Vendor) is { } app && (Demo || !WindowsPlatform.VendorSoftwareInstalled(card.Vendor)))
                { SwapExtraButton.Content = string.Format(T("swapVendorApp"), app.Name); SwapExtraButton.Tag = app.Url; SwapExtraButton.Visibility = Visibility.Visible; }
                break;
            default: SwapBanner.Visibility = Visibility.Collapsed; break;
        }
        UpdateButtons();
    }
    private void RelabelSwap() { if (swapState.Length > 0 && SwapBanner.Visibility == Visibility.Visible) ShowSwap(swapState, swapState == "done" ? TargetAdapter : swapNewcomer); }
    private async void SwapPrimary(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        switch (swapState)
        {
            case "prepared": case "waiting":
                if (Demo) { Status.Text = T("notDemo"); return; }
                if (MessageBox.Show(this, T("shutdownConfirm"), "Swap", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe")) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "/s", "/t", "0" } });
                break;
            case "swapped": case "ambiguous": await StartSwapInstallAsync(); break;
            case "done": if (lastValidationPath is not null && File.Exists(lastValidationPath)) Open(lastValidationPath); break;
        }
    }
    private async void SwapSecondary(object sender, RoutedEventArgs e)
    {
        if (busy || swapState != "done" || lastValidationPath is null || !File.Exists(lastValidationPath)) return;
        CopyText(await File.ReadAllTextAsync(lastValidationPath)); Open(GithubForm); Status.Text = T("swapShared");
    }
    private static (string Name, string Url)? VendorApp(Vendor vendor) => vendor switch
    {
        Vendor.Amd => ("AMD Software: Adrenalin Edition", "https://www.amd.com/en/support/download/drivers.html"),
        Vendor.Nvidia => ("NVIDIA App", "https://www.nvidia.com/en-us/software/nvidia-app/"),
        Vendor.Intel => ("Intel Graphics Software", "https://www.intel.com/content/www/us/en/download-center/home.html"),
        _ => null
    };
    private void SwapExtra(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        switch (SwapExtraButton.Tag)
        {
            case string url: Open(url); break;
            case DisplayOutput display:
                // The same keep-or-revert confirmation as in Diagnostics, where it is shown.
                DiagnosticsNav.IsChecked = true; DisplayDisclosure.IsExpanded = true;
                if (RefreshPanel.Children.OfType<Button>().FirstOrDefault(b => b.Tag is DisplayOutput d && d.Key == display.Key) is { } button) ApplyRefresh(button, new RoutedEventArgs());
                SwapExtraButton.Visibility = Visibility.Collapsed; break;
        }
    }
    private void SwapCancel(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        swapWait?.Cancel();
        if (swapState != "done") { ClearIntent(); Status.Text = T("swapCancelled"); }
        swapState = ""; SwapBanner.Visibility = Visibility.Collapsed; UpdateButtons();
    }
}
