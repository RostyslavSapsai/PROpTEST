using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PropTest.Core;
using PropTest.Desktop;
using Xunit;

namespace PropTest.Tests;
public sealed class MotorOverviewTests
{
    [AvaloniaFact]
    public async Task LegacyDataIsNotDuplicatedAndWindowsStayLive()
    {
        var vm = new MainViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "test.db"), true, false, () => []);
        var main = new MainWindow(vm); main.Show();
        var overview = main.FindControl<MotorOverview>("MotorsOverview")!;
        var windows = new List<Window>();
        try {
            var sample = new Measurement(Guid.NewGuid(),1,200,10,123,2) { SoundAdc=12, VibrationX=0, VibrationY=1, VibrationZ=0 };
            overview.UpdateLegacy([sample], "SIM · демонстраційні дані", true);
            Assert.Single(overview.MotorSamples(1));
            for(int i=2;i<=4;i++) Assert.Empty(overview.MotorSamples(i));
            var graph = (ChartWindow)overview.OpenMetric(0,0); windows.Add(graph);
            overview.UpdateLegacy([sample, sample with { Sequence=2, ElapsedMs=400, ThrustGrams=140 }], "SIM · демонстраційні дані", true);
            Assert.Equal(2, graph.Chart.Samples.Count);
            var other = (ChartWindow)overview.OpenMetric(1,0); windows.Add(other); Assert.Empty(other.Chart.Samples);
            var all = overview.OpenMotor(0); windows.Add(all);
            var output = Environment.GetEnvironmentVariable("PROPTEST_ARTIFACTS");
            void Capture(Window window, string name, int width, int height) {
                window.Width=width; window.Height=height; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                if(output is null) return; Directory.CreateDirectory(output);
                using var image = new RenderTargetBitmap(new PixelSize(width,height),new Vector(96,96)); image.Render(window); image.Save(Path.Combine(output,name+".png"));
            }
            vm.IsSimulation = true; vm.Connect(); vm.Start(); await Task.Delay(700); main.RefreshCharts();
            Capture(main,"motors-1220",1220,850);
            var drawing = overview.GetLogicalDescendants().OfType<Control>().Single(c => c.Name == "DroneDiagram");
            double scale=Math.Min(drawing.Bounds.Width/420,drawing.Bounds.Height/460);
            var hover = drawing.TranslatePoint(new Point((drawing.Bounds.Width-420*scale)/2+110*scale,(drawing.Bounds.Height-460*scale)/2+100*scale),main)!.Value;
            main.MouseMove(hover); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Cursor(StandardCursorType.Hand).ToString(),drawing.Cursor!.ToString());
            Capture(main,"motors-hover",1220,850);
            main.MouseMove(new Point(5,5)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Cursor(StandardCursorType.Arrow).ToString(),drawing.Cursor!.ToString());
            Capture(main,"motors-960",960,680);
            vm.Throttle=10; await Task.Delay(1000); main.RefreshCharts(); await Task.Delay(700);
            Capture(main,"motors-running",1220,850);
            vm.Stop(); main.RefreshCharts(); await Task.Delay(1700);
            Capture(main,"motors-stopped",1220,850);
            Capture(graph,"motor-compact",620,460); Capture(all,"motor-all",1000,720);
        } finally { foreach(var window in windows) window.Close(); main.Close(); }
    }
}
