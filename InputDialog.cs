using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MindMapCanvas;

/// <summary>Borderless themed single-line text prompt.</summary>
public class InputDialog : Window
{
    readonly TextBox _box;
    bool _ok;

    InputDialog(Window owner, string title, string prompt, string initial)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var titleText = new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        var promptText = new TextBlock { Text = prompt, FontSize = 13, Margin = new Thickness(0, 10, 0, 6) };
        promptText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.SubtleText");

        _box = new TextBox { Text = initial ?? "", MinWidth = 340, FontSize = 13 };
        _box.SetResourceReference(StyleProperty, "ThemedText");
        _box.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { _ok = true; Close(); }
        };

        var cancel = new Button { Content = "Cancel", MinWidth = 84 };
        cancel.SetResourceReference(StyleProperty, "DlgSecondary");
        cancel.Click += (s, e) => Close();
        var ok = new Button { Content = "Add", MinWidth = 84, Margin = new Thickness(8, 0, 0, 0) };
        ok.SetResourceReference(StyleProperty, "DlgPrimary");
        ok.Click += (s, e) => { _ok = true; Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        stack.Children.Add(titleText);
        stack.Children.Add(promptText);
        stack.Children.Add(_box);
        stack.Children.Add(buttons);

        var root = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Child = stack,
            Margin = new Thickness(20),
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 4, Opacity = 0.32 }
        };
        root.SetResourceReference(Border.BackgroundProperty, "Brush.PanelBg");
        root.SetResourceReference(Border.BorderBrushProperty, "Brush.PanelBorder");
        Content = root;

        MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && !(e.OriginalSource is TextBox)) DragMove();
        };
        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        Loaded += (s, e) => { _box.Focus(); _box.CaretIndex = _box.Text.Length; };
    }

    public static string Show(Window owner, string title, string prompt, string initial = "")
    {
        var d = new InputDialog(owner, title, prompt, initial);
        d.ShowDialog();
        return d._ok ? d._box.Text.Trim() : null;
    }
}
