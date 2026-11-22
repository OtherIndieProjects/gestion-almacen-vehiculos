using System.Windows;
using System.Windows.Media;
using ScottPlot.WPF;

namespace GestionAlmacenVehiculos.PC.Ui;

public static class PlotTheme
{
    public static void Apply(WpfPlot plot)
    {
        var paper = Hex(BrushHex("PaperBrush", "#F3F0E8"));
        var ink = Hex(BrushHex("InkBrush", "#0E1A1F"));
        var muted = Hex(BrushHex("InkMutedBrush", "#5C6B73"));
        var line = Hex(BrushHex("LineBrush", "#D6CFC2"));

        plot.Plot.FigureBackground.Color = paper;
        plot.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#FFFDF8");
        plot.Plot.Axes.Color(ink);
        plot.Plot.Grid.MajorLineColor = line;
        plot.Plot.Axes.Bottom.Label.ForeColor = muted;
        plot.Plot.Axes.Left.Label.ForeColor = muted;
    }

    public static ScottPlot.Color Free => Hex(BrushHex("ChartFreeBrush", "#94A3B8"));
    public static ScottPlot.Color Occ => Hex(BrushHex("ChartOccBrush", "#0F766E"));
    public static ScottPlot.Color Accent => Hex(BrushHex("ChartAccentBrush", "#1D4ED8"));

    public static ScottPlot.Color FromHex(string? hex, ScottPlot.Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return ScottPlot.Color.FromHex(hex.StartsWith('#') ? hex : "#" + hex); }
        catch { return fallback; }
    }

    private static string BrushHex(string key, string fallback)
    {
        try
        {
            if (Application.Current?.TryFindResource(key) is SolidColorBrush b)
                return $"#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}";
        }
        catch { /* design-time */ }
        return fallback;
    }

    private static ScottPlot.Color Hex(string hex) => ScottPlot.Color.FromHex(hex);
}
