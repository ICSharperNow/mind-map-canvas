using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MindMapCanvas;

public partial class ColorPickerWindow : Window
{
    double _h;      // 0..360
    double _s = 1;  // 0..1
    double _v = 1;  // 0..1
    bool _syncing;

    public Color SelectedColor { get; private set; }

    public ColorPickerWindow(Color initial)
    {
        InitializeComponent();
        RgbToHsv(initial, out _h, out _s, out _v);
        Loaded += (s, e) => Refresh();
    }

    void Refresh()
    {
        HueBase.Color = HsvToRgb(_h, 1, 1);
        SelectedColor = HsvToRgb(_h, _s, _v);
        PreviewSwatch.Background = new SolidColorBrush(SelectedColor);
        if (!_syncing)
        {
            _syncing = true;
            HexBox.Text = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
            _syncing = false;
        }
        Canvas.SetLeft(SvThumb, _s * SvBox.ActualWidth - 7);
        Canvas.SetTop(SvThumb, (1 - _v) * SvBox.ActualHeight - 7);
        Canvas.SetLeft(HueThumb, 0);
        Canvas.SetTop(HueThumb, _h / 360 * HueBox.ActualHeight - 3);
    }

    // --- Saturation/value area ---

    void Sv_Down(object sender, MouseButtonEventArgs e)
    {
        SvBox.CaptureMouse();
        SvApply(e.GetPosition(SvBox));
        e.Handled = true;
    }

    void Sv_Move(object sender, MouseEventArgs e)
    {
        if (SvBox.IsMouseCaptured) SvApply(e.GetPosition(SvBox));
    }

    void Sv_Up(object sender, MouseButtonEventArgs e) => SvBox.ReleaseMouseCapture();

    void SvApply(Point p)
    {
        _s = Math.Clamp(p.X / SvBox.ActualWidth, 0, 1);
        _v = 1 - Math.Clamp(p.Y / SvBox.ActualHeight, 0, 1);
        Refresh();
    }

    // --- Hue strip ---

    void Hue_Down(object sender, MouseButtonEventArgs e)
    {
        HueBox.CaptureMouse();
        HueApply(e.GetPosition(HueBox));
        e.Handled = true;
    }

    void Hue_Move(object sender, MouseEventArgs e)
    {
        if (HueBox.IsMouseCaptured) HueApply(e.GetPosition(HueBox));
    }

    void Hue_Up(object sender, MouseButtonEventArgs e) => HueBox.ReleaseMouseCapture();

    void HueApply(Point p)
    {
        _h = Math.Clamp(p.Y / HueBox.ActualHeight, 0, 1) * 360;
        Refresh();
    }

    // --- Hex box ---

    void Hex_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(HexBox.Text.Trim());
            _syncing = true;
            RgbToHsv(c, out _h, out _s, out _v);
            Refresh();
            _syncing = false;
        }
        catch
        {
            // Ignore partial/invalid hex while typing.
        }
    }

    // --- Window chrome ---

    void Window_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; }
        else if (e.Key == Key.Enter) { DialogResult = true; }
    }

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // --- Conversions ---

    static Color HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    static void RgbToHsv(Color col, out double h, out double s, out double v)
    {
        double r = col.R / 255.0, g = col.G / 255.0, b = col.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        s = max == 0 ? 0 : d / max;
        v = max;
    }
}
