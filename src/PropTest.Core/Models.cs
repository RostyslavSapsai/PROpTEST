namespace PropTest.Core;

public enum RunState { Disconnected, Connecting, Ready, Running, Faulted }
public sealed record RunSettings(string Name, int SampleIntervalMs, int ThrottleLimit, int DurationSeconds)
{
    public bool ThrustGuardEnabled { get; init; }
    public double ThrustJumpGrams { get; init; } = 50;
    public double ThrustJumpPercent { get; init; } = 30;
    public void Validate()
    {
        if (!double.IsFinite(ThrustJumpGrams) || ThrustJumpGrams < 1 || !double.IsFinite(ThrustJumpPercent) || ThrustJumpPercent is < 1 or > 1000)
            throw new ArgumentException("Поріг контролю тяги: від 1 г; відносний поріг 1–1000%.");
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("Не вказано назву тесту.");
        if (Name.Length > 120) throw new ArgumentException("Назва тесту задовга: більше 120 символів.");
        if (SampleIntervalMs is < 50 or > 2000) throw new ArgumentException("Інтервал: 50–2000 мс.");
        if (ThrottleLimit is < 1 or > 100) throw new ArgumentException("Ліміт газу: 1–100%.");
        if (DurationSeconds is < 1 or > 7200) throw new ArgumentException("Тривалість: 1–7200 с.");
    }
}
public sealed record Measurement(Guid RunId, long Sequence, long ElapsedMs, double ThrottlePercent, double? ThrustGrams, double? CurrentAmps)
{
    public double? ThrustRaw { get; init; }
    public double? SoundAdc { get; init; }
    public double? VibrationX { get; init; }
    public double? VibrationY { get; init; }
    public double? VibrationZ { get; init; }
    // Legacy firmware calls this dB, but its ADC conversion is not calibrated SPL.
    public double? SoundDb { get; init; }
}
public sealed record RunInfo(Guid Id, DateTimeOffset StartedUtc, RunSettings Settings, string Source, string Outcome, int SampleCount)
{
    public DateTimeOffset StartedLocal => StartedUtc.ToLocalTime();
}
public sealed record RunData(RunInfo Info, IReadOnlyList<Measurement> Samples);
public sealed record SessionSnapshot(RunState State, string Message, Guid? RunId, double RequestedThrottle, IReadOnlyList<Measurement> Samples);

public interface IRunRepository
{
    void Begin(RunInfo run);
    void Append(Measurement sample);
    void Finish(Guid id, string outcome);
    IReadOnlyList<RunInfo> List();
    RunData Load(Guid id);
}

// The device boundary is independent of serial, UI and storage.
public interface ITestDevice : IDisposable
{
    string Name { get; }
    bool CanRunTests => true;
    void CheckConnection(long nowMs) { }
    void ConfigureWifi(string ssid, string password) => throw new InvalidOperationException("Ця прошивка не підтримує налаштування Wi-Fi.");
    void Connect();
    void Begin(Guid runId, RunSettings settings, long nowMs);
    void SetThrottle(double percent);
    void Heartbeat(long nowMs);
    void Stop();
    void Disconnect() => Stop();
    // Returns received text chunks, which may contain partial or multiple newline-delimited frames.
    IReadOnlyList<string> Poll(long nowMs);
}


