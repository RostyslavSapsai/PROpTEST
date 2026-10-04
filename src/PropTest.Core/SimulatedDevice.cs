namespace PropTest.Core;

public sealed class SimulatedDevice : ITestDevice
{
    public string Name => "SIM / synthetic-v1";
    volatile bool linkLost;
    public bool LinkLost { get => linkLost; set => linkLost = value; }
    public double AppliedThrottle { get; private set; }
    public const int LeaseMs = 750;
    bool connected, active;
    Guid runId;
    RunSettings? settings;
    long started, lastSample, lastHeartbeat, lastTick, sequence;
    double requested;

    public void Connect() { connected = true; LinkLost = false; Stop(); }
    public void Begin(Guid id, RunSettings configuration, long nowMs)
    {
        configuration.Validate();
        if (!connected || LinkLost) throw new InvalidOperationException("Симулятор не підключено.");
        runId = id; settings = configuration; started = lastTick = lastHeartbeat = lastSample = nowMs;
        sequence = 0; requested = AppliedThrottle = 0; active = true;
    }
    public void SetThrottle(double percent)
    {
        if (LinkLost) return;
        if (!active || settings is null) throw new InvalidOperationException("Спочатку почніть тест.");
        if (!double.IsFinite(percent) || percent < 0 || percent > settings.ThrottleLimit) throw new ArgumentOutOfRangeException(nameof(percent));
        requested = percent;
    }
    public void Heartbeat(long nowMs) { if (!LinkLost) lastHeartbeat = nowMs; }
    public void Stop() { if (!LinkLost) LocalStop(); }
    void LocalStop() { requested = AppliedThrottle = 0; active = false; }
    public IReadOnlyList<string> Poll(long nowMs)
    {
        if (!active || settings is null) return Array.Empty<string>();
        if (nowMs - lastHeartbeat >= LeaseMs || nowMs - started >= settings.DurationSeconds * 1000L)
        { LocalStop(); return Array.Empty<string>(); }
        // Synthetic slew rate: 20 percentage points/s. This is not a motor model or a hardware limit.
        var change = Math.Max(0, nowMs - lastTick) * .02;
        AppliedThrottle += Math.Clamp(requested - AppliedThrottle, -change, change);
        lastTick = nowMs;
        if (LinkLost || nowMs - lastSample < settings.SampleIntervalMs) return Array.Empty<string>();
        lastSample = nowMs;
        var t = AppliedThrottle / 100;
        var s = new Measurement(runId, ++sequence, nowMs - started, AppliedThrottle,
            680 * t * t, .3 + 4.2 * t * t)
        { VibrationX = t * Math.Sin(sequence * .7), VibrationY = t * Math.Cos(sequence * .9), VibrationZ = 1 + t * Math.Sin(sequence), SoundAdc = 20 + t * 700 };
        return new[] { TelemetryProtocol.Encode(s) + "\n" };
    }
    public void Dispose() { LocalStop(); connected = false; }
}

