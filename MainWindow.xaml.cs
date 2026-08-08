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
    public Border Border;
    public TextBlock Label;
    public TextBox Editor;
    public Thumb Grip;
}

public class ConnectionVisual
{
    public ConnectionModel Model;
    public Line Body;
    public Line Hit;
    public Polygon Arrow;
}

public partial class MainWindow : Window
{
    const double GridSize = 24.0;
    const double WorldSize = 40000.0;
    const double MinZoom = 0.1, MaxZoom = 4.0;
    const double NodeMinW = 80, NodeMinH = 48;

    static readonly string[] Palette =
        { "#FFF9B1", "#FFCF7D", "#F8A5C2", "#D7B8F3", "#A8D8F0", "#C5E8A5", "#E4E7EB", "#FFFFFF" };

    static readonly SolidColorBrush AccentBrush = new(Color.FromRgb(0x4C, 0x6E, 0xF5));
    static readonly SolidColorBrush SoftBorderBrush = new(Color.FromArgb(0x30, 0x00, 0x00, 0x00));
    static readonly SolidColorBrush ConnBrush = new(Color.FromRgb(0x88, 0x95, 0xA7));
    static readonly SolidColorBrush TextBrush = new(Color.FromRgb(0x2D, 0x33, 0x3A));
    static readonly SolidColorBrush PlaceholderBrush = new(Color.FromRgb(0x9A, 0xA2, 0xAC));

    readonly Dictionary<Guid, NodeVisual> _nodes = new();
    readonly List<ConnectionVisual> _conns = new();
    readonly HashSet<Guid> _selected = new();
    ConnectionVisual _selectedConn;

    bool _spaceDown, _panning, _draggingNodes, _rubberBanding, _movedDuringDrag;
    Point _panMouseStart, _dragStartWorld, _rubberStart;
    double _panXStart, _panYStart;
    readonly Dictionary<Guid, Point> _dragOrigins = new();
    Rectangle _rubberRect;

    NodeVisual _editing;
    NodeVisual _connectSource;
    Line _previewLine;

    string _currentFile;
    bool _dirty;
    double _zoom = 1.0;
    int _zTop = 10;
    string _lastColor = "#FFF9B1";

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
                Width = 20, Height = 20,
                CornerRadius = new CornerRadius(4),
                Background = BrushFrom(color),
                BorderBrush = SoftBorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(3, 0, 3, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Color selected notes"
            };
            sw.MouseLeftButtonDown += (s, a) => { ApplyColor(color); a.Handled = true; };
            SwatchPanel.Children.Add(sw);
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
            MessageBox.Show(this, "Could not save: " + ex.Message, "MindMap Canvas",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(this, "Could not open: " + ex.Message, "MindMap Canvas",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
        var r = MessageBox.Show(this, "Save changes to the current mind map?", "MindMap Canvas",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r switch
        {
            MessageBoxResult.Yes => Save(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    void ClearDocument()
    {
        ClearConnectSource();
        _editing = null;
        _selected.Clear();
        _selectedConn = null;
        _nodes.Clear();
        _conns.Clear();
        World.Children.Clear();
        _rubberRect = null;
        _rubberBanding = false;
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

    // ---------- Node creation ----------

    static double Snap(double v) => Math.Round(v / GridSize) * GridSize;

    static SolidColorBrush BrushFrom(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return new SolidColorBrush(Colors.LightYellow); }
    }

    static ControlTemplate CreateGripTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.BackgroundProperty, AccentBrush);
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        return new ControlTemplate(typeof(Thumb)) { VisualTree = factory };
    }

    NodeVisual CreateNodeVisual(NodeModel m)
    {
        var label = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Margin = new Thickness(10),
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
            Margin = new Thickness(6),
            Background = Brushes.Transparent,
            Foreground = TextBrush,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        var grip = new Thumb
        {
            Width = 12, Height = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 2, 2),
            Cursor = Cursors.SizeNWSE,
            Visibility = Visibility.Collapsed,
            Template = CreateGripTemplate()
        };

        var grid = new Grid();
        grid.Children.Add(label);
        grid.Children.Add(editor);
        grid.Children.Add(grip);

        var border = new Border
        {
            Width = m.W,
            Height = m.H,
            Background = BrushFrom(m.Color),
            BorderBrush = SoftBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Direction = 270, Opacity = 0.18 }
        };
        Canvas.SetLeft(border, m.X);
        Canvas.SetTop(border, m.Y);
        Panel.SetZIndex(border, ++_zTop);

        var nv = new NodeVisual { Model = m, Border = border, Label = label, Editor = editor, Grip = grip };

        border.MouseLeftButtonDown += (s, e) => Node_Down(nv, e);
        border.MouseMove += (s, e) => Node_Move(nv, e);
        border.MouseLeftButtonUp += (s, e) => Node_Up(nv, e);
        editor.KeyDown += (s, e) => Editor_KeyDown(nv, e);
        editor.LostKeyboardFocus += (s, e) => { if (_editing == nv) CommitEdit(); };
        grip.DragDelta += (s, e) => Grip_DragDelta(nv, e);

        var menu = new ContextMenu();
        var miEdit = new MenuItem { Header = "Edit text" };
        miEdit.Click += (s, e) => BeginEdit(nv);
        var miDup = new MenuItem { Header = "Duplicate" };
        miDup.Click += (s, e) => { if (!_selected.Contains(nv.Model.Id)) SelectOnly(nv); DuplicateSelected(); };
        var miDel = new MenuItem { Header = "Delete" };
        miDel.Click += (s, e) => { if (!_selected.Contains(nv.Model.Id)) SelectOnly(nv); DeleteSelected(); };
        menu.Items.Add(miEdit);
        menu.Items.Add(miDup);
        menu.Items.Add(miDel);
        border.ContextMenu = menu;
        border.ContextMenuOpening += (s, e) => { if (!_selected.Contains(nv.Model.Id)) SelectOnly(nv); };

        RefreshLabel(nv);
        World.Children.Add(border);
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
            Color = _lastColor
        };
        if (SnapCheck.IsChecked == true) { m.X = Snap(m.X); m.Y = Snap(m.Y); }
        var nv = CreateNodeVisual(m);
        SelectOnly(nv);
        MarkDirty();
        BeginEdit(nv);
    }

