// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Swap.Core;
using Swap.Windows;
namespace Swap.App;

// KraicDev. Deep cleanup of disconnected brands: preview with reasons, explicit selection, backup, quarantine and undo.
public partial class MainWindow
{
    private ICleanupPlatform? cleanupPlatform;
    private RemnantInventory? cleanupInventory;
    private DeepCleanPlan? cleanupPlan, versionsPlan;
    private List<CleanupRow> cleanupRows = [];
    private sealed class CleanupRow(RemnantItem item, string title, string detail, string size) : INotifyPropertyChanged
    {
        private bool selected = item.Selected;
        public RemnantItem Item { get; } = item;
        public string Title { get; } = title;
        public string Detail { get; } = detail;
        public string Size { get; } = size;
        public bool Editable => Item.Kind != RemnantKind.Process;
        public bool Selected { get => selected; set { selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        public override string ToString() => Title;
    }
    private ICleanupPlatform Cleanup => cleanupPlatform ??= Demo ? new DemoCleanupPlatform(DemoSandbox()) { Physx = true } : new WindowsCleanupPlatform();
    // Cleanup never works inside Windows. Under the SYSTEM account the profile is there, so the simulated PC moves out.
    private string DemoSandbox() => DeepCleanPlanner.Within(dataRoot, Environment.GetFolderPath(Environment.SpecialFolder.Windows))
        ? Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "SwapDemo", "DemoSystem") : Path.Combine(dataRoot, "DemoSystem");
    private string CleanupBackupRoot() { if (!Demo) return WindowsCleanupPlatform.SecureRoot(); var path = Path.Combine(dataRoot, "DemoCleanup"); Directory.CreateDirectory(path); return path; }
    private string NewCleanupSession() => Path.Combine(dataRoot, "Sessions", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-CLEAN-" + Guid.NewGuid().ToString("N")[..6]);
    public static string Size(long bytes) => bytes <= 0 ? "" : bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0} MB" : $"{Math.Max(1, bytes / 1024)} KB";
    private static string KindName(RemnantKind kind) => T("kind" + kind);
    private static string VendorLabel(Vendor vendor) => vendor switch { Vendor.Nvidia => "NVIDIA", Vendor.Amd => "AMD", Vendor.Intel => "Intel", _ => "DirectX" };

