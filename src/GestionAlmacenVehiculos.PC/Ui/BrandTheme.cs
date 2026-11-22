using System.Windows;
using System.Windows.Media;
using GestionAlmacenVehiculos.Shared.Branding;

namespace GestionAlmacenVehiculos.PC.Ui;

public static class BrandTheme
{
    public static void Apply(Application app, BrandProfile brand)
    {
        SetBrush(app, "AccentBrush", brand.Accent);
        SetBrush(app, "AccentDeepBrush", brand.AccentDeep);
        SetBrush(app, "ChartOccBrush", brand.Accent);
        SetColor(app, "Accent", brand.Accent);
        SetColor(app, "AccentDeep", brand.AccentDeep);
        SetColor(app, "ChartOcc", brand.Accent);
    }

    public static LinearGradientBrush SidebarGradient(BrandProfile brand)
    {
        var accent = ParseColor(brand.Accent, Color.FromRgb(0x0F, 0x76, 0x6E));
        var deep = ParseColor(brand.AccentDeep, Color.FromRgb(0x11, 0x5E, 0x59));
        var darker = Color.FromRgb(
            (byte)(deep.R * 0.55),
            (byte)(deep.G * 0.55),
            (byte)(deep.B * 0.55));
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            [
                new GradientStop(darker, 0),
                new GradientStop(accent, 0.55),
                new GradientStop(deep, 1),
            ],
        };
    }

    private static void SetBrush(Application app, string key, string hex)
    {
        if (app.Resources.Contains(key))
            app.Resources[key] = new SolidColorBrush(ParseColor(hex, Colors.Teal));
    }

    private static void SetColor(Application app, string key, string hex)
    {
        if (app.Resources.Contains(key))
            app.Resources[key] = ParseColor(hex, Colors.Teal);
    }

    public static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            return (Color)ColorConverter.ConvertFromString(hex)!;
        }
        catch
        {
            return fallback;
        }
    }
}
