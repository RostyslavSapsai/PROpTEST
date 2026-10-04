using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PropTest.Core;
using PropTest.Desktop;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(PropTest.Tests.TestAppBuilder))]
namespace PropTest.Tests;
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
public sealed class UiTests
{
    [AvaloniaFact]
    public async Task ExportUsesChosenDestinationAndPreservesSelectedRun()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PropTestExport", Guid.NewGuid().ToString());
        var repository = new PropTest.Infrastructure.SqliteRunRepository(Path.Combine(folder, "test.db"));
        var id = Guid.NewGuid();
        repository.Begin(new(id, DateTimeOffset.Now, new("Тест: 90/звук", 200, 30, 60), "COM / test", "Running", 0));
        repository.Append(new(id, 1, 200, 6.1, 12.5, .4) { SoundAdc = 600 }); repository.Finish(id, "UserStopped");
        using var vm = new MainViewModel(repository.DatabasePath, false);
        vm.SelectedRun = vm.Archive.Single();
        var output = new MemoryStream(new byte[4096], true);
        await vm.ExportSelectedAsync(name =>
        {
            Assert.StartsWith("PROpTEST_Тест_ 90_звук_", name); Assert.EndsWith(".csv", name);
            vm.SelectedRun = null;
            return Task.FromResult<(Stream Stream, string Name)?>((output, "chosen.csv"));
        });
        var csv = System.Text.Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("6.1,12.5,0.4", csv); Assert.Contains("600", csv); Assert.DoesNotContain('\0', csv);
        Assert.Contains("chosen.csv", vm.Message); Assert.Null(vm.TakeError());
        vm.SelectedRun = vm.Archive.Single();
        await vm.ExportSelectedAsync(_ => Task.FromResult<(Stream Stream, string Name)?>(null));
        Assert.Contains("скасовано", vm.Message); Assert.Null(vm.TakeError());
        await vm.ExportSelectedAsync(_ => throw new IOException("write denied"));
        Assert.Contains("write denied", vm.TakeError());
        Assert.False(Directory.Exists(Path.Combine(folder, "exports")));
    }
    [AvaloniaFact]
    public async Task OrdinaryWheelScrollsAndErrorsAreDismissible()
    {
        var vm = new MainViewModel(Path.Combine(Path.GetTempPath(), "PropTestEngineering-tests", Guid.NewGuid().ToString(), "errors.db"));
        var window = new MainWindow(vm); window.Show(); window.FindControl<MotorOverview>("MotorsOverview")!.IsVisible = false; window.FindControl<Expander>("LegacyCharts")!.IsExpanded = true; window.UpdateLayout();
        var chart = window.FindControl<TelemetryChart>("ThrustChart")!;
        var scroll = chart.GetLogicalAncestors().OfType<ScrollViewer>().First();
        var range = chart.ViewRange;
        window.MouseWheel(chart.TranslatePoint(chart.PlotArea.Center, window)!.Value, new Vector(0, -1));
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.Offset.Y > 0); Assert.Equal(range, chart.ViewRange);
        Assert.Equal("0 ADC", vm.SoundText); Assert.Equal("0 g", vm.VibrationText);
        vm.SelectedPort = null; await vm.ConnectAsync(); window.RefreshCharts();
        var error = Assert.IsType<ErrorWindow>(window.ActiveError);
        Assert.Single(error.GetLogicalDescendants().OfType<Button>(), b => b.Name == "DismissErrorButton"); Assert.DoesNotContain(error.GetLogicalDescendants().OfType<Button>(), b => b.Name == "ErrorStopButton");
        error.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var directory = Environment.GetEnvironmentVariable("PROPTEST_ARTIFACTS");
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            using var bitmap = new RenderTargetBitmap(new PixelSize(600,410), new Vector(96,96)); bitmap.Render(error); bitmap.Save(Path.Combine(directory,"error.png"));
        }
        error.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "DismissErrorButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.RefreshCharts(); Assert.Null(window.ActiveError);
        vm.Stop(); window.RefreshCharts(); Assert.Null(window.ActiveError);
        vm.IsSimulation = true; vm.Connect(); vm.TestName = ""; vm.Start(); window.RefreshCharts();
        Assert.NotNull(window.ActiveError);
        Assert.Contains(window.ActiveError!.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text != null && t.Text.Contains("Налаштування тесту"));
        if (directory is not null) { window.ActiveError.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var nameError = new RenderTargetBitmap(new PixelSize(600,410), new Vector(96,96)); nameError.Render(window.ActiveError); nameError.Save(Path.Combine(directory,"error-name.png")); }
        window.ActiveError.Close();
        vm.TestName = "fault"; vm.Start(); vm.BreakLink(); await Task.Delay(2400); window.RefreshCharts();
        Assert.NotNull(window.ActiveError); Assert.False(vm.IsRunning);
        window.ActiveError!.Close(); window.RefreshCharts(); Assert.Null(window.ActiveError);
        window.Close();
    }
    [AvaloniaFact]
    public async Task ChartNavigationAndSeparateWindowPreserveData()
    {
        var id = Guid.NewGuid();
        var samples = Enumerable.Range(1, 3000).Select(i => new Measurement(id, i, i * 10, 20, 40 + 12 * Math.Sin(i / 100.0), 1)).ToArray();
        var source = new TelemetryChart(); source.Update(samples);
        bool stopped = false;
        var window = new ChartWindow(source, true, "SIM · перевірка графіка", () => stopped = true);
        window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var chart = window.Chart;
        Assert.Equal(0, chart.ViewRange.Start);
        chart.ResetView(); Assert.Equal(0, chart.ViewRange.Start); Assert.Equal(30, chart.ViewRange.End);
        Point At(double ratio) => chart.TranslatePoint(new Point(chart.PlotArea.Left + chart.PlotArea.Width * ratio, chart.PlotArea.Center.Y), window)!.Value;
        window.MouseDown(At(.25), MouseButton.Left); window.MouseMove(At(.75)); window.MouseUp(At(.75), MouseButton.Left);
        Assert.InRange(chart.ViewRange.Start, 7.4, 7.6); Assert.InRange(chart.ViewRange.End, 22.4, 22.6);
        var selected = chart.ViewRange;
        window.MouseWheel(At(.5), new Vector(0, 1)); Assert.Equal(selected, chart.ViewRange);
        window.MouseWheel(At(.5), new Vector(0, .15), RawInputModifiers.Control);
        Assert.True(chart.ViewRange.End-chart.ViewRange.Start < selected.End-selected.Start);
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control); window.KeyReleaseQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal(selected,chart.ViewRange);
        chart.RaiseEvent(new PinchEventArgs(1.2,chart.PlotArea.Center));
        chart.RaiseEvent(new PinchEventArgs(2,chart.PlotArea.Center));
        Assert.Equal((selected.End-selected.Start)/2,chart.ViewRange.End-chart.ViewRange.Start,6);
        chart.RaiseEvent(new PinchEndedEventArgs());
        chart.UndoView(); Assert.Equal(selected,chart.ViewRange);
        window.MouseWheel(At(.5), new Vector(0, 1), RawInputModifiers.Control); Assert.True(chart.ViewRange.End - chart.ViewRange.Start < selected.End - selected.Start);
        var zoomed = chart.ViewRange;
        window.MouseWheel(At(.5), new Vector(0, 1), RawInputModifiers.Control);
        window.MouseWheel(At(.5), new Vector(0, 1), RawInputModifiers.Control);
        var firstGesture = chart.ViewRange;
        await Task.Delay(600);
        window.MouseWheel(At(.5), new Vector(0, 1), RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control); window.KeyReleaseQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal(firstGesture, chart.ViewRange);
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control); window.KeyReleaseQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal(selected, chart.ViewRange);
        window.MouseWheel(At(.5), new Vector(0, 1), RawInputModifiers.Control);
        window.MouseDown(At(.5), MouseButton.Right); window.MouseMove(At(.6)); window.MouseUp(At(.6), MouseButton.Right);
        Assert.True(chart.ViewRange.Start < zoomed.Start);
        var viewed = chart.ViewRange;
        var bar = window.GetLogicalDescendants().OfType<ChartRangeBar>().Single();
        bar.From.Value = 30; bar.To.Value = 60; Assert.True(bar.Apply()); Assert.Equal((30d, 60d), chart.ViewRange);
        bar.From.Value = 60; bar.To.Value = 30; Assert.False(bar.Apply()); Assert.Equal((30d, 60d), chart.ViewRange);
        chart.UndoView(); Assert.Equal(viewed, chart.ViewRange);
        source.Update(samples.Append(new Measurement(id, 3001, 30010, 20, 45, 1)).ToArray());
        Assert.Equal(viewed, chart.ViewRange); Assert.Equal(3001, chart.Samples.Count);
        chart.ResetView(); Assert.Equal(0, chart.ViewRange.Start); Assert.Equal(30.01, chart.ViewRange.End);
        source.Update(samples.Append(new Measurement(id, 3001, 31000, 20, 45, 1)).ToArray()); Assert.Equal(31, chart.ViewRange.End);
        var directory = Environment.GetEnvironmentVariable("PROPTEST_ARTIFACTS");
        if (directory is not null)
        {
            bar.From.Value = 30; bar.To.Value = 60;
            Directory.CreateDirectory(directory); Dispatcher.UIThread.RunJobs();
            using var bitmap = new RenderTargetBitmap(new PixelSize(1100,720), new Vector(96,96)); bitmap.Render(window); bitmap.Save(Path.Combine(directory,"chart-detail.png"));
        }
        window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "DetailStopButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.True(stopped);
        window.Close();
        source.Update(Array.Empty<Measurement>()); Assert.Equal(3001, chart.Samples.Count);
        var archive = new ChartWindow(chart, false, "архів", () => { });
        chart.Update(Array.Empty<Measurement>()); Assert.Equal(3001, archive.Chart.Samples.Count); archive.Close();
    }
    [AvaloniaFact]
    public void DisconnectedDefaultAndConnectionSelection()
    {
        var vm = new MainViewModel(Path.Combine(Path.GetTempPath(), "PropTestEngineering-tests", Guid.NewGuid().ToString(), "default.db"), false);
        var window = new MainWindow(vm); window.Show(); window.FindControl<MotorOverview>("MotorsOverview")!.IsVisible = false; window.FindControl<Expander>("LegacyCharts")!.IsExpanded = true; window.UpdateLayout(); window.RefreshCharts();
        Assert.False(vm.IsSimulation); Assert.False(vm.CanStart); Assert.Equal("0 г", vm.ThrustText); Assert.Equal("0 А", vm.CurrentText);
        Assert.False(window.FindControl<Button>("StartButton")!.IsEnabled);
        foreach (var name in new[] { "ThrustChart", "CurrentChart", "VibrationChart", "SoundChart" }) Assert.NotNull(window.FindControl<TelemetryChart>(name));
        var directory = Environment.GetEnvironmentVariable("PROPTEST_ARTIFACTS");
        if (directory is not null)
        {
            Directory.CreateDirectory(directory); Dispatcher.UIThread.RunJobs();
            using var disconnected = new RenderTargetBitmap(new PixelSize(1220, 850), new Vector(96, 96));
            disconnected.Render(window); disconnected.Save(Path.Combine(directory, "disconnected.png"));
            window.FindControl<TabControl>("Tabs")!.SelectedIndex = 2; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using var connection = new RenderTargetBitmap(new PixelSize(1220, 850), new Vector(96, 96));
            connection.Render(window); connection.Save(Path.Combine(directory, "connection.png"));
        }
        window.Close();
    }
    [AvaloniaFact]
    public async Task ManualControlsAndRenderedCharts()
    {
        var path = Path.Combine(Path.GetTempPath(), "PropTestEngineering-tests", Guid.NewGuid().ToString(), "ui.db");
        var vm = new MainViewModel(path); var window = new MainWindow(vm); window.Show(); window.FindControl<MotorOverview>("MotorsOverview")!.IsVisible = false; window.FindControl<Expander>("LegacyCharts")!.IsExpanded = true;
        void Click(string name) => window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(vm.IsSimulation); Assert.False(vm.CanStart); Assert.Equal("0 г", vm.ThrustText); vm.Start(); Assert.Empty(vm.Samples); Assert.NotNull(vm.TakeError()); vm.IsSimulation = true; Click("ConnectButton"); await Task.Delay(100); vm.Refresh(); Assert.True(vm.CanStart); Click("StartButton"); Assert.True(vm.IsRunning);
        Assert.True(window.FindControl<Slider>("ThrottleSlider")!.IsEnabled); Assert.Equal(30, window.FindControl<Slider>("ThrottleSlider")!.Maximum); window.FindControl<Slider>("ThrottleSlider")!.Value = 100; Dispatcher.UIThread.RunJobs(); Assert.Equal(30, vm.Throttle); Assert.Equal(30, window.FindControl<Slider>("ThrottleSlider")!.Value);
        vm.Throttle = 25; Assert.Equal(25, vm.Throttle);
        window.FindControl<NumericUpDown>("ThrottleInput")!.Value = 6.1m; Dispatcher.UIThread.RunJobs(); Assert.Equal(6.1, vm.Throttle);
        Click("StopButton"); Assert.False(vm.IsRunning); Assert.Equal(0, vm.Throttle);
        Assert.False(window.FindControl<Slider>("ThrottleSlider")!.IsEnabled);
        vm.Limit = 15; Dispatcher.UIThread.RunJobs(); Assert.Equal(15, window.FindControl<Slider>("ThrottleSlider")!.Maximum);
        Click("StartButton"); vm.Throttle = 25; Assert.Equal(15, vm.Throttle);
        await Task.Delay(1600); window.RefreshCharts(); Assert.NotEmpty(vm.Samples);
        var chart = window.FindControl<TelemetryChart>("ThrustChart")!;
        window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var position = chart.TranslatePoint(chart.GetSamplePosition(vm.Samples.Count - 1), window)!.Value;
        window.MouseMove(position); Dispatcher.UIThread.RunJobs();
        Assert.NotNull(chart.HoveredSequence);
        // Render the hovered state synchronously; the live timer can invalidate it on the next UI tick.
        using (var hoverFrame = new RenderTargetBitmap(new PixelSize((int)chart.Bounds.Width,(int)chart.Bounds.Height),new Vector(96,96))) hoverFrame.Render(chart);
        Assert.NotNull(chart.HoverBounds); Assert.True(chart.PlotArea.Contains(chart.HoverBounds.Value));
        window.MouseMove(new Point(5, 5)); Assert.Null(chart.HoveredSequence);
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var directory = Environment.GetEnvironmentVariable("PROPTEST_ARTIFACTS");
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            window.MouseMove(chart.TranslatePoint(chart.GetSamplePosition(vm.Samples.Count - 1), window)!.Value); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var hover = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Vector(96,96))) { hover.Render(window); hover.Save(Path.Combine(directory, "hover.png")); }
            window.MouseMove(new Point(5, 5)); Dispatcher.UIThread.RunJobs();
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Vector(96, 96));
            bitmap.Render(window); bitmap.Save(Path.Combine(directory, "desktop.png"));
            window.Width = 960; window.Height = 680; window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.RefreshCharts(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using var compact = new RenderTargetBitmap(new PixelSize(960, 680), new Vector(96, 96)); compact.Render(window); compact.Save(Path.Combine(directory, "desktop-compact.png"));
            var stop = window.FindControl<Button>("StopButton")!;
            var stopPosition = stop.TranslatePoint(new Point(), window)!.Value;
            Assert.InRange(stopPosition.Y, 0, window.ClientSize.Height - stop.Bounds.Height);
            Click("StopButton"); vm.SelectedRun = vm.Archive.First(r => r.SampleCount > 0);
            window.FindControl<TabControl>("Tabs")!.SelectedIndex = 1; window.Width = 1220; window.Height = 850;
            window.RefreshCharts(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using var saved = new RenderTargetBitmap(new PixelSize(1220, 850), new Vector(96, 96)); saved.Render(window); saved.Save(Path.Combine(directory, "archive.png"));
        }
        vm.Stop(); vm.LimitEnabled = false; vm.Start(); vm.Throttle = 100; Assert.Equal(100, vm.Throttle); vm.Stop(); vm.Disconnect(); Assert.Empty(vm.Samples); Assert.Equal("0 г", vm.ThrustText);
        window.Close();
    }
}











