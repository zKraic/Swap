// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
namespace Swap.Core;

// KraicDev. Validation report of a GPU change, meant to be attached to a public issue. Observations from Windows and the
// graphics runtimes only: no machine identifier, account name, serial number or private path.
public sealed record ApiSummary(string D3D12, string Vulkan, string OpenCl);
public sealed record SwapValidation(int Schema, string App, string Version, DateTimeOffset Generated, bool Simulated, string Os,
    string FromVendor, string FromName, string FromDriver, string ToVendor, string ToName, string ToDriver, bool ToSigned, int ToProblem,
    bool DriverVerified, int CleanupRemoved, int CleanupReview, long CleanupFreedBytes, int LeftoversAfter,
    IReadOnlyList<HealthFinding> Health, IReadOnlyList<HealthFinding> GamesBefore, IReadOnlyList<HealthFinding> GamesAfter, ApiSummary Apis,
    IReadOnlyList<string> Displays, IReadOnlyList<HealthFinding> Changes, string Scope)
{
    public bool Attention => !DriverVerified || ToProblem != 0 || CleanupReview > 0 || LeftoversAfter > 0 || Health.Any(f => f.Attention) || GamesAfter.Any(f => f.Attention);
}

public static class SwapReport
{
    public const string Scope = "Windows PnP, driver signature, Direct3D 12, Vulkan, OpenCL and display observations after the change. No FPS, stability, power or cabling certification.";

    public static string VendorName(Vendor vendor) => vendor switch { Vendor.Nvidia => "NVIDIA", Vendor.Amd => "AMD", Vendor.Intel => "Intel", _ => "Unknown" };

    public static ApiSummary Summarize(ApiReport? report, Adapter? target)
    {
        if (report is null) return new("not checked", "not checked", "not checked");
        if (!report.Completed) return new("check failed", "check failed", "check failed");
        int vendor = target is null ? 0 : GameReadiness.PciVendor(target.Vendor);
        bool Mine(ApiDevice d) => vendor == 0 || d.VendorId == vendor;
        string List(IEnumerable<ApiDevice> devices, Func<ApiDevice, string> text) { var items = devices.Select(text).ToArray(); return items.Length == 0 ? "none" : string.Join("; ", items); }
        return new(List(report.D3D12.Where(Mine), d => d.Name + " · FL " + d.Version),
            report.Vulkan == "ok" ? List(report.VulkanDevices.Where(Mine), d => d.Name + " · Vulkan " + d.Version) : report.Vulkan,
            report.OpenCl == "ok" ? List(report.OpenClPlatforms, p => p.Name + " · " + p.Version + " · " + p.DeviceId + " GPU") : report.OpenCl);
    }

    // Removes the account and computer names wherever an observation could carry them.
    public static string Sanitize(string text)
    {
        foreach (var (value, placeholder) in new[] { (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
            (Environment.UserName, "<user>"), (Environment.MachineName, "<computer>") })
            if (value.Length >= 3) text = text.Replace(value, placeholder, StringComparison.OrdinalIgnoreCase);
        return text;
    }

    public static string Markdown(SwapValidation v, Func<string, string> describe)
    {
        string Line(HealthFinding f) => "- " + (f.Attention ? "⚠ " : "") + describe(f.Code) + (f.Detail.Length == 0 ? "" : " · " + f.Detail);
        var text = new StringBuilder();
        text.AppendLine("### Swap validation report").AppendLine();
        text.AppendLine(FormattableString.Invariant($"Swap {v.Version} · {v.Generated:yyyy-MM-dd} · {v.Os}"));
        if (v.Simulated) text.AppendLine().AppendLine("**Simulated demo: no physical GPU was changed.**");
        text.AppendLine().AppendLine("| | Before | After |").AppendLine("| --- | --- | --- |");
        text.AppendLine($"| GPU | {v.FromVendor} · {Cell(v.FromName)} | {v.ToVendor} · {Cell(v.ToName)} |");
        text.AppendLine($"| Driver | {Cell(v.FromDriver)} | {Cell(v.ToDriver)} · {(v.ToSigned ? "signed" : "unsigned")} · {(v.ToProblem == 0 ? "no PnP error" : "PnP problem " + v.ToProblem)} |");
        text.AppendLine().AppendLine($"**Driver change:** {(v.DriverVerified ? "verified" : "needs review")}");
        text.AppendLine($"**Cleanup of the previous brand:** {v.CleanupRemoved} removed · {v.CleanupReview} to review · {Size(v.CleanupFreedBytes)} freed · {v.LeftoversAfter} leftovers found afterwards");
        text.AppendLine().AppendLine("**Games and graphics APIs (after)**");
        text.AppendLine("- Direct3D 12: " + Cell(v.Apis.D3D12)).AppendLine("- Vulkan: " + Cell(v.Apis.Vulkan)).AppendLine("- OpenCL: " + Cell(v.Apis.OpenCl));
        foreach (var finding in v.GamesAfter) text.AppendLine(Line(finding));
        if (v.GamesBefore.Count > 0) { text.AppendLine().AppendLine("<details><summary>Before the change</summary>").AppendLine(); foreach (var finding in v.GamesBefore) text.AppendLine(Line(finding)); text.AppendLine("</details>"); }
        text.AppendLine().AppendLine("**Windows checks**"); foreach (var finding in v.Health) text.AppendLine(Line(finding));
        if (v.Displays.Count > 0) { text.AppendLine().AppendLine("**Displays**"); foreach (var display in v.Displays) text.AppendLine("- " + display); }
        if (v.Changes.Count > 0) { text.AppendLine().AppendLine("**Changes since the preparation**"); foreach (var finding in v.Changes) text.AppendLine(Line(finding)); }
        text.AppendLine().AppendLine("_" + v.Scope + " Generated by Swap (KraicDev); review before posting._");
        return Sanitize(text.ToString());
    }
    private static string Cell(string value) => value.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
    // English report: invariant number format whatever the Windows region.
    public static string Size(long bytes) => bytes >= 1L << 30 ? FormattableString.Invariant($"{bytes / (double)(1L << 30):0.0} GB") : bytes >= 1L << 20 ? FormattableString.Invariant($"{bytes / (double)(1L << 20):0} MB") : FormattableString.Invariant($"{Math.Max(0, bytes / 1024)} KB");
}
