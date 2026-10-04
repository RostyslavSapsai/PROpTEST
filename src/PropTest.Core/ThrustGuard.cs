namespace PropTest.Core;

// Host-side anomaly stop, not an independent hardware safety circuit.
public sealed class ThrustGuard
{
    readonly Queue<double> baseline = new();
    double requested = double.NaN, applied = double.NaN;
    long stableSince, previousTime;
    int violations;
    public bool Observe(Measurement sample, double target, RunSettings settings)
    {
        if (!settings.ThrustGuardEnabled) return false;
        if (target != requested || sample.ThrottlePercent != applied || sample.ThrustGrams is null
            || sample.ElapsedMs - previousTime > Math.Max(1000, settings.SampleIntervalMs * 2))
        {
            baseline.Clear(); violations = 0; stableSince = sample.ElapsedMs;
            requested = target; applied = sample.ThrottlePercent;
        }
        previousTime = sample.ElapsedMs;
        if (sample.ThrustGrams is not { } force || Math.Abs(applied - target) > .05 || sample.ElapsedMs - stableSince < 1500) return false;
        if (baseline.Count >= 3)
        {
            var mean = baseline.Average();
            var threshold = Math.Max(settings.ThrustJumpGrams, Math.Abs(mean) * settings.ThrustJumpPercent / 100);
            if (Math.Abs(force - mean) >= threshold) return ++violations >= 2;
        }
        violations = 0; baseline.Enqueue(force);
        if (baseline.Count > 5) baseline.Dequeue();
        return false;
    }
}
