using System.Windows;
using System.Windows.Media;

namespace MindMapCanvas;

public class Theme
{
    public string Name { get; init; }
    public Color WindowBg, PanelBg, PanelBorder, Text, SubtleText,
                 CanvasOuter, CanvasBg, GridLine,
                 Hover, Pressed, Checked, CheckedBorder;
}

public static class ThemeManager
{
    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static readonly Theme Light = new()
    {
        Name = "Light",
        WindowBg = C("#F4F5F7"), PanelBg = C("#FFFFFF"), PanelBorder = C("#DDE1E6"),
        Text = C("#2D333A"), SubtleText = C("#6B7280"),
        CanvasOuter = C("#E9ECEF"), CanvasBg = C("#FBFCFD"), GridLine = C("#AEB9C4"),
        Hover = C("#EEF1F4"), Pressed = C("#E2E6EA"),
        Checked = C("#DBE4FF"), CheckedBorder = C("#4C6EF5")
    };

    public static readonly Theme Dark = new()
    {
        Name = "Dark",
        WindowBg = C("#1B1E24"), PanelBg = C("#23272E"), PanelBorder = C("#333A44"),
        Text = C("#E6EAF0"), SubtleText = C("#9AA4B2"),
        CanvasOuter = C("#14161B"), CanvasBg = C("#1E2229"), GridLine = C("#4C5666"),
        Hover = C("#2E343D"), Pressed = C("#383F4A"),
        Checked = C("#33415E"), CheckedBorder = C("#6E8BFF")
    };

    public static readonly Theme Slate = new()
    {
        Name = "Slate",
        WindowBg = C("#232B36"), PanelBg = C("#2B3442"), PanelBorder = C("#3C4756"),
        Text = C("#E2E8F0"), SubtleText = C("#94A3B8"),
        CanvasOuter = C("#1D242E"), CanvasBg = C("#26303C"), GridLine = C("#51637B"),
        Hover = C("#35404F"), Pressed = C("#3E4B5C"),
        Checked = C("#3B4E6B"), CheckedBorder = C("#7FA3E0")
    };

    public static readonly Theme Sepia = new()
    {
        Name = "Sepia",
        WindowBg = C("#F3EBDD"), PanelBg = C("#FAF4E8"), PanelBorder = C("#E0D5C0"),
        Text = C("#4A3F30"), SubtleText = C("#8A7B64"),
        CanvasOuter = C("#EBE0CC"), CanvasBg = C("#F8F2E4"), GridLine = C("#C4B08E"),
        Hover = C("#EFE6D4"), Pressed = C("#E5D9C2"),
        Checked = C("#EAD9B8"), CheckedBorder = C("#B08D4F")
    };

    public static IReadOnlyList<Theme> Themes { get; } = new[] { Light, Dark, Slate, Sepia };

    public static Theme Current { get; private set; } = Light;

    public static Theme ByName(string name) =>
        Themes.FirstOrDefault(t => t.Name == name) ?? Light;

    public static void Apply(Theme t)
    {
        Current = t;
        var r = Application.Current.Resources;
        r["Brush.WindowBg"] = B(t.WindowBg);
        r["Brush.PanelBg"] = B(t.PanelBg);
        r["Brush.PanelBorder"] = B(t.PanelBorder);
        r["Brush.Text"] = B(t.Text);
        r["Brush.SubtleText"] = B(t.SubtleText);
        r["Brush.CanvasOuter"] = B(t.CanvasOuter);
        r["Brush.Hover"] = B(t.Hover);
        r["Brush.Pressed"] = B(t.Pressed);
        r["Brush.Checked"] = B(t.Checked);
        r["Brush.CheckedBorder"] = B(t.CheckedBorder);
        r["Brush.Grid"] = MakeGridBrush(t.CanvasBg, t.GridLine);
    }

    static SolidColorBrush B(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    static DrawingBrush MakeGridBrush(Color bg, Color line)
    {
        // Minor lines every 24px at low opacity, a stronger major line every 96px —
        // more structure than a flat grid, but softer overall.
        var minor = Color.FromArgb(0x42, line.R, line.G, line.B);
        var major = Color.FromArgb(0x7E, line.R, line.G, line.B);

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(bg), null, new RectangleGeometry(new Rect(0, 0, 96, 96))));
        group.Children.Add(new GeometryDrawing(
            null, new Pen(new SolidColorBrush(minor), 1),
            Geometry.Parse("M24,0 L24,96 M48,0 L48,96 M72,0 L72,96 M0,24 L96,24 M0,48 L96,48 M0,72 L96,72")));
        group.Children.Add(new GeometryDrawing(
            null, new Pen(new SolidColorBrush(major), 1),
            Geometry.Parse("M0,0 L0,96 M0,0 L96,0")));

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 96, 96),
            ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }
}
