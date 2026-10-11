using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
namespace Swap.App;

public sealed record Preferences(string Language = "auto", string Theme = "auto", int Source = 0)
{
    public static string DataRoot { get; } = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Data"))
        ? Path.Combine(AppContext.BaseDirectory, "Data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Swap");
    public static Preferences Load()
    {
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(DataRoot, "preferences.json"))) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DataRoot); var path = Path.Combine(DataRoot, "preferences.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this)); File.Move(path + ".tmp", path, true);
    }
    public bool Apply()
    {
        Localization.Apply(Language);
        bool light = Theme == "light" || Theme == "auto" && Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) is int value && value == 1;
        var keys = new[] { "WindowBg", "Side", "Card", "Subtle", "Stroke", "Ink", "Muted", "Accent", "AccentText", "Good", "Warning" };
        var colors = light ? new[] { "#F3F4F7", "#ECEEF3", "#FFFFFF", "#ECEFF6", "#D8DCE5", "#202431", "#596276", "#425BCD", "#FFFFFF", "#267B51", "#8A520D" }
            : new[] { "#14161B", "#101216", "#1C1F26", "#242833", "#333743", "#F1F2F5", "#A4ABBA", "#A5B3FF", "#101529", "#93DAB6", "#F4C88C" };
        if (SystemParameters.HighContrast)
        {
            var bg = SystemColors.WindowColor; var ink = SystemColors.WindowTextColor; var accent = SystemColors.HighlightColor;
            for (int i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush(i == 7 ? accent : i == 8 ? SystemColors.HighlightTextColor : i is 4 or 5 or 6 or 9 or 10 ? ink : bg);
        }
        else for (int i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        return !light;
    }
}
