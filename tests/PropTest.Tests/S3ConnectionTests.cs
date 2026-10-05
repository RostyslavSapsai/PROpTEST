using PropTest.Core;
using PropTest.Infrastructure;
using Xunit;
using System.Net;
using System.Net.Sockets;

namespace PropTest.Tests;

public sealed class S3ConnectionTests
{
    [Theory]
    [InlineData("0.3")]
    [InlineData("0.4")]
    public void S3OtaRecognizedWithoutStartingFakeSensorRecording(string version)
    {
        var wire = new Link { Identity = "PT:S3:"+version };
        using var device = new SerialTestDevice(_ => wire, 100) { PortName = "TEST" };
        device.Connect();
        Assert.True(device.SupportsBoardWeb); Assert.True(device.SupportsHomeWifi);
        Assert.False(device.CanRunTests); Assert.Contains(version+".0", device.Name);
        Assert.Throws<InvalidOperationException>(() => device.Begin(Guid.NewGuid(), new("test", 200, 10, 30), 0));
        Assert.DoesNotContain("B", wire.Commands);
        device.Stop(); Assert.Equal("G", wire.Commands[^1]);
    }
    [Fact]
    public void RouterCredentialsAreValidatedAndOnlySentOverUsb()
    {
        var wire = new Link { Identity = "PT:S3:0.2" };
        using var device = new SerialTestDevice(_ => wire, 100) { PortName = "TEST" };
        device.Connect(); Assert.True(device.SupportsHomeWifi);
        Assert.Throws<ArgumentException>(() => device.ConfigureWifi("home", "short"));
        Assert.DoesNotContain(wire.Commands, c => c.StartsWith("NET:"));
        device.ConfigureWifi("home", "test-only-key");
        Assert.Equal("NET:686F6D65:746573742D6F6E6C792D6B6579", wire.Commands[^1]);
        wire.Input = "NETWORK|CONNECTED|192.168.1.42|proptest-test.local\n";
        device.CheckConnection(Environment.TickCount64);
        Assert.Equal("192.168.1.42", device.HomeNetworkAddress);
        Assert.Contains("Плата в мережі", device.HomeNetworkStatus);
        using var remote = new SerialTestDevice(_ => new Link { Identity = "PT:S3:0.2" }, 100) { PortName = "TEST", NetworkOnly = true };
        remote.Connect(); Assert.Throws<InvalidOperationException>(() => remote.ConfigureWifi("home", "test-only-key"));
    }
    [Theory]
    [InlineData("VID_303A&PID_4001&MI_00", true)]
    [InlineData("VID_303A&PID_1001", true)]
    [InlineData("VID_1A86&PID_55D3", true)]
    [InlineData("VID_2341&PID_0043", true)]
    [InlineData("BTHENUM\\COM3", false)]
    public void UsbCandidatesRequireFirmwareHandshake(string id, bool expected) => Assert.Equal(expected, SerialPortCatalog.IsCandidateUsbId(id));

    [Fact]
    public void S3ConnectHasNoMeasurementsOrMotorCommandsAndDetectsLostHeartbeat()
    {
        var wire = new Link();
        using var device = new SerialTestDevice(_ => wire, 100) { PortName = "TEST" };
        var repository = new SqliteRunRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "runs.db"));
        using var controller = new RunController(device, repository);
        controller.Connect(); Assert.False(device.CanRunTests);
        Assert.Throws<InvalidOperationException>(() => controller.Start(new("test", 200, 30, 60), 0));
        Assert.Empty(repository.List()); Assert.Empty(controller.Snapshot().Samples);
        var now = Environment.TickCount64;
        controller.Tick(now); controller.Tick(now + 1);
        Assert.Contains("PROpTEST v2", device.BoardInfo);
        Assert.Equal("test-key-only", device.WifiKey);
        Assert.Equal(RunState.Ready, controller.Snapshot().State);
        wire.Answer = false;
        controller.Tick(now + 4000);
        Assert.Equal(RunState.Faulted, controller.Snapshot().State);
        Assert.All(wire.Commands, command => Assert.Contains(command, new[] { "P", "I", "H", "G" }));
    }

    [Fact]
    public async Task TcpTransportPerformsSameHandshakeAndRefusesLegacyMotorProtocol()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync();
            using var reader = new StreamReader(peer.GetStream());
            using var writer = new StreamWriter(peer.GetStream()) { AutoFlush = true };
            Assert.Equal("P", await reader.ReadLineAsync());
            await writer.WriteLineAsync("PT:S3:0.1");
            Assert.Equal("I", await reader.ReadLineAsync());
        });
        using var device = new SerialTestDevice(host => new TcpConnection(host, port), 1000) { PortName = "127.0.0.1", NetworkOnly = true };
        device.Connect(); Assert.True(device.IsS3); Assert.False(device.CanRunTests);
        await server.WaitAsync(TimeSpan.FromSeconds(3));
        using var legacy = new SerialTestDevice(_ => new Link { Identity = "PT:2.2" }, 80) { PortName = "TEST", NetworkOnly = true };
        Assert.Throws<IOException>(legacy.Connect);
    }

    sealed class Link : ISerialConnection
    {
        public string Identity = "PT:S3:0.1", Input = "";
        public bool Answer = true;
        public List<string> Commands = new();
        public void Open() { }
        public void WriteLine(string command)
        {
            Commands.Add(command);
            if (!Answer) return;
            Input += command switch { "P" => Identity + "\n", "I" => "INFO|PROpTEST v2|S3-Connect 0.1.0|NO_SENSORS|NO_MOTOR|test|192.168.4.1|8768\nWIFIKEY|test-key-only\n", "H" => "PONG\n", "G" => "ACK:G:0\n", _ => "" };
        }
        public string ReadAvailable() { var result = Input; Input = ""; return result; }
        public void ClearInput() => Input = "";
        public void Dispose() { }
    }
}
