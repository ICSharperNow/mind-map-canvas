using System.ComponentModel;
using System.IO;
using System.Text.Json;
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

    public NodeModel Clone(bool keepId = false) => new()
    {
        Id = keepId ? Id : Guid.NewGuid(),
        X = X, Y = Y, W = W, H = H,
        Text = Text, Color = Color, Shape = Shape,
        FontSize = FontSize, TextColor = TextColor, Align = Align,
        Bold = Bold, Italic = Italic
    };
}

public class ConnectionModel
{
    public Guid From { get; set; }
    public Guid To { get; set; }
}

public class DocumentModel
{
    public int Version { get; set; } = 1;
    public List<NodeModel> Nodes { get; set; } = new();
    public List<ConnectionModel> Connections { get; set; } = new();
}

// ---------- Runtime visuals ----------

public class NodeVisual
{
    public NodeModel Model;
    public Grid Root;
    public Shape ShapeEl;
    public TextBlock Label;
    public TextBox Editor;
    public Thumb Grip;
    public List<Ellipse> Handles = new();
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
        ("Ellipse", "⬭", "Ellipse"),
        ("Diamond", "◇", "Diamond"),
        ("Hexagon", "⬡", "Hexagon"),
        ("Parallelogram", "▱", "Parallelogram"),
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
    NodeVisual _linkHover;
    Line _linkPreview;

    readonly List<NodeModel> _clipboardNodes = new();
    readonly List<ConnectionModel> _clipboardConns = new();

    string _currentFile;
    bool _dirty;
    double _zoom = 1.0;
    int _zTop = 10;
    string _lastColor = "#FFF9B1";
    string _lastShape = "Rect";

    public MainWindow()
    {
        InitializeComponent();
    }

    // ---------- Startup / document lifecycle ----------

    void Window_Loaded(object sender, RoutedEventArgs e)
    {
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
        CurrentColorSwatch.Background = BrushFrom(_lastColor);

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
            TextColorWrap.Children.Add(sw);
        }

        ResetView();

        var m = new NodeModel
        {
            X = Snap(WorldSize / 2 - 84),
            Y = Snap(WorldSize / 2 - 48),
            Text = "Double-click the canvas to add your first idea"
        };
        CreateNodeVisual(m);
        _dirty = false;
        UpdateTitle();
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
        var dlg = new OpenFileDialog { Filter = "Mind map (*.json)|*.json|All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        LoadFile(dlg.FileName);
    }

    void Save_Click(object sender, RoutedEventArgs e) => Save();

    void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();

    void Exit_Click(object sender, RoutedEventArgs e) => Close();

    void Duplicate_Click(object sender, RoutedEventArgs e) => DuplicateSelected();

    void SelectAll_Click(object sender, RoutedEventArgs e) => SelectAllNodes();

    void Settings_Click(object sender, RoutedEventArgs e) =>
        new SettingsWindow { Owner = this }.ShowDialog();

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
            Filter = "Mind map (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = string.IsNullOrEmpty(_currentFile) ? "mindmap.json" : IOPath.GetFileName(_currentFile)
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
            var doc = new DocumentModel
            {
                Nodes = _nodes.Values.Select(n => n.Model).ToList(),
                Connections = _conns.Select(c => c.Model).ToList()
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
        foreach (var c in doc.Connections) AddConnection(c.From, c.To);
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
    }

    void MarkDirty()
    {
        if (!_dirty) { _dirty = true; UpdateTitle(); }
    }

    void UpdateTitle()
    {
        var name = string.IsNullOrEmpty(_currentFile) ? "untitled" : IOPath.GetFileName(_currentFile);
        Title = $"MindMap Canvas — {name}{(_dirty ? " *" : "")}";
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
        // Classic diagonal-lines resize glyph instead of a plain square.
        var path = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        path.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M11,3 L3,11 M11,7 L7,11"));
        path.SetValue(Shape.StrokeProperty, new SolidColorBrush(Color.FromArgb(0x8C, 0x00, 0x00, 0x00)));
        path.SetValue(Shape.StrokeThicknessProperty, 1.6);
        path.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
        path.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);

