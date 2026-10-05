using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PropTest.Core;

namespace PropTest.Desktop;

// Each slot owns its charts. Legacy telemetry is explicitly assigned to M1 only.
public sealed class MotorOverview : UserControl
{
    readonly TelemetryChart[][] charts = Enumerable.Range(0,4).Select(_ => Enum.GetValues<ChartChannel>().Select(c => new TelemetryChart { Channel = c }).ToArray()).ToArray();
    readonly TextBlock[][] values = Enumerable.Range(0,4).Select(_ => Enumerable.Range(0,6).Select(_ => new TextBlock()).ToArray()).ToArray();
    readonly TextBlock[] states = Enumerable.Range(0,4).Select(_ => new TextBlock { FontSize = 10, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Height = 14, VerticalAlignment = VerticalAlignment.Center }).ToArray();
    readonly StackPanel motorDetails = new() { Spacing=10 };
    readonly DroneDrawing drawing;
    readonly Grid diagramGrid;
    readonly List<Viewbox> cardViews = new();
    readonly TextBlock totalCurrent=new(), totalSound=new();
    readonly TextBlock note = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brush.Parse("#596779") };
    string source = "";
    public Func<int,Window>? ControlRequested { get; set; }
    Window OpenControl(int motor)=>ControlRequested?.Invoke(motor)??OpenMotor(motor);
    public Action Stop { get; set; } = () => { };
    public IReadOnlyList<Measurement> MotorSamples(int motor) => charts[motor - 1][0].Samples;
    public MotorOverview()
    {
        drawing = new DroneDrawing(OpenControl) { Name = "DroneDiagram", Height = 340, VerticalAlignment = VerticalAlignment.Center };
        var grid = diagramGrid = new Grid { MaxWidth = 1400, HorizontalAlignment = HorizontalAlignment.Center, ColumnDefinitions = new ColumnDefinitions("160,*,160"), RowDefinitions = new RowDefinitions("*,*") };
        for (int i = 0; i < 4; i++) {
            int motor = i;
            var title = new Button { Content = $"Мотор {i+1}", MinHeight = 0, Height = 26, Padding = new Thickness(8,4), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Background = Brush.Parse("#F3F5F7"), BorderBrush = Brush.Parse("#DDE2E7"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Cursor = new Cursor(StandardCursorType.Hand), FontWeight = FontWeight.SemiBold };
            ToolTip.SetTip(title, "Керувати газом цього мотора");
            title.Click += (_, _) => OpenControl(motor);
            var card = new StackPanel { Spacing = 3, Children = { title, states[i] } };
            var metrics = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
            card.Children.Add(metrics);
            var labels = new[] { "Тяга", "Струм", "Вібрація |a|", "Звук", "Темп., °C", "Оберти" };
            for (int j = 0; j < labels.Length; j++) {
                int channel = j;
                var line = new StackPanel { Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center };
                line.Children.Add(new TextBlock { Text = labels[j], FontSize = 9, Foreground = Brushes.White });
                values[i][j].TextAlignment = TextAlignment.Center; values[i][j].Text = "—"; values[i][j].FontWeight = FontWeight.SemiBold; values[i][j].FontSize = 14;
                line.Children.Add(values[i][j]);
                var button = new Button { Content = line, MinHeight = 0, Height = 34, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(4,2), Margin = new Thickness(2), Background = Brush.Parse(TelemetryChart.SeriesColors[new[]{0,1,2,5,6,7}[j]]), Foreground=Brushes.White, CornerRadius = new CornerRadius(5) };
                button.Click += (_, _) => OpenMetric(motor, channel);
                if(j is 1 or 3) continue;
                int slot=j switch {0=>0,2=>1,4=>2,_=>3};
                Grid.SetColumn(button,slot%2); Grid.SetRow(button,slot/2); metrics.Children.Add(button);
            }
            var graphs=new Button {Content="Графіки",Height=24,MinHeight=0,Padding=new(4,2),FontSize=11,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center};
            graphs.Click+=(_,_)=>OpenMotor(motor);card.Children.Add(graphs);
            var border = new Border { Margin = new Thickness(0,0,0,4), Child = card, BorderBrush = Brush.Parse("#DCE1E6"), BorderThickness = new Thickness(1), Background = Brushes.White, Padding = new Thickness(5), VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(10) };
            border.Width=160;
            var view=new Viewbox { Child=border,Width=160,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center }; cardViews.Add(view);
            Grid.SetColumn(view, i % 2 == 0 ? 0 : 2); Grid.SetRow(view, i / 2); grid.Children.Add(view);
        }

        Grid.SetColumn(drawing, 1); Grid.SetRowSpan(drawing, 2); grid.Children.Add(drawing);
        Content = new StackPanel { Spacing = 12, Children = {
            ChartUi.Heading("Огляд чотирьох моторів", "Натисніть показник, щоб відкрити компактний графік. Натисніть мотор, щоб керувати його газом. Кнопка «Графіки» відкриває всі його показники. Розгорнути графіки можна стандартною кнопкою вікна.",22),
            new Border { Child = grid, Background = Brushes.White, Padding = new Thickness(8,24,8,8), CornerRadius = new CornerRadius(10) }, note
        }};
        var body=(StackPanel)Content!;
        var summary=new Grid {ColumnDefinitions=new("*,*,*")};
        var summaryLabels=new[]{"Загальний струм, А","Звук стенда","Напруга"};
        for(int n=0;n<3;n++) {
            var value=n==0?totalCurrent:n==1?totalSound:new TextBlock {Text="Немає даних"}; value.FontSize=22;
            var box=new Border {Background=Brushes.White,CornerRadius=new(8),Padding=new(14),Margin=new(0,0,8,0),Child=new StackPanel {Children={new TextBlock {Text=summaryLabels[n],FontSize=12},value}}}; Grid.SetColumn(box,n);summary.Children.Add(box);
        }
        body.Children.Add(summary);
        body.Children.Add(new MotorChartPanel(charts[0][0],1,null,"Поточні виміри · струм і звук спільні",()=>Stop(),charts.Select(c=>c[0]).ToArray()) { Height=680 });
        body.Children.Add(motorDetails);
        for(int m=0;m<4;m++) {
            int index=m;
            var panel=new StackPanel { Spacing=8 };
            panel.Children.Add(new ChartRangeBar(()=>charts[index].ToArray()));
            for(int c=0;c<4;c++) { if(c is 1 or 3)continue; int axis=c; var chart=charts[m][c]; chart.Height=220;
                panel.Children.Add(new Expander { Header=ChartUi.ChannelHeading(new[]{"Тяга, г","Струм, А","Вібрація XYZ, g","Звук"}[c],new[]{0,1,2,5}[c]),Content=chart,IsExpanded=true,HorizontalAlignment=HorizontalAlignment.Stretch });
            }
            foreach(var name in new[]{"Температура, °C","Оберти, RPM"}) panel.Children.Add(new Expander {Header=ChartUi.ChannelHeading(name,name.StartsWith("Температура")?6:7),Content=new TextBlock {Text="Немає даних",Margin=new(12)},HorizontalAlignment=HorizontalAlignment.Stretch});
            motorDetails.Children.Add(new Expander { Header=$"Мотор {m+1} · окремі графіки",Content=panel,IsVisible=false,HorizontalAlignment=HorizontalAlignment.Stretch });
        }
        UpdateLegacy([], "", false);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        if(double.IsFinite(availableSize.Width)) {
            double width=Math.Min(1400,Math.Max(320,availableSize.Width-16));
            double cardWidth=Math.Clamp(width*.20,160,240);
            diagramGrid.ColumnDefinitions[0].Width=new GridLength(cardWidth); diagramGrid.ColumnDefinitions[2].Width=new GridLength(cardWidth);
            foreach(var card in cardViews)card.Width=cardWidth;
            diagramGrid.Width=width;
            drawing.Height=Math.Clamp((width-cardWidth*2)*.85,280,620);
        }
        return base.MeasureOverride(availableSize);
    }
    public void UpdateLegacy(IReadOnlyList<Measurement> samples, string sourceLabel, bool recording)
    {
        source = sourceLabel;
        var latest=samples.LastOrDefault();
        totalCurrent.Text=latest?.CurrentAmps is {} amps?$"{amps:0.##}":"—";
        totalSound.Text=(latest?.SoundDb??latest?.SoundAdc) is {} sound?$"{sound:0.##} {(latest?.SoundDb is not null ? "dB*" : "ADC")}":"—";
        for (int i = 0; i < 4; i++) {
            IReadOnlyList<Measurement> channel = i == 0 ? samples : Array.Empty<Measurement>();
            foreach (var chart in charts[i]) chart.Update(channel);
            motorDetails.Children[i].IsVisible=channel.Count>0;
            var last = channel.LastOrDefault();
            string Format(double? value, string unit) => value.HasValue ? $"{value:0.##} {unit}" : "—";
            values[i][0].Text = Format(last?.ThrustGrams, "г"); values[i][1].Text = Format(last?.CurrentAmps, "А");
            double? acceleration = last is { VibrationX: { } x, VibrationY: { } y, VibrationZ: { } z } ? Math.Sqrt(x*x+y*y+z*z) : null;
            values[i][2].Text = Format(acceleration, "g"); values[i][3].Text = Format(last?.SoundDb ?? last?.SoundAdc, last?.SoundDb is not null ? "dB*" : "ADC");
            drawing.SetThrottle(i, recording ? last?.ThrottlePercent ?? 0 : 0);
            states[i].Text = last is null ? "Наявність не визначена" : recording ? "Є виміри каналу" : "Останній запис";

        }
        drawing.InvalidateVisual();
        note.Text = samples.Count > 0 ? sourceLabel + " · Один потік вимірів показано лише на M1. Синє підсвічування при наведенні означає вибір мотора, не підтвердження його наявності. Температура й RPM ще не надходять."
            : "Поточна ESP32-прошивка не передає вимірів і стану чотирьох моторів. Сірий — стан невідомий, а не відсутній мотор. Загальний струм ESC не можна розділити на чотири мотори без окремих вимірів.";
        note.Text += " Стрілки схематичні; анімація за газом каналу, не виміряні оберти.";
    }
    public Window OpenMetric(int motor, int channel)
    {
        var window = new MotorChartWindow(charts[motor][0],motor+1,channel,source,()=>Stop(),charts.Select(c=>c[0]).ToArray());
        ShowDetail(window); return window;
    }
    public Window OpenMotor(int motor)
    {
        var window = new MotorChartWindow(charts[motor][0],motor+1,null,source,()=>Stop(),charts.Select(c=>c[0]).ToArray());
        ShowDetail(window); return window;
    }
    public Window OpenLegacyMotor(int motor)
    {
        var window = new Window { Title = $"PROpTEST · Мотор {motor+1}", Width = 1000, Height = 720, MinWidth = 600, MinHeight = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
        stack.Children.Add(new TextBlock { Text = $"Мотор {motor+1} · " + (charts[motor][0].Samples.Count == 0 ? "немає вимірів" : source), FontSize = 20, TextWrapping = TextWrapping.Wrap });
        var copies = new List<TelemetryChart>();
        foreach (var chart in charts[motor]) {
            var copy = new TelemetryChart { Channel = chart.Channel, Height = 250 }; copy.Update(chart.Samples); copies.Add(copy);
            string title = chart.Channel switch { ChartChannel.Thrust => "Тяга, г", ChartChannel.Current => "Струм, А", ChartChannel.Vibration => "Прискорення XYZ, g", _ => "Звук, " + chart.SoundUnit };
            stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold }); stack.Children.Add(copy);
            void Update() => copy.Update(chart.Samples);
            chart.DataUpdated += Update; window.Closed += (_, _) => chart.DataUpdated -= Update;
        }
        stack.Children.Insert(1, new ChartRangeBar(() => copies.ToArray()));
        stack.Children.Add(new TextBlock { Text = "Температура та оберти: дані ще не надходять.", TextWrapping = TextWrapping.Wrap });
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") }; root.Children.Add(new ScrollViewer { Content = stack });
        var stop = new Button { Content = "■ STOP · зупинити", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18) }; stop.Classes.Add("stop"); stop.Click += (_, _) => Stop();
        Grid.SetRow(stop,1); root.Children.Add(stop); window.Content = root; ShowDetail(window); return window;
    }
    void ShowDetail(Window window) { if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner); else window.Show(); }
}

