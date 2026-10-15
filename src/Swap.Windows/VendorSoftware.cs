// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Net;
using Swap.Core;
namespace Swap.Windows;

// KraicDev. Optional install of the manufacturer's control panel, on request only:
// 1. read the newest installer link from the vendor's official page,
// 2. download it over HTTPS from the vendor's own download host (checked again after redirects),
// 3. refuse it unless Windows reports a valid Authenticode signature from that vendor,
// 4. run it (silently where the vendor supports it) and check that Windows now lists it.
public static class VendorSoftware
{
    private const long MaximumBytes = 2L * 1024 * 1024 * 1024;
    private static readonly HttpClient Http = CreateClient();
    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, MaxAutomaticRedirections = 3 }) { Timeout = TimeSpan.FromMinutes(30) };
        // Vendor sites answer browsers; this is the same request a browser on this PC would make.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36 Swap/1.1");
        return client;
    }

    public static async Task<SuiteOffer?> FindAsync(Vendor vendor, CancellationToken token = default)
    {
        if (!VendorSuite.Downloadable(vendor)) return null;
        // The page is small: a slow or unreachable site falls back to opening it in the browser instead of waiting.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(45));
        using var page = new HttpRequestMessage(HttpMethod.Get, VendorSuite.Page(vendor));
        using var response = await Http.SendAsync(page, HttpCompletionOption.ResponseContentRead, limit.Token);
        if (!response.IsSuccessStatusCode) return null;
        return VendorSuite.Parse(vendor, await response.Content.ReadAsStringAsync(limit.Token));
    }

    public static async Task<string> DownloadAsync(SuiteOffer offer, string folder, IProgress<double>? progress, CancellationToken token = default)
    {
        if (!VendorSuite.Trusted(offer.Url)) throw new IOException("Untrusted download host.");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, Path.GetFileName(offer.Url.AbsolutePath));
        using var request = new HttpRequestMessage(HttpMethod.Get, offer.Url);
        if (offer.Referer is not null) request.Headers.Referrer = offer.Referer;
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is not { } final || !VendorSuite.Trusted(final)) throw new IOException("The download was redirected outside the vendor.");
        var total = response.Content.Headers.ContentLength ?? -1;
        if (total > MaximumBytes) throw new IOException("The installer is larger than expected.");
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var file = new FileStream(target + ".part", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[1 << 16]; long done = 0; int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), token); done += read;
                    if (done > MaximumBytes) throw new IOException("The installer is larger than expected.");
                    if (total > 0) progress?.Report(100.0 * done / total);
                }
                // A connection that drops early must not leave a truncated installer that looks complete.
                if (total > 0 && done != total) throw new IOException("The download was incomplete.");
            }
            File.Move(target + ".part", target, true);
            return target;
        }
        catch
        {
            // Cancelled, cut off or refused: nothing half-downloaded stays behind.
            try { File.Delete(target + ".part"); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    // Only an installer that Windows considers validly signed by the card's manufacturer is ever started.
    public static async Task<bool> VerifyAsync(string path, Vendor vendor)
    {
        var (valid, subject) = await WindowsPlatform.PublisherAsync(path);
        return valid && VendorSuite.SignedBy(subject, vendor);
    }

    // Waits for the installer, however long the vendor takes; nothing is killed. Returns its exit code.
    public static async Task<int> RunAsync(string path, Vendor vendor, CancellationToken token = default)
    {
        var info = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
        foreach (var argument in VendorSuite.Arguments(vendor)) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("The installer did not start.");
        await process.WaitForExitAsync(token);
        return process.ExitCode;
    }
}
