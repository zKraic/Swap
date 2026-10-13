// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
namespace Swap.Core;

// KraicDev. Minimal INF reader: identity data only ([Version] and model sections). Never executes or trusts directives.
// SystemFiles: files the package copies outside its Driver Store folder, into System32 or SysWOW64 (relative to the Windows folder).
// ComponentIds: software components the package creates with AddComponent (hardware IDs "SWC\<id>" of their own packages).
public sealed record InfSummary(string Class, string Provider, IReadOnlyList<string> HardwareIds, IReadOnlyList<string>? SystemFiles = null,
    IReadOnlyList<string>? ComponentIds = null);

public static class InfReader
{
    public static InfSummary Read(string text)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string>? current = null; var pending = "";
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = pending + StripComment(raw).Trim(); pending = "";
            if (line.EndsWith('\\')) { pending = line[..^1]; continue; }
            if (line.Length == 0) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                if (!sections.TryGetValue(name, out current)) sections[name] = current = [];
                continue;
            }
            current?.Add(line);
        }
        var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections.Where(s => s.Key.Equals("Strings", StringComparison.OrdinalIgnoreCase) || s.Key.StartsWith("Strings.", StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Key.Length))
            foreach (var entry in section.Value.Select(Split).Where(e => e.Key.Length > 0)) strings.TryAdd(entry.Key, Unquote(entry.Value));
        string Resolve(string value)
        {
            value = Unquote(value.Trim());
            return value.Length > 2 && value.StartsWith('%') && value.EndsWith('%') && strings.TryGetValue(value[1..^1], out var resolved) ? resolved : value;
        }
        string Version(string key) => sections.TryGetValue("Version", out var version)
            ? version.Select(Split).Where(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Select(e => Resolve(e.Value)).FirstOrDefault() ?? "" : "";
        var ids = new List<string>();
        if (sections.TryGetValue("Manufacturer", out var manufacturers))
            foreach (var entry in manufacturers)
            {
                var value = entry.Contains('=') ? Split(entry).Value : entry;
                var parts = Tokens(value);
                if (parts.Count == 0 || parts[0].Length == 0) continue;
                var names = new List<string> { parts[0] };
                names.AddRange(parts.Skip(1).Where(p => p.Length > 0).Select(p => parts[0] + "." + p));
                foreach (var name in names)
                    if (sections.TryGetValue(name, out var models))
                        foreach (var model in models.Where(m => m.Contains('=')))
                            ids.AddRange(Tokens(Split(model).Value).Skip(1).Select(Resolve).Where(id => id.Length > 0));
            }
        // Only sections explicitly directed to System32/SysWOW64 count; DefaultDestDir is not assumed.
        var system = new List<string>();
        if (sections.TryGetValue("DestinationDirs", out var destinations))
            foreach (var (key, value) in destinations.Select(Split).Where(e => e.Key.Length > 0 && !e.Key.Equals("DefaultDestDir", StringComparison.OrdinalIgnoreCase)))
            {
                var parts = Tokens(value);
                var root = parts.Count == 0 ? "" : parts[0] switch
                {
                    "11" => "System32",
                    "16425" => "SysWOW64",
                    "10" when parts.Count > 1 && parts[1].Equals("System32", StringComparison.OrdinalIgnoreCase) => "System32",
                    "10" when parts.Count > 1 && parts[1].Equals("SysWOW64", StringComparison.OrdinalIgnoreCase) => "SysWOW64",
                    _ => ""
                };
                if (root.Length == 0 || !sections.TryGetValue(key, out var files)) continue;
                var sub = parts.Count > 1 && parts[0] == "11" ? parts[1] : "";
                if (sub.Contains("..") || sub.Contains(':') || sub.StartsWith('\\')) continue;
                foreach (var line in files.Where(l => !l.Contains('=')))
                {
                    var name = Resolve(Tokens(line)[0]);
                    if (name.Length == 0 || name.IndexOfAny(['\\', '/', ':', '%', '*', '?']) >= 0 || name.Contains("..")) continue;
                    system.Add(sub.Length == 0 ? root + "\\" + name : root + "\\" + sub + "\\" + name);
                }
            }
        // AddComponent = name, flags, install-section; the install section lists ComponentIDs.
        var components = new List<string>();
        foreach (var line in sections.Values.SelectMany(s => s).Select(Split).Where(e => e.Key.Equals("AddComponent", StringComparison.OrdinalIgnoreCase)))
        {
            var parts = Tokens(line.Value);
            if (parts.Count < 3 || !sections.TryGetValue(parts[2], out var install)) continue;
            foreach (var entry in install.Select(Split).Where(e => e.Key.Equals("ComponentIDs", StringComparison.OrdinalIgnoreCase)))
                components.AddRange(Tokens(entry.Value).Select(Resolve).Where(id => id.Length > 0 && id.IndexOfAny(['\\', '/']) < 0).Select(id => @"SWC\" + id));
        }
        return new(Version("Class"), Version("Provider"), ids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), system.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            components.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
    private static string StripComment(string line)
    {
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ';' && !quoted) return line[..i];
        }
        return line;
    }
    private static (string Key, string Value) Split(string line)
    {
        var index = line.IndexOf('=');
        return index < 0 ? ("", line) : (Unquote(line[..index].Trim()), line[(index + 1)..].Trim());
    }
    private static List<string> Tokens(string value)
    {
        var result = new List<string>(); bool quoted = false; int start = 0;
        for (int i = 0; i <= value.Length; i++)
        {
            if (i < value.Length && value[i] == '"') quoted = !quoted;
            if (i == value.Length || value[i] == ',' && !quoted) { result.Add(Unquote(value[start..i].Trim())); start = i + 1; }
        }
        return result;
    }
    private static string Unquote(string value) => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
}