    private async Task AnalyzeCleanupAsync()
    {
        if (readOnly) { Status.Text = T("adminNeeded"); return; }
        Status.Text = T("scanning");
        cleanupInventory = await Cleanup.InventoryAsync();
        if (!cleanupInventory.Admin || !cleanupInventory.Complete) { cleanupPlan = versionsPlan = null; PaintCleanup(); Status.Text = T("adminNeeded"); return; }
        var present = DeepCleanPlanner.PresentGpuVendors(cleanupInventory); var candidates = DeepCleanPlanner.Candidates(cleanupInventory);
        foreach (var (box, vendor) in new[] { (CleanNvidia, Vendor.Nvidia), (CleanAmd, Vendor.Amd), (CleanIntel, Vendor.Intel) })
        { box.IsEnabled = !present.Contains(vendor); box.IsChecked = candidates.Contains(vendor); }
        LabelCleanupBrands(present);
        PlanCleanup();
        versionsPlan = DeepCleanPlanner.Refresh(cleanupInventory, new(cleanupInventory.MachineId, [], [], [], [], true));
        PaintCleanup(); Status.Text = T("ready");
    }
    private void PlanCleanup()
    {
        if (cleanupInventory is null) return;
        var vendors = new[] { (CleanNvidia, Vendor.Nvidia), (CleanAmd, Vendor.Amd), (CleanIntel, Vendor.Intel) }.Where(v => v.Item1.IsChecked == true && v.Item1.IsEnabled).Select(v => v.Item2).ToArray();
        cleanupPlan = DeepCleanPlanner.Create(cleanupInventory, vendors);
        cleanupRows = Rows(cleanupPlan, null);
    }
    private static List<CleanupRow> Rows(DeepCleanPlan plan, IReadOnlyDictionary<string, bool>? selection) => plan.Items.Select(i =>
        new CleanupRow(selection is not null && selection.TryGetValue(i.Key, out var chosen) ? i with { Selected = chosen } : i, KindName(i.Kind) + " · " + i.Label,
            VendorLabel(i.Vendor) + " · " + T(i.Reason), Size(i.Bytes))).ToList();
    private void LabelCleanupBrands(IReadOnlySet<Vendor> present)
    {
        foreach (var (box, vendor) in new[] { (CleanNvidia, Vendor.Nvidia), (CleanAmd, Vendor.Amd), (CleanIntel, Vendor.Intel) })
            box.Content = VendorLabel(vendor) + (present.Contains(vendor) ? " · " + T("cleanupConnected") : "");
    }
    // Language changes rebuild the visible text and keep the person's selection.
    private void RelabelCleanup()
    {
        if (cleanupInventory is null || cleanupPlan is null) return;
        LabelCleanupBrands(DeepCleanPlanner.PresentGpuVendors(cleanupInventory));
        cleanupRows = Rows(cleanupPlan, cleanupRows.ToDictionary(r => r.Item.Key, r => r.Selected, StringComparer.OrdinalIgnoreCase));
        PaintCleanup();
    }
    private void PaintCleanup()
    {
        CleanupList.ItemsSource = cleanupRows;
        bool any = cleanupRows.Count > 0;
        CleanupList.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        CleanupEmpty.Visibility = any || cleanupInventory is null ? Visibility.Collapsed : Visibility.Visible;
        var notes = (cleanupPlan?.Notes ?? []).Select(n => { var parts = n.Split(':'); return parts.Length == 2 && Enum.TryParse<Vendor>(parts[1], out var v) ? string.Format(T(parts[0]), VendorLabel(v)) : T(n); }).Distinct().ToArray();
        CleanupNotes.Text = string.Join("\n", notes); CleanupNotes.Visibility = notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var kept = cleanupPlan?.Kept ?? [];
        CleanupKeptText.Text = string.Join("\n", kept.Select(k => VendorLabel(k.Vendor) + " · " + KindName(k.Kind) + " · " + k.Label + " — " + T(k.Reason)));
        CleanupKeptPanel.Visibility = kept.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCleanupSummary();
        var versions = versionsPlan?.Items ?? [];
        FreeSpaceText.Text = cleanupInventory is null ? "—" : versions.Count == 0 ? T("freeSpaceNone") : string.Format(T("freeSpaceFound"), versions.Count, Size(versions.Sum(v => v.Bytes))) + "\n" + string.Join("\n", versions.Select(v => v.Label));
        FreeSpaceButton.IsEnabled = !busy && !readOnly && versions.Count > 0;
    }
    private void UpdateCleanupSummary()
    {
        var chosen = cleanupRows.Where(r => r.Selected && r.Item.Kind != RemnantKind.Process).ToArray();
        CleanupSummary.Text = cleanupRows.Count == 0 ? "" : string.Format(T("cleanupSummary"), chosen.Length, Size(chosen.Sum(r => r.Item.Bytes)) is { Length: > 0 } size ? size : "0 KB");
        CleanupRunButton.IsEnabled = !busy && !readOnly && chosen.Length > 0;
    }
    private void CleanupItemChanged(object sender, RoutedEventArgs e) => UpdateCleanupSummary();
    private void CleanupVendorChanged(object sender, RoutedEventArgs e) { if (busy || cleanupInventory is null) return; PlanCleanup(); PaintCleanup(); }
    private async void AnalyzeCleanup(object sender, RoutedEventArgs e) => await Guard(AnalyzeCleanupAsync);

