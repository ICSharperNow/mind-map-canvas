using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MindMapCanvas;

public partial class SettingsWindow : Window
{
    readonly AppSettings _settings;
    readonly Action _changed;
    bool _loading = true;

    public SettingsWindow(AppSettings settings, Action changed)
    {
        _settings = settings;
        _changed = changed;
        InitializeComponent();

        foreach (var t in ThemeManager.Themes)
        {
            var theme = t;

            var swatches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in new[] { theme.PanelBg, theme.CanvasBg, theme.CheckedBorder })
                swatches.Children.Add(new Border
                {
                    Width = 14, Height = 14,
                    Margin = new Thickness(1, 0, 1, 0),
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = new SolidColorBrush(theme.PanelBorder),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(c)
                });

            var name = new TextBlock
            {
                Text = theme.Name,
                Margin = new Thickness(7, 0, 0, 0),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(swatches);
            content.Children.Add(name);

            var rb = new RadioButton
            {
                GroupName = "theme",
                Content = content,
                Width = 176,
                Margin = new Thickness(2, 4, 0, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsChecked = ThemeManager.Current == theme
            };
            rb.Checked += (s, e) =>
            {
                if (_loading) return;
                ThemeManager.Apply(theme);
                _settings.Theme = theme.Name;
                SettingsStore.Save(_settings);
            };
            ThemeList.Children.Add(rb);
        }

        BuildCustomThemeRow();

        GridCheck.IsChecked = _settings.ShowGrid;
        SnapDefCheck.IsChecked = _settings.SnapToGrid;
        RememberCheck.IsChecked = _settings.RememberLastStyle;
        _loading = false;
    }

    RadioButton _customRb;

    void BuildCustomThemeRow()
    {
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        swatches.Children.Add(MakeCustomSwatch(() => _settings.CustomPanel, v => _settings.CustomPanel = v, "Panel / toolbar color"));
        swatches.Children.Add(MakeCustomSwatch(() => _settings.CustomCanvas, v => _settings.CustomCanvas = v, "Canvas color"));
        swatches.Children.Add(MakeCustomSwatch(() => _settings.CustomAccent, v => _settings.CustomAccent = v, "Accent color"));

        var name = new TextBlock
        {
            Text = "Custom",
            Margin = new Thickness(7, 0, 0, 0),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(swatches);
        content.Children.Add(name);

        _customRb = new RadioButton
        {
            GroupName = "theme",
            Content = content,
            Width = 176,
            Margin = new Thickness(2, 4, 0, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
            IsChecked = _settings.Theme == "Custom"
        };
        _customRb.Checked += (s, e) => { if (!_loading) ApplyCustomTheme(); };
        ThemeList.Children.Add(_customRb);
    }

    Border MakeCustomSwatch(Func<string> get, Action<string> set, string tooltip)
    {
        Color Parse(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Colors.Gray; }
        }

        var sw = new Border
        {
            Width = 16, Height = 16,
            Margin = new Thickness(1, 0, 1, 0),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Parse(get())),
            Cursor = Cursors.Hand,
            ToolTip = tooltip + " - click to change"
        };
        sw.SetResourceReference(Border.BorderBrushProperty, "Brush.PanelBorder");
        sw.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            var dlg = new ColorPickerWindow(Parse(get())) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            var hex = $"#{dlg.SelectedColor.R:X2}{dlg.SelectedColor.G:X2}{dlg.SelectedColor.B:X2}";
            set(hex);
            sw.Background = new SolidColorBrush(dlg.SelectedColor);
            if (_customRb.IsChecked != true) _customRb.IsChecked = true;
            else ApplyCustomTheme();
        };
        return sw;
    }

    void ApplyCustomTheme()
    {
        _settings.Theme = "Custom";
        ThemeManager.Apply(ThemeManager.Resolve(_settings));
        SettingsStore.Save(_settings);
    }

    void Grid_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.ShowGrid = GridCheck.IsChecked == true;
        ThemeManager.ShowGrid = _settings.ShowGrid;
        ThemeManager.Apply(ThemeManager.Current);
        SettingsStore.Save(_settings);
    }

    void Snap_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SnapToGrid = SnapDefCheck.IsChecked == true;
        SettingsStore.Save(_settings);
        _changed?.Invoke();
    }

    void Remember_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.RememberLastStyle = RememberCheck.IsChecked == true;
        SettingsStore.Save(_settings);
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
