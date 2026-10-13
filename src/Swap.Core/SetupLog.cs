// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
namespace Swap.Core;

// KraicDev. Windows records every file a driver installation places outside the Driver Store (setupapi.dev.log): current
// builds hard-link System32 files to the store ("Hardlinking"), older ones copy them ("Copying"). Those lines keep naming the
// Driver Store folder after the package is gone, which is the evidence for files a removal left in System32.
public static partial class SetupLog
{
    [GeneratedRegex(@"(?:Copying|Hardlinking)\s+'(?<source>[^']+)'\s+to\s+'(?<target>[^']+)'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Copy();
    private const string Store = @"\DriverStore\FileRepository\";

    // Target path and the Driver Store folder it came from. Copies from anywhere else are ignored.
    public static IEnumerable<(string Target, string Source)> Copies(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (line.Length > 4096 || line.IndexOf("Copying", StringComparison.OrdinalIgnoreCase) < 0 && line.IndexOf("Hardlinking", StringComparison.OrdinalIgnoreCase) < 0) continue;
            var match = Copy().Match(line); if (!match.Success) continue;
            var source = match.Groups["source"].Value; var target = match.Groups["target"].Value;
            var index = source.IndexOf(Store, StringComparison.OrdinalIgnoreCase); if (index < 0) continue;
            var folder = source[(index + Store.Length)..].Split('\\')[0];
            if (folder.Length == 0 || folder.Contains("..") || target.Contains("..") || !Path.IsPathRooted(target)) continue;
            yield return (target, folder);
        }
    }
}
