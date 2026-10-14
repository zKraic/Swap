using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Swap.Core;
using Swap.Windows;
namespace Swap.App;

public partial class MainWindow
{
    // Test-only entry point: DemoPlatform, never WindowsPlatform. Render actual WPF layouts.
    private async Task UiAuditAsync(string output)
    {
        var results = new List<object>();
        try
        {
            if (!Demo) throw new InvalidOperationException("UI audit requires demo.");
            output = Path.GetFullPath(output); Directory.CreateDirectory(output);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            var area=SystemParameters.WorkArea;
            var windowFits=ActualWidth <= area.Width + 16 && ActualHeight <= area.Height + 16;
            File.WriteAllText(Path.Combine(output,"window-fit.json"),JsonSerializer.Serialize(new { passed=windowFits, width=ActualWidth, height=ActualHeight, availableWidth=area.Width, availableHeight=area.Height, state=WindowState.ToString() }));
            if (!windowFits) throw new InvalidOperationException("Initial window exceeds the available Windows work area.");
            Capture(Path.Combine(output,"window-initial.png"));
            foreach (var row in Localization.Translations) if (row.Value.Length != 6 || row.Value.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("Incomplete translation: " + row.Key);
            foreach (var language in Localization.Languages)
            foreach (var theme in new[] { "dark", "light" })
            foreach (var width in new[] { 1120, 880 })
            {
                preferences = new(language, theme); dark = preferences.Apply(); PopulatePreferences(); PaintSnapshot(); RelabelCleanup(); SetFrame(); Status.Text = T("ready");
                WindowState = WindowState.Normal;
                Width = width; Height = width == 880 ? 640 : 810;
                foreach (var page in new[] { HomePage, CleanupPage, LibraryPage, RecoveryPage, DiagnosticsPage, SettingsPage })
                {
                    foreach (var candidate in new[] { HomePage, CleanupPage, LibraryPage, RecoveryPage, DiagnosticsPage, SettingsPage }) candidate.Visibility = candidate == page ? Visibility.Visible : Visibility.Collapsed;
                    var name = page.Name.Replace("Page", "");
                    (name switch { "Home" => HomeNav, "Cleanup" => CleanupNav, "Library" => LibraryNav, "Recovery" => RecoveryNav, "Diagnostics" => DiagnosticsNav, _ => SettingsNav }).IsChecked = true;
                    PageTitle.Text = T(name == "Home" ? "home" : name.ToLowerInvariant());
                    UpdateLayout(); PageScroll.ScrollToTop(); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    // Check wrapping and parent bounds inside the scrolled content, independently of viewport height.
                    var issues = new List<string>();
                    InspectLayout(page, issues);
                    if (issues.Count > 0) { Capture(Path.Combine(output, "layout-failure.png")); throw new InvalidOperationException(language + "/" + theme + "/" + width + "/" + name + ": " + string.Join(", ", issues)); }
                    if (language is "es" or "en" && (width == 1120 || language == "es")) Capture(Path.Combine(output, $"{language}-{theme}-{width}-{name}.png"));
                    if(width==1120 && name is "Home" or "Settings" && PageScroll.ScrollableHeight>2) {
                        Capture(Path.Combine(output,"scroll-failure.png"));
                        throw new InvalidOperationException($"Unnecessary scroll: {language}/{theme}/{name}: {PageScroll.ScrollableHeight}");
                    }
                    results.Add(new { language, theme, width, actualWidth=ActualWidth, actualHeight=ActualHeight, page = name, scroll=PageScroll.ScrollableHeight, passed = true });
                }
            }
            preferences = new("es", "dark"); dark = preferences.Apply(); PopulatePreferences();
            Width = 1120; Height = 810; HomeNav.IsChecked = true;
            foreach (var page in new[] { HomePage, CleanupPage, LibraryPage, RecoveryPage, DiagnosticsPage, SettingsPage }) page.Visibility = page == HomePage ? Visibility.Visible : Visibility.Collapsed;
            PaintSnapshot(); CleanupPreview.IsExpanded = true; UpdateLayout();
            var cleanupIssues = new List<string>(); InspectLayout(HomePage, cleanupIssues);
            if(cleanupIssues.Count>0 || !CleanupText.Text.Contains("oem")) throw new InvalidOperationException("Cleanup preview layout or inventory failed.");
            Capture(Path.Combine(output,"cleanup-preview.png")); CleanupPreview.IsExpanded = false;
            results.Add(new { scenario="cleanup-preview-demo",passed=true });
            populating=true; GpuBox.SelectedItem=null; populating=false;
            platform = new DemoPlatform { ExtraAdapters = [new("Other basic GPU",@"PCI\VEN_8086&DEV_1234\EXTRA",Vendor.Intel,"display.inf","1",0,true)] };
            await Guard(AnalyzeAsync);
            if(TargetAdapter is not null || RunButton.IsEnabled || GpuName.Text!=T("selectGpu")) throw new InvalidOperationException("Ambiguous GPUs were automatically selected.");
            GpuBox.SelectedItem=snapshot!.Adapters[0]; UpdateButtons();
            if(!RunButton.IsEnabled || !snapshot.Adapters[1].SelectionLabel.Contains("DEV_1234")) throw new InvalidOperationException("Explicit GPU selection or basic-adapter identification failed.");
            results.Add(new { scenario="explicit-ambiguous-gpu-selection-demo",passed=true });
            foreach (var vendor in new[] { Vendor.Amd, Vendor.Nvidia, Vendor.Intel })
            {
                platform = new DemoPlatform { Target = vendor }; await Guard(() => AutomaticAsync(false));
                if (snapshot is null || !snapshot.Adapters.Any(a => a.Vendor == vendor && Planner.IsOem(a.Inf))) throw new InvalidOperationException("Demo workflow failed: " + vendor);
                results.Add(new { scenario = "full-demo-" + vendor, passed = true });
            }
            UpdateLayout(); Capture(Path.Combine(output, "demo-completed.png"));
            await SaveCheckpointCoreAsync();PaintGraphics();
            if(checkpoint is null || !File.Exists(CheckpointPath) || !ComparisonText.Text.Contains(T("checkpointUnchanged")))throw new InvalidOperationException("Checkpoint roundtrip failed.");
            results.Add(new {scenario="checkpoint-roundtrip-demo",passed=true});
            DiagnosticsNav.IsChecked=true;DisplayDisclosure.IsExpanded=EventDisclosure.IsExpanded=CheckpointDisclosure.IsExpanded=true;
            UpdateLayout();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
            var expandedIssues=new List<string>();InspectLayout(DiagnosticsPage,expandedIssues);
            if(expandedIssues.Count>0)throw new InvalidOperationException("Expanded health layout: "+string.Join(", ",expandedIssues));
            Capture(Path.Combine(output,"health-expanded.png"));
            results.Add(new {scenario="health-disclosures-demo",passed=true});
            // Deep cleanup on simulated leftovers of a removed NVIDIA card: preview, run, verify and undo.
            platform = new DemoPlatform(); cleanupPlatform = null; await AnalyzeAsync();
            CleanupNav.IsChecked = true; await AnalyzeCleanupAsync();
            // Opening the page also starts its own analysis; the buttons must be usable once it ends.
            while (busy) await Task.Delay(50);
            UpdateLayout();
            if (cleanupRows.Count < 15 || !cleanupRows.Any(r => r.Item.Kind == RemnantKind.Device) || cleanupPlan?.Kept.Any(k => k.Reason == "physxKept") != true || !CleanupRunButton.IsEnabled)
                throw new InvalidOperationException("Deep cleanup preview failed: " + cleanupRows.Count);
            var cleanupIssues2 = new List<string>(); InspectLayout(CleanupPage, cleanupIssues2);
            if (cleanupIssues2.Count > 0) throw new InvalidOperationException("Deep cleanup layout: " + string.Join(", ", cleanupIssues2));
            Capture(Path.Combine(output, "deep-cleanup-preview.png"));
            progressCapture = Path.Combine(output, "cleanup-progress.png"); Progress.Visibility = Visibility.Visible;
            var cleaned = await RunCleanupCoreAsync(cleanupPlan!, cleanupRows.Where(r => r.Selected).Select(r => r.Item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase), CancellationToken.None);
            if (cleaned.Failed != 0 || cleaned.Done < 15 || cleanupReports < cleaned.Done) throw new InvalidOperationException($"Deep cleanup demo failed: done={cleaned.Done} failed={cleaned.Failed} progress={cleanupReports}");
            await AnalyzeCleanupAsync(); UpdateLayout();
            if (cleanupRows.Count != 0 || CleanupEmpty.Visibility != Visibility.Visible) throw new InvalidOperationException("Deep cleanup left items: " + cleanupRows.Count);
            Capture(Path.Combine(output, "deep-cleanup-done.png"));
            var undone = await new DeepCleanEngine(Cleanup).UndoAsync(cleaned.Session, CleanupBackupRoot(), Log);
            if (undone.Results.Count(r => r.Status == "restored") < 8 || undone.Results.Any(r => r.Status == "failed")) throw new InvalidOperationException("Deep cleanup undo failed.");
            results.Add(new { scenario = "deep-cleanup-run-and-undo-demo", passed = true, cleaned.Done, restored = undone.Results.Count(r => r.Status == "restored") });
            // Releasing the backups of earlier cleanups: offered with its size, everything goes, and undo no longer looks for them.
            await PaintBackupsAsync();
            if (FreeBackupsButton.Visibility != Visibility.Visible || backupBytes <= 0) throw new InvalidOperationException("Backup release not offered.");
            var released = await DiscardBackupsCoreAsync();
            if (released.Locked != 0 || released.Sessions < 1 || FreeBackupsButton.Visibility == Visibility.Visible ||
                Directory.EnumerateDirectories(CleanupBackupRoot()).Any(d => DeepCleanEngine.ValidSession(Path.GetFileName(d))) ||
                File.ReadAllText(Path.Combine(cleaned.Session, "state.json")).Contains("\"complete\"")) throw new InvalidOperationException($"Backup release failed: {released}");
            results.Add(new { scenario = "free-cleanup-backups-demo", passed = true, released.Sessions, released.Bytes });
            DiagnosticsNav.IsChecked = true; await InspectGraphicsAsync(); UpdateLayout();
            if (RefreshPanel.Children.Count != 1) throw new InvalidOperationException("Refresh-rate offer missing.");
            ApplyRefresh(RefreshPanel.Children[0], new RoutedEventArgs()); await Task.Delay(100);
            if (RefreshConfirm.Visibility != Visibility.Visible) throw new InvalidOperationException("Refresh confirmation missing.");
            Capture(Path.Combine(output, "refresh-confirm.png"));
            await FinishRefreshAsync(false);
            if (demoHz != 60 || RefreshConfirm.Visibility == Visibility.Visible) throw new InvalidOperationException("Refresh revert failed.");
            demoHz = null; await InspectGraphicsAsync();
            results.Add(new { scenario = "refresh-apply-and-revert-demo", passed = true });            platform = new DemoPlatform { Target = Vendor.Intel }; await AnalyzeAsync();
            var backupPath = await new GraphicsBackup(platform).RunAsync(snapshot!, NewSession(), Log);
            foreach (var directory in Directory.GetDirectories(Path.Combine(backupPath, "backup"))) await BackupIntegrity.VerifyAsync(directory);
            RefreshFiles(); RecoveryNav.IsChecked = true; UpdateLayout();
            if (BackupList.Items.Count == 0 || CreateBackupButton.Content?.ToString() != T("backupCreate")) throw new InvalidOperationException("Independent backup UI failed.");
            results.Add(new { scenario="independent-backup-demo",passed=true });
            using (var report = JsonDocument.Parse(await File.ReadAllTextAsync(await ExportReportAsync())))
            {
                var exported=report.RootElement;
                var expectedVersion=System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
                if(exported.GetProperty("version").GetString()!=expectedVersion || !VersionLabel.Text.Contains(expectedVersion) || exported.GetProperty("author").GetString()!="KraicDev" || !exported.GetProperty("simulated").GetBoolean() || exported.GetProperty("adapters").GetArrayLength()!=snapshot!.Adapters.Count || exported.GetProperty("preparation").GetProperty("DriverHealthy").GetBoolean() || exported.GetProperty("preparation").GetProperty("Ready").GetBoolean())
                    throw new InvalidOperationException("Exported diagnostic report metadata or inventory failed.");
            }
            results.Add(new { scenario="diagnostic-report-export-demo",passed=true });
            // Graphics APIs as games see them: missing on the basic adapter, available once the driver is installed.
            platform = new DemoPlatform { Target = Vendor.Amd }; await AnalyzeAsync();
            if (!GamesText.Text.Contains(T("d3d12Missing")) || !GpuHealthSummary.Text.Contains(T("gamesAttention"))) throw new InvalidOperationException("Basic adapter shown as ready for games.");
            await Guard(() => AutomaticAsync(false));
            DiagnosticsNav.IsChecked = true; GamesDisclosure.IsExpanded = true; UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (!GamesText.Text.Contains(T("d3d12Ok")) || !GamesText.Text.Contains(T("vulkanOk")) || !GpuHealthSummary.Text.Contains(T("gamesReady"))) throw new InvalidOperationException("Game readiness missing: " + GamesText.Text);
            var gameIssues = new List<string>(); InspectLayout(DiagnosticsPage, gameIssues);
            if (gameIssues.Count > 0) throw new InvalidOperationException("Games layout: " + string.Join(", ", gameIssues));
            Capture(Path.Combine(output, "games-apis.png")); GamesDisclosure.IsExpanded = false;
            results.Add(new { scenario = "games-apis-demo", passed = true });
            // Guided card change: prepared on an NVIDIA card, opened again with an AMD card, installed, cleaned up and reported.
            if (File.Exists(IntentFile)) File.Delete(IntentFile);
            platform = new DemoPlatform { Target = Vendor.Nvidia }; cleanupPlatform = null; await AnalyzeAsync(); HomeNav.IsChecked = true;
            await Operation(PrepareSwapCoreAsync);
            if (swapIntent is not { Phase: "prepared", FromVendor: Vendor.Nvidia } || SwapBanner.Visibility != Visibility.Visible || PrepareButton.IsEnabled || RunButton.IsEnabled || DownloadButton.IsEnabled || !File.Exists(IntentFile))
                throw new InvalidOperationException("Card change preparation failed.");
            UpdateLayout(); Capture(Path.Combine(output, "swap-prepared.png"));
            // The first install fails as if Windows were busy after sign-in; the card change retries on its own.
            platform = new DemoPlatform { Target = Vendor.Amd, FailedInstalls = 1 }; await AnalyzeAsync();
            swapIntent = LoadIntent(); await ResumeSwapAsync();
            if (swapState != "swapped" || TargetAdapter?.Vendor != Vendor.Amd || swapIntent?.Starts != 1) throw new InvalidOperationException("New card not detected: " + swapState);
            UpdateLayout(); Capture(Path.Combine(output, "swap-detected.png"));
            await StartSwapInstallAsync();
            if (swapState != "done" || swapIntent is not null || File.Exists(IntentFile) || lastValidationPath is null || !File.Exists(lastValidationPath) || lastValidation is not { DriverVerified: true, CleanupRemoved: > 0, LeftoversAfter: 0 } ||
                swapAttempts != 2 || ((DemoPlatform)platform).FailedInstalls != 0 || !RunButton.IsEnabled || SwapExtraButton.Visibility != Visibility.Visible)
                throw new InvalidOperationException($"Card change did not finish: {swapState} {lastValidation}");
            var markdown = await File.ReadAllTextAsync(lastValidationPath);
            if (!markdown.Contains("### Swap validation report") || !markdown.Contains("Simulated demo") || !markdown.Contains("Direct3D 12 available") ||
                Environment.UserName.Length >= 3 && markdown.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Validation report content failed.");
            foreach (var language in new[] { "es", "de" })
            {
                preferences = new(language, "dark"); dark = preferences.Apply(); PopulatePreferences(); PaintSnapshot(); RelabelSwap();
                Width = 880; Height = 640; UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                var swapIssues = new List<string>(); InspectLayout(HomePage, swapIssues);
                if (swapIssues.Count > 0) throw new InvalidOperationException("Card change banner layout " + language + ": " + string.Join(", ", swapIssues));
            }
            Capture(Path.Combine(output, "swap-done.png")); Width = 1120; Height = 810;
            results.Add(new { scenario = "guided-card-change-demo", passed = true, lastValidation.CleanupRemoved, report = Path.GetFileName(lastValidationPath) });
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed = results.Count, failed = 0, scope = "Actual WPF rendering + simulated workflow. No physical driver operations.", results }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(0);
        }
        catch (Exception e)
        {
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed = results.Count, failed = 1, error = e.ToString(), results })); Application.Current.Shutdown(1);
        }
    }
    private static void InspectLayout(DependencyObject root, List<string> issues)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Visibility: Visibility.Visible } element && element.ActualWidth > 0)
            {
                if (element is TextBlock text && text.DesiredSize.Width > text.ActualWidth + text.Margin.Left + text.Margin.Right + 2) issues.Add("text width: " + text.Text);
                if (element is TextBlock exposed && (exposed.Text.Contains("Choice {") || exposed.Text.Contains("FileEntry {"))) issues.Add("implementation data exposed");
                if (element is Button button && button.DesiredSize.Width > button.ActualWidth + button.Margin.Left + button.Margin.Right + 2) issues.Add("button width: " + button.Content);
                if (element is not ScrollViewer) InspectLayout(child, issues);
            }
        }
    }
    private void Capture(string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(WindowRoot.ActualWidth), (int)Math.Ceiling(WindowRoot.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(WindowRoot); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
