using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MindMapCanvas;

/// <summary>
/// Borderless, theme-aware replacement for MessageBox with up to three buttons.
/// </summary>
public class ModernDialog : Window
{
    public enum Outcome { Primary, Secondary, Cancel }

    Outcome _outcome = Outcome.Cancel;

    ModernDialog(Window owner, string title, string message, string primary, string secondary, string cancel)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var titleText = new TextBlock
        {
            Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");

        var msgText = new TextBlock
        {
            Text = message, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0), MaxWidth = 380, LineHeight = 19
        };
        msgText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.SubtleText");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };
        if (cancel != null) buttons.Children.Add(MakeButton(cancel, "DlgSecondary", Outcome.Cancel));
        if (secondary != null) buttons.Children.Add(MakeButton(secondary, "DlgSecondary", Outcome.Secondary));
        buttons.Children.Add(MakeButton(primary, "DlgPrimary", Outcome.Primary));

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 22), MinWidth = 300 };
        stack.Children.Add(titleText);
        stack.Children.Add(msgText);
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
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) { _outcome = Outcome.Cancel; Close(); }
            else if (e.Key == Key.Enter) { _outcome = Outcome.Primary; Close(); }
        };
    }

    Button MakeButton(string text, string styleKey, Outcome outcome)
    {
        var b = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0), MinWidth = 88 };
        b.SetResourceReference(StyleProperty, styleKey);
        b.Click += (s, e) => { _outcome = outcome; Close(); };
        return b;
    }

    public static Outcome Show(Window owner, string title, string message,
        string primary, string secondary = null, string cancel = null)
    {
        var d = new ModernDialog(owner, title, message, primary, secondary, cancel);
        d.ShowDialog();
        return d._outcome;
    }
}