        var root = new FrameworkElementFactory(typeof(Border));
        root.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        root.AppendChild(path);
        return new ControlTemplate(typeof(Thumb)) { VisualTree = root };
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
            _ => new Rectangle { RadiusX = 8, RadiusY = 8 },
        };
        s.StrokeThickness = 1;
        s.Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Direction = 270, Opacity = 0.18 };
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
        RefreshNodeChrome(nv);
    }

    NodeVisual CreateNodeVisual(NodeModel m)
    {
        var label = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Margin = new Thickness(12),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
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
            Width = 15, Height = 15,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 2, 2),
            Cursor = Cursors.SizeNWSE,
            Visibility = Visibility.Collapsed,
            Template = CreateGripTemplate()
        };

        var shape = MakeShapeElement(m.Shape);
        shape.Fill = BrushFrom(m.Color);
        shape.Stroke = SoftBorderBrush;

        var root = new Grid { Width = m.W, Height = m.H, Background = Brushes.Transparent };
        root.Children.Add(shape);
        root.Children.Add(label);
        root.Children.Add(editor);
        root.Children.Add(grip);

        Canvas.SetLeft(root, m.X);
        Canvas.SetTop(root, m.Y);
        Panel.SetZIndex(root, ++_zTop);

        var nv = new NodeVisual { Model = m, Root = root, ShapeEl = shape, Label = label, Editor = editor, Grip = grip };

        // Side connector handles (Mural/Miro style): drag one onto another shape to link.
        foreach (Side side in Enum.GetValues<Side>())
        {
            var handle = MakeHandle(nv, side);
            nv.Handles.Add(handle);
            root.Children.Add(handle);
        }

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
        var miEdit = new MenuItem { Header = "Edit text" };
        miEdit.Click += (s, e) => BeginEdit(nv);
        var miDup = new MenuItem { Header = "Duplicate", InputGestureText = "Ctrl+D" };
        miDup.Click += (s, e) => { if (!_selected.Contains(nv.Model.Id)) SelectOnly(nv); DuplicateSelected(); };
        var miCopy = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C" };
        miCopy.Click += (s, e) => { if (!_selected.Contains(nv.Model.Id)) SelectOnly(nv); CopySelected(); };
        var miCut = new MenuItem { Header = "Cut", InputGestureText = "Ctrl+X" };
        miCut.Click += (s, e) => { if (!_selected.Contains(nv.Model.Id)) SelectOnly(nv); CutSelected(); };
        var miDel = new MenuItem { Header = "Delete", InputGestureText = "Del" };
        miDel.Click += (s, e) => { if (!_selected.Contains(nv.Model.Id)) SelectOnly(nv); DeleteSelected(); };
        menu.Items.Add(miEdit);
        menu.Items.Add(miDup);
        menu.Items.Add(miCopy);
        menu.Items.Add(miCut);
        menu.Items.Add(new Separator());
        menu.Items.Add(miDel);
        root.ContextMenu = menu;
        root.ContextMenuOpening += (s, e) => { if (!_selected.Contains(nv.Model.Id)) SelectOnly(nv); };

        ApplyTextStyle(nv);
        World.Children.Add(root);
        _nodes[m.Id] = nv;
        return nv;
    }

    void CreateNoteAt(Point worldCenter)
    {
        CommitEdit();
        var m = new NodeModel
        {
            X = worldCenter.X - 84,
            Y = worldCenter.Y - 48,
            Color = _lastColor,
            Shape = _lastShape
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
            nv.Label.Text = "Double-click to edit";
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
        FontSizeLabel.Text = _selected.Count > 0
            ? Math.Round(_nodes[_selected.First()].Model.FontSize).ToString()
            : "—";
    }

    void ApplyTextFormat(Action<NodeModel> change)
    {
        if (_selected.Count == 0) return;
        CommitEdit();
        foreach (var id in _selected)
        {
            change(_nodes[id].Model);
            ApplyTextStyle(_nodes[id]);
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
            ApplyColor($"#{dlg.SelectedColor.R:X2}{dlg.SelectedColor.G:X2}{dlg.SelectedColor.B:X2}");
    }

    void ApplyColor(string hex)
    {
        _lastColor = hex;
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
        nv.ShapeEl.StrokeThickness = highlighted ? 2 : 1;
        nv.Grip.Visibility = _selected.Contains(nv.Model.Id) ? Visibility.Visible : Visibility.Collapsed;
    }

    void SelectOnly(NodeVisual nv)
    {
        ClearSelection();
        _selected.Add(nv.Model.Id);
        RefreshNodeChrome(nv);
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
        _selectedConn.Body.Stroke = ConnBrush;
        _selectedConn.Arrow.Fill = ConnBrush;
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
            BeginEdit(nv);
            e.Handled = true;
            return;
        }

        Panel.SetZIndex(nv.Root, ++_zTop);
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

        bool snap = SnapCheck.IsChecked == true;
        foreach (var kv in _dragOrigins)
        {
            if (!_nodes.TryGetValue(kv.Key, out var n)) continue;
            double nx = kv.Value.X + dx, ny = kv.Value.Y + dy;
            if (snap) { nx = Snap(nx); ny = Snap(ny); }
            n.Model.X = nx;
            n.Model.Y = ny;
            Canvas.SetLeft(n.Root, nx);
            Canvas.SetTop(n.Root, ny);
            UpdateConnectionsFor(kv.Key);
        }
        MarkDirty();
    }

    void Node_Up(NodeVisual nv, MouseButtonEventArgs e)
    {
        if (!_draggingNodes) return;
        _draggingNodes = false;
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
        nv.Root.Width = m.W;
        nv.Root.Height = m.H;
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
            AddConnection(map[c.Model.From], map[c.Model.To]);
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
                _clipboardConns.Add(new ConnectionModel { From = c.Model.From, To = c.Model.To });
    }

    void CutSelected()
    {
        CopySelected();
        DeleteSelected();
    }

    void Paste()
    {
        if (_clipboardNodes.Count == 0) return;
        var b = Rect.Empty;
        foreach (var n in _clipboardNodes) b.Union(new Rect(n.X, n.Y, n.W, n.H));

        // Paste under the cursor when it's over the board, otherwise offset from the source.
        Point target = Viewport.IsMouseOver
            ? _lastWorldMouse
            : new Point(b.X + b.Width / 2 + GridSize, b.Y + b.Height / 2 + GridSize);
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
            AddConnection(map[c.From], map[c.To]);
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
        _nodes.Remove(id);
        if (_editing == nv) _editing = null;
        if (_linkSource == nv || _linkHover == nv) CancelLink();
    }

    // ---------- Connector handles & linking ----------

    Ellipse MakeHandle(NodeVisual nv, Side side)
    {
        var (ha, va, margin) = side switch
        {
            Side.Left => (HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(-7, 0, 0, 0)),
            Side.Right => (HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(0, 0, -7, 0)),
            Side.Top => (HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0, -7, 0, 0)),
            Side.Bottom => (HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, -7)),
            Side.TopLeft => (HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(-7, -7, 0, 0)),
            Side.TopRight => (HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, -7, -7, 0)),
            Side.BottomLeft => (HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(-7, 0, 0, -7)),
            _ => (HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, -7, -7)),
        };
        var el = new Ellipse
        {
            Width = 12, Height = 12,
            Fill = AccentBrush,
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            HorizontalAlignment = ha,
            VerticalAlignment = va,
            Margin = margin,
            Cursor = Cursors.Cross,
            Visibility = Visibility.Collapsed,
            ToolTip = "Drag onto another shape to connect"
        };
        el.MouseLeftButtonDown += (s, e) => { StartLink(nv, side, el); e.Handled = true; };
        el.MouseMove += (s, e) => Link_Move(el, e);
        el.MouseLeftButtonUp += (s, e) => Link_Up(el, e);
        return el;
    }

    void ShowHandles(NodeVisual nv, bool show)
    {
        var vis = show ? Visibility.Visible : Visibility.Collapsed;
        foreach (var h in nv.Handles) h.Visibility = vis;
    }

    static Point SideAnchor(NodeVisual nv, Side side)
    {
        var m = nv.Model;
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

    void StartLink(NodeVisual nv, Side side, Ellipse handle)
    {
        CommitEdit();
        _linking = true;
        _linkSource = nv;
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
        _linkPreview.X2 = p.X;
        _linkPreview.Y2 = p.Y;

        var target = HitNode(p, _linkSource);
        if (target != _linkHover)
        {
            var old = _linkHover;
            _linkHover = target;
            if (old != null) RefreshNodeChrome(old);
            if (target != null) RefreshNodeChrome(target);
        }
    }

    void Link_Up(Ellipse handle, MouseButtonEventArgs e)
    {
        if (!_linking) return;
        handle.ReleaseMouseCapture();
        var p = e.GetPosition(World);
        var src = _linkSource;
        var target = HitNode(p, src);
        CancelLink();
        if (target != null && AddConnection(src.Model.Id, target.Model.Id) != null)
            MarkDirty();
        if (!src.Root.IsMouseOver) ShowHandles(src, false);
        e.Handled = true;
    }

    void CancelLink()
    {
        if (_linkPreview != null)
        {
            World.Children.Remove(_linkPreview);
            _linkPreview = null;
        }
        _linking = false;
        _linkSource = null;
        var hover = _linkHover;
        _linkHover = null;
        if (hover != null) RefreshNodeChrome(hover);
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

    ConnectionVisual AddConnection(Guid from, Guid to)
    {
        if (from == to) return null;
        if (!_nodes.ContainsKey(from) || !_nodes.ContainsKey(to)) return null;
        if (_conns.Any(c => c.Model.From == from && c.Model.To == to)) return null;

        var cv = new ConnectionVisual { Model = new ConnectionModel { From = from, To = to } };
        cv.Body = new Line
        {
            Stroke = ConnBrush, StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        cv.Arrow = new Polygon { Fill = ConnBrush, IsHitTestVisible = false };
        cv.Hit = new Line { Stroke = Brushes.Transparent, StrokeThickness = 14, Cursor = Cursors.Hand };
        Panel.SetZIndex(cv.Body, 2);
        Panel.SetZIndex(cv.Arrow, 2);
        Panel.SetZIndex(cv.Hit, 3);
        cv.Hit.MouseLeftButtonDown += (s, e) =>
        {
            CommitEdit();
            SelectConnection(cv);
            e.Handled = true;
        };
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
        var p1 = EdgePointFor(a.Model, ca, cb);
        var p2 = EdgePointFor(b.Model, cb, ca);
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

    // Arrows land on the actual shape outline for ellipses and diamonds,
    // and on the bounding box for the rest.
    static Point EdgePointFor(NodeModel m, Point from, Point to)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return from;
        double hw = m.W / 2, hh = m.H / 2;
        double t;
        switch (m.Shape)
        {
            case "Ellipse":
                t = 1 / Math.Sqrt(dx * dx / (hw * hw) + dy * dy / (hh * hh));
                break;
            case "Diamond":
                t = 1 / (Math.Abs(dx) / hw + Math.Abs(dy) / hh);
                break;
            default:
                return EdgeIntersect(new Rect(m.X, m.Y, m.W, m.H), from, to);
        }
        return new Point(from.X + dx * t, from.Y + dy * t);
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

    // ---------- Canvas interaction ----------

    void World_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_spaceDown || _panning || _linking) return;
        if (e.OriginalSource != World) return;
        CommitEdit();

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
                if (r.IntersectsWith(new Rect(nv.Model.X, nv.Model.Y, nv.Model.W, nv.Model.H)))
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
    }

    void ResetView()
    {
        _zoom = 1;
        Scale.ScaleX = Scale.ScaleY = 1;
        CenterOnWorld(WorldSize / 2, WorldSize / 2);
        UpdateZoomLabel();
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
            b.Union(new Rect(nv.Model.X, nv.Model.Y, nv.Model.W, nv.Model.H));
        if (b.IsEmpty) return b;
        b.Inflate(margin, margin);
        return b;
    }

    void UpdateCursor()
    {
        Viewport.Cursor = _spaceDown || _panning ? Cursors.Hand : Cursors.Arrow;
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
                ClearSelection();
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

    void Export_Click(object sender, RoutedEventArgs e)
    {
        CommitEdit();
        ClearSelection();
        var b = ContentBounds(48);
        if (b.IsEmpty)
        {
            ModernDialog.Show(this, "Export", "Nothing to export yet — add some shapes first.", "OK");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            FileName = "mindmap.png"
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
                dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(dlg.FileName);
            enc.Save(fs);
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
