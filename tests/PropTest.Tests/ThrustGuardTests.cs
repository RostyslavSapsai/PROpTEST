using PropTest.Core;
using PropTest.Infrastructure;
using Xunit;

namespace PropTest.Tests;

public sealed class ThrustGuardTests
{
    static readonly RunSettings Settings = new("guard", 200, 30, 60) { ThrustGuardEnabled = true };
    static Measurement Sample(long time, double? force = 500, double throttle = 10) => new(Guid.Empty, time / 200 + 1, time, throttle, force, 1);
    static ThrustGuard Stable()
    {
        var guard = new ThrustGuard();
        for (int t = 0; t <= 2400; t += 200) Assert.False(guard.Observe(Sample(t), 10, Settings));
        return guard;
    }
    [Theory]
    [InlineData(-200)]
    [InlineData(800)]
    public void RequiresTwoExcursionsAfterStableThrottle(double changed)
    {
        var guard = Stable();
        Assert.False(guard.Observe(Sample(2600, changed), 10, Settings));
        Assert.True(guard.Observe(Sample(2800, changed), 10, Settings));
    }
    [Fact]
    public void IgnoresSingleSpikeRampMissingAndDisabled()
    {
        var guard = Stable();
        Assert.False(guard.Observe(Sample(2600, -300), 10, Settings));
        Assert.False(guard.Observe(Sample(2800, 500), 10, Settings));
        Assert.False(guard.Observe(Sample(3000, -300), 10, Settings));
        Assert.False(guard.Observe(Sample(3200, 200, 10.1), 11, Settings));
        Assert.False(guard.Observe(Sample(3400, 200, 11), 11, Settings));
        guard = Stable();
        Assert.False(guard.Observe(Sample(2600, -300), 10, Settings));
        Assert.False(guard.Observe(Sample(2800, null), 10, Settings));
        Assert.False(guard.Observe(Sample(3000, -300), 10, Settings));
        guard = Stable();
        for (int t = 2600; t < 4000; t += 200) Assert.False(guard.Observe(Sample(t, -300), 10, Settings with { ThrustGuardEnabled = false }));
    }
    [Fact]
    public void StopReachesDeviceAndOutcomeIsStored()
    {
        var repository = new SqliteRunRepository(Path.Combine(Path.GetTempPath(), "PropTestGuard", Guid.NewGuid().ToString(), "test.db"));
        var device = new ForceDevice();
        using var controller = new RunController(device, repository);
        controller.Connect(); controller.Start(Settings, 0); controller.SetThrottle(10);
        for (int t = 0; t <= 2800; t += 200) controller.Tick(t);
        Assert.True(device.Stopped); Assert.Equal(RunState.Faulted, controller.Snapshot().State);
        Assert.Equal(0, controller.Snapshot().RequestedThrottle);
        var record = Assert.Single(repository.List()); Assert.Equal("ThrustAnomaly", record.Outcome);
        Assert.True(record.Settings.ThrustGuardEnabled);
        Assert.Equal(-200, repository.Load(record.Id).Samples[^1].ThrustGrams);
    }
    sealed class ForceDevice : ITestDevice
    {
        Guid id;
        public bool Stopped;
        public string Name => "injected force";
        public void Connect() { }
        public void Begin(Guid runId, RunSettings settings, long nowMs) => id = runId;
        public void SetThrottle(double percent) { }
        public void Heartbeat(long nowMs) { }
        public void Stop() => Stopped = true;
        public IReadOnlyList<string> Poll(long nowMs) => [TelemetryProtocol.Encode(Sample(nowMs, nowMs >= 2600 ? -200 : 500) with { RunId = id }) + "\n"];
        public void Dispose() { }
    }
}
