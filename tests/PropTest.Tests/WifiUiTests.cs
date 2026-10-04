using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using PropTest.Desktop;
using Xunit;

namespace PropTest.Tests;

public sealed class WifiUiTests
{
    [AvaloniaFact]
    public async Task WifiWaitsForUserAndLeavesSettingsAvailable()
    {
        var scans = 0;
        var vm = new MainViewModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "wifi.db"),
            runWorker: false, autoConnect: true, listPorts: () => { scans++; return []; });
        var window = new MainWindow(vm); window.Show();
        try
        {
            vm.IsWifi = true;
            var before = scans;
            await vm.CheckPortsAsync(); vm.Refresh();
            Assert.Equal(before, scans);
            Assert.True(vm.CanChooseDevice); Assert.True(vm.CanConfigure);
            Assert.Contains("Відключено", vm.Status);
            Assert.Equal("Підключити", vm.ConnectionButtonText);
            Assert.Null(vm.TakeError()); Assert.False(vm.HasWifiPassword);
            Assert.Contains("Пароль ще не прочитано", vm.WifiDetails);
            window.FindControl<TabControl>("Tabs")!.SelectedIndex = 2;
            var directory = Environment.GetEnvironmentVariable("PROPTEST_ARTIFACTS");
            foreach (var size in new[] { new PixelSize(1220, 850), new PixelSize(960, 680) })
            {
                window.Width = size.Width; window.Height = size.Height;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                if (directory is null) continue;
                Directory.CreateDirectory(directory);
                using var image = new RenderTargetBitmap(size, new Vector(96, 96));
                image.Render(window); image.Save(Path.Combine(directory, $"wifi-manual-{size.Width}.png"));
            }
            window.Width = 1220; window.Height = 850;
            var routerPanel = window.GetLogicalDescendants().OfType<Expander>().Distinct().Single(e => e.Header?.ToString()?.StartsWith("Wi-Fi через роутер") == true);
            routerPanel.IsExpanded = true;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.False(vm.CanConfigureNetwork);
            vm.HomePassword = "test-password";
            Assert.Equal('●', vm.HomePasswordChar);
            vm.ShowHomePassword = true; Dispatcher.UIThread.RunJobs();
            Assert.Equal('\0', window.FindControl<TextBox>("HomePasswordInput")!.PasswordChar);
            Assert.Equal("test-password", vm.HomePassword);
            vm.ShowHomePassword = false; vm.HomePassword = "";
            if (directory is not null)
            {
                using var router = new RenderTargetBitmap(new PixelSize(1220, 850), new Vector(96, 96));
                router.Render(window); router.Save(Path.Combine(directory, "wifi-router.png"));
            }
            vm.IsWifi = false; Assert.False(vm.IsWifi);
        }
        finally { window.Close(); }
    }
}
