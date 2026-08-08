using System.Windows;

namespace MindMapCanvas;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = SettingsStore.Load();
        ThemeManager.Apply(ThemeManager.ByName(settings.Theme));
    }
}
