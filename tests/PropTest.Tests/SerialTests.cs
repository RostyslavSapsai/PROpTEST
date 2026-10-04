using PropTest.Core;
using PropTest.Infrastructure;
using Xunit;

namespace PropTest.Tests;
public sealed class SerialTests
{
    [Fact]
    public void FineThrottleUsesTenthsAndOlderFirmwareRejectsFractions()
    {
        var wire = new FakeSerial { Reply = "PT:2.2\n" };
        using var device = new SerialTestDevice(_ => wire, 100) { PortName = "TEST" };
        device.Connect(); Assert.True(device.SupportsFineThrottle);
        device.Begin(Guid.NewGuid(), new("test", 200, 15, 60), 0);
        device.SetThrottle(6.1); Assert.Equal("U:61", wire.Commands[^1]);
        device.SetThrottle(15); Assert.Equal("U:150", wire.Commands[^1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => device.SetThrottle(15.1));
        wire.Input = "D|1|10000|6.1|123456|1|0|1|0|22.25|1|-123\n";
        Assert.True(TelemetryProtocol.TryParse(Assert.Single(device.Poll(200)), out var sample));
        Assert.Equal(6.1, sample!.ThrottlePercent); Assert.Null(sample.SoundDb); Assert.Equal(22.25, sample.SoundAdc);
        device.Stop(); Assert.Equal("G", wire.Commands[^1]);
        wire.Reply = "PT:2.1\n"; device.Connect(); Assert.False(device.SupportsFineThrottle);
        device.Begin(Guid.NewGuid(), new("test", 200, 15, 60), 0);
        Assert.Throws<InvalidOperationException>(() => device.SetThrottle(6.1));
    }
    [Theory]
    [InlineData("PT:2.0\n")]
    [InlineData("PT:2.1\n")]
    public void CurrentFirmwareUsesDirectPercentAcknowledgedStopAndHeartbeat(string reply)
    {
        var wire = new FakeSerial { Reply = reply };
        using var device = new SerialTestDevice(_ => wire, 100) { PortName = "TEST" };
        device.Connect(); Assert.True(device.HasWatchdog);
        Assert.Throws<InvalidOperationException>(() => device.Begin(Guid.NewGuid(), new("test", 200, 100, 60), 0));
        device.Begin(Guid.NewGuid(), new("test", 200, 30, 60), 1000);
        device.SetThrottle(19); Assert.Equal("W:19", wire.Commands[^1]);
        device.Heartbeat(1250); Assert.Equal("H", wire.Commands[^1]);
        wire.Input = "D|1|10000|19|123456||0.1|0.2|1|200|1\n";
        Assert.True(TelemetryProtocol.TryParse(Assert.Single(device.Poll(1300)), out var sample));
        Assert.Null(sample!.ThrustGrams); Assert.Null(sample.CurrentAmps); Assert.Null(sample.SoundDb);
        Assert.Equal(123456, sample.ThrustRaw); Assert.Equal(200, sample.SoundAdc);
        Assert.True(SerialTestDevice.TryParseCurrent("D|2|10200|19|120000|0.4|0|1|0|200|1|-12.5", Guid.NewGuid(), 200, out var recovered));
        Assert.Equal(12.5, recovered!.ThrustGrams);
        wire.Input = "D|1|10000|19|123456||0.1|0.2|1|200|1\n";
        Assert.Empty(device.Poll(1350));
        wire.Input = "FAULT:CURRENT\n";
        Assert.Contains("датчика струму", Assert.Throws<InvalidOperationException>(() => device.Poll(1400)).Message);
        device.Stop(); Assert.Equal("G", wire.Commands[^1]);
    }
    [Theory]
    [InlineData("D|1|1|31||||||100|1")]
    [InlineData("D|1|1|0||||||100|0")]
    [InlineData("D|1|1|0|NaN|||||100|1")]
    [InlineData("D|1|1|0||||||1024|1")]
    public void CurrentFirmwareRejectsInvalidOrDisarmedMeasurements(string frame) =>
        Assert.False(SerialTestDevice.TryParseCurrent(frame, Guid.NewGuid(), 0, out _));

    [Fact]
    public void DiscoverySkipsBluetoothAndRetriesOnlyAfterReattachment()
    {
        var tracker = new AutoConnectionTracker();
        SerialPortInfo[] present = [new("COM3", "Bluetooth", false), new("COM5", "Arduino Uno", true)];
        tracker.Observe(present); Assert.Equal("COM5", tracker.Next(present));
        Assert.Null(tracker.Next(present));
        tracker.Observe([present[0]]); tracker.Observe(present); Assert.Equal("COM5", tracker.Next(present));
        tracker.Suppress("COM5"); Assert.Null(tracker.Next(present));
    }
    [Fact]
    public void SilentPortIsNotAcceptedAsLegacyAndReceivesNoMotorCommands()
    {
        var wire = new FakeSerial { Reply = "" };
        using var device = new SerialTestDevice(_ => wire, 40) { PortName = "TEST" };
        Assert.Throws<InvalidOperationException>(device.Connect);
        Assert.True(wire.Disposed); Assert.NotEmpty(wire.Commands);
        Assert.All(wire.Commands, command => Assert.Equal("P", command));
    }
    [Fact]
    public void HandshakeAllowsWhitespaceAndCaseAsOldApplicationDid()
    {
        var wire = new FakeSerial { Reply = "  pt:1.0 \r\n" };
        using var device = new SerialTestDevice(_ => wire, 100) { PortName = "TEST" };
        device.Connect(); Assert.Equal(new[] { "P", "G" }, wire.Commands);
    }
    [Fact]
    public void LegacyCommandsPreserveLimitAndTelemetryAcrossChunks()
    {
        var wire = new FakeSerial();
        using var device = new SerialTestDevice(_ => wire) { PortName = "TEST" };
        device.Connect(); Assert.Equal(new[] { "P", "G" }, wire.Commands);
        var id = Guid.NewGuid(); device.Begin(id, new("Test", 200, 30, 60), 1000);
        Assert.Contains("M:30", wire.Commands); Assert.Contains("F:200", wire.Commands); Assert.Equal("W:0", wire.Commands[^1]);
        device.SetThrottle(19); Assert.Equal("W:64", wire.Commands[^1]);
        device.SetThrottle(30); Assert.Equal("W:100", wire.Commands[^1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => device.SetThrottle(31));
        wire.Input = "12.5|0.1|-0.2|1.0|"; Assert.Empty(device.Poll(1100));
        wire.Input = "2.3|45.6|19\n";
        var frame = Assert.Single(device.Poll(1200)); Assert.True(TelemetryProtocol.TryParse(frame, out var sample));
        Assert.Equal(id, sample!.RunId); Assert.Equal(200, sample.ElapsedMs); Assert.Equal(19, sample.ThrottlePercent);
        Assert.Equal(12.5, sample.ThrustGrams); Assert.Equal(2.3, sample.CurrentAmps); Assert.Equal(-.2, sample.VibrationY); Assert.Equal(45.6, sample.SoundDb);
        device.SetThrottle(0); device.Poll(1400); Assert.Equal("W:0", wire.Commands[^1]);
        device.Stop(); Assert.Equal("G", wire.Commands[^1]); Assert.Empty(device.Poll(2000));
        device.Disconnect(); Assert.True(wire.Disposed);
    }
    [Theory]
    [InlineData("PT:1.0")]
    [InlineData("1|2|3|4|5|6")]
    [InlineData("1|2|3|4|5|NaN|10")]
    [InlineData("1|2|3|4|5|6|101")]
    public void RejectsNonMeasurementAndInvalidTelemetry(string line) => Assert.False(SerialTestDevice.TryParseLegacy(line, Guid.NewGuid(), 1, 0, out _));
    [Fact]
    public void OldFramesRemainReadableWithoutInventingChannels()
    {
        Assert.True(TelemetryProtocol.TryParse($"PT2|S|{Guid.NewGuid():D}|1|0|0|0|0", out var s));
        Assert.Null(s!.VibrationX); Assert.Null(s.SoundDb);
    }
    [Fact]
    public void DisconnectCannotProduceSyntheticMeasurements()
    {
        var store = new SqliteRunRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "test.db"));
        using var controller = new RunController(new SelectableDevice(), store);
        Assert.Throws<InvalidOperationException>(() => controller.Start(new("test", 200, 30, 60), 0));
        controller.Tick(1000); Assert.Empty(controller.Snapshot().Samples); Assert.Empty(store.List());
    }
    sealed class FakeSerial : ISerialConnection
    {
        public List<string> Commands { get; } = new();
        public string Input = "";
        public string Reply = "PT:1.0\n";
        public bool Disposed;
        public void Open() { }
        public void WriteLine(string command)
        {
            Commands.Add(command); if (command == "P") Input += Reply;
            if ((Reply == "PT:2.0\n" || Reply == "PT:2.1\n" || Reply == "PT:2.2\n") && command is "G" or "B") Input += $"ACK:{command}:0\n";
        }
        public string ReadAvailable() { var result = Input; Input = ""; return result; }
        public void ClearInput() => Input = "";
        public void Dispose() => Disposed = true;
    }
}



