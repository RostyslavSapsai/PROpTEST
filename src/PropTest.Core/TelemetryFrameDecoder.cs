using System.Text;

namespace PropTest.Core;

/// <summary>Incremental bounded line framing; an oversized line is discarded through its newline.</summary>
public sealed class TelemetryFrameDecoder
{
    readonly StringBuilder buffer = new();
    bool discard;
    public void Reset() { buffer.Clear(); discard = false; }
    public IReadOnlyList<string> Push(string chunk)
    {
        var frames = new List<string>();
        foreach (var ch in chunk)
        {
            if (ch == '\n')
            {
                if (!discard && buffer.Length > 0) frames.Add(buffer.ToString().TrimEnd('\r'));
                Reset();
            }
            else if (!discard)
            {
                if (buffer.Length >= TelemetryProtocol.MaxFrameLength) { buffer.Clear(); discard = true; }
                else buffer.Append(ch);
            }
        }
        return frames;
    }
}
