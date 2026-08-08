using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MindMapCanvas;

public partial class TemplateWindow : Window
{
    public string SelectedKey { get; private set; }

    public TemplateWindow()
    {
        InitializeComponent();

        foreach (var (key, name, desc) in Templates.Catalog)
        {
            var k = key;
            var title = new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            var sub = new TextBlock { Text = desc, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.SubtleText");
            var text = new StackPanel { Margin = new Thickness(12, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(title);
            text.Children.Add(sub);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var preview = BuildPreview(Templates.Build(k));
            row.Children.Add(preview);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var btn = new Button
            {
                Content = row,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 3, 0, 3),
                Padding = new Thickness(8, 6, 8, 6)
            };
            btn.SetResourceReference(StyleProperty, "ToolBtn");
            btn.Click += (s, e) =>
            {
                SelectedKey = k;
                DialogResult = true;
            };
            List.Children.Add(btn);
        }
    }

    // Tiny schematic rendering of a template: zones and shapes as colored
    // blocks, connections as lines.
    static UIElement BuildPreview(DocumentModel doc)
    {
        const double W = 148, H = 92, pad = 6;
        var canvas = new Canvas { Width = W, Height = H, ClipToBounds = true };

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var n in doc.Nodes)
        {
            minX = Math.Min(minX, n.X); minY = Math.Min(minY, n.Y);
            maxX = Math.Max(maxX, n.X + n.W); maxY = Math.Max(maxY, n.Y + n.H);
        }
        double scale = Math.Min((W - pad * 2) / (maxX - minX), (H - pad * 2) / (maxY - minY));
        double ox = (W - (maxX - minX) * scale) / 2 - minX * scale;
        double oy = (H - (maxY - minY) * scale) / 2 - minY * scale;

        Color Parse(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Colors.Gray; }
        }

        var byId = doc.Nodes.ToDictionary(n => n.Id);
        foreach (var c in doc.Connections)
        {
            if (!byId.TryGetValue(c.From, out var a) || !byId.TryGetValue(c.To, out var b)) continue;
            canvas.Children.Add(new Line
            {
                X1 = (a.X + a.W / 2) * scale + ox, Y1 = (a.Y + a.H / 2) * scale + oy,
                X2 = (b.X + b.W / 2) * scale + ox, Y2 = (b.Y + b.H / 2) * scale + oy,
                Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0x88, 0x95, 0xA7)),
                StrokeThickness = 1
            });
        }

        foreach (var n in doc.Nodes.OrderBy(n => n.Kind == "Zone" ? 0 : 1))
        {
            var color = Parse(n.Color);
            Shape s = n.Shape is "Ellipse" or "Circle"
                ? new Ellipse()
                : new Rectangle { RadiusX = 2, RadiusY = 2 };
            s.Width = Math.Max(2, n.W * scale);
            s.Height = Math.Max(2, n.H * scale);
            if (color.A == 0)
            {
                // Transparent text nodes: show a faint outline so they register.
                s.Fill = Brushes.Transparent;
                s.Stroke = new SolidColorBrush(Color.FromArgb(0x50, 0x88, 0x95, 0xA7));
                s.StrokeThickness = 0.8;
            }
            else
            {
                s.Fill = new SolidColorBrush(color);
                s.Opacity = n.Kind == "Zone" ? Math.Max(0.18, n.Opacity) : 1;
            }
            Canvas.SetLeft(s, n.X * scale + ox);
            Canvas.SetTop(s, n.Y * scale + oy);
            canvas.Children.Add(s);
        }

        var frame = new Border
        {
            Child = canvas,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "Brush.PanelBorder");
        frame.SetResourceReference(Border.BackgroundProperty, "Brush.CanvasOuter");
        return frame;
    }

    void Window_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
