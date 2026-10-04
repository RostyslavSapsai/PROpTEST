using PropTest.Core;
using PropTest.Infrastructure;
using Xunit;

namespace PropTest.Tests;
public sealed class CoreTests
{
    static RunSettings Settings => new("Перевірка", 100, 30, 60);
    static SqliteRunRepository Store() => new(Path.Combine(Path.GetTempPath(), "PropTestEngineering-tests", Guid.NewGuid().ToString(), "test.db"));
    static void Advance(RunController controller, int start, int end) { for (var t = start; t <= end; t += 50) controller.Tick(t); }

    [Fact]
    public void StopZerosOutputAndSeparateRunsRoundTrip()
    {
        var store = Store(); var sim = new SimulatedDevice(); using var controller = new RunController(sim, store);
        controller.Connect(); controller.Start(Settings, 0); controller.SetThrottle(25); Advance(controller, 50, 1000);
        var first = controller.Snapshot(); Assert.NotEmpty(first.Samples); Assert.True(sim.AppliedThrottle > 0);
        controller.Stop(); Assert.Equal(0, sim.AppliedThrottle); Assert.Equal(0, controller.Snapshot().RequestedThrottle);
        Assert.Equal(first.Samples, store.Load(first.RunId!.Value).Samples);
        controller.Start(Settings, 1100); Assert.Empty(controller.Snapshot().Samples); Advance(controller, 1150, 1500); controller.Stop();
        var second = controller.Snapshot(); Assert.NotEqual(first.RunId, second.RunId); Assert.Equal(1, second.Samples[0].Sequence);
        Assert.All(second.Samples, s => Assert.Equal(second.RunId, s.RunId)); Assert.Equal(2, store.List().Count);
    }
    [Fact]
    public void LossAfterFirstSampleStopsDeviceAndFaultsHost()
    {
        var store = Store(); var sim = new SimulatedDevice(); using var controller = new RunController(sim, store);
        controller.Connect(); controller.Start(Settings, 0); controller.SetThrottle(30); Advance(controller, 50, 1000);
        sim.LinkLost = true; Advance(controller, 1050, 1800); Assert.Equal(0, sim.AppliedThrottle);
        Advance(controller, 1850, 3100); Assert.Equal(RunState.Faulted, controller.Snapshot().State);
        Assert.Equal("TelemetryLost", store.List()[0].Outcome);
        Assert.Throws<InvalidOperationException>(() => controller.Start(Settings, 3200));
        controller.Connect(); controller.Start(Settings, 3200); Assert.Empty(controller.Snapshot().Samples);
    }
    [Fact]
    public void DeviceLeaseExpiresWithoutHostRenewal()
    {
        using var sim = new SimulatedDevice(); sim.Connect(); sim.Begin(Guid.NewGuid(), Settings, 0); sim.SetThrottle(30);
        sim.Poll(500); Assert.True(sim.AppliedThrottle > 0); sim.Poll(751); Assert.Equal(0, sim.AppliedThrottle);
        sim.Heartbeat(800); sim.Poll(900); Assert.Equal(0, sim.AppliedThrottle);
    }
    [Fact]
    public void TimeLimitIsDistinctFromUserStop()
    {
        var store = Store(); var sim = new SimulatedDevice(); using var c = new RunController(sim, store);
        c.Connect(); c.Start(Settings with { DurationSeconds = 1 }, 0); c.SetThrottle(10); Advance(c, 50, 1000);
        Assert.Equal(RunState.Ready, c.Snapshot().State); Assert.Equal("TimeLimit", store.List()[0].Outcome); Assert.Equal(0, sim.AppliedThrottle);
    }
    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e999")]
    [InlineData("abc")]
    public void InvalidMeasurementsAreRejected(string value)
    {
        Assert.False(TelemetryProtocol.TryParse($"PT2|S|{Guid.NewGuid():D}|1|100|10|{value}|1", out _));
    }
    [Fact]
    public void ProtocolPreservesZeroNegativeThrustAndExactUnits()
    {
        var expected = new Measurement(Guid.NewGuid(), 1, 125, 0, -1.25, .5);
        Assert.True(TelemetryProtocol.TryParse(TelemetryProtocol.Encode(expected), out var result)); Assert.Equal(expected, result);
        Assert.False(TelemetryProtocol.TryParse(TelemetryProtocol.Encode(expected) + "|extra", out _));
        Assert.False(TelemetryProtocol.TryParse(new string('x', 257), out _));
    }
    [Fact]
    public void ThrottleAboveConfiguredLimitIsRejected()
    {
        var sim = new SimulatedDevice(); using var c = new RunController(sim, Store()); c.Connect(); c.Start(Settings, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => c.SetThrottle(31)); Assert.Throws<ArgumentOutOfRangeException>(() => c.SetThrottle(double.NaN));
        Assert.Equal(0, sim.AppliedThrottle);
    }
    [Fact]
    public void IncompleteRunRecoversWithoutLosingSamples()
    {
        var store = Store(); var id = Guid.NewGuid(); store.Begin(new(id, DateTimeOffset.UtcNow, Settings, "SIM", "Running", 0));
        store.Append(new(id, 1, 100, 0, 0, .3));
        var reopened = new SqliteRunRepository(store.DatabasePath); Assert.Equal("Interrupted", reopened.Load(id).Info.Outcome); Assert.Single(reopened.Load(id).Samples);
    }
    [Fact]
    public void StorageFailureStopsOutput()
    {
        var sim = new SimulatedDevice(); var store = new FailingStore(); using var c = new RunController(sim, store);
        c.Connect(); c.Start(Settings, 0); c.SetThrottle(30); c.Tick(100);
        Assert.Equal(RunState.Faulted, c.Snapshot().State); Assert.Equal(0, sim.AppliedThrottle); Assert.Equal("Fault", store.Outcome);
    }
    [Fact]
    public void DecoderHandlesSplitCombinedAndOversizedFrames()
    {
        var decoder = new TelemetryFrameDecoder();
        Assert.Empty(decoder.Push("PT"));
        Assert.Equal(new[] { "PT2|one", "two" }, decoder.Push("2|one\r\ntwo\npartial"));
        decoder.Reset(); Assert.Equal(new[] { "new" }, decoder.Push("new\n"));
        Assert.Empty(decoder.Push(new string('x', 4000)));
        Assert.Equal(new[] { "valid" }, decoder.Push("ignored\nvalid\n"));
    }
    [Fact]
    public void StaleDuplicateAndBackwardsSamplesCannotContaminateRun()
    {
        var store = Store(); var device = new ScriptedDevice(); using var c = new RunController(device, store);
        c.Connect(); c.Start(Settings, 0); var id = c.Snapshot().RunId!.Value;
        device.Queue(new(id, 1, 100, 10, 1, 2)); c.Tick(100);
        device.Queue(new(Guid.NewGuid(), 2, 200, 10, 999, 2));
        device.Queue(new(id, 1, 200, 10, 999, 2));
        device.Queue(new(id, 2, 50, 10, 999, 2));
        device.Queue(new(id, 3, 9000, 10, 999, 2)); c.Tick(200);
        Assert.Single(c.Snapshot().Samples);
        c.Tick(2201); Assert.Equal(RunState.Faulted, c.Snapshot().State);
    }
    [Fact]
    public void ReportedOverLimitOutputStopsRatherThanClampsEvidence()
    {
        var device = new ScriptedDevice(); using var c = new RunController(device, Store()); c.Connect(); c.Start(Settings, 0);
        device.Queue(new(c.Snapshot().RunId!.Value, 1, 100, 31, 10, 2)); c.Tick(100);
        Assert.True(device.Stopped); Assert.Equal(RunState.Faulted, c.Snapshot().State); Assert.Empty(c.Snapshot().Samples);
    }
    [Fact]
    public void PartialStartFailureStillSendsStopAndRequiresReconnect()
    {
        var store = Store(); var device = new ScriptedDevice { FailBegin = true }; using var c = new RunController(device, store);
        c.Connect(); c.Start(Settings, 0); Assert.True(device.Stopped); Assert.Equal(RunState.Faulted, c.Snapshot().State);
        Assert.Equal("StartFailed", store.List()[0].Outcome);
        c.Stop(); Assert.Equal(RunState.Faulted, c.Snapshot().State);
    }
    sealed class ScriptedDevice : ITestDevice
    {
        readonly List<string> chunks = new();
        public bool FailBegin; public bool Stopped;
        public string Name => "Test";
        public void Queue(Measurement s) => chunks.Add(TelemetryProtocol.Encode(s) + "\n");
        public void Connect() { }
        public void Begin(Guid id, RunSettings settings, long nowMs) { if (FailBegin) throw new IOException("Partial start"); }
        public void SetThrottle(double p) { }
        public void Heartbeat(long t) { }
        public void Stop() => Stopped = true;
        public IReadOnlyList<string> Poll(long t) { var result = chunks.ToArray(); chunks.Clear(); return result; }
        public void Dispose() { }
    }
    sealed class FailingStore : IRunRepository
    {
        public string? Outcome;
        public void Begin(RunInfo r) { }
        public void Append(Measurement s) => throw new IOException("Disk full");
        public void Finish(Guid id, string outcome) => Outcome = outcome;
        public IReadOnlyList<RunInfo> List() => Array.Empty<RunInfo>();
        public RunData Load(Guid id) => throw new NotSupportedException();
    }
}
