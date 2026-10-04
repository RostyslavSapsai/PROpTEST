using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PropTest.Desktop;
using PropTest.Infrastructure;
using Xunit;

namespace PropTest.Tests;

public sealed class S3DesktopControlTests
{
    sealed class FakeBoard : HttpMessageHandler
    {
        public List<string> Requests = [];
        public TaskCompletionSource? ArmWait;
        public bool Armed;
        public int Pin = 14;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var path = request.RequestUri!.AbsolutePath; Requests.Add(path);
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            Assert.Equal("1", request.Headers.GetValues("X-Proptest").Single());
            if (path == "/arm") { if (ArmWait is not null) await ArmWait.Task.WaitAsync(cancellation); Armed = true; }
            if (path == "/stop") Armed = false;
            var state = new S3ControlState("S3-Connect 0.3.0", Pin, 100, Armed, true, 0, 0, "STOP", false, "app0", 10000, path == "/arm" ? "1234" : null);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(state)) };
        }
    }
    [Fact]
    public async Task RejectsUnexpectedPinAndNeverArmsOnStatus()
    {
        var board = new FakeBoard { Pin = 20 };
        using var client = new S3ControlClient("http://127.0.0.1/", "test", board);
        await Assert.ThrowsAsync<IOException>(() => client.SendAsync("/status"));
        Assert.Equal(new[] { "/status" }, board.Requests);
    }
    [AvaloniaFact]
    public async Task StopDuringPendingArmDoesNotStartHeartbeatOrResume()
    {
        var board = new FakeBoard();
        var window = new S3ControlWindow("http://127.0.0.1/", "test", (address, key) => new S3ControlClient(address, key, board));
        window.Show();
        Button Find(string text) => window.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => b.Content?.ToString() == text);
        try
        {
            Find("Підключитися").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(40); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("/arm", board.Requests);
            board.ArmWait = new TaskCompletionSource();
            Find("Підготувати тест").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(20);
            Find("■ STOP").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            board.ArmWait.SetResult();
            await Task.Delay(350); Dispatcher.UIThread.RunJobs();
            Assert.False(board.Armed); Assert.DoesNotContain("/lease", board.Requests);
            Assert.Contains("/stop", board.Requests);
            Assert.False(Find("Задати газ").IsEnabled);
            var directory = Environment.GetEnvironmentVariable("PROPTEST_ARTIFACTS");
            if (directory is not null) {
                Directory.CreateDirectory(directory); window.UpdateLayout();
                using var image = new RenderTargetBitmap(new PixelSize(660, 700), new Vector(96, 96));
                image.Render(window); image.Save(Path.Combine(directory, "native-motor.png"));
            }
        }
        finally { window.Close(); }
    }
}
