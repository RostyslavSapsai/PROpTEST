using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PropTest.Core;
using System.Globalization;

namespace PropTest.Desktop;

public enum ChartChannel { Thrust, Current, Vibration, Sound }
public sealed class TelemetryChart : Control
{
    IReadOnlyList<Measurement> samples = Array.Empty<Measurement>();
    public ChartChannel Channel { get; set; }
    public bool Current { get => Channel == ChartChannel.Current; set { if (value) Channel = ChartChannel.Current; } }
    Point? pointer, dragStart, dragEnd;
    bool panning;
    double? viewMin, viewMax;
    readonly List<(double? Min, double? Max)> history = new();
    bool panRemembered;
    long? lastWheelTime;
    (double Min, double Max, double Ratio)? pinchStart;
    public TelemetryChart() {
        Focusable = true;
        GestureRecognizers.Add(new PinchGestureRecognizer());
        AddHandler(Gestures.PinchEvent, (_, e) => {
            if(samples.Count==0 || !double.IsFinite(e.Scale) || e.Scale<=0) return;
            if(pinchStart is null) {
                if(!PlotArea.Contains(e.ScaleOrigin)) return;
                var l=Limits();
                pinchStart=(l.MinX,l.MaxX,Math.Clamp((e.ScaleOrigin.X-PlotArea.Left)/PlotArea.Width,0,1));
                RememberView(); Focus();
            }
            var start=pinchStart.Value;
            double anchor=start.Min+(start.Max-start.Min)*start.Ratio;
            double span=(start.Max-start.Min)/e.Scale;
            SetRange(anchor-span*start.Ratio,anchor+span*(1-start.Ratio),false);
            e.Handled=true;
        });
        AddHandler(Gestures.PinchEndedEvent, (_, e) => { pinchStart=null; e.Handled=true; });
    }
    double dragMin, dragMax;
    public event Action? DataUpdated;
    public IReadOnlyList<Measurement> Samples => samples;
    public (double Start, double End) ViewRange { get { var l = Limits(); return (l.MinX, l.MaxX); } }
    void RememberView()
    {
        lastWheelTime = null;
        if (history.Count == 50) history.RemoveAt(0);
        history.Add((viewMin, viewMax));
    }
    public void UndoView()
    {
        lastWheelTime = null;
        if (history.Count == 0) return;
        (viewMin, viewMax) = history[^1]; history.RemoveAt(history.Count - 1);
        SelectPoint(); InvalidateVisual();
    }
    public void ResetView() { if (viewMin is not null) RememberView(); viewMin = viewMax = null; SelectPoint(); InvalidateVisual(); }
    public void SetTimeRange(double min, double max)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || min < 0 || max - min < .05 || max > 7200)
            throw new ArgumentException("Час: 0–7200 с; «До» має бути більше «Від» щонайменше на 0,05 с.");
        RememberView(); viewMin = min; viewMax = max; SelectPoint(); InvalidateVisual();
    }
    void SetRange(double min, double max, bool remember = true)
    {
        if (remember) RememberView();
        var end = Math.Max(10, samples.LastOrDefault()?.ElapsedMs / 1000.0 ?? 10);
        var span = Math.Clamp(max - min, .05, end);
        viewMin = Math.Clamp(min, 0, end - span); viewMax = viewMin + span;
        SelectPoint(); InvalidateVisual();
    }
    int selected = -1, selectedAxis;
    public long? HoveredSequence => selected < 0 ? null : samples[selected].Sequence;
    public Rect? HoverBounds { get; private set; }
    public Rect PlotArea => new(48, 14, Math.Max(1, Bounds.Width - 72), Math.Max(1, Bounds.Height - 46));
    int Axes => Channel == ChartChannel.Vibration ? 3 : 1;
    static readonly IBrush[] Colors = [Brush.Parse("#C44F0A"), Brush.Parse("#24756A"), Brush.Parse("#436AB3")];
    IBrush Color(int axis) => Channel == ChartChannel.Current ? Colors[1] : Channel == ChartChannel.Sound ? Colors[2] : Colors[axis];
    public string SoundUnit => samples.Any(s => s.SoundDb.HasValue) ? "dB*" : "ADC";
    double? Value(Measurement s, int axis) => Channel switch
    { ChartChannel.Current => s.CurrentAmps, ChartChannel.Sound => s.SoundDb ?? s.SoundAdc, ChartChannel.Vibration => axis == 0 ? s.VibrationX : axis == 1 ? s.VibrationY : s.VibrationZ, _ => s.ThrustGrams };
    (int Start, double MinX, double MaxX, double MinY, double MaxY) Limits()
    {
        if (samples.Count == 0) return (0, viewMin ?? 0, viewMax ?? 10, 0, 1);
        int start = 0;
        double x = viewMin ?? 0;
        double end = viewMax ?? Math.Max(x + 10, samples[^1].ElapsedMs / 1000.0);
        // Binary search keeps navigation into a long recording independent of its preceding history.
        if (viewMin is not null)
        {
            int low = 0, high = samples.Count;
            while (low < high) { int middle = (low + high) / 2; if (samples[middle].ElapsedMs / 1000.0 < x) low = middle + 1; else high = middle; }
            start = Math.Max(0, low - 1);
        }
        double min = 0, max = 1;
        for (int i = start; i < samples.Count && samples[i].ElapsedMs / 1000.0 <= end; i++)
            for (int a = 0; a < Axes; a++) if (Value(samples[i], a) is { } v) { min = Math.Min(min, v); max = Math.Max(max, v); }
        return (start, x, end, min < 0 ? min * 1.1 : 0, max * 1.1);
    }
    Point Position(int index, int axis, (int Start, double MinX, double MaxX, double MinY, double MaxY) l)
    {
        var area = PlotArea;
        return new(area.Left + (samples[index].ElapsedMs / 1000.0 - l.MinX) / (l.MaxX - l.MinX) * area.Width,
            area.Bottom - (Value(samples[index], axis)!.Value - l.MinY) / (l.MaxY - l.MinY) * area.Height);
    }
    public Point GetSamplePosition(int index, int axis = 0) => Position(index, axis, Limits());
    public void Update(IReadOnlyList<Measurement> values) { if (samples.FirstOrDefault()?.RunId is { } previous && previous != values.FirstOrDefault()?.RunId) { viewMin = viewMax = null; history.Clear(); lastWheelTime = null; } samples = values; SelectPoint(); InvalidateVisual(); DataUpdated?.Invoke(); }
    void SelectPoint()
    {
        selected = -1; HoverBounds = null;
        if (dragStart is not null || pointer is not { } p || !PlotArea.Contains(p)) return;
        var l = Limits(); double distance = 64;
        for (int i = l.Start; i < samples.Count && samples[i].ElapsedMs / 1000.0 <= l.MaxX; i++)
            for (int a = 0; a < Axes; a++)
            {
                if (Value(samples[i], a) is null) continue;
                var position = Position(i, a, l); if (!PlotArea.Contains(position)) continue;
                var v = position - p;
                var d = v.X * v.X + v.Y * v.Y;
                if (d <= distance) { distance = d; selected = i; selectedAxis = a; }
            }
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (Bounds.Width < 100 || Bounds.Height < 100) return;
        var l = Limits(); var area = PlotArea;
        void Text(string text, double x, double y) => context.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter, Segoe UI"), 11, Brush.Parse("#344054")), new Point(x, y));
        for (int i = 0; i <= 4; i++)
        {
            var y = area.Bottom - area.Height * i / 4;
            context.DrawLine(new Pen(Brush.Parse("#E5EAF0")), new(area.Left, y), new(area.Right, y));
            Text((l.MinY + (l.MaxY - l.MinY) * i / 4).ToString("0.#"), 1, y - 7);
            Text((l.MinX + (l.MaxX - l.MinX) * i / 4).ToString("0.#"), area.Left + area.Width * i / 4 - 6, area.Bottom + 8);
        }
        Text("с", area.Right + 12, area.Bottom + 8);
        using (context.PushClip(area.Inflate(3)))
        {
            bool any = false;
            for (int a = 0; a < Axes; a++)
            {
                Point? previous = null; var pen = new Pen(Color(a), 1.5);
                for (int i = l.Start; i < samples.Count && samples[i].ElapsedMs / 1000.0 <= l.MaxX; i++)
                {
                    if (Value(samples[i], a) is null) { previous = null; continue; }
                    any = true; var p = Position(i, a, l);
                    if (previous is { } prior) context.DrawLine(pen, prior, p);
                    context.DrawEllipse(Color(a), null, p, 2, 2); previous = p;
                }
            }
            if (!any) Text("Немає вимірів", area.Left + 12, area.Center.Y);
        }
        if (dragStart is { } begin && dragEnd is { } finish && !panning)
        {
            var left = Math.Clamp(Math.Min(begin.X, finish.X), area.Left, area.Right);
            var right = Math.Clamp(Math.Max(begin.X, finish.X), area.Left, area.Right);
            context.DrawRectangle(Brush.Parse("#33436AB3"), new Pen(Colors[2]), new Rect(left, area.Top, right - left, area.Height));
        }
        if (selected >= 0)
        {
            var p = Position(selected, selectedAxis, l); var s = samples[selected];
            context.DrawEllipse(Brushes.White, new Pen(Color(selectedAxis), 2.5), p, 5, 5);
            var unit = Channel switch { ChartChannel.Current => "А", ChartChannel.Sound => SoundUnit, ChartChannel.Vibration => "g · " + "XYZ"[selectedAxis], _ => "г" };
            var text = $"№{s.Sequence} · {s.ElapsedMs / 1000.0:0.000} с\nГаз: {s.ThrottlePercent:0.#}%\n{Value(s, selectedAxis):0.000} {unit}";
            var width = Math.Min(174, area.Width); var height = Math.Min(64, area.Height);
            var x = p.X + 12 + width <= area.Right ? p.X + 12 : p.X - width - 12;
            var box = new Rect(Math.Clamp(x, area.Left, area.Right - width), Math.Clamp(p.Y - height - 10, area.Top, area.Bottom - height), width, height);
            HoverBounds = box;
            context.DrawRectangle(Brushes.White, new Pen(Color(selectedAxis)), box, 5, 5);
            using (context.PushClip(box)) Text(text, box.X + 8, box.Y + 8);
        }
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y == 0) return;
        var p = e.GetPosition(this);
        if (samples.Count == 0 || !PlotArea.Contains(p)) return;
        Focus();
        var l = Limits(); double ratio = (p.X - PlotArea.Left) / PlotArea.Width;
        double anchor = l.MinX + ratio * (l.MaxX - l.MinX);
        double span = (l.MaxX - l.MinX) * Math.Pow(1.25, -Math.Clamp(e.Delta.Y, -5, 5));
        var now = Environment.TickCount64;
        // One undo entry per wheel gesture; a pause starts a new gesture.
        SetRange(anchor - span * ratio, anchor + span * (1 - ratio), lastWheelTime is null || now - lastWheelTime.Value >= 500);
        lastWheelTime = now; e.Handled = true;
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this); var buttons = e.GetCurrentPoint(this).Properties;
        if (!PlotArea.Contains(p) || samples.Count == 0) return;
        Focus(); panRemembered = false;
        if (e.ClickCount == 2) { ResetView(); e.Handled = true; return; }
        if (!buttons.IsLeftButtonPressed && !buttons.IsRightButtonPressed) return;
        var l = Limits(); dragMin = l.MinX; dragMax = l.MaxX;
        dragStart = dragEnd = p; panning = buttons.IsRightButtonPressed;
        e.Pointer.Capture(this); SelectPoint(); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); pointer = e.GetPosition(this);
        if (dragStart is { } start)
        {
            dragEnd = pointer;
            if (panning) { if (!panRemembered) { RememberView(); panRemembered = true; } var shift = (pointer.Value.X - start.X) / PlotArea.Width * (dragMax - dragMin); SetRange(dragMin - shift, dragMax - shift, false); }
            e.Handled = true;
        }
        SelectPoint(); InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (dragStart is not { } start) return;
        var end = e.GetPosition(this);
        if (!panning && Math.Abs(end.X - start.X) >= 8)
        {
            double Time(double x) => dragMin + Math.Clamp((x - PlotArea.Left) / PlotArea.Width, 0, 1) * (dragMax - dragMin);
            SetRange(Time(Math.Min(start.X, end.X)), Time(Math.Max(start.X, end.X)));
        }
        dragStart = dragEnd = null; e.Pointer.Capture(null); SelectPoint(); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); dragStart = dragEnd = null; InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); pointer = null; SelectPoint(); InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control) { UndoView(); e.Handled = true; }
    }
}



