// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
namespace Swap.Core;

// KraicDev. The manufacturer's own control panel (NVIDIA App, AMD Software: Adrenalin Edition, Intel Graphics Software).
// A Windows Update driver works without it; Swap offers it as an optional step. This class only knows where each vendor
// publishes the official installer, how to read its version from that page, which hosts and signers are trusted, and how
// the installer is started. Downloading and running it live in Swap.Windows.
public sealed record SuiteOffer(Vendor Vendor, string Name, string Version, Uri Url, Uri? Referer);

public static partial class VendorSuite
{
    public static string Name(Vendor vendor) => vendor switch
    {
        Vendor.Nvidia => "NVIDIA App",
        Vendor.Amd => "AMD Software: Adrenalin Edition",
        Vendor.Intel => "Intel Graphics Software",
        _ => ""
    };

    // The official page that links the latest installer (and the page people are sent to when it cannot be downloaded).
    public static Uri Page(Vendor vendor) => new(vendor switch
    {
        Vendor.Nvidia => "https://www.nvidia.com/en-us/software/nvidia-app/",
        Vendor.Amd => "https://www.amd.com/en/support/download/drivers.html",
        _ => "https://www.intel.com/content/www/us/en/download/785597/intel-arc-graphics-windows.html"
    });

    // Intel's download site refuses scripted requests, so Swap opens its page instead of downloading.
    public static bool Downloadable(Vendor vendor) => vendor is Vendor.Nvidia or Vendor.Amd;

    // The company that must have signed the installer (Authenticode subject common name).
    public static string Publisher(Vendor vendor) => vendor switch
    {
        Vendor.Nvidia => "NVIDIA Corporation",
        Vendor.Amd => "Advanced Micro Devices",
        Vendor.Intel => "Intel Corporation",
        _ => "\0"
    };

    private static readonly string[] Hosts = ["us.download.nvidia.com", "international.download.nvidia.com", "drivers.amd.com"];
    // Only HTTPS downloads from the vendors' own download hosts, before and after any redirect.
    public static bool Trusted(Uri url) => url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && Hosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase);

    // The whole common name must be the vendor's ("NVIDIA Corporation", "Advanced Micro Devices, Inc."), not merely start with it.
    public static bool SignedBy(string subject, Vendor vendor) =>
        Regex.IsMatch(subject, @"(^|,\s*)CN=(""?)" + Regex.Escape(Publisher(vendor)) + @"(,?\s+Inc\.?)?\2\s*(,|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // NVIDIA's installer is silent with -s; AMD's web installer is opened as is, because its silent switches are not documented.
    public static IReadOnlyList<string> Arguments(Vendor vendor) => vendor == Vendor.Nvidia ? ["-s", "-noreboot"] : [];
    public static bool Silent(Vendor vendor) => Arguments(vendor).Count > 0;

    [GeneratedRegex(@"https://us\.download\.nvidia\.com/nvapp/client/(?<v>\d+(?:\.\d+){1,3})/NVIDIA_app_v\k<v>\.exe", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NvidiaLink();
    [GeneratedRegex(@"https://drivers\.amd\.com/drivers/installer/[\d.]+/whql/amd-software-adrenalin-edition-(?<v>\d+(?:\.\d+){1,3})-minimalsetup-\d+_web\.exe", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AmdLink();

    // Reads the newest installer link from the official page. Anything else on the page is ignored.
    public static SuiteOffer? Parse(Vendor vendor, string html)
    {
        var regex = vendor switch { Vendor.Nvidia => NvidiaLink(), Vendor.Amd => AmdLink(), _ => null };
        if (regex is null || string.IsNullOrEmpty(html)) return null;
        var best = regex.Matches(html).Select(m => (Url: m.Value, Version: m.Groups["v"].Value))
            .Where(m => Version.TryParse(m.Version.Count(c => c == '.') == 0 ? m.Version + ".0" : m.Version, out _))
            .OrderByDescending(m => Version.Parse(m.Version.Count(c => c == '.') == 0 ? m.Version + ".0" : m.Version)).FirstOrDefault();
        if (best.Url is null || !Trusted(new Uri(best.Url))) return null;
        return new(vendor, Name(vendor), best.Version, new Uri(best.Url), vendor == Vendor.Amd ? new Uri("https://www.amd.com/") : null);
    }
}
