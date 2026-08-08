using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
            var content = new StackPanel { Margin = new Thickness(4, 2, 4, 2) };
            content.Children.Add(title);
            content.Children.Add(sub);

            var row = new Button
            {
                Content = content,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 2, 0, 2)
            };
            row.SetResourceReference(StyleProperty, "ToolBtn");
            row.Click += (s, e) =>
            {
                SelectedKey = k;
                DialogResult = true;
            };
            List.Children.Add(row);
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
