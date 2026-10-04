using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PropTest.Desktop;
using PropTest.Infrastructure;
using Xunit;

namespace PropTest.Tests;

public sealed class SavedBoardTests
{
    static string Folder() => Path.Combine(Path.GetTempPath(), "proptest-boards-" + Guid.NewGuid());
    [Fact]
    public void RemembersDistinctBoardsAndProtectsAccess()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Folder(), "boards-v1.json");
        var store = new SavedBoardStore(path);
        var first = store.Remember("proptest-v2-111111.local", "192.168.1.10", "private-test-key");
        store.Replace(first with { Name = "Стенд 1", AutoConnect = false });
        store.Remember("proptest-v2-222222.local", "192.168.1.11", "second-key");
        store.Remember(first.Hostname, "192.168.1.20", "private-test-key");
        var loaded = new SavedBoardStore(path); loaded.Load();
        Assert.Equal(2, loaded.Boards.Count);
        var board = loaded.Boards.Single(b => b.Hostname == first.Hostname);
        Assert.Equal("Стенд 1", board.Name); Assert.False(board.AutoConnect);
        Assert.Equal("192.168.1.20", board.LastAddress);
        Assert.Equal("private-test-key", LocalBoardSecret.Unprotect(board.ProtectedKey));
        Assert.DoesNotContain("private-test-key", File.ReadAllText(path));
        Assert.Equal(first.Hostname, loaded.SelectedHostname);
    }
    [Fact]
    public async Task VerifySavedPhysicalBoardWhenExplicitlyEnabled()
    {
        if (Environment.GetEnvironmentVariable("PROPTEST_VERIFY_SAVED_BOARD") != "1") return;
        var source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PropTestEngineering", "boards-v1.json");
        var dir = Folder(); Directory.CreateDirectory(dir);
        File.Copy(source, Path.Combine(dir, "boards-v1.json"));
        var store = new SavedBoardStore(Path.Combine(dir, "boards-v1.json")); store.Load();
        var original = store.SelectedHostname;
        store.Remember("proptest-v2-unavailable.local", "", "test");
        store.SelectedHostname = original; store.Save();
        using var vm = new MainViewModel(Path.Combine(dir, "test.db"), false, true, () => []);
        var first = vm.SelectedBoard;
        Assert.True(vm.HasSavedBoard);
        await vm.CheckPortsAsync(); vm.Refresh();
        Assert.Contains("Підключено", vm.Status);
        Assert.True(vm.CanOpenBoardWeb); Assert.False(vm.CanStart);
        Assert.Null(vm.TakeError());
        Assert.True(vm.CanSelectBoard);
        vm.SelectedBoard = vm.SavedBoards.Single(b => b.Hostname == "proptest-v2-unavailable.local");
        Assert.Contains("Підключено", vm.ConnectedBoardLabel);
        Assert.Contains("Збережена", vm.SelectedBoardStatus);
        await vm.ConnectAsync();
        Assert.Contains("Відключено", vm.Status);
        Assert.NotNull(vm.TakeError());
        vm.SelectedBoard = first;
        await vm.ConnectAsync();
        Assert.Contains("Підключено", vm.SelectedBoardStatus);
        // No arming or throttle commands. Disconnect also requests a stop.
        vm.Disconnect();
        await vm.CheckPortsAsync();
        Assert.Contains("Відключено", vm.Status);
    }
    [Fact]
    public void CorruptProfileIsNotOverwritten()
    {
        var path = Path.Combine(Folder(), "boards-v1.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string invalid = "{\"Version\":1,\"Boards\":[null]}";
        File.WriteAllText(path, invalid);
        var store = new SavedBoardStore(path); store.Load();
        Assert.NotEmpty(store.LoadError); Assert.Throws<IOException>(store.Save);
        Assert.Equal(invalid, File.ReadAllText(path));
    }
    [AvaloniaFact]
    public async Task DisconnectSurvivesBindingRefreshAndUnavailableBoardDoesNotBlockUi()
    {
        if (!OperatingSystem.IsWindows()) return;
        string dir = Folder();
        var store = new SavedBoardStore(Path.Combine(dir, "boards-v1.json"));
        store.Remember("proptest-v2-222222.local", "192.168.1.10", "test-key");
        store.Remember("proptest-v2-111111.local", "192.168.1.11", "test-key");
        int probes = 0;
        var vm = new MainViewModel(Path.Combine(dir, "test.db"), false, true, () => [], (host, key, token) => {
            probes++; Assert.StartsWith("proptest-v2-", host); Assert.Equal("test-key", key);
            return Task.FromException(new IOException("Not available"));
        });
        var window = new MainWindow(vm); window.Show();
        try {
            Assert.True(vm.IsWifi); Assert.True(vm.HasWifiPassword);
            vm.Disconnect(); vm.Refresh(); Dispatcher.UIThread.RunJobs();
            vm.SelectedBoard = vm.SelectedBoard; vm.BoardAutoConnect = vm.BoardAutoConnect;
            await vm.CheckPortsAsync(); Assert.Equal(0, probes);
            vm.BoardAutoConnect = false; vm.BoardAutoConnect = true;
            await vm.CheckPortsAsync(); Assert.Equal(1, probes);
            Assert.True(vm.CanConfigure); Assert.True(vm.CanChooseDevice); Assert.Null(vm.TakeError());
            vm.BoardName = "Мій стенд"; vm.RenameBoard(); vm.Refresh(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Мій стенд", vm.SelectedBoard!.Name);
            Assert.Equal(2, vm.SavedBoards.Count);
            window.FindControl<TabControl>("Tabs")!.SelectedIndex = 2;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            window.FindControl<BoardPickerControl>("BoardsPanel")!.IsExpanded = true;
            await vm.ScanSavedBoardsAsync(); Dispatcher.UIThread.RunJobs();
            Assert.Contains("Недоступна", vm.BoardAvailability("proptest-v2-222222.local"));
            var output = Environment.GetEnvironmentVariable("PROPTEST_ARTIFACTS");
            foreach (var size in new[] { new PixelSize(1220, 850), new PixelSize(960, 680) }) {
                window.Width = size.Width; window.Height = size.Height;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                if (output is null) continue;
                Directory.CreateDirectory(output);
                using var image = new RenderTargetBitmap(size, new Vector(96,96));
                image.Render(window); image.Save(Path.Combine(output, $"saved-boards-{size.Width}.png"));
            }
            using var helpImage = new RenderTargetBitmap(new PixelSize(620,590), new Vector(96,96));
            var help = new ConnectionHelpWindow(); help.Show(); help.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            if (output is not null) { helpImage.Render(help); helpImage.Save(Path.Combine(output, "connection-help.png")); }
            help.Close();
        } finally { window.Close(); }
    }
}
