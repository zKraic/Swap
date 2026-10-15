// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using System.Windows;
using Swap.Core;
using Swap.Windows;
namespace Swap.App;

// KraicDev. Software and components around the driver (Diagnostics), and the optional install of the manufacturer's
// control panel: official page, vendor download host, valid vendor signature, then the vendor's own installer.
public partial class MainWindow
{
    private SoftwareFacts? softwareFacts;
    private bool demoSuiteInstalled;

    private async Task InspectSoftwareAsync()
    {
        if (snapshot is null || TargetAdapter is not { } card) { softwareFacts = null; PaintSoftware(); return; }
        softwareFacts = Demo ? new(demoSuiteInstalled, demoSuiteInstalled ? "26.8.1" : "", true, true, "14.44.35211", true, true, card.Vendor == Vendor.Nvidia, 2, false)
            : await SoftwareInspector.ReadAsync(card.Vendor, snapshot.PendingReboot);
        PaintSoftware();
    }
    private IReadOnlyList<HealthFinding> SoftwareFindings() =>
        softwareFacts is null || TargetAdapter is null ? [] : SoftwareReadiness.Evaluate(TargetAdapter.Vendor, softwareFacts);
    private void PaintSoftware()
    {
        var findings = SoftwareFindings();
        SoftwareText.Text = findings.Count == 0 ? T("unknown") : string.Join("\n", findings.Select(f => (f.Attention ? "⚠ " : "✓ ") + Describe(f)));
        var vendor = TargetAdapter?.Vendor ?? Vendor.Unknown;
        // Not offered for a card that is about to be replaced (a prepared change waits for the new one).
        bool offer = softwareFacts is { SuiteInstalled: false } && swapIntent is null && vendor is Vendor.Nvidia or Vendor.Amd or Vendor.Intel;
        SuiteButton.Content = offer ? string.Format(T(CanInstallSuite(vendor) ? "suiteInstall" : "suiteOpenPage"), VendorSuite.Name(vendor)) : "";
        SuiteButton.Visibility = offer ? Visibility.Visible : Visibility.Collapsed;
    }
    // The vendor installer also installs drivers: it needs the same conditions as a driver change (administrator,
    // full mode, no remote session). Otherwise, and for Intel, the official page opens instead.
    private bool CanInstallSuite(Vendor vendor) => VendorSuite.Downloadable(vendor) && !readOnly && snapshot is { Admin: true, Remote: false };
    private async void InstallSuite(object sender, RoutedEventArgs e)
    {
        if (busy || TargetAdapter is not { } card) return;
        await InstallVendorSoftwareAsync(card.Vendor);
    }

    // One click: find the official installer, confirm with the person, download with progress, check the signature, install, verify.
    private async Task InstallVendorSoftwareAsync(Vendor vendor)
    {
        var name = VendorSuite.Name(vendor);
        if (!CanInstallSuite(vendor)) { Open(VendorSuite.Page(vendor).ToString()); Status.Text = string.Format(T("suitePageOpened"), name); return; }
        SuiteOffer? offer = null;
        await Guard(async () =>
        {
            ShowProgress(null, string.Format(T("suiteSearching"), name));
            offer = Demo ? new(vendor, name, "26.8.1", new Uri("https://drivers.amd.com/demo.exe"), null) : await VendorSoftware.FindAsync(vendor);
        });
        if (offer is null) { Open(VendorSuite.Page(vendor).ToString()); Status.Text = string.Format(T("suiteUnavailable"), name); return; }
        var text = string.Format(T(VendorSuite.Silent(vendor) ? "suiteConfirmSilent" : "suiteConfirm"), name, offer.Version, offer.Url.Host);
        if (!IsAuditRun && MessageBox.Show(this, text, "Swap", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await Guard(() => Operation(async token =>
        {
            string? installer = null;
            var folder = Path.Combine(dataRoot, "Vendor");
            try
            {
                if (!Demo && Directory.Exists(folder))
                    foreach (var old in Directory.EnumerateFiles(folder))
                        try { File.Delete(old); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log(ex.Message); }
                var progress = new Progress<double>(p => ShowProgress(p * 0.8, string.Format(T("suiteDownloading"), name, offer.Version)));
                if (Demo) { for (int i = 1; i <= 5; i++) { ((IProgress<double>)progress).Report(i * 20); await Task.Delay(120, token); } }
                else installer = await VendorSoftware.DownloadAsync(offer, folder, progress, token);
                ShowProgress(82, string.Format(T("suiteVerifying"), VendorSuite.Publisher(vendor)));
                if (!Demo && !await VendorSoftware.VerifyAsync(installer!, vendor)) throw new InvalidOperationException(string.Format(T("suiteSignature"), VendorSuite.Publisher(vendor)));
                ShowProgress(null, string.Format(T(VendorSuite.Silent(vendor) ? "suiteRunningSilent" : "suiteRunning"), name));
                int code = 0;
                if (Demo) { await Task.Delay(300, token); demoSuiteInstalled = true; } else code = await VendorSoftware.RunAsync(installer!, vendor, token);
                Log(name + " " + offer.Version + " · exit " + code);
                await InspectSoftwareAsync();
                Status.Text = string.Format(T(softwareFacts is { SuiteInstalled: true } ? "suiteDone" : "suiteNotDetected"), name);
                await WriteInstallReportAsync(name, offer, code, softwareFacts is { SuiteInstalled: true });
            }
            finally
            {
                // The installer is not kept: it is large and the vendor's own updater takes over from here.
                // If the person cancelled while it still runs, the file is in use; the next install clears it first.
                if (installer is not null) try { File.Delete(installer); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log(ex.Message); }
            }
        }));
    }
    private async Task WriteInstallReportAsync(string name, SuiteOffer offer, int code, bool installed)
    {
        try
        {
            var text = $"# {T("installReportTitle")}\n\nSwap {ApplicationVersion} · {DateTime.Now:yyyy-MM-dd HH:mm}\n\n- {name} {offer.Version} · {offer.Url}\n- {T("installReportSignature")}: {VendorSuite.Publisher(offer.Vendor)}\n- {T("installReportResult")}: {(installed ? T("suiteInstalledShort") : T("suiteNotDetectedShort"))} (exit {code})\n";
            await File.WriteAllTextAsync(ReportPath("-install.md"), text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log(e.Message); }
    }
}
