using System.IO;
using System.Windows;

namespace MindMapCanvas;

public partial class App : Application
{
    public string StartupFile { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            StartupFile = e.Args[0];
        var settings = SettingsStore.Load();
        ThemeManager.Apply(ThemeManager.ByName(settings.Theme));
    }
}