    void AddNote_Click(object sender, RoutedEventArgs e)
    {
        var wx = (Viewport.ActualWidth / 2 - Pan.X) / _zoom;
        var wy = (Viewport.ActualHeight / 2 - Pan.Y) / _zoom;
        CreateNoteAt(new Point(wx, wy));
    }

    void RefreshLabel(NodeVisual nv)
    {
        if (string.IsNullOrWhiteSpace(nv.Model.Text))
        {
            nv.Label.Text = "Double-click to edit";
            nv.Label.Foreground = PlaceholderBrush;
            nv.Label.FontStyle = FontStyles.Italic;
        }
        else
        {
            nv.Label.Text = nv.Model.Text;
            nv.Label.Foreground = TextBrush;
            nv.Label.FontStyle = FontStyles.Normal;
        }
    }

    Point NodeCenter(NodeVisual nv) =>
        new(nv.Model.X + nv.Model.W / 2, nv.Model.Y + nv.Model.H / 2);

    // ---------- Selection ----------

    void RefreshNodeChrome(NodeVisual nv)
    {
        bool highlighted = _selected.Contains(nv.Model.Id) || _connectSource == nv;
        nv.Border.BorderBrush = highlighted ? AccentBrush : SoftBorderBrush;
        nv.Border.BorderThickness = new Thickness(highlighted ? 2 : 1);
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
        if (_spaceDown || _panning) return;
        if (_editing == nv) return;
        CommitEdit();

        if (ConnectToggle.IsChecked == true)
        {
            HandleConnectClick(nv);
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            BeginEdit(nv);
            e.Handled = true;
            return;
        }

        Panel.SetZIndex(nv.Border, ++_zTop);
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
        nv.Border.CaptureMouse();
        e.Handled = true;
    }

