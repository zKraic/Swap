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
                preferences = new(language, theme); dark = preferences.Apply(); PopulatePreferences(); PaintSnapshot(); SetFrame(); Status.Text = T("ready");
                WindowState = WindowState.Normal;
                Width = width; Height = width == 880 ? 640 : 810;
                foreach (var page in new[] { HomePage, LibraryPage, RecoveryPage, DiagnosticsPage, SettingsPage })
                {
                    foreach (var candidate in new[] { HomePage, LibraryPage, RecoveryPage, DiagnosticsPage, SettingsPage }) candidate.Visibility = candidate == page ? Visibility.Visible : Visibility.Collapsed;
                    var name = page.Name.Replace("Page", "");
                    (name switch { "Home" => HomeNav, "Library" => LibraryNav, "Recovery" => RecoveryNav, "Diagnostics" => DiagnosticsNav, _ => SettingsNav }).IsChecked = true;
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
            foreach (var page in new[] { HomePage, LibraryPage, RecoveryPage, DiagnosticsPage, SettingsPage }) page.Visibility = page == HomePage ? Visibility.Visible : Visibility.Collapsed;
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
            platform = new DemoPlatform { Target = Vendor.Intel }; await AnalyzeAsync();
            var backupPath = await new GraphicsBackup(platform).RunAsync(snapshot!, NewSession(), Log);
            foreach (var directory in Directory.GetDirectories(Path.Combine(backupPath, "backup"))) await BackupIntegrity.VerifyAsync(directory);
            RefreshFiles(); RecoveryNav.IsChecked = true; UpdateLayout();
            if (BackupList.Items.Count == 0 || CreateBackupButton.Content?.ToString() != T("backupCreate")) throw new InvalidOperationException("Independent backup UI failed.");
            results.Add(new { scenario="independent-backup-demo",passed=true });
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
