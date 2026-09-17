using System.Windows;
using System.Windows.Media;

namespace ModMenagerie.Desktop;

internal static class Theme
{
    public static string[] Accents { get; } = ["Forest", "Ocean", "Teal", "Violet", "Rose", "Amber"];
    public static string Accent { get; private set; } = "Forest";

    public static void SetAccent(string? accent)
    {
        Accent = Accents.Contains(accent) ? accent! : "Forest";
        Apply(IsDark);
    }

    public static bool IsDark { get; private set; }

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var app = System.Windows.Application.Current;
        // WPF's built-in Fluent theme covers controls, popups and child windows.
#pragma warning disable WPF0001 // Opt into the framework's theme selection API.
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        var (sidebar, strong, soft) = Accent switch
        {
            "Ocean" => ("#183149", "#287ABB", "#DFEDF9"),
            "Teal" => ("#163B3E", "#168582", "#DAF0ED"),
            "Violet" => ("#342747", "#9060CF", "#EEE4FA"),
            "Rose" => ("#462837", "#BD527C", "#F8E1EB"),
            "Amber" => ("#42341C", "#B47D19", "#F7EDD5"),
            _ => ("#18382F", "#287551", "#D9ECDF")
        };
        foreach (var (key, night, day) in new[]
        {
            ("Canvas", "#171D21", "#F3F5F7"),
            ("Surface", "#222B30", "#FFFFFF"),
            ("Edge", "#3D4B51", "#D9E1DC"),
            ("Ink", "#E6EEEA", "#233B36"),
            ("Muted", "#B3C5BC", "#52685F"),
            ("AccentSurface", sidebar, soft),
            ("AccentSidebar", sidebar, sidebar),
            ("AccentStrong", strong, strong),
            ("StableInk", "#85D9AA", "#17663C"),
            ("BetaInk", "#EAD17C", "#806000"),
            ("AlphaInk", "#FFB980", "#A24D00"),
            ("BlockedInk", "#FF9FA7", "#B32D38")
        })
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? night : day));
            brush.Freeze();
            app.Resources[key] = brush;
        }
    }
}
