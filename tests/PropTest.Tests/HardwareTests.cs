using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PropTest.Desktop;
using PropTest.Core;
using PropTest.Infrastructure;
using System.Text.Json;
using Xunit;

namespace PropTest.Tests;

public sealed class HardwareTests
{
    [AvaloniaFact]
    public void RenderCapturedSound()
    {
        var capture = Environment.GetEnvironmentVariable("PROPTEST_SOUND_CAPTURE");
        var output = Environment.GetEnvironmentVariable("PROPTEST_ARTIFACTS");
        if (capture is null || output is null) return;
        var samples = new List<Measurement>();
        var id = Guid.NewGuid(); long? first = null;
        foreach (var line in File.ReadLines(capture))
        {
            if (!line.StartsWith("D|")) continue;
            var time = long.Parse(line.Split('|')[2]); first ??= time;
            if (SerialTestDevice.TryParseCurrent(line, id, time - first.Value, out var sample)) samples.Add(sample!);
        }
        Assert.NotEmpty(samples);
        var source = new TelemetryChart { Channel = ChartChannel.Sound }; source.Update(samples);
        var window = new ChartWindow(source, false, "Реальна проба · 0 → 5 → 10% · PT:2.1 · 50 мс peak-to-peak", () => { });
        try
        {
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Directory.CreateDirectory(output);
            using var bitmap = new RenderTargetBitmap(new PixelSize(1100, 720), new Vector(96, 96));
            bitmap.Render(window); bitmap.Save(Path.Combine(output, "sound-capture.png"));
        }
        finally { window.Close(); }
    }
    // Explicit opt-in only. Never sends nonzero throttle. Uses a separate database.
    [AvaloniaFact]
    public async Task HardwareZeroRecording()
    {
        var port = Environment.GetEnvironmentVariable("PROPTEST_HARDWARE_PORT");
        if (string.IsNullOrEmpty(port)) return;
        var folder = Path.Combine(Path.GetTempPath(), "PropTestHardware", Guid.NewGuid().ToString());
        var vm = new MainViewModel(Path.Combine(folder, "zero.db"));
        var window = new MainWindow(vm);
        try
        {
            window.Show(); vm.SelectedPort = port;
            await vm.ConnectAsync(); Assert.True(vm.CanStart, vm.Message);
            vm.TestName = "Hardware zero-output verification"; vm.Start();
            await Task.Delay(1600); window.RefreshCharts();
            Assert.True(vm.IsRunning, vm.Message); Assert.NotEmpty(vm.Samples);
            Assert.All(vm.Samples, s => Assert.Equal(0, s.ThrottlePercent));
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var output = Environment.GetEnvironmentVariable("PROPTEST_ARTIFACTS");
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "hardware-zero.json"), JsonSerializer.Serialize(vm.Samples));
                using var bitmap = new RenderTargetBitmap(new PixelSize(1220, 850), new Vector(96, 96));
                bitmap.Render(window); bitmap.Save(Path.Combine(output, "hardware-zero.png"));
            }
            vm.Stop(); Assert.True(vm.CanStart, vm.Message);
            vm.SelectedRun = vm.Archive.First();
            Assert.Equal(vm.Samples.Count, vm.SelectedData!.Samples.Count);
        }
        finally { vm.Stop(); window.Close(); }
    }
}
