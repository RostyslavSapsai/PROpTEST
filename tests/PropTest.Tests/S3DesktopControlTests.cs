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
    sealed class FourBoard : HttpMessageHandler
    {
        public int[] Gas=new int[4];
        public bool Armed, BadStop, BadPins;
        public List<string> Commands=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation) {
            string path=request.RequestUri!.AbsolutePath;
            string body=request.Content is null?"":await request.Content.ReadAsStringAsync(cancellation);
            Commands.Add(path+"?"+body);
            if(path=="/arm")Armed=true;
            if(path=="/gas") {
                var fields=body.Split('&').Select(v=>v.Split('=')).ToDictionary(v=>v[0],v=>v[1]);
                Assert.True(Armed);Assert.Equal("1234",fields["token"]);
                Gas[int.Parse(fields["motor"])-1]=int.Parse(fields["value"]);
            }
            if(path=="/stop"){Armed=false;if(!BadStop)Array.Clear(Gas);}
            var motors=Gas.Select((g,i)=>new S3MotorState(BadPins?14:10+i,g,g)).ToArray();
            return new(HttpStatusCode.OK) {Content=new StringContent(JsonSerializer.Serialize(new S3ControlState("S3-Connect 0.4.0",10,100,Armed,true,Gas[0],Gas[0],"STOP",false,"app0",10000,path=="/arm"?"1234":null,motors)))};
        }
    }
    [Fact]
    public async Task FourChannelStopChecksEveryOutputAndPin() {
        var board=new FourBoard {BadStop=true};board.Gas[3]=50;
        using var client=new S3ControlClient("http://127.0.0.1/","test",board);
        await Assert.ThrowsAsync<IOException>(()=>client.SendAsync("/stop",new()));
        board.BadStop=false;var stopped=await client.SendAsync("/stop",new());Assert.All(stopped.Motors!,m=>Assert.Equal(0,m.Applied));
        board.BadPins=true;await Assert.ThrowsAsync<IOException>(()=>client.SendAsync("/status"));
    }
    [AvaloniaFact]
    public async Task FourMotorsHaveIndependentCommandsAndAlwaysVisibleStop() {
        var board=new FourBoard();
        var window=new S3ControlWindow("http://127.0.0.1/","test",(a,k)=>new S3ControlClient(a,k,board),motor:2,requireFour:true);window.Show();
        Button Find(string label)=>window.GetLogicalDescendants().OfType<Button>().Single(b=>Equals(b.Content,label));
        async Task Click(string label){Find(label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(50);Dispatcher.UIThread.RunJobs();}
        try {
            await Task.Delay(80);Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(board.Commands,c=>c.StartsWith("/arm"));
            await Click("Підготувати тест");
            var slider=window.GetLogicalDescendants().OfType<Slider>().Single();slider.Value=4;
            await Click("Задати газ");Assert.Equal(new[]{0,0,40,0},board.Gas);
            window.SelectMotor(0);slider.Value=2;await Click("Задати газ");Assert.Equal(new[]{20,0,40,0},board.Gas);
            await Click("Зупинити цей мотор");Assert.Equal(new[]{0,0,40,0},board.Gas);
            window.SelectMotor(2);
            var output=Environment.GetEnvironmentVariable("PROPTEST_ARTIFACTS");
            foreach(var size in new[]{new PixelSize(660,780),new PixelSize(500,550)}) {
                window.Width=size.Width;window.Height=size.Height;window.UpdateLayout();Dispatcher.UIThread.RunJobs();
                var stop=Find("■ STOP · усі мотори");var at=stop.TranslatePoint(new Point(),window)!.Value;Assert.InRange(at.Y,0,window.ClientSize.Height-stop.Bounds.Height);
                if(output is not null){Directory.CreateDirectory(output);using var image=new RenderTargetBitmap(size,new Vector(96,96));image.Render(window);image.Save(Path.Combine(output,$"four-control-{size.Width}.png"));}
            }
            await Click("■ STOP · усі мотори");Assert.All(board.Gas,g=>Assert.Equal(0,g));Assert.False(slider.IsEnabled);
        } finally {window.Close();}
    }
    [AvaloniaFact]
    public async Task FourMotorEntryDoesNotArmLegacyGpio14() {
        var board=new FakeBoard();var window=new S3ControlWindow("http://127.0.0.1/","test",(a,k)=>new S3ControlClient(a,k,board),requireFour:true);window.Show();
        try {await Task.Delay(80);Dispatcher.UIThread.RunJobs();Assert.False(window.GetLogicalDescendants().OfType<Button>().Single(b=>Equals(b.Content,"Підготувати тест")).IsEnabled);Assert.DoesNotContain("/arm",board.Requests);}
        finally {window.Close();}
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
            Find("■ STOP · усі мотори").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
