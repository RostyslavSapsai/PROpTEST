using System.Globalization;

namespace PropTest.Core;

public static class TelemetryProtocol
{
    public const int MaxFrameLength = 512;
    public static string Encode(Measurement s) => FormattableString.Invariant($"PT2|S|{s.RunId:D}|{s.Sequence}|{s.ElapsedMs}|{s.ThrottlePercent:R}|{s.ThrustGrams:R}|{s.CurrentAmps:R}|{s.VibrationX:R}|{s.VibrationY:R}|{s.VibrationZ:R}|{s.SoundDb:R}|{s.ThrustRaw:R}|{s.SoundAdc:R}");
    public static bool TryParse(string frame, out Measurement? sample)
    {
        sample = null;
        if (frame.Length > MaxFrameLength) return false;
        var p = frame.TrimEnd('\r', '\n').Split('|');
        if (p.Length is not (8 or 12 or 14) || p[0] != "PT2" || p[1] != "S" || !Guid.TryParseExact(p[2], "D", out var id)) return false;
        if (!long.TryParse(p[3], NumberStyles.None, CultureInfo.InvariantCulture, out var seq) || seq < 1
            || !long.TryParse(p[4], NumberStyles.None, CultureInfo.InvariantCulture, out var time)) return false;
        var values = new double?[3];
        for (var i = 0; i < 3; i++)
        {
            if (i > 0 && p[i + 5].Length == 0 && p.Length == 14) continue;
            if (!double.TryParse(p[i + 5], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) return false;
            values[i] = value;
        }
        if (values[0] is < 0 or > 100) return false;
        sample = new(id, seq, time, values[0]!.Value, values[1], values[2]);
        if (p.Length >= 12)
        {
            var extra = new double?[6];
            for (int i = 0; i < p.Length - 8; i++)
            {
                if (p[i + 8].Length == 0) continue;
                if (!double.TryParse(p[i + 8], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v)) { sample = null; return false; }
                extra[i] = v;
            }
            sample = sample with { VibrationX = extra[0], VibrationY = extra[1], VibrationZ = extra[2], SoundDb = extra[3], ThrustRaw = extra[4], SoundAdc = extra[5] };
        }
        return true;
    }
}
