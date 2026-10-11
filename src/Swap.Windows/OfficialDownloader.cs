using System.Net;
namespace Swap.Windows;

public static class OfficialDownloader
{
    public static bool Allowed(Uri uri) => uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo.Length == 0 &&
        (uri.Host.Equals("nvidia.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".nvidia.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("amd.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".amd.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("intel.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".intel.com", StringComparison.OrdinalIgnoreCase));
    public static async Task<string> DownloadAsync(string url, string folder, Action<long> progress)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !Allowed(uri)) throw new IOException("Solo se permiten enlaces HTTPS oficiales de AMD o NVIDIA, puerto 443.");
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, "download-" + Guid.NewGuid().ToString("N") + ".partial");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location ?? throw new IOException("Redirección sin destino.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (!Allowed(uri)) throw new IOException("La descarga redirige fuera de AMD/NVIDIA. Se bloqueó.");
                continue;
            }
            response.EnsureSuccessStatusCode();
            const long max = 4L * 1024 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > max) throw new IOException("El instalador supera 4 GB.");
            using var source = await response.Content.ReadAsStreamAsync(deadline.Token);
            using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920]; long total = 0; long next = 0; int count;
                while ((count = await source.ReadAsync(buffer, deadline.Token)) > 0)
                {
                    total += count; if (total > max) throw new IOException("La descarga supera 4 GB.");
                    await target.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
                    if (total >= next) { progress(total); next = total + 8 * 1024 * 1024; }
                }
                if (total == 0 || response.Content.Headers.ContentLength is long length && total != length) throw new IOException("Descarga incompleta.");
            }
            var exe = Path.ChangeExtension(temp, ".exe"); File.Move(temp, exe);
            return exe; // The caller must validate vendor signature before import/use.
        }
        throw new IOException("Demasiadas redirecciones.");
    }
}