    void Node_Move(NodeVisual nv, MouseEventArgs e)
    {
        if (!_draggingNodes || !nv.Border.IsMouseCaptured) return;
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
            Canvas.SetLeft(n.Border, nx);
            Canvas.SetTop(n.Border, ny);
            UpdateConnectionsFor(kv.Key);
        }
        MarkDirty();
    }

    void Node_Up(NodeVisual nv, MouseButtonEventArgs e)
    {
        if (!_draggingNodes) return;
        _draggingNodes = false;
        if (nv.Border.IsMouseCaptured) nv.Border.ReleaseMouseCapture();
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (!_movedDuringDrag && !ctrl && _selected.Count > 1) SelectOnly(nv);
        e.Handled = true;
    }

    void Grip_DragDelta(NodeVisual nv, DragDeltaEventArgs e)
    {
        var m = nv.Model;
        m.W = Math.Max(NodeMinW, m.W + e.HorizontalChange);
        m.H = Math.Max(NodeMinH, m.H + e.VerticalChange);
        nv.Border.Width = m.W;
        nv.Border.Height = m.H;
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
            Canvas.SetLeft(n.Border, n.Model.X);
            Canvas.SetTop(n.Border, n.Model.Y);
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
            var src = _nodes[id].Model;
            var m = new NodeModel
            {
                X = src.X + GridSize, Y = src.Y + GridSize,
                W = src.W, H = src.H,
                Text = src.Text, Color = src.Color
            };
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
        World.Children.Remove(nv.Border);
        _nodes.Remove(id);
        if (_editing == nv) _editing = null;
        if (_connectSource == nv) ClearConnectSource();
    }

    void ApplyColor(string hex)
    {
        _lastColor = hex;
        bool any = false;
        foreach (var id in _selected)
        {
            var nv = _nodes[id];
            nv.Model.Color = hex;
            nv.Border.Background = BrushFrom(hex);
            any = true;
        }
        if (any) MarkDirty();
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
        var ra = new Rect(a.Model.X, a.Model.Y, a.Model.W, a.Model.H);
        var rb = new Rect(b.Model.X, b.Model.Y, b.Model.W, b.Model.H);
        var ca = new Point(ra.X + ra.Width / 2, ra.Y + ra.Height / 2);
        var cb = new Point(rb.X + rb.Width / 2, rb.Y + rb.Height / 2);
        var p1 = EdgeIntersect(ra, ca, cb);
        var p2 = EdgeIntersect(rb, cb, ca);
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

    void HandleConnectClick(NodeVisual nv)
    {
        if (_connectSource == null)
        {
            _connectSource = nv;
            RefreshNodeChrome(nv);
            var c = NodeCenter(nv);
            _previewLine = new Line
            {
                X1 = c.X, Y1 = c.Y, X2 = c.X, Y2 = c.Y,
                Stroke = AccentBrush, StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            };
            Panel.SetZIndex(_previewLine, 99998);
            World.Children.Add(_previewLine);
        }
        else if (_connectSource == nv)
        {
            ClearConnectSource();
        }
        else
        {
            var src = _connectSource;
            var cv = AddConnection(src.Model.Id, nv.Model.Id);
            if (cv != null) MarkDirty();
            ClearConnectSource();
        }
    }

    void ClearConnectSource()
    {
        if (_previewLine != null)
        {
            World.Children.Remove(_previewLine);
            _previewLine = null;
        }
        var src = _connectSource;
        _connectSource = null;
        if (src != null) RefreshNodeChrome(src);
    }

    void ConnectToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (ConnectToggle.IsChecked != true) ClearConnectSource();
        UpdateCursor();
    }

    // ---------- Canvas interaction (select box, create, connect preview) ----------

    void World_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_spaceDown || _panning) return;
        if (e.OriginalSource != World) return;
        CommitEdit();

        if (ConnectToggle.IsChecked == true)
        {
            ClearConnectSource();
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
        e.Handled = true;
    }

    void World_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(World);

        if (_rubberBanding && _rubberRect != null)
        {
            Canvas.SetLeft(_rubberRect, Math.Min(p.X, _rubberStart.X));
            Canvas.SetTop(_rubberRect, Math.Min(p.Y, _rubberStart.Y));
            _rubberRect.Width = Math.Abs(p.X - _rubberStart.X);
            _rubberRect.Height = Math.Abs(p.Y - _rubberStart.Y);
        }

        if (_previewLine != null)
        {
            _previewLine.X2 = p.X;
            _previewLine.Y2 = p.Y;
        }
    }

    void World_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
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
            Viewport.ReleaseMouseCapture();
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
        Viewport.Cursor = _spaceDown || _panning
            ? Cursors.Hand
            : (ConnectToggle.IsChecked == true ? Cursors.Cross : Cursors.Arrow);
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
                if (_connectSource != null) ClearConnectSource();
                else if (ConnectToggle.IsChecked == true) ConnectToggle.IsChecked = false;
                else ClearSelection();
                e.Handled = true; break;
            case Key.C:
                ConnectToggle.IsChecked = ConnectToggle.IsChecked != true;
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
            MessageBox.Show(this, "Nothing to export yet.", "MindMap Canvas",
                MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show(this, "Could not export: " + ex.Message, "MindMap Canvas",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Scale.ScaleX = Scale.ScaleY = oz;
            Pan.X = ox;
            Pan.Y = oy;
        }
    }
}
