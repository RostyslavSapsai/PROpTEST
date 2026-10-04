using Avalonia;

namespace PropTest.Desktop;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.SmokeTest = args.Contains("--smoke-test", StringComparer.Ordinal);
        using var instance = new Mutex(true, App.SmokeTest ? "PropTestEngineering-smoke" : "PropTestEngineering-v01", out var first);
        if (!first) return;
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}
