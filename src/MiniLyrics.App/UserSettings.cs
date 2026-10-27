using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using MiniLyrics.Core;

namespace MiniLyrics.App;

public sealed class UserSettings
{
    public double Left { get; set; } = 80;
    public double Top { get; set; } = 80;
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 220;
    public double FontSize { get; set; } = 20;
    public double Opacity { get; set; } = 0.96;
    public bool AlwaysOnTop { get; set; } = true;
    public bool TextOnly { get; set; } = true;
    public bool TextOnlyDefaultApplied { get; set; }
    public bool IgnoreMouseClicks { get; set; } = true;
    public LyricTextMode TextMode { get; set; } = LyricTextMode.Latin;
    public RomanizationLanguage RomanizationLanguage { get; set; }
    public LyricScrollMode ScrollMode { get; set; } = LyricScrollMode.Continuous;
    public double CorrectionSeconds { get; set; }
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniLyricsForSpotify");
    private static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public static UserSettings Load(string? path = null)
    {
        UserSettings settings;
        try { settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path ?? SettingsPath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { settings = new(); }
        // Upgrade older settings once: the requested default is lyrics without a panel.
        // Subsequent explicit choices in the tray menu are still remembered.
        if (!settings.TextOnlyDefaultApplied)
        {
            settings.TextOnly = true;
            settings.TextOnlyDefaultApplied = true;
        }
        settings.Width = Bound(settings.Width, 360, 1200, 420);
        settings.Height = Bound(settings.Height, 180, 1000, 220);
        settings.FontSize = Bound(settings.FontSize, 14, 36, 20);
        settings.Opacity = Bound(settings.Opacity, 0.35, 1, 0.96);
        settings.CorrectionSeconds = Bound(settings.CorrectionSeconds, -10, 10, 0);
        if (!Enum.IsDefined(settings.TextMode)) settings.TextMode = LyricTextMode.Latin;
        if (!Enum.IsDefined(settings.RomanizationLanguage)) settings.RomanizationLanguage = RomanizationLanguage.Automatic;
        if (!Enum.IsDefined(settings.ScrollMode)) settings.ScrollMode = LyricScrollMode.Continuous;
        settings.Left = Bound(settings.Left, SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenLeft + Math.Max(0, SystemParameters.VirtualScreenWidth - settings.Width), 80);
        settings.Top = Bound(settings.Top, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenTop + Math.Max(0, SystemParameters.VirtualScreenHeight - settings.Height), 80);
        return settings;
    }

    private static double Bound(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public bool Save(string? path = null)
    {
        TextOnlyDefaultApplied = true;
        try
        {
            var destination = path ?? SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(destination + ".tmp", destination, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
