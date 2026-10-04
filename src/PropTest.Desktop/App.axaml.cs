using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PropTest.Desktop;
public sealed partial class App : Application
{
    public static bool SmokeTest { get; set; }
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = SmokeTest
                ? new MainWindow(new MainViewModel(Path.Combine(Path.GetTempPath(), "PropTestEngineering-smoke", Guid.NewGuid().ToString(), "smoke.db")))
                : new MainWindow();
            if (SmokeTest)
                desktop.MainWindow.Opened += (_, _) => Avalonia.Threading.DispatcherTimer.RunOnce(() => desktop.MainWindow.Close(), TimeSpan.FromSeconds(1));
        }
        base.OnFrameworkInitializationCompleted();
    }
}
