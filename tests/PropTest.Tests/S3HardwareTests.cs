using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PropTest.Desktop;
using Xunit;

namespace PropTest.Tests;

// Opt-in only: PROPTEST_S3_PORT is set explicitly for a connected S3 bring-up board.
public sealed class S3HardwareTests
{
    [AvaloniaFact]
    public async Task ConnectedS3AutoDetectsWithoutStartingTest()
    {
        var port = Environment.GetEnvironmentVariable("PROPTEST_S3_PORT");
        if (port is null) return;
        var vm = new MainViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "s3.db"), autoConnect: true);
        var window = new MainWindow(vm); window.Show();
        try
        {
            await vm.CheckPortsAsync();
            Assert.Equal(port, vm.SelectedPort);
            for (int n = 0; n < 30; n++) { await Task.Delay(100); vm.Refresh(); }
            Assert.Contains("Підключено", vm.Status); Assert.Contains("S3-Connect", vm.FirmwareInfo);
            Assert.Contains("Пароль Wi-Fi:", vm.WifiDetails);
            Assert.False(vm.CanStart); Assert.Empty(vm.Samples); Assert.Null(vm.TakeError());
            vm.Start(); Assert.NotNull(vm.TakeError()); Assert.Empty(vm.Archive);
            for (int n = 0; n < 40; n++) { await Task.Delay(100); vm.Refresh(); }
            Assert.Contains("Підключено", vm.Status);
            window.FindControl<TabControl>("Tabs")!.SelectedIndex = 2;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var directory = Environment.GetEnvironmentVariable("PROPTEST_ARTIFACTS");
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
                using var bitmap = new RenderTargetBitmap(new PixelSize(1220, 850), new Vector(96, 96));
                bitmap.Render(window); bitmap.Save(Path.Combine(directory, "s3-usb-connected.png"));
            }
            var notification = new NetworkStatusWindow(vm); notification.Show(window);
            notification.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            if (directory is not null)
            {
                using var image = new RenderTargetBitmap(new PixelSize(580, 440), new Vector(96, 96));
                image.Render(notification); image.Save(Path.Combine(directory, "network-notification.png"));
            }
            notification.Close();
            vm.Disconnect(); Assert.Contains("Відключено", vm.Status);
            await Task.Delay(1100); await vm.CheckPortsAsync();
            Assert.Contains("Відключено", vm.Status);
            await vm.ConnectAsync(); Assert.Contains("Підключено", vm.Status);
        }
        finally { window.Close(); }
    }
}
