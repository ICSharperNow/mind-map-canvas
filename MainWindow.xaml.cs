using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace MindMapCanvas;

// ---------- Persisted model ----------

public class NodeModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 168;
    public double H { get; set; } = 96;
    public string Text { get; set; } = "";
    public string Color { get; set; } = "#FFF9B1";
    public string Shape { get; set; } = "Rect";
    public double FontSize { get; set; } = 14;
    public string TextColor { get; set; } = "#2D333A";
    public string Align { get; set; } = "Center";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public double Rotation { get; set; }
    public int Z { get; set; }
    public double Opacity { get; set; } = 1.0;
    public string Font { get; set; } = "Segoe UI";
    public string Kind { get; set; } = "Shape";   // Shape | Image | Link | Zone | Text
    public string ImageData { get; set; }          // base64 image for Image/Link previews
    public string ImageFit { get; set; } = "Fit";  // Fit | Fill | Stretch | Center
    public string Url { get; set; }

    public NodeModel Clone(bool keepId = false) => new()
    {
        Id = keepId ? Id : Guid.NewGuid(),
        X = X, Y = Y, W = W, H = H,
        Text = Text, Color = Color, Shape = Shape,
        FontSize = FontSize, TextColor = TextColor, Align = Align,
        Bold = Bold, Italic = Italic,
        Rotation = Rotation, Kind = Kind, Opacity = Opacity, Font = Font,
        ImageData = ImageData, ImageFit = ImageFit, Url = Url
    };
}

public class ConnectionModel
{
    public Guid From { get; set; }
    public Guid To { get; set; }
    // Anchor names (Side enum values). Null means auto: aim at the other node's center.
    public string FromAnchor { get; set; }
    public string ToAnchor { get; set; }
    // Null means the theme's default connector color.
    public string Color { get; set; }
}

public class CellModel
{
    public int X { get; set; }
    public int Y { get; set; }
    public string Color { get; set; }
    public double Opacity { get; set; } = 0.5;
}

public readonly record struct CellData(string Hex, double Op);

public class DocumentModel
{
    public int Version { get; set; } = 1;
    public List<NodeModel> Nodes { get; set; } = new();
    public List<ConnectionModel> Connections { get; set; } = new();
    public List<CellModel> Cells { get; set; } = new();
}

// ---------- Runtime visuals ----------

public class NodeVisual
{
    public NodeModel Model;
    public Grid Root;
    public Grid Content;
    public Shape ShapeEl;
    public Image ImageEl;
    public RotateTransform Rot;
    public FrameworkElement RotHandle;
    public TextBlock Label;
    public TextBox Editor;
    public Thumb Grip;
    public List<Ellipse> Handles = new();
    // Shared inverse-zoom scale so handles stay clickable at any zoom level.
    public ScaleTransform HandleScaleT = new(1, 1);
}

public class ConnectionVisual
{
    public ConnectionModel Model;
    public Line Body;
    public Line Hit;
    public Polygon Arrow;
}

enum Side { Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

public partial class MainWindow : Window
{
    const double GridSize = 24.0;
    const double WorldSize = 40000.0;
    const double MinZoom = 0.1, MaxZoom = 4.0;
    const double NodeMinW = 80, NodeMinH = 48;

    static readonly string[] Palette =
    {
        "#FFF9B1", "#FFE066", "#FFCF7D", "#FFAB76",
        "#F5A9A9", "#F8A5C2", "#E1A8E8", "#D7B8F3",
        "#B3C7F7", "#A8D8F0", "#9FE8E0", "#C5E8A5",
        "#A5D6A7", "#E6DFD3", "#E4E7EB", "#FFFFFF"
    };

    static readonly (string Kind, string Icon, string Name)[] ShapeDefs =
    {
        ("Rect", "▭", "Rectangle"),
        ("Pill", "▬", "Pill"),
        ("Ellipse", "⬭", "Ellipse"),
        ("Diamond", "◇", "Diamond"),
        ("Hexagon", "⬡", "Hexagon"),
        ("Parallelogram", "▱", "Parallelogram"),
        ("Trapezoid", "⏢", "Trapezoid"),
        ("Triangle", "△", "Triangle"),
        ("Octagon", "◉", "Octagon"),
    };

    static readonly string[] TextPalette =
    {
        "#2D333A", "#FFFFFF", "#6B7280", "#C0392B",
        "#D97706", "#1D4ED8", "#047857", "#7C3AED"
    };

    static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(0x4C, 0x6E, 0xF5));
    static readonly SolidColorBrush SoftBorderBrush = new(Color.FromArgb(0x30, 0x00, 0x00, 0x00));
    static readonly SolidColorBrush ConnBrush = new(Color.FromRgb(0x88, 0x95, 0xA7));
    static readonly SolidColorBrush TextBrush = new(Color.FromRgb(0x2D, 0x33, 0x3A));
    static readonly SolidColorBrush PlaceholderBrush = new(Color.FromRgb(0x9A, 0xA2, 0xAC));

    readonly Dictionary<Guid, NodeVisual> _nodes = new();
    readonly List<ConnectionVisual> _conns = new();
    readonly HashSet<Guid> _selected = new();
    ConnectionVisual _selectedConn;

    bool _spaceDown, _panning, _draggingNodes, _rubberBanding, _movedDuringDrag, _drawingNew;
    Point _panMouseStart, _dragStartWorld, _rubberStart, _drawStart, _lastWorldMouse;
    double _panXStart, _panYStart;
    readonly Dictionary<Guid, Point> _dragOrigins = new();
    Rectangle _rubberRect, _drawRect;
    readonly Dictionary<string, Border> _shapeTiles = new();

    NodeVisual _editing;

    bool _linking;
    NodeVisual _linkSource;
    Side _linkSourceSide;
    NodeVisual _linkHover;
    Line _linkPreview;
    Ellipse _anchorRing;

    AppSettings _settings = new();
    Point _canvasMenuPos;

    // Painted grid cells from older board files (legacy, render-only).
    readonly Dictionary<(int, int), CellData> _cellColors = new();
    readonly Dictionary<(int, int), Rectangle> _cellRects = new();
    bool _areaPainting;
    Point _areaStart;
    Rectangle _areaRect;
    bool _syncingOpacity;
    string _lastConnColor;

    readonly List<NodeModel> _clipboardNodes = new();
    readonly List<ConnectionModel> _clipboardConns = new();

    string _currentFile;
    bool _dirty;
    double _zoom = 1.0;
    int _zTop = 10;
    int _zBottom;
    string _lastColor = "#FFF9B1";
    string _lastShape = "Rect";

    public MainWindow()
    {
        InitializeComponent();
    }

    // ---------- Startup / document lifecycle ----------

    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        SnapCheck.IsChecked = _settings.SnapToGrid;
        _lastConnColor = _settings.LastConnColor;
        if (_settings.RememberLastStyle)
        {
            if (!string.IsNullOrEmpty(_settings.LastColor)) _lastColor = _settings.LastColor;
            if (ShapeDefs.Any(d => d.Kind == _settings.LastShape)) _lastShape = _settings.LastShape;
            ShapeIcon.Text = ShapeDefs.First(d => d.Kind == _lastShape).Icon;
        }