sealed class DroneDrawing : Control
{
    readonly Func<int, Window> open;
    readonly DispatcherTimer animation = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly double[] targets = new double[4], speeds = new double[4], phases = new double[4];
    long lastTick;
    public DroneDrawing(Func<int, Window> open) {
        this.open = open;
        animation.Tick += (_, _) => {
            long now = Environment.TickCount64;
            double dt = Math.Clamp((now-lastTick)/1000.0, 0, .1); lastTick = now;
            for(int i=0;i<4;i++) {
                speeds[i] += (targets[i]-speeds[i]) * (1-Math.Exp(-dt*5));
                if(targets[i]==0 && speeds[i]<.02) speeds[i]=0;
                phases[i] = (phases[i]+speeds[i]*.08*dt) % (2*Math.PI);
            }
            InvalidateVisual();
            if(speeds.All(v=>v==0) && targets.All(v=>v==0)) animation.Stop();
        };
    }
    public void SetThrottle(int motor, double percent) {
        targets[motor] = double.IsFinite(percent) ? Math.Clamp(percent,0,100) : 0;
        if(targets[motor]>0 && !animation.IsEnabled && VisualRoot is not null) { lastTick=Environment.TickCount64; animation.Start(); }
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        animation.Stop(); Array.Clear(speeds); Array.Clear(targets); base.OnDetachedFromVisualTree(e);
    }
    public bool[] Detected { get; } = new bool[4];
    static readonly Geometry Blade = Geometry.Parse("M -3,-4 C 23,-9 64,-18 92,-21 L 85,-13 C 62,-1 22,8 3,4 Z");
    static readonly Geometry Frame = Geometry.Parse("M 193,168 L 186,150 L 190,135 L 230,135 L 234,150 L 227,168 L 226,286 L 235,317 L 227,326 L 193,326 L 185,317 L 194,286 Z");
    int hovered = -1;
    static Point Center(int i) => new(i % 2 == 0 ? 110 : 310, i / 2 == 0 ? 100 : 340);
    (double Scale, double X, double Y) Layout() {
        double scale = Math.Min(Bounds.Width/420,Bounds.Height/460);
        return (scale,(Bounds.Width-420*scale)/2,(Bounds.Height-460*scale)/2);
    }
    double Angle(int motor, int blade) => blade*Math.PI*2/3 - .25 + phases[motor];
    int Hit(Point pointer) {
        var t = Layout(); if (t.Scale <= 0) return -1;
        var p = new Point((pointer.X-t.X)/t.Scale,(pointer.Y-t.Y)/t.Scale);
        for (int i=0;i<4;i++) {
            double x=(p.X-Center(i).X)*(i%2==0?1:-1), y=(p.Y-Center(i).Y)*(i<2?1:-1);
            if(x*x+y*y <= (speeds[i]>1 ? 94*94 : 13*13)) return i;
            for(int b=0;b<3;b++) {
                double a=Angle(i,b), c=Math.Cos(a), sin=Math.Sin(a);
                if(Blade.FillContains(new Point(c*x+sin*y,-sin*x+c*y))) return i;
            }
        }
        return -1;
    }
    static void DrawRotationArrows(DrawingContext context, Point center, int motor) {
        bool clockwise = motor==0 || motor==3;
        using var placement=context.PushTransform(Matrix.CreateScale(motor%2==0?1:-1,motor<2?1:-1)*Matrix.CreateTranslation(center.X,center.Y));
        var pen = new Pen(Brush.Parse(clockwise ? "#568C7D" : "#B2746C"), 1.8);
        double direction = 1;
        for(int segment=0;segment<3;segment++) {
            double start=(20+segment*120)*Math.PI/180;
            double sweep=45*Math.PI/180;
            Point At(double angle) => new(72*Math.Cos(angle),72*Math.Sin(angle));
            var arc = new StreamGeometry();
            using(var path=arc.Open()) {
                path.BeginFigure(At(start),false);
                for(int step=1;step<=24;step++) path.LineTo(At(start+direction*sweep*step/24));
                path.EndFigure(false);
            }
            context.DrawGeometry(null,pen,arc);
            double end=start+direction*sweep;
            var tip=At(end);
            var tangent=new Vector(-Math.Sin(end)*direction,Math.Cos(end)*direction);
            var normal=new Vector(Math.Cos(end),Math.Sin(end));
            context.DrawLine(pen,tip,tip-tangent*6+normal*3);
            context.DrawLine(pen,tip,tip-tangent*6-normal*3);
        }
    }
    public override void Render(DrawingContext context)
    {
        var t=Layout(); if(t.Scale<=0) return;
        using var placement=context.PushTransform(Matrix.CreateScale(t.Scale,t.Scale)*Matrix.CreateTranslation(t.X,t.Y));
        var dark=Brush.Parse("#272A2E"); var edge=Brush.Parse("#53585E");
        for(int i=0;i<4;i++) {
            var p=Center(i); var root=new Point(i%2==0?197:223,i<2?196:272);
            context.DrawLine(new Pen(edge,17),root,p); context.DrawLine(new Pen(dark,12),root,p);
            context.DrawLine(new Pen(Brush.Parse("#41464D"),2),new Point(root.X+2,root.Y-3),new Point(p.X+2,p.Y-3));
        }
        context.DrawGeometry(dark,new Pen(edge,2),Frame);
        context.DrawRectangle(Brush.Parse("#454A50"),null,new Rect(198,191,24,88),2,2);
        for(int j=0;j<3;j++) context.DrawRectangle(Brush.Parse("#191C20"),null,new Rect(199,200+j*27,22,12),2,2);
        foreach(double y in new[]{160.0,185,291,315}) foreach(double x in new[]{197.0,223}) {
            context.DrawEllipse(Brush.Parse("#60666C"),null,new Point(x,y),3,3);
            context.DrawEllipse(dark,null,new Point(x,y),1.3,1.3);
        }
        context.DrawRectangle(Brush.Parse("#191B1F"),new Pen(edge,1),new Rect(195,131,30,21),3,3);
        context.DrawEllipse(Brush.Parse("#666C72"),null,new Point(210,135),8,5);
        context.DrawEllipse(Brush.Parse("#16181B"),null,new Point(210,134),5,3);
        context.DrawLine(new Pen(dark,3),new Point(210,326),new Point(210,418));
        context.DrawRectangle(dark,null,new Rect(204,415,12,10),3,3);
        for(int i=0;i<4;i++) {
            var p=Center(i); bool hot=i==hovered;
            context.DrawEllipse(Brush.Parse(Detected[i]?"#272A2E":"#929AA3"),new Pen(edge,2),p,17,17);
            for(int k=0;k<8;k++) {
                double a=k*Math.PI/4;
                context.DrawLine(new Pen(Brush.Parse("#70757A"),2),new Point(p.X+10*Math.Cos(a),p.Y+10*Math.Sin(a)),new Point(p.X+14*Math.Cos(a),p.Y+14*Math.Sin(a)));
            }
            double blur = Math.Clamp(speeds[i]/8,0,1);
            if(blur>0) {
                using var haze = context.PushOpacity(blur*.25);
                context.DrawEllipse(Brush.Parse(hot?"#2687EA":"#7D8791"), new Pen(Brush.Parse("#7D8791"),1), p,94,94);
            }
            using (context.PushOpacity(1-blur*.94))
            for(int b=0;b<3;b++) {
                using var rotate=context.PushTransform(Matrix.CreateRotation(Angle(i,b))*Matrix.CreateScale(i%2==0?1:-1,i<2?1:-1)*Matrix.CreateTranslation(p.X,p.Y));
                context.DrawGeometry(Brush.Parse(hot?"#76899C":Detected[i]?"#383C41":"#9BA3AC"),new Pen(Brush.Parse(hot?"#2687EA":"#555A60"),hot?2.5:1),Blade);
            }
            context.DrawEllipse(Brush.Parse(Detected[i]?"#24282D":"#7B858F"),new Pen(Brush.Parse(hot?"#2687EA":"#969CA2"),2),p,9,9);
            context.DrawEllipse(Brush.Parse("#C3C7CB"),null,p,4,4);
            context.DrawEllipse(dark,null,p,2,2);
            DrawRotationArrows(context,p,i);
            var text=new FormattedText($"M{i+1}",CultureInfo.CurrentCulture,FlowDirection.LeftToRight,Typeface.Default,14,Brush.Parse(hot?"#247DCA":"#626C76"));
            context.DrawText(text,new Point(p.X-text.Width/2,(i<2?-16:440)));
        }
    }
    protected override void OnPointerMoved(PointerEventArgs e) {
        base.OnPointerMoved(e); int next=Hit(e.GetPosition(this)); if(next==hovered)return;
        hovered=next; Cursor=new Cursor(hovered>=0?StandardCursorType.Hand:StandardCursorType.Arrow); InvalidateVisual();
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); hovered=-1; Cursor=new Cursor(StandardCursorType.Arrow); InvalidateVisual(); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) {
        base.OnPointerPressed(e); if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        int motor=Hit(e.GetPosition(this)); if(motor<0)return; open(motor); e.Handled=true;
    }
}
