// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.RegularExpressions;
namespace Swap.Core;

// KraicDev. Files referenced by a service, task, startup or uninstall command. Text only: nothing is executed.
public static partial class CommandFiles
{
    [GeneratedRegex(@"[A-Za-z]:\\[^""<>|*?\r\n]*?\.(?:exe|dll|sys|cmd|bat)(?=$|[\s"",])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Absolute();

    public static IReadOnlyList<string> Parse(string command, Func<string, string>? expand = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return [];
        var text = (expand ?? Environment.ExpandEnvironmentVariables)(command.Trim());
        if (text.StartsWith(@"\??\", StringComparison.Ordinal)) text = text[4..];
        if (text.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), text[12..]);
        else if (text.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase) || text.StartsWith(@"SysWOW64\", StringComparison.OrdinalIgnoreCase))
            text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), text);
        var files = Absolute().Matches(text).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // rundll32/msiexec only host the real module: judge the module, not the Windows host.
        if (files.Count > 1 && Host(files[0])) files.RemoveAt(0);
        return files;
    }
    private static bool Host(string path) => Path.GetFileName(path).ToLowerInvariant() is "rundll32.exe" or "msiexec.exe" or "cmd.exe" or "svchost.exe";
}