        foreach (var hex in Palette)
        {
            var color = hex;
            var sw = new Border
            {
                Width = 22, Height = 22,
                CornerRadius = new CornerRadius(6),
                Background = BrushFrom(color),
                BorderBrush = SoftBorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2),
                Cursor = Cursors.Hand,
                ToolTip = color
            };
            sw.MouseLeftButtonDown += (s, a) =>
            {
                ApplyColor(color);
                ColorPopup.IsOpen = false;
                a.Handled = true;
            };
            PaletteWrap.Children.Add(sw);
        }
        var noneSw = new Border
        {
            Width = 22, Height = 22,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.White,
            BorderBrush = SoftBorderBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(2),
            Cursor = Cursors.Hand,
            ToolTip = "Transparent (no fill)",
            Child = new Line
            {
                X1 = 3, Y1 = 17, X2 = 17, Y2 = 3,
                Stroke = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)),
                StrokeThickness = 2
            }
        };
        noneSw.MouseLeftButtonDown += (s, a) =>
        {
            ApplyColor("#00FFFFFF");
            ColorPopup.IsOpen = false;
            a.Handled = true;
        };
        PaletteWrap.Children.Add(noneSw);
        CurrentColorSwatch.Background = BrushFrom(_lastColor);

        foreach (var f in new[] { "Segoe UI", "Arial", "Georgia", "Times New Roman", "Consolas", "Comic Sans MS", "Impact", "Calibri" })
        {
            var fam = f;
            var fb = new Button
            {
                Content = fam == "Times New Roman" ? "Times" : fam == "Comic Sans MS" ? "Comic Sans" : fam,
                FontFamily = new FontFamily(fam),
                Width = 108,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            fb.SetResourceReference(StyleProperty, "ToolBtn");
            fb.Click += (s, a) => ApplyTextFormat(mm => mm.Font = fam);
            fb.MouseEnter += (s, a) =>
            {
                var ff = new FontFamily(fam);
                foreach (var id in _selected)
                    if (_nodes.TryGetValue(id, out var nv))
                    {
                        nv.Label.FontFamily = ff;
                        nv.Editor.FontFamily = ff;
                    }
            };
            fb.MouseLeave += (s, a) =>
            {
                foreach (var id in _selected)
                    if (_nodes.TryGetValue(id, out var nv))
                        ApplyTextStyle(nv);
            };
            FontWrap.Children.Add(fb);
        }

        foreach (var def in ShapeDefs)
        {
            var kind = def.Kind;

            var preview = MakeShapeElement(kind);
            preview.Effect = null;
            preview.Width = 46;
            preview.Height = 30;
            preview.Fill = new SolidColorBrush(Color.FromRgb(0xB9, 0xC4, 0xD8));
            preview.Stroke = new SolidColorBrush(Color.FromRgb(0x7A, 0x86, 0x99));
            preview.StrokeThickness = 1.2;
            if (preview is Rectangle rr) { rr.RadiusX = 5; rr.RadiusY = 5; }

            var name = new TextBlock
            {
                Text = def.Name, FontSize = 11,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");

            var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            inner.Children.Add(preview);
            inner.Children.Add(name);

            var tile = new Border
            {
                Width = 104,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 9, 6, 8),
                Margin = new Thickness(3),
                BorderThickness = new Thickness(1.5),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = inner
            };
            tile.MouseEnter += (s, a) => { if (_lastShape != kind) tile.SetResourceReference(BackgroundProperty, "Brush.Hover"); };
            tile.MouseLeave += (s, a) => { if (_lastShape != kind) tile.Background = Brushes.Transparent; };
            tile.MouseLeftButtonDown += (s, a) => { ChooseShape(kind); a.Handled = true; };
            _shapeTiles[kind] = tile;
            ShapeWrap.Children.Add(tile);
        }
        RefreshShapeTiles();

        foreach (var hex in TextPalette)
        {
            var color = hex;
            var sw = new Border
            {
                Width = 22, Height = 22,
                CornerRadius = new CornerRadius(6),
                Background = BrushFrom(color),
                BorderBrush = SoftBorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(3),
                Cursor = Cursors.Hand,
                ToolTip = color
            };
            sw.MouseLeftButtonDown += (s, a) =>
            {
                ApplyTextFormat(m => m.TextColor = color);
                a.Handled = true;
            };
            sw.MouseEnter += (s, a) =>
            {
                var b = BrushFrom(color);
                PreviewFormat(nv =>
                {
                    if (!string.IsNullOrWhiteSpace(nv.Model.Text)) nv.Label.Foreground = b;
                    nv.Editor.Foreground = b;
                });
            };
            sw.MouseLeave += Format_PreviewEnd;
            TextColorWrap.Children.Add(sw);
        }

        BuildRecentSwatches();

        // Right-click menu for empty canvas: paste and quick actions.
        var canvasMenu = new ContextMenu();
        var miPaste = new MenuItem { Header = "Paste", InputGestureText = "Ctrl+V" };
        miPaste.Click += (s, a) => PasteAt(_canvasMenuPos);
        var miAddHere = new MenuItem { Header = "Add shape here" };
        foreach (var def in ShapeDefs)
        {
            var kind = def.Kind;
            var prev = MakeShapeElement(kind);
            prev.Effect = null;
            prev.Width = 26;
            prev.Height = 17;
            prev.Fill = new SolidColorBrush(Color.FromRgb(0xB9, 0xC4, 0xD8));
            prev.Stroke = new SolidColorBrush(Color.FromRgb(0x7A, 0x86, 0x99));
            prev.StrokeThickness = 1;
            if (prev is Rectangle prr) { prr.RadiusX = 3; prr.RadiusY = 3; }
            var hdr = new StackPanel { Orientation = Orientation.Horizontal };
            hdr.Children.Add(prev);
            var nm = new TextBlock { Text = def.Name, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            nm.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            hdr.Children.Add(nm);
            var sub = new MenuItem { Header = hdr };
            sub.Click += (s, a) => CreateNoteAt(_canvasMenuPos, kind);
            miAddHere.Items.Add(sub);
        }
        var miTextHere = new MenuItem { Header = "Add text box here" };
        miTextHere.Click += (s, a) => CreateTextAt(_canvasMenuPos);
        var miSelAll = new MenuItem { Header = "Select all", InputGestureText = "Ctrl+A" };
        miSelAll.Click += (s, a) => SelectAllNodes();
        var miFit = new MenuItem { Header = "Zoom to fit" };
        miFit.Click += (s, a) => ZoomToFit();
        canvasMenu.Items.Add(miPaste);
        canvasMenu.Items.Add(miAddHere);
        canvasMenu.Items.Add(miTextHere);
        canvasMenu.Items.Add(new Separator());
        canvasMenu.Items.Add(miSelAll);
        canvasMenu.Items.Add(miFit);
        World.ContextMenu = canvasMenu;
        World.ContextMenuOpening += (s, a) =>
        {
            _canvasMenuPos = Mouse.GetPosition(World);
            miPaste.IsEnabled = _clipboardNodes.Count > 0;
        };

        ResetView();

        var startupFile = ((App)Application.Current).StartupFile;
        if (startupFile != null)
        {
            LoadFile(startupFile);
        }
        else
        {
            var m = new NodeModel
            {
                X = Snap(WorldSize / 2 - 84),
                Y = Snap(WorldSize / 2 - 48),
                Text = "Double-click the canvas to add your first idea"
            };
            CreateNodeVisual(m);
            _dirty = false;
            UpdateTitle();
        }
        Focus();
    }

    void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!ConfirmDiscard()) e.Cancel = true;
    }

    void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        ClearDocument();
        _currentFile = null;
        _dirty = false;
        ResetView();
        UpdateTitle();
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog
        {
            Filter = "MindMap board (*.mindmap)|*.mindmap|JSON (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        LoadFile(dlg.FileName);
    }

    void Save_Click(object sender, RoutedEventArgs e) => Save();

    void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();

    void Exit_Click(object sender, RoutedEventArgs e) => Close();

    void Duplicate_Click(object sender, RoutedEventArgs e) => DuplicateSelected();

    void SelectAll_Click(object sender, RoutedEventArgs e) => SelectAllNodes();

    void Settings_Click(object sender, RoutedEventArgs e) =>
        new SettingsWindow(_settings, () => SnapCheck.IsChecked = _settings.SnapToGrid) { Owner = this }.ShowDialog();

    bool Save()
    {
        CommitEdit();
        if (string.IsNullOrEmpty(_currentFile)) return SaveAs();
        return WriteFile(_currentFile);
    }

    bool SaveAs()
    {
        CommitEdit();
        var dlg = new SaveFileDialog
        {
            Filter = "MindMap board (*.mindmap)|*.mindmap|JSON (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".mindmap",
            FileName = string.IsNullOrEmpty(_currentFile) ? "untitled.mindmap" : IOPath.GetFileName(_currentFile)
        };
        if (dlg.ShowDialog(this) != true) return false;
        if (!WriteFile(dlg.FileName)) return false;
        _currentFile = dlg.FileName;
        UpdateTitle();
        return true;
    }

    bool WriteFile(string path)
    {
        try
        {
            foreach (var nv in _nodes.Values)
                nv.Model.Z = Panel.GetZIndex(nv.Root);
            var doc = new DocumentModel
            {
                Nodes = _nodes.Values.Select(n => n.Model).ToList(),
                Connections = _conns.Select(c => c.Model).ToList(),
                Cells = _cellColors.Select(kv => new CellModel
                {
                    X = kv.Key.Item1, Y = kv.Key.Item2, Color = kv.Value.Hex, Opacity = kv.Value.Op
                }).ToList()
            };
            File.WriteAllText(path, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            _dirty = false;
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            ModernDialog.Show(this, "Could not save", ex.Message, "OK");
            return false;
        }
    }

    void LoadFile(string path)
    {
        DocumentModel doc;
        try
        {
            doc = JsonSerializer.Deserialize<DocumentModel>(File.ReadAllText(path));
            if (doc == null) throw new InvalidDataException("File is empty or not a mind map.");
        }
        catch (Exception ex)
        {
            ModernDialog.Show(this, "Could not open", ex.Message, "OK");
            return;
        }

        ClearDocument();
        foreach (var n in doc.Nodes) CreateNodeVisual(n);
        foreach (var c in doc.Connections) AddConnection(c.From, c.To, c.FromAnchor, c.ToAnchor, c.Color);
        if (doc.Cells != null)
            foreach (var cell in doc.Cells)
                SetCell((cell.X, cell.Y), new CellData(cell.Color, cell.Opacity <= 0 ? 0.5 : cell.Opacity));
        _currentFile = path;
        _dirty = false;
        UpdateTitle();
        ZoomToFit();
    }

    bool ConfirmDiscard()
    {
        CommitEdit();
        if (!_dirty) return true;
        var r = ModernDialog.Show(this, "Unsaved changes",
            "Save changes to the current mind map before continuing?",
            "Save", "Don't Save", "Cancel");
        return r switch
        {
            ModernDialog.Outcome.Primary => Save(),
            ModernDialog.Outcome.Secondary => true,
            _ => false
        };
    }

    void ClearDocument()
    {
        CancelLink();
        _editing = null;
        _selected.Clear();
        _selectedConn = null;
        _nodes.Clear();
        _conns.Clear();
        World.Children.Clear();
        _rubberRect = null;
        _rubberBanding = false;
        _drawRect = null;
        _drawingNew = false;
        _draggingNodes = false;
        _cellColors.Clear();
        _cellRects.Clear();
        _areaPainting = false;
        _areaRect = null;
        _guideV = null;
        _guideH = null;
    }

    void MarkDirty()
    {
        if (!_dirty) { _dirty = true; UpdateTitle(); }
    }

    void UpdateTitle()
    {
        var name = string.IsNullOrEmpty(_currentFile) ? "untitled" : IOPath.GetFileName(_currentFile);
        Title = $"MindMap Canvas - {name}{(_dirty ? " *" : "")}";
    }

    // ---------- Shape & node creation ----------

    static double Snap(double v) => Math.Round(v / GridSize) * GridSize;

    static SolidColorBrush BrushFrom(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return new SolidColorBrush(Colors.LightYellow); }
    }

    static ControlTemplate CreateGripTemplate()
    {
        // Circular badge with a diagonal double-arrow, matching the rotate handle.
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, "⤡");
        text.SetValue(TextBlock.FontSizeProperty, 13.0);
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        text.SetValue(TextBlock.ForegroundProperty, AccentBrush);
        text.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.MarginProperty, new Thickness(0, -1, 0, 0));

        var badge = new FrameworkElementFactory(typeof(Border));
        badge.SetValue(Border.BackgroundProperty, Brushes.White);
        badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        badge.SetValue(Border.BorderBrushProperty, AccentBrush);
        badge.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        badge.AppendChild(text);
        return new ControlTemplate(typeof(Thumb)) { VisualTree = badge };
    }

    static readonly DropShadowEffect NodeShadow = MakeFrozen(new DropShadowEffect
    {
        BlurRadius = 10, ShadowDepth = 2, Direction = 270, Opacity = 0.18
    });

    static readonly DropShadowEffect SelectedGlow = MakeFrozen(new DropShadowEffect
    {
        BlurRadius = 16, ShadowDepth = 0, Color = Color.FromRgb(0x4C, 0x6E, 0xF5), Opacity = 0.9
    });

    static DropShadowEffect MakeFrozen(DropShadowEffect e)
    {
        e.Freeze();
        return e;
    }

    double HandleScaleFactor() => Math.Clamp(1.0 / _zoom, 0.6, 3.0);

    void UpdateHandleScale()
    {
        double k = HandleScaleFactor();
        foreach (var nv in _nodes.Values)
        {
            nv.HandleScaleT.ScaleX = nv.HandleScaleT.ScaleY = k;
            UpdateHandlePositions(nv);
        }
        double hit = Math.Max(10, 14 * k);
        foreach (var cv in _conns)
            cv.Hit.StrokeThickness = hit;
    }

    static Shape MakeShapeElement(string kind)
    {
        Shape s = kind switch
        {
            "Ellipse" => new Ellipse(),
            "Diamond" => new Polygon
            {
                Points = new PointCollection { new(0.5, 0), new(1, 0.5), new(0.5, 1), new(0, 0.5) },
                Stretch = Stretch.Fill
            },
            "Hexagon" => new Polygon
            {
                Points = new PointCollection { new(0.25, 0), new(0.75, 0), new(1, 0.5), new(0.75, 1), new(0.25, 1), new(0, 0.5) },
                Stretch = Stretch.Fill
            },
            "Parallelogram" => new Polygon
            {
                Points = new PointCollection { new(0.2, 0), new(1, 0), new(0.8, 1), new(0, 1) },
                Stretch = Stretch.Fill
            },
            "Pill" => new Rectangle { RadiusX = 200, RadiusY = 200 },
            "Triangle" => new Polygon
            {
                Points = new PointCollection { new(0.5, 0), new(1, 1), new(0, 1) },
                Stretch = Stretch.Fill
            },
            "Trapezoid" => new Polygon
            {
                Points = new PointCollection { new(0.22, 0), new(0.78, 0), new(1, 1), new(0, 1) },
                Stretch = Stretch.Fill
            },
            "Octagon" => new Polygon
            {
                Points = new PointCollection
                {
                    new(0.3, 0), new(0.7, 0), new(1, 0.3), new(1, 0.7),
                    new(0.7, 1), new(0.3, 1), new(0, 0.7), new(0, 0.3)
                },
                Stretch = Stretch.Fill
            },
            _ => new Rectangle { RadiusX = 8, RadiusY = 8 },
        };
        s.StrokeThickness = 1;
        s.Effect = NodeShadow;
        return s;
    }

    void ShapeBtn_Click(object sender, RoutedEventArgs e) =>
        ShapePopup.IsOpen = !ShapePopup.IsOpen;

    void RefreshShapeTiles()
    {
        foreach (var (kind, tile) in _shapeTiles)
        {
            bool sel = kind == _lastShape;
            tile.BorderBrush = sel ? AccentBrush : Brushes.Transparent;
            if (sel) tile.SetResourceReference(BackgroundProperty, "Brush.Checked");
            else tile.Background = Brushes.Transparent;
        }
    }

    void ChooseShape(string kind)
    {
        _lastShape = kind;
        if (_settings.RememberLastStyle)
        {
            _settings.LastShape = kind;
            SettingsStore.Save(_settings);
        }
        ShapeIcon.Text = ShapeDefs.First(d => d.Kind == kind).Icon;
        RefreshShapeTiles();
        ShapePopup.IsOpen = false;
        bool any = false;
        foreach (var id in _selected)
        {
            var nv = _nodes[id];
            if (nv.Model.Shape == kind) continue;
            nv.Model.Shape = kind;
            SwapShape(nv);
            any = true;
        }
        if (any) MarkDirty();
    }

    void SwapShape(NodeVisual nv)
    {
        int idx = nv.Root.Children.IndexOf(nv.ShapeEl);
        var s = MakeShapeElement(nv.Model.Shape);
        s.Fill = BrushFrom(nv.Model.Color);
        nv.Root.Children.RemoveAt(idx);
        nv.Root.Children.Insert(idx, s);
        nv.ShapeEl = s;
        UpdateTextInsets(nv);
        UpdateHandlePositions(nv);
        RefreshNodeChrome(nv);
    }

    // Keep text inside the visible area of non-rectangular shapes.
    static Thickness TextInsets(NodeModel m)
    {
        double w = m.W, h = m.H;
        return m.Shape switch
        {
            "Ellipse" => new Thickness(w * 0.15, h * 0.13, w * 0.15, h * 0.13),
            "Diamond" => new Thickness(w * 0.24, h * 0.24, w * 0.24, h * 0.24),
            "Hexagon" => new Thickness(w * 0.20, h * 0.10, w * 0.20, h * 0.10),
            "Parallelogram" => new Thickness(w * 0.22, h * 0.10, w * 0.22, h * 0.10),
            "Pill" => new Thickness(w * 0.14, h * 0.10, w * 0.14, h * 0.10),
            "Triangle" => new Thickness(w * 0.22, h * 0.42, w * 0.22, h * 0.06),
            "Trapezoid" => new Thickness(w * 0.22, h * 0.10, w * 0.22, h * 0.08),
            "Octagon" => new Thickness(w * 0.15, h * 0.12, w * 0.15, h * 0.12),
            _ when m.Kind == "Text" => new Thickness(10, 6, 10, 6),
            _ => new Thickness(12)
        };
    }

    void UpdateTextInsets(NodeVisual nv)
    {
        var t = TextInsets(nv.Model);
        nv.Label.Margin = t;
        nv.Editor.Margin = t;
    }

    NodeVisual CreateNodeVisual(NodeModel m)
    {
        var label = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Margin = new Thickness(12),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };

        var editor = new TextBox
        {
            Visibility = Visibility.Collapsed,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Margin = new Thickness(8),
            Background = Brushes.Transparent,
            Foreground = TextBrush,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        var grip = new Thumb
        {
            Width = 22, Height = 22,
            Cursor = Cursors.SizeNWSE,
            Visibility = Visibility.Collapsed,
            ToolTip = "Drag to resize",
            Template = CreateGripTemplate()
        };

        var shape = MakeShapeElement(m.Shape);
        shape.Fill = BrushFrom(m.Color);
        shape.Stroke = SoftBorderBrush;
        if (m.Kind == "Text") shape.Effect = null;

        var root = new Grid { Width = m.W, Height = m.H, Background = Brushes.Transparent };
        var rot = new RotateTransform(m.Rotation);
        root.RenderTransformOrigin = new Point(0.5, 0.5);
        root.RenderTransform = rot;
        // Content layer: everything that should fade with the object's opacity,
        // while selection handles stay fully visible.
        var content = new Grid { Opacity = m.Opacity <= 0 ? 1 : m.Opacity };
        root.Children.Add(content);
        content.Children.Add(shape);

        Image img = null;
        if (m.Kind != "Shape" && !string.IsNullOrEmpty(m.ImageData))
        {
            img = new Image { Stretch = StretchOf(m.ImageFit), IsHitTestVisible = false, Margin = new Thickness(2) };
            try { img.Source = ImageFromBase64(m.ImageData); } catch { }
            content.Children.Add(img);
        }
        if (m.Kind == "Link")
        {
            var banner = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x10, 0x12, 0x18)),
                VerticalAlignment = VerticalAlignment.Bottom,
                Padding = new Thickness(7, 3, 7, 3),
                IsHitTestVisible = false,
                CornerRadius = new CornerRadius(0, 0, 6, 6),
                Child = new TextBlock
                {
                    Text = "🔗 " + DomainOf(m.Url),
                    FontSize = 11,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            content.Children.Add(banner);
        }

        content.Children.Add(label);
        content.Children.Add(editor);

        var rotHandle = new Border
        {
            Width = 21, Height = 21,
            CornerRadius = new CornerRadius(10.5),
            Background = Brushes.White,
            BorderBrush = AccentBrush,
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            ToolTip = "Drag to rotate - snaps near 0°/45°/90°; hold Shift for 15° steps",
            Child = new TextBlock
            {
                Text = "⟳",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = AccentBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0)
            }
        };
        // Handles live on a Canvas overlay: unlike Grid margins, Canvas children
        // are never clipped when positioned past the node's edges.
        var handleLayer = new Canvas { IsHitTestVisible = true };
        root.Children.Add(handleLayer);
        handleLayer.Children.Add(rotHandle);
        if (m.Kind == "Zone")
        {
            // Zones render behind everything, so their resize badge lives on the
            // world canvas above all objects instead of inside the zone visual.
            Panel.SetZIndex(grip, 100001);
            World.Children.Add(grip);
        }
        else
        {
            handleLayer.Children.Add(grip);
        }

        Canvas.SetLeft(root, m.X);
        Canvas.SetTop(root, m.Y);
        // Zones live behind connections and shapes; saved boards restore layering.
        int z = m.Z != 0 ? m.Z : (m.Kind == "Zone" ? 1 : ++_zTop);
        if (m.Z > _zTop) _zTop = m.Z;
        if (m.Z < _zBottom) _zBottom = m.Z;
        Panel.SetZIndex(root, z);

        var nv = new NodeVisual
        {
            Model = m, Root = root, Content = content, ShapeEl = shape, ImageEl = img, Rot = rot,
            RotHandle = rotHandle, Label = label, Editor = editor, Grip = grip
        };

        rotHandle.MouseLeftButtonDown += (s, e) => { StartRotate(nv, rotHandle); e.Handled = true; };
        rotHandle.MouseMove += (s, e) => Rotate_Move(nv, rotHandle, e);
        rotHandle.MouseLeftButtonUp += (s, e) => Rotate_Up(rotHandle, e);

        // Connector handles (Mural/Miro style): drag one onto another shape to link.
        foreach (Side side in Enum.GetValues<Side>())
        {
            var handle = MakeHandle(nv, side);
            nv.Handles.Add(handle);
            handleLayer.Children.Add(handle);
        }
        UpdateHandlePositions(nv);
        grip.RenderTransformOrigin = new Point(0.5, 0.5);
        grip.RenderTransform = nv.HandleScaleT;
        rotHandle.RenderTransformOrigin = new Point(0.5, 0.5);
        rotHandle.RenderTransform = nv.HandleScaleT;
        nv.HandleScaleT.ScaleX = nv.HandleScaleT.ScaleY = HandleScaleFactor();

        root.MouseLeftButtonDown += (s, e) => Node_Down(nv, e);
        root.MouseMove += (s, e) => Node_Move(nv, e);
        root.MouseLeftButtonUp += (s, e) => Node_Up(nv, e);
        root.MouseEnter += (s, e) => ShowHandles(nv, true);
        root.MouseLeave += (s, e) =>
        {
            if (!(_linking && _linkSource == nv)) ShowHandles(nv, false);
        };
        editor.KeyDown += (s, e) => Editor_KeyDown(nv, e);
        editor.LostKeyboardFocus += (s, e) => { if (_editing == nv) CommitEdit(); };
        grip.DragDelta += (s, e) => Grip_DragDelta(nv, e);

        var menu = new ContextMenu();
        if (m.Kind == "Link")
        {
            var miOpen = new MenuItem { Header = "Open link in browser" };
            miOpen.Click += (s, e) => OpenUrl(nv.Model.Url);
            var miRefresh = new MenuItem { Header = "Refresh preview" };
            miRefresh.Click += async (s, e) => await RefreshLinkPreview(nv);
            var miChange = new MenuItem { Header = "Change address…" };
            miChange.Click += async (s, e) =>
            {
                var newUrl = InputDialog.Show(this, "Change link", "Web address:", nv.Model.Url ?? "", "https://github.com");
                if (string.IsNullOrWhiteSpace(newUrl) || newUrl == nv.Model.Url) return;
                if (!newUrl.Contains("://")) newUrl = "https://" + newUrl;
                nv.Model.Url = newUrl;
                MarkDirty();
                await RefreshLinkPreview(nv);
            };
            menu.Items.Add(miOpen);
            menu.Items.Add(miChange);
            menu.Items.Add(miRefresh);
            menu.Items.Add(new Separator());
        }
        if (m.Kind != "Shape")
        {
            var fit = new MenuItem { Header = "Image fit" };
            foreach (var f in new[] { "Fit", "Fill", "Stretch", "Center" })
            {
                var mode = f;
                var item = new MenuItem { Header = mode };
                item.Click += (s, e) =>
                {
                    nv.Model.ImageFit = mode;
                    if (nv.ImageEl != null) nv.ImageEl.Stretch = StretchOf(mode);
                    MarkDirty();
                };
                fit.Items.Add(item);
            }
            menu.Items.Add(fit);
        }
        var miResetRot = new MenuItem { Header = "Reset rotation" };
        miResetRot.Click += (s, e) =>
        {
            nv.Model.Rotation = 0;
            nv.Rot.Angle = 0;
            UpdateConnectionsFor(nv.Model.Id);
            MarkDirty();
        };
        menu.Items.Add(miResetRot);
        var miEdit = new MenuItem { Header = "Edit text" };
        miEdit.Click += (s, e) => BeginEdit(nv);
        var miDup = new MenuItem { Header = "Duplicate", InputGestureText = "Ctrl+D" };
        miDup.Click += (s, e) => { if (!_selected.Contains(nv.Model.Id)) SelectOnly(nv); DuplicateSelected(); };
        var miCopy = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C" };
        miCopy.Click += (s, e) => { if (!_selected.Contains(nv.Model.Id)) SelectOnly(nv); CopySelected(); };
        var miCut = new MenuItem { Header = "Cut", InputGestureText = "Ctrl+X" };
        miCut.Click += (s, e) => { if (!_selected.Contains(nv.Model.Id)) SelectOnly(nv); CutSelected(); };
        var miFront = new MenuItem { Header = "Bring to front" };
        miFront.Click += (s, e) =>
        {
            Panel.SetZIndex(nv.Root, ++_zTop);
            nv.Model.Z = _zTop;
            MarkDirty();
        };
        var miBack = new MenuItem { Header = "Send to back" };
        miBack.Click += (s, e) =>
        {
            Panel.SetZIndex(nv.Root, --_zBottom);
            nv.Model.Z = _zBottom;
            MarkDirty();
        };
        var miDel = new MenuItem { Header = "Delete", InputGestureText = "Del" };
        miDel.Click += (s, e) => { if (!_selected.Contains(nv.Model.Id)) SelectOnly(nv); DeleteSelected(); };
        menu.Items.Add(miEdit);
        menu.Items.Add(miDup);
        menu.Items.Add(miCopy);
        menu.Items.Add(miCut);
        menu.Items.Add(new Separator());
        menu.Items.Add(miFront);
        menu.Items.Add(miBack);
        menu.Items.Add(new Separator());
        menu.Items.Add(miDel);
        root.ContextMenu = menu;
        root.ContextMenuOpening += (s, e) => { if (!_selected.Contains(nv.Model.Id)) SelectOnly(nv); };

        UpdateTextInsets(nv);
        ApplyTextStyle(nv);
        World.Children.Add(root);
        _nodes[m.Id] = nv;
        return nv;
    }

    void CreateNoteAt(Point worldCenter, string shapeKind = null)
    {
        CommitEdit();
        var m = new NodeModel
        {
            X = worldCenter.X - 84,
            Y = worldCenter.Y - 48,
            Color = _lastColor,
            Shape = shapeKind ?? _lastShape
        };
        if (SnapCheck.IsChecked == true) { m.X = Snap(m.X); m.Y = Snap(m.Y); }
        var nv = CreateNodeVisual(m);
        // Select but don't auto-edit: the new shape can be dragged into place
        // right away; double-click it to start typing.
        SelectOnly(nv);
        MarkDirty();
    }

    void AddNote_Click(object sender, RoutedEventArgs e)
    {
        var wx = (Viewport.ActualWidth / 2 - Pan.X) / _zoom;
        var wy = (Viewport.ActualHeight / 2 - Pan.Y) / _zoom;
        CreateNoteAt(new Point(wx, wy));
    }

    void AddText_Click(object sender, RoutedEventArgs e) => CreateTextAt(ViewCenterWorld());

    void CreateTextAt(Point center)
    {
        CommitEdit();
        var m = new NodeModel
        {
            Kind = "Text",
            Shape = "Rect",
            Color = "#00FFFFFF",
            W = 240, H = 44,
            X = center.X - 120, Y = center.Y - 22,
            Align = "Left",
            FontSize = 14
        };
        if (SnapCheck.IsChecked == true) { m.X = Snap(m.X); m.Y = Snap(m.Y); }
        var nv = CreateNodeVisual(m);
        SelectOnly(nv);
        MarkDirty();
        BeginEdit(nv);
    }

    // ---------- Media import ----------

    Point ViewCenterWorld() => new(
        (Viewport.ActualWidth / 2 - Pan.X) / _zoom,
        (Viewport.ActualHeight / 2 - Pan.Y) / _zoom);

    static Stretch StretchOf(string fit) => fit switch
    {
        "Fill" => Stretch.UniformToFill,
        "Stretch" => Stretch.Fill,
        "Center" => Stretch.None,
        _ => Stretch.Uniform
    };

    static BitmapImage ImageFromBase64(string data)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.StreamSource = new MemoryStream(Convert.FromBase64String(data));
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    static string DomainOf(string url)
    {
        try { return new Uri(url).Host; }
        catch { return url ?? ""; }
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    void ImportImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        byte[] bytes;
        BitmapImage probe;
        try
        {
            bytes = File.ReadAllBytes(dlg.FileName);
            probe = ImageFromBase64(Convert.ToBase64String(bytes));
        }
        catch (Exception ex)
        {
            ModernDialog.Show(this, "Could not load image", ex.Message, "OK");
            return;
        }

        double scale = Math.Min(1.0, 360.0 / Math.Max(probe.PixelWidth, probe.PixelHeight));
        double w = Math.Max(NodeMinW, probe.PixelWidth * scale);
        double h = Math.Max(NodeMinH, probe.PixelHeight * scale);
        var center = ViewCenterWorld();
        var m = new NodeModel
        {
            Kind = "Image",
            ImageData = Convert.ToBase64String(bytes),
            ImageFit = "Fit",
            Color = "#FFFFFF",
            Shape = "Rect",
            X = center.X - w / 2, Y = center.Y - h / 2, W = w, H = h
        };
        if (SnapCheck.IsChecked == true) { m.X = Snap(m.X); m.Y = Snap(m.Y); }
        var nv = CreateNodeVisual(m);
        SelectOnly(nv);
        MarkDirty();
    }

    async void ImportLink_Click(object sender, RoutedEventArgs e)
    {
        var url = InputDialog.Show(this, "Add link", "Web address:", "", "https://github.com");
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!url.Contains("://")) url = "https://" + url;

        Mouse.OverrideCursor = Cursors.Wait;
        byte[] shot = null;
        try { shot = await CaptureUrlPreviewAsync(url); }
        catch { }
        finally { Mouse.OverrideCursor = null; }

        var center = ViewCenterWorld();
        var m = new NodeModel
        {
            Kind = "Link",
            Url = url,
            ImageFit = "Fill",
            Color = "#FFFFFF",
            Shape = "Rect",
            X = center.X - 144, Y = center.Y - 108, W = 288, H = 216
        };
        if (shot != null)
        {
            m.ImageData = Convert.ToBase64String(shot);
        }
        else
        {
            // No WebView2 runtime or the page failed to load: fall back to a link card.
            m.Color = "#A8D8F0";
            m.Text = url;
            m.H = 120;
        }
        if (SnapCheck.IsChecked == true) { m.X = Snap(m.X); m.Y = Snap(m.Y); }
        var nv = CreateNodeVisual(m);
        SelectOnly(nv);
        MarkDirty();
    }

    async Task RefreshLinkPreview(NodeVisual nv)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        byte[] shot = null;
        try { shot = await CaptureUrlPreviewAsync(nv.Model.Url); }
        catch { }
        finally { Mouse.OverrideCursor = null; }
        if (shot != null) nv.Model.ImageData = Convert.ToBase64String(shot);
        var replacement = CreateReplacementVisual(nv);
        MarkDirty();
        SelectOnly(replacement);
        if (shot == null)
            ModernDialog.Show(this, "Preview failed",
                "The page could not be loaded for a preview. Check the address and your connection.", "OK");
    }

    // Rebuilds a node's visuals from its model (used when content changes shape).
    NodeVisual CreateReplacementVisual(NodeVisual nv)
    {
        int z = Panel.GetZIndex(nv.Root);
        World.Children.Remove(nv.Root);
        _nodes.Remove(nv.Model.Id);
        var fresh = CreateNodeVisual(nv.Model);
        Panel.SetZIndex(fresh.Root, z);
        UpdateConnectionsFor(fresh.Model.Id);
        return fresh;
    }

    async Task<byte[]> CaptureUrlPreviewAsync(string url)
    {
        var wv = new Microsoft.Web.WebView2.Wpf.WebView2();
        var host = new Window
        {
            Width = 1024, Height = 768,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -30000, Top = -30000,
            Content = wv
        };
        try
        {
            host.Show();
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null,
                IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MindMapCanvas", "WebView2"));
            await wv.EnsureCoreWebView2Async(env);
            var tcs = new TaskCompletionSource<bool>();
            wv.CoreWebView2.NavigationCompleted += (s, a) => tcs.TrySetResult(a.IsSuccess);
            wv.CoreWebView2.Navigate(url);
            var done = await Task.WhenAny(tcs.Task, Task.Delay(15000));
            if (done != tcs.Task || !tcs.Task.Result) return null;
            await Task.Delay(1500); // let images and layout settle
            using var ms = new MemoryStream();
            await wv.CoreWebView2.CapturePreviewAsync(
                Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, ms);
            return ms.ToArray();
        }
        finally
        {
            host.Close();
        }
    }

    static TextAlignment AlignOf(string a) => a switch
    {
        "Left" => TextAlignment.Left,
        "Right" => TextAlignment.Right,
        _ => TextAlignment.Center
    };

    void RefreshLabel(NodeVisual nv)
    {
        var m = nv.Model;
        nv.Label.FontSize = m.FontSize;
        nv.Label.TextAlignment = AlignOf(m.Align);
        nv.Label.FontWeight = m.Bold ? FontWeights.Bold : FontWeights.Normal;
        if (string.IsNullOrWhiteSpace(m.Text))
        {
            bool wantsPlaceholder = m.Kind == "Shape" || m.Kind == "Text";
            nv.Label.Text = wantsPlaceholder ? "Double-click to edit" : "";
            nv.Label.Foreground = PlaceholderBrush;
            nv.Label.FontStyle = FontStyles.Italic;
        }
        else
        {
            nv.Label.Text = m.Text;
            nv.Label.Foreground = BrushFrom(m.TextColor);
            nv.Label.FontStyle = m.Italic ? FontStyles.Italic : FontStyles.Normal;
        }
    }

    void ApplyTextStyle(NodeVisual nv)
    {
        var m = nv.Model;
        var ff = new FontFamily(string.IsNullOrEmpty(m.Font) ? "Segoe UI" : m.Font);
        nv.Label.FontFamily = ff;
        nv.Editor.FontFamily = ff;
        nv.Editor.FontSize = m.FontSize;
        nv.Editor.TextAlignment = AlignOf(m.Align);
        nv.Editor.FontWeight = m.Bold ? FontWeights.Bold : FontWeights.Normal;
        nv.Editor.FontStyle = m.Italic ? FontStyles.Italic : FontStyles.Normal;
        nv.Editor.Foreground = BrushFrom(m.TextColor);
        RefreshLabel(nv);
    }

    // ---------- Text formatting ----------

    void TextBtn_Click(object sender, RoutedEventArgs e)
    {
        UpdateFontSizeLabel();
        TextPopup.IsOpen = !TextPopup.IsOpen;
    }

    void UpdateFontSizeLabel()
    {
        FontSizeLabel.Text = _selected.Count > 0 && _nodes.TryGetValue(_selected.First(), out var nv)
            ? Math.Round(nv.Model.FontSize).ToString()
            : "-";
    }

    // Temporarily paints a formatting change on the selected shapes while the
    // pointer hovers an option; MouseLeave restores the real style.
    void PreviewFormat(Action<NodeVisual> apply)
    {
        foreach (var id in _selected)
            if (_nodes.TryGetValue(id, out var nv))
                apply(nv);
    }

    void Format_PreviewEnd(object sender, MouseEventArgs e)
    {
        foreach (var id in _selected)
            if (_nodes.TryGetValue(id, out var nv))
                ApplyTextStyle(nv);
    }

    void Bold_Preview(object sender, MouseEventArgs e)
    {
        if (_selected.Count == 0) return;
        bool target = !_selected.All(id => _nodes.TryGetValue(id, out var n) && n.Model.Bold);
        var w = target ? FontWeights.Bold : FontWeights.Normal;
        PreviewFormat(nv => { nv.Label.FontWeight = w; nv.Editor.FontWeight = w; });
    }

    void Italic_Preview(object sender, MouseEventArgs e)
    {
        if (_selected.Count == 0) return;
        bool target = !_selected.All(id => _nodes.TryGetValue(id, out var n) && n.Model.Italic);
        var st = target ? FontStyles.Italic : FontStyles.Normal;
        PreviewFormat(nv => { nv.Label.FontStyle = st; nv.Editor.FontStyle = st; });
    }

    void AlignPreview(TextAlignment a) =>
        PreviewFormat(nv => { nv.Label.TextAlignment = a; nv.Editor.TextAlignment = a; });

    void AlignLeft_Preview(object sender, MouseEventArgs e) => AlignPreview(TextAlignment.Left);
    void AlignCenter_Preview(object sender, MouseEventArgs e) => AlignPreview(TextAlignment.Center);
    void AlignRight_Preview(object sender, MouseEventArgs e) => AlignPreview(TextAlignment.Right);

    void ApplyTextFormat(Action<NodeModel> change)
    {
        if (_selected.Count == 0) return;
        foreach (var id in _selected)
        {
            if (!_nodes.TryGetValue(id, out var nv)) continue;
            change(nv.Model);
            // Styles hit both the label and the live editor, so formatting works
            // mid-edit and on every object kind.
            ApplyTextStyle(nv);
        }
        UpdateFontSizeLabel();
        MarkDirty();
    }

    void FontMinus_Click(object sender, RoutedEventArgs e) =>
        ApplyTextFormat(m => m.FontSize = Math.Max(9, m.FontSize - 2));

    void FontPlus_Click(object sender, RoutedEventArgs e) =>
        ApplyTextFormat(m => m.FontSize = Math.Min(48, m.FontSize + 2));

    void Bold_Click(object sender, RoutedEventArgs e)
    {
        if (_selected.Count == 0) return;
        bool target = !_selected.All(id => _nodes[id].Model.Bold);
        ApplyTextFormat(m => m.Bold = target);
    }

    void Italic_Click(object sender, RoutedEventArgs e)
    {
        if (_selected.Count == 0) return;
        bool target = !_selected.All(id => _nodes[id].Model.Italic);
        ApplyTextFormat(m => m.Italic = target);
    }

    void AlignLeft_Click(object sender, RoutedEventArgs e) => ApplyTextFormat(m => m.Align = "Left");
    void AlignCenter_Click(object sender, RoutedEventArgs e) => ApplyTextFormat(m => m.Align = "Center");
    void AlignRight_Click(object sender, RoutedEventArgs e) => ApplyTextFormat(m => m.Align = "Right");

    void CustomTextColor_Click(object sender, RoutedEventArgs e)
    {
        if (_selected.Count == 0) return;
        TextPopup.IsOpen = false;
        Color initial;
        try { initial = (Color)ColorConverter.ConvertFromString(_nodes[_selected.First()].Model.TextColor); }
        catch { initial = Colors.Black; }
        var dlg = new ColorPickerWindow(initial) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var hex = $"#{dlg.SelectedColor.R:X2}{dlg.SelectedColor.G:X2}{dlg.SelectedColor.B:X2}";
            ApplyTextFormat(m => m.TextColor = hex);
        }
    }

    // ---------- Colors ----------

    void ColorBtn_Click(object sender, RoutedEventArgs e) =>
        ColorPopup.IsOpen = !ColorPopup.IsOpen;

    void CustomColor_Click(object sender, RoutedEventArgs e)
    {
        ColorPopup.IsOpen = false;
        Color initial;
        try { initial = (Color)ColorConverter.ConvertFromString(_lastColor); }
        catch { initial = Colors.LightYellow; }
        var dlg = new ColorPickerWindow(initial) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var hex = $"#{dlg.SelectedColor.R:X2}{dlg.SelectedColor.G:X2}{dlg.SelectedColor.B:X2}";
            RememberCustomColor(hex);
            ApplyColor(hex);
        }
    }

    void RememberCustomColor(string hex)
    {
        if (Palette.Contains(hex, StringComparer.OrdinalIgnoreCase)) return;
        _settings.CustomColors.RemoveAll(c => string.Equals(c, hex, StringComparison.OrdinalIgnoreCase));
        _settings.CustomColors.Insert(0, hex);
        if (_settings.CustomColors.Count > 8)
            _settings.CustomColors.RemoveRange(8, _settings.CustomColors.Count - 8);
        SettingsStore.Save(_settings);
        BuildRecentSwatches();
    }

    void BuildRecentSwatches()
    {
        RecentWrap.Children.Clear();
        RecentLabel.Visibility = _settings.CustomColors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var hex in _settings.CustomColors)
        {
            var color = hex;
            var sw = new Border
            {
                Width = 22, Height = 22,
                CornerRadius = new CornerRadius(6),
                Background = BrushFrom(color),
                BorderBrush = SoftBorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2),
                Cursor = Cursors.Hand,
                ToolTip = color
            };
            sw.MouseLeftButtonDown += (s, a) =>
            {
                ApplyColor(color);
                ColorPopup.IsOpen = false;
                a.Handled = true;
            };
            RecentWrap.Children.Add(sw);
        }
    }

    void ApplyColor(string hex)
    {
        _lastColor = hex;
        if (_settings.RememberLastStyle)
        {
            _settings.LastColor = hex;
            SettingsStore.Save(_settings);
        }
        CurrentColorSwatch.Background = BrushFrom(hex);
        bool any = false;
        foreach (var id in _selected)
        {
            var nv = _nodes[id];
            nv.Model.Color = hex;
            nv.ShapeEl.Fill = BrushFrom(hex);
            any = true;
        }
        if (any) MarkDirty();
    }

    // ---------- Selection ----------

    void RefreshNodeChrome(NodeVisual nv)
    {
        bool highlighted = _selected.Contains(nv.Model.Id) || _linkHover == nv;
        nv.ShapeEl.Stroke = highlighted ? AccentBrush : SoftBorderBrush;
        if (nv.Model.Kind == "Text" && !highlighted) nv.ShapeEl.Stroke = Brushes.Transparent;
        nv.ShapeEl.StrokeThickness = highlighted ? 3 : 1;
        nv.ShapeEl.Effect = highlighted ? SelectedGlow : NodeShadow;
        var sel = _selected.Contains(nv.Model.Id) ? Visibility.Visible : Visibility.Collapsed;
        nv.Grip.Visibility = sel;
        nv.RotHandle.Visibility = nv.Model.Kind == "Zone" ? Visibility.Collapsed : sel;
    }

    // ---------- Rotation ----------

    bool _rotating;

    void StartRotate(NodeVisual nv, FrameworkElement handle)
    {
        CommitEdit();
        if (!_selected.Contains(nv.Model.Id)) SelectOnly(nv);
        _rotating = true;
        handle.CaptureMouse();
    }

    void Rotate_Move(NodeVisual nv, FrameworkElement handle, MouseEventArgs e)
    {
        if (!_rotating || !handle.IsMouseCaptured) return;
        var p = e.GetPosition(World);
        var c = CenterOf(nv.Model);
        double angle = Math.Atan2(p.Y - c.Y, p.X - c.X) * 180.0 / Math.PI + 90.0;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            angle = Math.Round(angle / 15.0) * 15.0;
        }
        else
        {
            // Magnetic snap onto the axes and diagonals (0/45/90/...).
            double nearest = Math.Round(angle / 45.0) * 45.0;
            if (Math.Abs(angle - nearest) <= 4) angle = nearest;
        }
        angle = ((angle % 360) + 360) % 360;
        nv.Model.Rotation = angle;
        nv.Rot.Angle = angle;
        UpdateConnectionsFor(nv.Model.Id);
        MarkDirty();
    }

    void Rotate_Up(FrameworkElement handle, MouseButtonEventArgs e)
    {
        if (!_rotating) return;
        _rotating = false;
        handle.ReleaseMouseCapture();
        e.Handled = true;
    }

    void SelectOnly(NodeVisual nv)
    {
        ClearSelection();
        _selected.Add(nv.Model.Id);
        RefreshNodeChrome(nv);
        if (nv.Model.Kind == "Zone" || nv.Model.Opacity < 1)
        {
            _syncingOpacity = true;
            PaintOpacity.Value = nv.Model.Opacity <= 0 ? 0.5 : nv.Model.Opacity;
            _syncingOpacity = false;
        }
    }

    void ToggleSelect(NodeVisual nv)
    {
        ClearConnSelection();
        if (!_selected.Add(nv.Model.Id)) _selected.Remove(nv.Model.Id);
        RefreshNodeChrome(nv);
    }

    void ClearSelection()
    {
        if (_selected.Count > 0)
        {
            var ids = _selected.ToList();
            _selected.Clear();
            foreach (var id in ids)
                if (_nodes.TryGetValue(id, out var nv)) RefreshNodeChrome(nv);
        }
        ClearConnSelection();
    }

    void SelectAllNodes()
    {
        ClearConnSelection();
        foreach (var nv in _nodes.Values)
        {
            _selected.Add(nv.Model.Id);
            RefreshNodeChrome(nv);
        }
    }

    void SelectConnection(ConnectionVisual cv)
    {
        ClearSelection();
        _selectedConn = cv;
        cv.Body.Stroke = AccentBrush;
        cv.Arrow.Fill = AccentBrush;
    }

    void ClearConnSelection()
    {
        if (_selectedConn == null) return;
        _selectedConn.Body.Stroke = ConnStrokeOf(_selectedConn);
        _selectedConn.Arrow.Fill = ConnStrokeOf(_selectedConn);
        _selectedConn = null;
    }

    // ---------- Editing text ----------

    void BeginEdit(NodeVisual nv)
    {
        CommitEdit();
        SelectOnly(nv);
        _editing = nv;
        nv.Editor.Text = nv.Model.Text;
        nv.Label.Visibility = Visibility.Collapsed;
        nv.Editor.Visibility = Visibility.Visible;
        nv.Editor.Focus();
        nv.Editor.SelectAll();
    }

    void CommitEdit()
    {
        if (_editing == null) return;
        var nv = _editing;
        _editing = null;
        if (nv.Model.Text != nv.Editor.Text)
        {
            nv.Model.Text = nv.Editor.Text;
            MarkDirty();
        }
        nv.Editor.Visibility = Visibility.Collapsed;
        nv.Label.Visibility = Visibility.Visible;
        RefreshLabel(nv);
        Focus();
    }

    void CancelEdit()
    {
        if (_editing == null) return;
        var nv = _editing;
        _editing = null;
        nv.Editor.Visibility = Visibility.Collapsed;
        nv.Label.Visibility = Visibility.Visible;
        Focus();
    }

    void Editor_KeyDown(NodeVisual nv, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            CommitEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelEdit();
            e.Handled = true;
        }
    }

    // ---------- Node interaction ----------

    void Node_Down(NodeVisual nv, MouseButtonEventArgs e)
    {
        if (_spaceDown || _panning || _linking) return;
        if (_editing == nv) return;
        CommitEdit();

        if (e.ClickCount == 2)
        {
            if (nv.Model.Kind == "Link" && !string.IsNullOrEmpty(nv.Model.Url))
                OpenUrl(nv.Model.Url);
            else
                BeginEdit(nv);
            e.Handled = true;
            return;
        }

        if (nv.Model.Kind != "Zone")
        {
            Panel.SetZIndex(nv.Root, ++_zTop);
            nv.Model.Z = _zTop;
        }
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl) ToggleSelect(nv);
        else if (!_selected.Contains(nv.Model.Id)) SelectOnly(nv);

        if (!_selected.Contains(nv.Model.Id)) { e.Handled = true; return; }

        _draggingNodes = true;
        _movedDuringDrag = false;
        _dragStartWorld = e.GetPosition(World);
        _dragOrigins.Clear();
        foreach (var id in _selected)
            _dragOrigins[id] = new Point(_nodes[id].Model.X, _nodes[id].Model.Y);
        nv.Root.CaptureMouse();
        e.Handled = true;
    }

    void Node_Move(NodeVisual nv, MouseEventArgs e)
    {
        if (!_draggingNodes || !nv.Root.IsMouseCaptured) return;
        var p = e.GetPosition(World);
        double dx = p.X - _dragStartWorld.X, dy = p.Y - _dragStartWorld.Y;
        if (!_movedDuringDrag && Math.Abs(dx) < 2 && Math.Abs(dy) < 2) return;
        _movedDuringDrag = true;

        // Position the grabbed node (grid snap, then axis alignment against other
        // shapes), then move the rest of the selection by the same final delta so
        // relative layout is preserved.
        var origin = _dragOrigins[nv.Model.Id];
        double nx = origin.X + dx, ny = origin.Y + dy;
        if (SnapCheck.IsChecked == true) { nx = Snap(nx); ny = Snap(ny); }
        ApplyAlignmentSnap(nv, ref nx, ref ny);

        double fdx = nx - origin.X, fdy = ny - origin.Y;
        foreach (var kv in _dragOrigins)
        {
            if (!_nodes.TryGetValue(kv.Key, out var n)) continue;
            n.Model.X = kv.Value.X + fdx;
            n.Model.Y = kv.Value.Y + fdy;
            Canvas.SetLeft(n.Root, n.Model.X);
            Canvas.SetTop(n.Root, n.Model.Y);
            if (n.Model.Kind == "Zone") UpdateHandlePositions(n);
            UpdateConnectionsFor(kv.Key);
        }
        MarkDirty();
    }

    // ---------- Alignment guides ----------

    Line _guideV, _guideH;

    void ApplyAlignmentSnap(NodeVisual nv, ref double nx, ref double ny)
    {
        double w = nv.Model.W, h = nv.Model.H;
        double threshold = 8 / _zoom;
        double bestDx = double.MaxValue, bestDy = double.MaxValue;
        double guideX = 0, guideY = 0;
        Rect otherX = Rect.Empty, otherY = Rect.Empty;

        double[] mineX = { nx, nx + w / 2, nx + w };
        double[] mineY = { ny, ny + h / 2, ny + h };
        double[] tx = new double[3];
        double[] ty = new double[3];

        foreach (var other in _nodes.Values)
        {
            if (_selected.Contains(other.Model.Id)) continue;
            var ob = NodeBounds(other.Model);
            tx[0] = ob.X; tx[1] = ob.X + ob.Width / 2; tx[2] = ob.Right;
            ty[0] = ob.Y; ty[1] = ob.Y + ob.Height / 2; ty[2] = ob.Bottom;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    double d = tx[j] - mineX[i];
                    if (Math.Abs(d) < threshold && Math.Abs(d) < Math.Abs(bestDx))
                    { bestDx = d; guideX = tx[j]; otherX = ob; }
                    d = ty[j] - mineY[i];
                    if (Math.Abs(d) < threshold && Math.Abs(d) < Math.Abs(bestDy))
                    { bestDy = d; guideY = ty[j]; otherY = ob; }
                }
        }

        if (bestDx != double.MaxValue)
        {
            nx += bestDx;
            ShowGuide(ref _guideV, vertical: true, guideX, new Rect(nx, ny, w, h), otherX);
        }
        else HideGuide(ref _guideV);

        if (bestDy != double.MaxValue)
        {
            ny += bestDy;
            ShowGuide(ref _guideH, vertical: false, guideY, new Rect(nx, ny, w, h), otherY);
        }
        else HideGuide(ref _guideH);
    }

    void ShowGuide(ref Line guide, bool vertical, double at, Rect a, Rect b)
    {
        if (guide == null)
        {
            guide = new Line
            {
                Stroke = AccentBrush,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            };
            Panel.SetZIndex(guide, 99997);
            World.Children.Add(guide);
        }
        guide.StrokeThickness = Math.Max(0.5, 1.2 / _zoom);
        if (vertical)
        {
            guide.X1 = guide.X2 = at;
            guide.Y1 = Math.Min(a.Y, b.Y) - 24;
            guide.Y2 = Math.Max(a.Bottom, b.Bottom) + 24;
        }
        else
        {
            guide.Y1 = guide.Y2 = at;
            guide.X1 = Math.Min(a.X, b.X) - 24;
            guide.X2 = Math.Max(a.Right, b.Right) + 24;
        }
    }

    void HideGuide(ref Line guide)
    {
        if (guide == null) return;
        World.Children.Remove(guide);
        guide = null;
    }

    void Node_Up(NodeVisual nv, MouseButtonEventArgs e)
    {
        if (!_draggingNodes) return;
        _draggingNodes = false;
        HideGuide(ref _guideV);
        HideGuide(ref _guideH);
        if (nv.Root.IsMouseCaptured) nv.Root.ReleaseMouseCapture();
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (!_movedDuringDrag && !ctrl && _selected.Count > 1) SelectOnly(nv);
        e.Handled = true;
    }

    void Grip_DragDelta(NodeVisual nv, DragDeltaEventArgs e)
    {
        var m = nv.Model;
        m.W = Math.Max(NodeMinW, m.W + e.HorizontalChange);
        m.H = Math.Max(NodeMinH, m.H + e.VerticalChange);
        if (m.Kind == "Zone")
        {
            m.W = Math.Max(GridSize * 2, Snap(m.W));
            m.H = Math.Max(GridSize * 2, Snap(m.H));
        }
        nv.Root.Width = m.W;
        nv.Root.Height = m.H;
        UpdateTextInsets(nv);
        UpdateHandlePositions(nv);
        UpdateConnectionsFor(m.Id);
        MarkDirty();
    }

    void Nudge(double dx, double dy)
    {
        if (_selected.Count == 0) return;
        foreach (var id in _selected)
        {
            var n = _nodes[id];
            n.Model.X += dx;
            n.Model.Y += dy;
            Canvas.SetLeft(n.Root, n.Model.X);
            Canvas.SetTop(n.Root, n.Model.Y);
            if (n.Model.Kind == "Zone") UpdateHandlePositions(n);
            UpdateConnectionsFor(id);
        }
        MarkDirty();
    }

    void DuplicateSelected()
    {
        CommitEdit();
        if (_selected.Count == 0) return;
        var map = new Dictionary<Guid, Guid>();
        var clones = new List<NodeVisual>();
        foreach (var id in _selected.ToList())
        {
            var m = _nodes[id].Model.Clone();
            m.X += GridSize;
            m.Y += GridSize;
            map[id] = m.Id;
            clones.Add(CreateNodeVisual(m));
        }
        foreach (var c in _conns.Where(c => map.ContainsKey(c.Model.From) && map.ContainsKey(c.Model.To)).ToList())
            AddConnection(map[c.Model.From], map[c.Model.To], c.Model.FromAnchor, c.Model.ToAnchor, c.Model.Color);
        ClearSelection();
        foreach (var nv in clones)
        {
            _selected.Add(nv.Model.Id);
            RefreshNodeChrome(nv);
        }
        MarkDirty();
    }

    // ---------- Clipboard ----------

    void CopySelected()
    {
        CommitEdit();
        if (_selected.Count == 0) return;
        _clipboardNodes.Clear();
        _clipboardConns.Clear();
        foreach (var id in _selected)
            _clipboardNodes.Add(_nodes[id].Model.Clone(keepId: true));
        foreach (var c in _conns)
            if (_selected.Contains(c.Model.From) && _selected.Contains(c.Model.To))
                _clipboardConns.Add(new ConnectionModel
                {
                    From = c.Model.From, To = c.Model.To,
                    FromAnchor = c.Model.FromAnchor, ToAnchor = c.Model.ToAnchor,
                    Color = c.Model.Color
                });
    }

    void CutSelected()
    {
        CopySelected();
        DeleteSelected();
    }

    void Paste() =>
        PasteAt(Viewport.IsMouseOver ? _lastWorldMouse : (Point?)null);

    void PasteAt(Point? at)
    {
        if (_clipboardNodes.Count == 0) return;
        var b = Rect.Empty;
        foreach (var n in _clipboardNodes) b.Union(new Rect(n.X, n.Y, n.W, n.H));

        // Paste centered on the requested point, otherwise offset from the source.
        Point target = at ?? new Point(b.X + b.Width / 2 + GridSize, b.Y + b.Height / 2 + GridSize);
        double ox = target.X - (b.X + b.Width / 2);
        double oy = target.Y - (b.Y + b.Height / 2);
        bool snap = SnapCheck.IsChecked == true;

        var map = new Dictionary<Guid, Guid>();
        var created = new List<NodeVisual>();
        foreach (var n in _clipboardNodes)
        {
            var m = n.Clone();
            m.X = n.X + ox;
            m.Y = n.Y + oy;
            if (snap) { m.X = Snap(m.X); m.Y = Snap(m.Y); }
            map[n.Id] = m.Id;
            created.Add(CreateNodeVisual(m));
        }
        foreach (var c in _clipboardConns)
            AddConnection(map[c.From], map[c.To], c.FromAnchor, c.ToAnchor, c.Color);
        ClearSelection();
        foreach (var nv in created)
        {
            _selected.Add(nv.Model.Id);
            RefreshNodeChrome(nv);
        }
        MarkDirty();
    }

    void DeleteSelected()
    {
        CommitEdit();
        bool any = false;
        foreach (var id in _selected.ToList())
        {
            RemoveNode(id);
            any = true;
        }
        _selected.Clear();
        if (_selectedConn != null)
        {
            RemoveConnectionVisual(_selectedConn);
            any = true;
        }
        if (any) MarkDirty();
    }

    void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    void RemoveNode(Guid id)
    {
        if (!_nodes.TryGetValue(id, out var nv)) return;
        foreach (var cv in _conns.Where(c => c.Model.From == id || c.Model.To == id).ToList())
            RemoveConnectionVisual(cv);
        World.Children.Remove(nv.Root);
        if (nv.Model.Kind == "Zone") World.Children.Remove(nv.Grip);
        _nodes.Remove(id);
        if (_editing == nv) _editing = null;
        if (_linkSource == nv || _linkHover == nv) CancelLink();
    }

    // ---------- Connector handles & linking ----------

    Ellipse MakeHandle(NodeVisual nv, Side side)
    {
        var el = new Ellipse
        {
            Width = 12, Height = 12,
            Fill = AccentBrush,
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Cross,
            Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = nv.HandleScaleT,
            ToolTip = "Drag onto another shape to connect"
        };
        el.MouseLeftButtonDown += (s, e) => { StartLink(nv, side, el); e.Handled = true; };
        el.MouseMove += (s, e) => Link_Move(el, e);
        el.MouseLeftButtonUp += (s, e) => Link_Up(el, e);
        return el;
    }

    // Position each connector dot on the shape outline; called on create/resize/
    // reshape/zoom. The rotate button floats above the top edge, backing away as
    // the handles scale up so nothing overlaps.
    void UpdateHandlePositions(NodeVisual nv)
    {
        var m = nv.Model;
        int i = 0;
        foreach (Side side in Enum.GetValues<Side>())
        {
            var p = SideAnchorLocal(m, side);
            var el = nv.Handles[i++];
            Canvas.SetLeft(el, p.X - m.X - 6);
            Canvas.SetTop(el, p.Y - m.Y - 6);
        }
        double k = HandleScaleFactor();
        Canvas.SetLeft(nv.RotHandle, m.W / 2 - 10.5);
        Canvas.SetTop(nv.RotHandle, -(12 + 24 * k));
        double gripOffset = 8 + 10 * k - 11;
        if (m.Kind == "Zone")
        {
            // World coordinates: the zone grip is a direct World child.
            Canvas.SetLeft(nv.Grip, m.X + m.W + gripOffset);
            Canvas.SetTop(nv.Grip, m.Y + m.H + gripOffset);
        }
        else
        {
            Canvas.SetLeft(nv.Grip, m.W + gripOffset);
            Canvas.SetTop(nv.Grip, m.H + gripOffset);
        }
    }

    void ShowHandles(NodeVisual nv, bool show)
    {
        if (nv.Model.Kind == "Zone") show = false;
        var vis = show ? Visibility.Visible : Visibility.Collapsed;
        foreach (var h in nv.Handles) h.Visibility = vis;
    }

    static Point CenterOf(NodeModel m) => new(m.X + m.W / 2, m.Y + m.H / 2);

    static Point RotatePt(Point p, Point c, double deg)
    {
        if (deg == 0) return p;
        double a = deg * Math.PI / 180.0, cos = Math.Cos(a), sin = Math.Sin(a);
        double dx = p.X - c.X, dy = p.Y - c.Y;
        return new Point(c.X + dx * cos - dy * sin, c.Y + dx * sin + dy * cos);
    }

    // Axis-aligned bounds of a (possibly rotated) node.
    static Rect NodeBounds(NodeModel m)
    {
        if (m.Rotation == 0) return new Rect(m.X, m.Y, m.W, m.H);
        var c = CenterOf(m);
        var b = Rect.Empty;
        b.Union(RotatePt(new Point(m.X, m.Y), c, m.Rotation));
        b.Union(RotatePt(new Point(m.X + m.W, m.Y), c, m.Rotation));
        b.Union(RotatePt(new Point(m.X, m.Y + m.H), c, m.Rotation));
        b.Union(RotatePt(new Point(m.X + m.W, m.Y + m.H), c, m.Rotation));
        return b;
    }

    static Point SideAnchor(NodeVisual nv, Side side) => SideAnchorM(nv.Model, side);

    static Point SideAnchorM(NodeModel m, Side side) =>
        RotatePt(SideAnchorLocal(m, side), CenterOf(m), m.Rotation);

    // Anchor pulled onto the actual outline so arrows and dots touch curved shapes
    // (a bounding-box corner sits outside an ellipse or diamond).
    static Point SideAnchorLocal(NodeModel m, Side side)
    {
        var raw = SideAnchorRaw(m, side);
        if (m.Shape == "Ellipse" || ShapeOutlines.ContainsKey(m.Shape))
            return EdgePointLocal(m, CenterOf(m), raw);
        return raw;
    }

    static Point SideAnchorRaw(NodeModel m, Side side)
    {
        return side switch
        {
            Side.Left => new Point(m.X, m.Y + m.H / 2),
            Side.Right => new Point(m.X + m.W, m.Y + m.H / 2),
            Side.Top => new Point(m.X + m.W / 2, m.Y),
            Side.Bottom => new Point(m.X + m.W / 2, m.Y + m.H),
            Side.TopLeft => new Point(m.X, m.Y),
            Side.TopRight => new Point(m.X + m.W, m.Y),
            Side.BottomLeft => new Point(m.X, m.Y + m.H),
            _ => new Point(m.X + m.W, m.Y + m.H),
        };
    }

    static string NearestAnchor(NodeModel m, Point p)
    {
        Side best = Side.Left;
        double bd = double.MaxValue;
        foreach (Side s in Enum.GetValues<Side>())
        {
            var d = (SideAnchorM(m, s) - p).LengthSquared;
            if (d < bd) { bd = d; best = s; }
        }
        return best.ToString();
    }

    void StartLink(NodeVisual nv, Side side, Ellipse handle)
    {
        CommitEdit();
        _linking = true;
        _linkSource = nv;
        _linkSourceSide = side;
        _linkHover = null;
        var a = SideAnchor(nv, side);
        _linkPreview = new Line
        {
            X1 = a.X, Y1 = a.Y, X2 = a.X, Y2 = a.Y,
            Stroke = AccentBrush, StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false
        };
        Panel.SetZIndex(_linkPreview, 99998);
        World.Children.Add(_linkPreview);
        handle.CaptureMouse();
    }

    void Link_Move(Ellipse handle, MouseEventArgs e)
    {
        if (!_linking || !handle.IsMouseCaptured) return;
        var p = e.GetPosition(World);

        var target = HitNode(p, _linkSource);
        if (target != _linkHover)
        {
            var old = _linkHover;
            _linkHover = target;
            if (old != null)
            {
                RefreshNodeChrome(old);
                ShowHandles(old, false);
            }
            if (target != null)
            {
                RefreshNodeChrome(target);
                // Reveal every dot the connection could snap to on the target.
                ShowHandles(target, true);
            }
        }

        if (target != null)
        {
            // Preview the exact dot the connection will snap to.
            var anchor = SideAnchorM(target.Model, Enum.Parse<Side>(NearestAnchor(target.Model, p)));
            _linkPreview.X2 = anchor.X;
            _linkPreview.Y2 = anchor.Y;
            ShowAnchorRing(anchor);
        }
        else
        {
            _linkPreview.X2 = p.X;
            _linkPreview.Y2 = p.Y;
            HideAnchorRing();
        }
    }

    void ShowAnchorRing(Point at)
    {
        if (_anchorRing == null)
        {
            _anchorRing = new Ellipse
            {
                Width = 20, Height = 20,
                Stroke = AccentBrush,
                StrokeThickness = 2.5,
                Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x4C, 0x6E, 0xF5)),
                IsHitTestVisible = false
            };
            Panel.SetZIndex(_anchorRing, 99999);
            World.Children.Add(_anchorRing);
        }
        Canvas.SetLeft(_anchorRing, at.X - 10);
        Canvas.SetTop(_anchorRing, at.Y - 10);
    }

    void HideAnchorRing()
    {
        if (_anchorRing == null) return;
        World.Children.Remove(_anchorRing);
        _anchorRing = null;
    }

    void Link_Up(Ellipse handle, MouseButtonEventArgs e)
    {
        if (!_linking) return;
        handle.ReleaseMouseCapture();
        var p = e.GetPosition(World);
        var src = _linkSource;
        var srcSide = _linkSourceSide;
        var target = HitNode(p, src);
        CancelLink();
        if (target != null &&
            AddConnection(src.Model.Id, target.Model.Id,
                srcSide.ToString(), NearestAnchor(target.Model, p), _lastConnColor) != null)
            MarkDirty();
        if (!src.Root.IsMouseOver) ShowHandles(src, false);
        e.Handled = true;
    }

    void CancelLink()
    {
        HideAnchorRing();
        if (_linkPreview != null)
        {
            World.Children.Remove(_linkPreview);
            _linkPreview = null;
        }
        _linking = false;
        _linkSource = null;
        var hover = _linkHover;
        _linkHover = null;
        if (hover != null)
        {
            RefreshNodeChrome(hover);
            if (!hover.Root.IsMouseOver) ShowHandles(hover, false);
        }
    }

    NodeVisual HitNode(Point p, NodeVisual exclude)
    {
        NodeVisual best = null;
        int bestZ = int.MinValue;
        foreach (var nv in _nodes.Values)
        {
            if (nv == exclude) continue;
            var m = nv.Model;
            if (p.X >= m.X - 8 && p.X <= m.X + m.W + 8 &&
                p.Y >= m.Y - 8 && p.Y <= m.Y + m.H + 8)
            {
                int z = Panel.GetZIndex(nv.Root);
                if (z >= bestZ) { bestZ = z; best = nv; }
            }
        }
        return best;
    }

    // ---------- Connections ----------

    Brush ConnStrokeOf(ConnectionVisual cv) =>
        cv.Model.Color == null ? ConnBrush : BrushFrom(cv.Model.Color);

    void ApplyConnColor(ConnectionVisual cv, string hex, bool all)
    {
        if (all)
        {
            foreach (var other in _conns)
            {
                other.Model.Color = hex;
                if (other != _selectedConn)
                {
                    other.Body.Stroke = ConnStrokeOf(other);
                    other.Arrow.Fill = ConnStrokeOf(other);
                }
            }
            _lastConnColor = hex;
            _settings.LastConnColor = hex;
            SettingsStore.Save(_settings);
            MarkDirty();
        }
        else
        {
            SetConnColor(cv, hex);
        }
        // Deselect so the new color is visible immediately instead of the
        // selection highlight.
        ClearConnSelection();
    }

    void SetConnColor(ConnectionVisual cv, string hex)
    {
        cv.Model.Color = hex;
        _lastConnColor = hex;
        _settings.LastConnColor = hex;
        SettingsStore.Save(_settings);
        if (_selectedConn != cv)
        {
            cv.Body.Stroke = ConnStrokeOf(cv);
            cv.Arrow.Fill = ConnStrokeOf(cv);
        }
        MarkDirty();
    }

    ConnectionVisual AddConnection(Guid from, Guid to, string fromAnchor = null, string toAnchor = null, string color = null)
    {
        if (from == to) return null;
        if (!_nodes.ContainsKey(from) || !_nodes.ContainsKey(to)) return null;
        if (_conns.Any(c => c.Model.From == from && c.Model.To == to &&
                            c.Model.FromAnchor == fromAnchor && c.Model.ToAnchor == toAnchor)) return null;

        var cv = new ConnectionVisual
        {
            Model = new ConnectionModel { From = from, To = to, FromAnchor = fromAnchor, ToAnchor = toAnchor, Color = color }
        };
        cv.Body = new Line
        {
            Stroke = ConnStrokeOf(cv), StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        cv.Arrow = new Polygon { Fill = ConnStrokeOf(cv), IsHitTestVisible = false };
        cv.Hit = new Line
        {
            Stroke = Brushes.Transparent,
            StrokeThickness = Math.Max(10, 14 / Math.Max(0.2, _zoom)),
            Cursor = Cursors.Hand
        };
        Panel.SetZIndex(cv.Body, 2);
        Panel.SetZIndex(cv.Arrow, 2);
        Panel.SetZIndex(cv.Hit, 3);
        cv.Hit.MouseLeftButtonDown += (s, e) =>
        {
            CommitEdit();
            SelectConnection(cv);
            e.Handled = true;
        };
        cv.Hit.MouseRightButtonDown += (s, e) => SelectConnection(cv);

        var connMenu = new ContextMenu();
        var colorMenu = new MenuItem { Header = "Color" };
        foreach (var (name, hex) in new (string, string)[]
        {
            ("Default", null), ("Blue", "#4C6EF5"), ("Red", "#E5484D"), ("Orange", "#F59E0B"),
            ("Green", "#2F9E68"), ("Purple", "#8B5CF6"), ("Black", "#1F2328"), ("White", "#FFFFFF")
        })
        {
            var chosen = hex;
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new Border
            {
                Width = 14, Height = 14,
                CornerRadius = new CornerRadius(4),
                Background = chosen == null ? ConnBrush : BrushFrom(chosen),
                BorderBrush = SoftBorderBrush,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            });
            var nameText = new TextBlock { Text = name, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            nameText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            header.Children.Add(nameText);
            var colorItem = new MenuItem { Header = header };
            var applyOne = new MenuItem { Header = "Apply to this connector" };
            applyOne.Click += (s, e) => ApplyConnColor(cv, chosen, all: false);
            var applyAll = new MenuItem { Header = "Apply to all connectors" };
            applyAll.Click += (s, e) => ApplyConnColor(cv, chosen, all: true);
            colorItem.Items.Add(applyOne);
            colorItem.Items.Add(applyAll);
            colorMenu.Items.Add(colorItem);
        }
        var miCustomConn = new MenuItem { Header = "Custom…" };
        miCustomConn.Click += (s, e) =>
        {
            Color initial;
            try { initial = (Color)ColorConverter.ConvertFromString(cv.Model.Color ?? "#8895A7"); }
            catch { initial = Colors.Gray; }
            var dlg = new ColorPickerWindow(initial) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            var hex = $"#{dlg.SelectedColor.R:X2}{dlg.SelectedColor.G:X2}{dlg.SelectedColor.B:X2}";
            var scope = ModernDialog.Show(this, "Apply custom color",
                "Apply this color to just this connector, or to every connector on the board?",
                "This connector", "All connectors", "Cancel");
            if (scope == ModernDialog.Outcome.Primary) ApplyConnColor(cv, hex, all: false);
            else if (scope == ModernDialog.Outcome.Secondary) ApplyConnColor(cv, hex, all: true);
        };
        colorMenu.Items.Add(miCustomConn);
        connMenu.Items.Add(colorMenu);

        var miReverse = new MenuItem { Header = "Reverse direction" };
        miReverse.Click += (s, e) =>
        {
            (cv.Model.From, cv.Model.To) = (cv.Model.To, cv.Model.From);
            (cv.Model.FromAnchor, cv.Model.ToAnchor) = (cv.Model.ToAnchor, cv.Model.FromAnchor);
            UpdateConnectionVisual(cv);
            ClearConnSelection();
            MarkDirty();
        };
        var miDelConn = new MenuItem { Header = "Delete connection", InputGestureText = "Del" };
        miDelConn.Click += (s, e) =>
        {
            RemoveConnectionVisual(cv);
            MarkDirty();
        };
        connMenu.Items.Add(miReverse);
        connMenu.Items.Add(new Separator());
        connMenu.Items.Add(miDelConn);
        cv.Hit.ContextMenu = connMenu;

        World.Children.Add(cv.Body);
        World.Children.Add(cv.Arrow);
        World.Children.Add(cv.Hit);
        _conns.Add(cv);
        UpdateConnectionVisual(cv);
        return cv;
    }

    void RemoveConnectionVisual(ConnectionVisual cv)
    {
        World.Children.Remove(cv.Body);
        World.Children.Remove(cv.Arrow);
        World.Children.Remove(cv.Hit);
        _conns.Remove(cv);
        if (_selectedConn == cv) _selectedConn = null;
    }

    void UpdateConnectionsFor(Guid id)
    {
        foreach (var cv in _conns)
            if (cv.Model.From == id || cv.Model.To == id)
                UpdateConnectionVisual(cv);
    }

    void UpdateConnectionVisual(ConnectionVisual cv)
    {
        if (!_nodes.TryGetValue(cv.Model.From, out var a) || !_nodes.TryGetValue(cv.Model.To, out var b)) return;
        var ca = new Point(a.Model.X + a.Model.W / 2, a.Model.Y + a.Model.H / 2);
        var cb = new Point(b.Model.X + b.Model.W / 2, b.Model.Y + b.Model.H / 2);
        // Pinned anchors stay on the exact dot the user chose; otherwise aim center-to-center.
        var p1 = Enum.TryParse<Side>(cv.Model.FromAnchor, out var sa)
            ? SideAnchorM(a.Model, sa)
            : EdgePointFor(a.Model, ca, cb);
        var p2 = Enum.TryParse<Side>(cv.Model.ToAnchor, out var sb)
            ? SideAnchorM(b.Model, sb)
            : EdgePointFor(b.Model, cb, ca);
        var v = p2 - p1;

        bool visible = v.Length > 2;
        var vis = visible ? Visibility.Visible : Visibility.Collapsed;
        cv.Body.Visibility = vis;
        cv.Arrow.Visibility = vis;
        cv.Hit.Visibility = vis;
        if (!visible) return;

        v.Normalize();
        var perp = new Vector(-v.Y, v.X);
        var tail = p2 - v * 10;
        cv.Body.X1 = p1.X; cv.Body.Y1 = p1.Y;
        cv.Body.X2 = tail.X + v.X * 2; cv.Body.Y2 = tail.Y + v.Y * 2;
        cv.Hit.X1 = p1.X; cv.Hit.Y1 = p1.Y;
        cv.Hit.X2 = p2.X; cv.Hit.Y2 = p2.Y;
        cv.Arrow.Points = new PointCollection { p2, tail + perp * 5, tail - perp * 5 };
    }

    // Unit-space outlines for the polygon shapes, mirroring MakeShapeElement.
    static readonly Dictionary<string, Point[]> ShapeOutlines = new()
    {
        ["Diamond"] = new[] { new Point(0.5, 0), new Point(1, 0.5), new Point(0.5, 1), new Point(0, 0.5) },
        ["Hexagon"] = new[] { new Point(0.25, 0), new Point(0.75, 0), new Point(1, 0.5), new Point(0.75, 1), new Point(0.25, 1), new Point(0, 0.5) },
        ["Parallelogram"] = new[] { new Point(0.2, 0), new Point(1, 0), new Point(0.8, 1), new Point(0, 1) },
        ["Triangle"] = new[] { new Point(0.5, 0), new Point(1, 1), new Point(0, 1) },
        ["Trapezoid"] = new[] { new Point(0.22, 0), new Point(0.78, 0), new Point(1, 1), new Point(0, 1) },
        ["Octagon"] = new[] { new Point(0.3, 0), new Point(0.7, 0), new Point(1, 0.3), new Point(1, 0.7), new Point(0.7, 1), new Point(0.3, 1), new Point(0, 0.7), new Point(0, 0.3) },
    };

    // Arrows and connector dots land on the actual shape outline for every
    // curved/polygonal shape, and on the bounding box for rectangles and pills.
    // Rotation is handled by working in the node's local (unrotated) space and
    // rotating the result back.
    static Point EdgePointFor(NodeModel m, Point from, Point to)
    {
        var c = CenterOf(m);
        var toLocal = RotatePt(to, c, -m.Rotation);
        var p = EdgePointLocal(m, from, toLocal);
        return RotatePt(p, c, m.Rotation);
    }

    static Point EdgePointLocal(NodeModel m, Point from, Point to)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return from;

        if (m.Shape == "Ellipse")
        {
            double hw = m.W / 2, hh = m.H / 2;
            double te = 1 / Math.Sqrt(dx * dx / (hw * hw) + dy * dy / (hh * hh));
            return new Point(from.X + dx * te, from.Y + dy * te);
        }

        if (ShapeOutlines.TryGetValue(m.Shape, out var unit))
        {
            // Ray from the center against each polygon edge.
            double best = double.PositiveInfinity;
            for (int i = 0; i < unit.Length; i++)
            {
                var p1 = new Point(m.X + unit[i].X * m.W, m.Y + unit[i].Y * m.H);
                var u2 = unit[(i + 1) % unit.Length];
                var p2 = new Point(m.X + u2.X * m.W, m.Y + u2.Y * m.H);
                double ex = p2.X - p1.X, ey = p2.Y - p1.Y;
                double denom = dx * ey - dy * ex;
                if (Math.Abs(denom) < 1e-9) continue;
                double t = ((p1.X - from.X) * ey - (p1.Y - from.Y) * ex) / denom;
                double s = ((p1.X - from.X) * dy - (p1.Y - from.Y) * dx) / denom;
                if (t > 0 && s >= -0.001 && s <= 1.001 && t < best) best = t;
            }
            if (!double.IsPositiveInfinity(best))
                return new Point(from.X + dx * best, from.Y + dy * best);
        }

        return EdgeIntersect(new Rect(m.X, m.Y, m.W, m.H), from, to);
    }

    static Point EdgeIntersect(Rect r, Point from, Point to)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return from;
        double t = double.PositiveInfinity;
        if (Math.Abs(dx) > 1e-9) t = Math.Min(t, ((dx > 0 ? r.Right : r.Left) - from.X) / dx);
        if (Math.Abs(dy) > 1e-9) t = Math.Min(t, ((dy > 0 ? r.Bottom : r.Top) - from.Y) / dy);
        t = Math.Max(0, t);
        return new Point(from.X + dx * t, from.Y + dy * t);
    }

    // ---------- Zones & legacy painted cells ----------

    void PaintToggle_Changed(object sender, RoutedEventArgs e) => UpdateCursor();

    static (int, int) CellAt(Point p) =>
        ((int)Math.Floor(p.X / GridSize), (int)Math.Floor(p.Y / GridSize));

    // Legacy support: boards saved before zones existed contain painted cells.
    void SetCell((int, int) key, CellData? data)
    {
        if (data == null)
        {
            if (!_cellColors.Remove(key)) return;
            if (_cellRects.TryGetValue(key, out var old))
            {
                World.Children.Remove(old);
                _cellRects.Remove(key);
            }
            MarkDirty();
            return;
        }
        _cellColors[key] = data.Value;
        if (!_cellRects.TryGetValue(key, out var r))
        {
            r = new Rectangle
            {
                Width = GridSize, Height = GridSize,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(r, key.Item1 * GridSize);
            Canvas.SetTop(r, key.Item2 * GridSize);
            Panel.SetZIndex(r, 1);
            World.Children.Add(r);
            _cellRects[key] = r;
        }
        r.Fill = BrushFrom(data.Value.Hex);
        r.Opacity = data.Value.Op;
        MarkDirty();
    }

    void StartZoneDraw(Point at)
    {
        CommitEdit();
        ClearSelection();
        _areaPainting = true;
        _areaStart = at;
        _areaRect = new Rectangle
        {
            Width = 0, Height = 0,
            Opacity = Math.Max(0.15, PaintOpacity.Value),
            Fill = BrushFrom(_lastColor),
            Stroke = AccentBrush,
            StrokeThickness = Math.Max(0.5, 1.5 / _zoom),
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false
        };
        Canvas.SetLeft(_areaRect, at.X);
        Canvas.SetTop(_areaRect, at.Y);
        Panel.SetZIndex(_areaRect, 99999);
        World.Children.Add(_areaRect);
        World.CaptureMouse();
    }

    void FinishZoneDraw()
    {
        _areaPainting = false;
        World.ReleaseMouseCapture();
        if (_areaRect == null) return;
        var r = new Rect(Canvas.GetLeft(_areaRect), Canvas.GetTop(_areaRect), _areaRect.Width, _areaRect.Height);
        World.Children.Remove(_areaRect);
        _areaRect = null;
        if (r.Width < 8 || r.Height < 8) return;

        // Zones always snap to whole grid cells.
        double x1 = Math.Floor(r.X / GridSize) * GridSize;
        double y1 = Math.Floor(r.Y / GridSize) * GridSize;
        double x2 = Math.Ceiling(r.Right / GridSize) * GridSize;
        double y2 = Math.Ceiling(r.Bottom / GridSize) * GridSize;

        var m = new NodeModel
        {
            Kind = "Zone",
            Shape = "Rect",
            Color = _lastColor,
            Opacity = PaintOpacity.Value,
            X = x1, Y = y1,
            W = Math.Max(GridSize * 2, x2 - x1),
            H = Math.Max(GridSize * 2, y2 - y1)
        };
        var nv = CreateNodeVisual(m);
        SelectOnly(nv);
        MarkDirty();
    }

    void PaintOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingOpacity || _selected == null || _selected.Count == 0) return;
        bool any = false;
        foreach (var id in _selected)
        {
            if (!_nodes.TryGetValue(id, out var nv)) continue;
            nv.Model.Opacity = PaintOpacity.Value;
            nv.Content.Opacity = nv.Model.Opacity;
            any = true;
        }
        if (any) MarkDirty();
    }

    // ---------- Canvas interaction ----------

    void World_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_spaceDown || _panning || _linking) return;
        if (e.OriginalSource != World) return;
        CommitEdit();

        if (PaintToggle.IsChecked == true)
        {
            StartZoneDraw(e.GetPosition(World));
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            CreateNoteAt(e.GetPosition(World));
            e.Handled = true;
            return;
        }

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        if (alt)
        {
            // Alt+drag draws a new shape at the dragged size.
            ClearSelection();
            _drawingNew = true;
            _drawStart = e.GetPosition(World);
            _drawRect = new Rectangle
            {
                Width = 0, Height = 0,
                Stroke = AccentBrush,
                StrokeThickness = Math.Max(0.5, 1.5 / _zoom),
                Fill = new SolidColorBrush(Color.FromArgb(0x18, 0x4C, 0x6E, 0xF5)),
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
                RadiusX = 6, RadiusY = 6
            };
            Canvas.SetLeft(_drawRect, _drawStart.X);
            Canvas.SetTop(_drawRect, _drawStart.Y);
            Panel.SetZIndex(_drawRect, 99999);
            World.Children.Add(_drawRect);
            World.CaptureMouse();
        }
        else if (shift || ctrl)
        {
            // Box select (Ctrl keeps the existing selection).
            if (!ctrl) ClearSelection();
            _rubberBanding = true;
            _rubberStart = e.GetPosition(World);
            _rubberRect = new Rectangle
            {
                Width = 0, Height = 0,
                Stroke = AccentBrush,
                StrokeThickness = Math.Max(0.5, 1 / _zoom),
                Fill = new SolidColorBrush(Color.FromArgb(0x20, 0x4C, 0x6E, 0xF5)),
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_rubberRect, _rubberStart.X);
            Canvas.SetTop(_rubberRect, _rubberStart.Y);
            Panel.SetZIndex(_rubberRect, 99999);
            World.Children.Add(_rubberRect);
            World.CaptureMouse();
        }
        else
        {
            // Plain drag on empty canvas pans the board.
            ClearSelection();
            _panning = true;
            _panMouseStart = e.GetPosition(Viewport);
            _panXStart = Pan.X;
            _panYStart = Pan.Y;
            Viewport.CaptureMouse();
            UpdateCursor();
        }
        e.Handled = true;
    }

    void World_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(World);
        _lastWorldMouse = p;

        if (_areaPainting && _areaRect != null)
        {
            Canvas.SetLeft(_areaRect, Math.Min(p.X, _areaStart.X));
            Canvas.SetTop(_areaRect, Math.Min(p.Y, _areaStart.Y));
            _areaRect.Width = Math.Abs(p.X - _areaStart.X);
            _areaRect.Height = Math.Abs(p.Y - _areaStart.Y);
            return;
        }

        if (_rubberBanding && _rubberRect != null)
        {
            Canvas.SetLeft(_rubberRect, Math.Min(p.X, _rubberStart.X));
            Canvas.SetTop(_rubberRect, Math.Min(p.Y, _rubberStart.Y));
            _rubberRect.Width = Math.Abs(p.X - _rubberStart.X);
            _rubberRect.Height = Math.Abs(p.Y - _rubberStart.Y);
        }

        if (_drawingNew && _drawRect != null)
        {
            Canvas.SetLeft(_drawRect, Math.Min(p.X, _drawStart.X));
            Canvas.SetTop(_drawRect, Math.Min(p.Y, _drawStart.Y));
            _drawRect.Width = Math.Abs(p.X - _drawStart.X);
            _drawRect.Height = Math.Abs(p.Y - _drawStart.Y);
        }
    }

    void World_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_areaPainting)
        {
            FinishZoneDraw();
            e.Handled = true;
            return;
        }

        if (_drawingNew)
        {
            _drawingNew = false;
            World.ReleaseMouseCapture();
            if (_drawRect == null) return;

            var dr = new Rect(Canvas.GetLeft(_drawRect), Canvas.GetTop(_drawRect),
                _drawRect.Width, _drawRect.Height);
            World.Children.Remove(_drawRect);
            _drawRect = null;

            if (dr.Width >= 30 && dr.Height >= 24)
            {
                var m = new NodeModel
                {
                    X = dr.X, Y = dr.Y,
                    W = Math.Max(NodeMinW, dr.Width),
                    H = Math.Max(NodeMinH, dr.Height),
                    Color = _lastColor,
                    Shape = _lastShape
                };
                if (SnapCheck.IsChecked == true)
                {
                    m.X = Snap(m.X); m.Y = Snap(m.Y);
                    m.W = Math.Max(NodeMinW, Snap(m.W));
                    m.H = Math.Max(NodeMinH, Snap(m.H));
                }
                var nv = CreateNodeVisual(m);
                SelectOnly(nv);
                MarkDirty();
            }
            e.Handled = true;
            return;
        }

        if (!_rubberBanding) return;
        _rubberBanding = false;
        World.ReleaseMouseCapture();
        if (_rubberRect == null) return;

        var r = new Rect(Canvas.GetLeft(_rubberRect), Canvas.GetTop(_rubberRect),
            _rubberRect.Width, _rubberRect.Height);
        World.Children.Remove(_rubberRect);
        _rubberRect = null;

        if (r.Width > 2 || r.Height > 2)
        {
            ClearConnSelection();
            foreach (var nv in _nodes.Values)
                if (r.IntersectsWith(NodeBounds(nv.Model)))
                {
                    _selected.Add(nv.Model.Id);
                    RefreshNodeChrome(nv);
                }
        }
        e.Handled = true;
    }

    // ---------- Pan & zoom ----------

    void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Keep the same world point centered when the window is resized.
        if (e.PreviousSize.Width < 1 || e.PreviousSize.Height < 1) return;
        Pan.X += (e.NewSize.Width - e.PreviousSize.Width) / 2;
        Pan.Y += (e.NewSize.Height - e.PreviousSize.Height) / 2;
    }

    void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetZoom(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), e.GetPosition(Viewport));
        e.Handled = true;
    }

    void Viewport_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle ||
            (_spaceDown && e.ChangedButton == MouseButton.Left))
        {
            CommitEdit();
            _panning = true;
            _panMouseStart = e.GetPosition(Viewport);
            _panXStart = Pan.X;
            _panYStart = Pan.Y;
            Viewport.CaptureMouse();
            UpdateCursor();
            e.Handled = true;
        }
    }

    void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var p = e.GetPosition(Viewport);
        Pan.X = _panXStart + (p.X - _panMouseStart.X);
        Pan.Y = _panYStart + (p.Y - _panMouseStart.Y);
    }

    void Viewport_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning &&
            (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left))
        {
            _panning = false;
            if (Viewport.IsMouseCaptured) Viewport.ReleaseMouseCapture();
            UpdateCursor();
            e.Handled = true;
        }
    }

    void SetZoom(double z, Point viewportAnchor)
    {
        z = Math.Clamp(z, MinZoom, MaxZoom);
        double wx = (viewportAnchor.X - Pan.X) / _zoom;
        double wy = (viewportAnchor.Y - Pan.Y) / _zoom;
        _zoom = z;
        Scale.ScaleX = Scale.ScaleY = z;
        Pan.X = viewportAnchor.X - wx * z;
        Pan.Y = viewportAnchor.Y - wy * z;
        UpdateZoomLabel();
        UpdateHandleScale();
    }

    void ZoomAtCenter(double factor) =>
        SetZoom(_zoom * factor, new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2));

    void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomAtCenter(1.25);
    void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomAtCenter(0.8);

    void Zoom100_Click(object sender, RoutedEventArgs e) =>
        SetZoom(1.0, new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2));

    void Fit_Click(object sender, RoutedEventArgs e) => ZoomToFit();

    void ZoomToFit()
    {
        var b = ContentBounds(80);
        if (b.IsEmpty)
        {
            ResetView();
            return;
        }
        double z = Math.Min(Viewport.ActualWidth / b.Width, Viewport.ActualHeight / b.Height);
        z = Math.Clamp(z, MinZoom, 1.5);
        _zoom = z;
        Scale.ScaleX = Scale.ScaleY = z;
        Pan.X = Viewport.ActualWidth / 2 - (b.X + b.Width / 2) * z;
        Pan.Y = Viewport.ActualHeight / 2 - (b.Y + b.Height / 2) * z;
        UpdateZoomLabel();
        UpdateHandleScale();
    }

    void ResetView()
    {
        _zoom = 1;
        Scale.ScaleX = Scale.ScaleY = 1;
        CenterOnWorld(WorldSize / 2, WorldSize / 2);
        UpdateZoomLabel();
        UpdateHandleScale();
    }

    void CenterOnWorld(double wx, double wy)
    {
        Pan.X = Viewport.ActualWidth / 2 - wx * _zoom;
        Pan.Y = Viewport.ActualHeight / 2 - wy * _zoom;
    }

    void UpdateZoomLabel() => ZoomLabel.Text = $"{Math.Round(_zoom * 100)}%";

    Rect ContentBounds(double margin)
    {
        var b = Rect.Empty;
        foreach (var nv in _nodes.Values)
            b.Union(NodeBounds(nv.Model));
        foreach (var key in _cellColors.Keys)
            b.Union(new Rect(key.Item1 * GridSize, key.Item2 * GridSize, GridSize, GridSize));
        if (b.IsEmpty) return b;
        b.Inflate(margin, margin);
        return b;
    }

    void UpdateCursor()
    {
        Viewport.Cursor = _spaceDown || _panning
            ? Cursors.Hand
            : (PaintToggle.IsChecked == true ? Cursors.Pen : Cursors.Arrow);
    }

    // ---------- Keyboard ----------

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            if (Keyboard.FocusedElement is TextBox) return;
            if (!_spaceDown) { _spaceDown = true; UpdateCursor(); }
            e.Handled = true;
            return;
        }
        if (Keyboard.FocusedElement is TextBox) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.N: New_Click(null, null); e.Handled = true; break;
                case Key.O: Open_Click(null, null); e.Handled = true; break;
                case Key.S: if (shift) SaveAs(); else Save(); e.Handled = true; break;
                case Key.A: SelectAllNodes(); e.Handled = true; break;
                case Key.D: DuplicateSelected(); e.Handled = true; break;
                case Key.C: CopySelected(); e.Handled = true; break;
                case Key.X: CutSelected(); e.Handled = true; break;
                case Key.V: Paste(); e.Handled = true; break;
                case Key.OemPlus:
                case Key.Add: ZoomAtCenter(1.25); e.Handled = true; break;
                case Key.OemMinus:
                case Key.Subtract: ZoomAtCenter(0.8); e.Handled = true; break;
                case Key.D0:
                case Key.NumPad0: Zoom100_Click(null, null); e.Handled = true; break;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.Delete:
            case Key.Back:
                DeleteSelected(); e.Handled = true; break;
            case Key.Escape:
                if (PaintToggle.IsChecked == true) PaintToggle.IsChecked = false;
                else ClearSelection();
                e.Handled = true; break;
            case Key.P:
                PaintToggle.IsChecked = PaintToggle.IsChecked != true;
                e.Handled = true; break;
            case Key.Left: Nudge(-(shift ? 1 : GridSize), 0); e.Handled = true; break;
            case Key.Right: Nudge(shift ? 1 : GridSize, 0); e.Handled = true; break;
            case Key.Up: Nudge(0, -(shift ? 1 : GridSize)); e.Handled = true; break;
            case Key.Down: Nudge(0, shift ? 1 : GridSize); e.Handled = true; break;
        }
    }

    void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _spaceDown)
        {
            _spaceDown = false;
            UpdateCursor();
            e.Handled = true;
        }
    }

    // ---------- Export ----------

    void ExportBtn_Click(object sender, RoutedEventArgs e) =>
        ExportPopup.IsOpen = !ExportPopup.IsOpen;

    void ExportFormat_Click(object sender, RoutedEventArgs e)
    {
        ExportPopup.IsOpen = false;
        ExportBoard((string)((FrameworkElement)sender).Tag);
    }

    void ExportBoard(string format)
    {
        CommitEdit();
        ClearSelection();
        var b = ContentBounds(48);
        if (b.IsEmpty)
        {
            ModernDialog.Show(this, "Export", "Nothing to export yet - add some shapes first.", "OK");
            return;
        }

        var (label, pattern) = format switch
        {
            "jpg" => ("JPEG image", "*.jpg"),
            "pdf" => ("PDF document", "*.pdf"),
            "bmp" => ("BMP image", "*.bmp"),
            "tif" => ("TIFF image", "*.tif"),
            _ => ("PNG image", "*.png")
        };
        var dlg = new SaveFileDialog
        {
            Filter = $"{label} ({pattern})|{pattern}",
            DefaultExt = "." + format,
            FileName = "mindmap." + format
        };
        if (dlg.ShowDialog(this) != true) return;

        // Keep very large boards within a sane bitmap size.
        double renderScale = Math.Min(1.0, 6000.0 / Math.Max(b.Width, b.Height));
        int w = Math.Max(1, (int)Math.Ceiling(b.Width * renderScale));
        int h = Math.Max(1, (int)Math.Ceiling(b.Height * renderScale));

        double oz = _zoom, ox = Pan.X, oy = Pan.Y;
        try
        {
            Scale.ScaleX = Scale.ScaleY = 1;
            Pan.X = 0; Pan.Y = 0;
            World.UpdateLayout();

            var brush = new VisualBrush(World)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = b,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, w, h),
                Stretch = Stretch.Fill
            };
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Opaque background so JPEG/PDF don't render transparency as black.
                dc.DrawRectangle(new SolidColorBrush(ThemeManager.Current.CanvasBg), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var frame = BitmapFrame.Create(rtb);

            string ext = IOPath.GetExtension(dlg.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) ext = "." + format;
            if (ext == ".pdf")
            {
                var jpeg = new JpegBitmapEncoder { QualityLevel = 92 };
                jpeg.Frames.Add(frame);
                using var ms = new MemoryStream();
                jpeg.Save(ms);
                PdfWriter.WriteImagePdf(dlg.FileName, ms.ToArray(), w, h);
            }
            else
            {
                BitmapEncoder enc = ext switch
                {
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
                    ".bmp" => new BmpBitmapEncoder(),
                    ".tif" or ".tiff" => new TiffBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };
                enc.Frames.Add(frame);
                using var fs = File.Create(dlg.FileName);
                enc.Save(fs);
            }
        }
        catch (Exception ex)
        {
            ModernDialog.Show(this, "Could not export", ex.Message, "OK");
        }
        finally
        {
            Scale.ScaleX = Scale.ScaleY = oz;
            Pan.X = ox;
            Pan.Y = oy;
        }
    }
}
