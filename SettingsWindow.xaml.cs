using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MindMapCanvas;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        foreach (var t in ThemeManager.Themes)
        {
            var theme = t;

            var swatches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in new[] { theme.PanelBg, theme.CanvasBg, theme.CheckedBorder })
                swatches.Children.Add(new Border
                {
                    Width = 16, Height = 16,
                    Margin = new Thickness(2, 0, 2, 0),
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = new SolidColorBrush(theme.PanelBorder),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(c)
                });

            var name = new TextBlock
            {
                Text = theme.Name,
                Margin = new Thickness(8, 0, 0, 0),
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
                Margin = new Thickness(2, 4, 0, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsChecked = ThemeManager.Current == theme
            };
            rb.Checked += (s, e) =>
            {
                ThemeManager.Apply(theme);
                var settings = SettingsStore.Load();
                settings.Theme = theme.Name;
                SettingsStore.Save(settings);
            };
            ThemeList.Children.Add(rb);
        }
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