    private async void RunCleanup(object sender, RoutedEventArgs e)
    {
        if (busy || cleanupPlan is null) return;
        var keys = cleanupRows.Where(r => r.Selected).Select(r => r.Item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var count = cleanupRows.Count(r => r.Selected && r.Item.Kind != RemnantKind.Process);
        if (count == 0) return;
        if (MessageBox.Show(this, string.Format(T("cleanupConfirm"), count), "Swap", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var plan = cleanupPlan;
        await Guard(() => Operation(async token => { await RunCleanupCoreAsync(plan, keys, token); var message = Status.Text; await AnalyzeCleanupAsync(); Status.Text = message; }));
    }
    private async void FreeSpace(object sender, RoutedEventArgs e)
    {
        if (busy || versionsPlan is not { Items.Count: > 0 } plan) return;
        if (MessageBox.Show(this, string.Format(T("cleanupConfirm"), plan.Items.Count), "Swap", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await Guard(() => Operation(async token => { await RunCleanupCoreAsync(plan, plan.Items.Select(i => i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase), token); var message = Status.Text; await AnalyzeCleanupAsync(); Status.Text = message; }));
    }
    private async Task<CleanupOutcome> RunCleanupCoreAsync(DeepCleanPlan plan, IReadOnlySet<string> keys, CancellationToken token)
    {
        if (readOnly) throw new InvalidOperationException(T("adminNeeded"));
        var current = await platform.ScanAsync();
        if (current.Remote) throw new InvalidOperationException(T("remote"));
        if (current.PendingReboot && !Demo) throw new InvalidOperationException(T("systemReboot"));
        EnsureWritable(dataRoot);
        var outcome = await new DeepCleanEngine(Cleanup).RunAsync(plan, keys, NewCleanupSession(), CleanupBackupRoot(), Log, token, CleanupStage, CleanupProgress);
        Status.Text = string.Format(T("cleanupDone"), outcome.Done, outcome.Failed, Size(outcome.Freed) is { Length: > 0 } size ? size : "0 KB") + (outcome.RestartRecommended ? " " + T("cleanupRestart") : "");
        foreach (var result in outcome.Results.Where(r => r.Status != "done")) Log(T("status" + char.ToUpperInvariant(result.Status[0]) + result.Status[1..]) + " · " + result.Label + " · " + result.Message);
        if (outcome.RestartRecommended && !Demo) { rebootRequested = true; }
        return outcome;
    }

    // After a verified driver change, remove leftovers of disconnected brands that need no manual review.
    private async Task AutoCleanupAsync(CancellationToken token)
    {
        // A prepared card change asked for the cleanup explicitly.
        if (!preferences.DeepClean && !swapCleanup || readOnly) return;
        try
        {
            var inventory = await Cleanup.InventoryAsync(token);
            if (!inventory.Admin || !inventory.Complete) return;
            var vendors = DeepCleanPlanner.Candidates(inventory);
            if (vendors.Count == 0) return;
            var plan = DeepCleanPlanner.Create(inventory, vendors);
            var keys = plan.Items.Where(i => i.Selected && i.Kind != RemnantKind.Uninstaller).Select(i => i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!keys.Any(k => !k.StartsWith("process:", StringComparison.Ordinal))) return;
            Log(T("cleanupTitle") + " · " + string.Join(", ", plan.Vendors.Select(VendorLabel)));
            var outcome = await new DeepCleanEngine(Cleanup).RunAsync(plan, keys, NewCleanupSession(), CleanupBackupRoot(), Log, token, CleanupStage, CleanupProgress);
            lastCleanup = outcome;
            Log(string.Format(T("cleanupDone"), outcome.Done, outcome.Failed, Size(outcome.Freed) is { Length: > 0 } size ? size : "0 KB"));
        }
        // The driver change already succeeded; a leftover that needs review must not turn it into a failure.
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or JsonException) { Log(T("cleanupTitle") + " · " + e.Message); }
        finally { cleanupInventory = null; }
    }

    private async void UndoCleanup(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var sessions = Path.Combine(dataRoot, "Sessions");
        var last = Directory.Exists(sessions) ? Directory.GetDirectories(sessions).Where(d => DeepCleanEngine.ValidSession(Path.GetFileName(d)))
            .Where(d => { try { return File.ReadAllText(Path.Combine(d, "state.json")).Contains("\"complete\""); } catch (IOException) { return false; } })
            .OrderByDescending(Path.GetFileName).FirstOrDefault() : null;
        if (last is null) { Status.Text = T("cleanupNoUndo"); return; }
        if (MessageBox.Show(this, T("cleanupUndoConfirm"), "Swap", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await Guard(() => Operation(async token =>
        {
            var outcome = await new DeepCleanEngine(Cleanup).UndoAsync(last, CleanupBackupRoot(), Log, token);
            await AnalyzeCleanupAsync();
            Status.Text = string.Format(T("cleanupUndone"), outcome.Results.Count(r => r.Status == "restored"));
        }));
    }
}
