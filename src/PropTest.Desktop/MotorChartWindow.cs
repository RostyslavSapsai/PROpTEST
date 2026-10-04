using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PropTest.Desktop;

public sealed class MotorChartWindow : Window
{
    public TelemetryChart Chart { get; } = new() { Combined = true };
    public bool IsLegacy { get; private set; }
    public MotorChartWindow(TelemetryChart source, int motor, int? metric, string sourceLabel, Action stop, TelemetryChart[]? motorSources = null)
    {
        var sources=motorSources ?? new[]{source};
        var selectedMotors=Enumerable.Range(0,sources.Length).Select(m=>motorSources is null || m==motor-1).ToArray();
        Title = $"PROpTEST · Мотор {motor}"; Width=1000; Height=720; MinWidth=760; MinHeight=600;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        for(int i=0;i<32;i++) Chart.VisibleSeries[i]=metric is null || (metric switch { 0=>i%8==0,1=>i%8==1,2=>i%8 is >=2 and <=4,3=>i%8==5,4=>i%8==6,_=>i%8==7 });
        var root=new Grid { RowDefinitions=new("Auto,Auto,*,Auto"), Margin=new(18) };
        var header=new Grid { ColumnDefinitions=new("*,Auto"), Margin=new(0,0,0,12) };
        header.Children.Add(new StackPanel { Children = { new TextBlock { Text="Графіки моторів", FontSize=22 }, new TextBlock { Text=sourceLabel, FontSize=11, TextWrapping=TextWrapping.Wrap, Foreground=Brush.Parse("#66717C") } } });
        var toggle=new Button { Content="Окремі графіки", Name="ToggleChartLayout" }; Grid.SetColumn(toggle,1); header.Children.Add(toggle); root.Children.Add(header);
        var legacyByMotor=sources.Select(_=>Enum.GetValues<ChartChannel>().Select(c=>new TelemetryChart { Channel=c, Height=240 }).ToArray()).ToArray();
        var legacyCharts=legacyByMotor.SelectMany(c=>c).ToArray();
        var range=new ChartRangeBar(()=>IsLegacy?legacyCharts:[Chart]); Grid.SetRow(range,1); root.Children.Add(range);
        var combined=new Grid { ColumnDefinitions=new("240,*"), Margin=new(0,14,0,0) };
        var legend=new StackPanel { Spacing=6, Margin=new(0,0,12,0) };
        legend.Children.Add(new TextBlock { Text="Канали", FontWeight=FontWeight.SemiBold });
        var motorPicker=new WrapPanel();
        for(int m=0;m<sources.Length;m++) {
            int index=m; var pick=new CheckBox { Content=$"M{m+1}", IsChecked=selectedMotors[m], Margin=new(0,0,8,4), Name=$"MotorToggle{m}" };
            motorPicker.Children.Add(pick);
        }
        legend.Children.Add(motorPicker);
        var groups=Enumerable.Range(0,4).Select(m=>new StackPanel { Spacing=4 }).ToArray();
        var sections=groups.Select((g,m)=>new Expander { Header=$"Мотор {m+1}", Content=g, IsExpanded=true, HorizontalAlignment=HorizontalAlignment.Stretch }).ToArray();
        foreach(var section in sections) legend.Children.Add(section);
        var buttons=new Button[32]; var labels=new TextBlock[32];
        for(int i=0;i<32;i++) {
            int axis=i;
            labels[i]=new TextBlock { FontSize=11, Width=174, Height=18, TextWrapping=TextWrapping.NoWrap };
            buttons[i]=new Button { Name=$"SeriesToggle{i}", Content=labels[i], HorizontalAlignment=HorizontalAlignment.Stretch, HorizontalContentAlignment=HorizontalAlignment.Left, Padding=new(10,6) };
            buttons[i].Click+=(_,_)=>{ Chart.SetSeriesVisible(axis,!Chart.VisibleSeries[axis]); RefreshLegend(); };
            groups[i/8].Children.Add(buttons[i]);
        }
        combined.Children.Add(new ScrollViewer { Content=legend });
        var plot = new Grid { RowDefinitions=new("Auto,*") };
        plot.Children.Add(new TextBlock { Text="Незалежні масштаби · межі — у підказках легенди · значення при наведенні", FontSize=11, TextWrapping=TextWrapping.Wrap, Foreground=Brush.Parse("#66717C"), Margin=new(0,0,0,6) });
        Grid.SetRow(Chart,1); plot.Children.Add(Chart); Grid.SetColumn(plot,1); combined.Children.Add(plot); Grid.SetRow(combined,2); root.Children.Add(combined);
        var old=new StackPanel { Spacing=10 };
        var oldGroups=new Expander[sources.Length];
        for(int m=0;m<sources.Length;m++) {
            var group=new StackPanel { Spacing=8 };
            for(int c=0;c<4;c++) group.Children.Add(new Expander { Header=new[]{"Тяга, г","Струм, А","Вібрація XYZ, g","Звук"}[c], Content=legacyByMotor[m][c], IsExpanded=true, HorizontalAlignment=HorizontalAlignment.Stretch });
            foreach(var name in new[]{"Температура, °C","Оберти, RPM"}) group.Children.Add(new Expander { Header=name,Content=new TextBlock {Text="Немає даних",Margin=new(12)},HorizontalAlignment=HorizontalAlignment.Stretch });
            oldGroups[m]=new Expander { Header=$"Мотор {m+1}",Content=group,IsExpanded=true,HorizontalAlignment=HorizontalAlignment.Stretch }; old.Children.Add(oldGroups[m]);
        }
        var legacy=new ScrollViewer { Content=old, IsVisible=false, Margin=new(0,14,0,0) }; Grid.SetRow(legacy,2); root.Children.Add(legacy);
        toggle.Click+=(_,_)=> { IsLegacy=!IsLegacy; legacy.IsVisible=IsLegacy; combined.IsVisible=!IsLegacy; toggle.Content=IsLegacy?"Спільний графік":"Окремі графіки"; };
        var footer=new Grid { ColumnDefinitions=new("*,Auto"), Margin=new(0,12,0,0) };
        footer.Children.Add(new TextBlock { Text="Ctrl + прокручування — масштаб · Ctrl+Z — назад\nЛКМ — ділянка · ПКМ — пересування", FontSize=11, TextWrapping=TextWrapping.Wrap, VerticalAlignment=VerticalAlignment.Center });
        var stopButton=new Button { Content="■ STOP · зупинити" }; stopButton.Classes.Add("stop"); stopButton.Click+=(_,_)=>stop(); Grid.SetColumn(stopButton,1); footer.Children.Add(stopButton); Grid.SetRow(footer,3); root.Children.Add(footer);
        Content=root;
        void RefreshLegend() {
            for(int i=0;i<32;i++) {
                bool active=Chart.VisibleSeries[i], has=Chart.HasSeries(i); var scale=Chart.SeriesScale(i); var value=Chart.LatestValue(i);
                labels[i].Text=$"{(active?"●":"○")} {TelemetryChart.SeriesNames[i%8]}  "+(has?$"{value:0.##} {Chart.SeriesUnit(i)}":"немає даних");
                ToolTip.SetTip(buttons[i],$"Масштаб: {scale.Min:0.##}…{scale.Max:0.##} {Chart.SeriesUnit(i)}");
                labels[i].Foreground=Brush.Parse(active&&has?"#FFFFFF":"#59616B");
                buttons[i].Background=Brush.Parse(active&&has?TelemetryChart.SeriesColors[i%8]:"#E4E7EB");
            }
        }
        void Update() {
            Chart.UpdateMotors(sources.Select((c,m)=>(IReadOnlyList<PropTest.Core.Measurement>)(selectedMotors[m]?c.Samples:Array.Empty<PropTest.Core.Measurement>())).ToArray());
            for(int m=0;m<4;m++) sections[m].IsVisible=m<sources.Length && selectedMotors[m];
            for(int m=0;m<sources.Length;m++) { foreach(var c in legacyByMotor[m])c.Update(sources[m].Samples); oldGroups[m].IsVisible=selectedMotors[m] && sources[m].Samples.Count>0; } RefreshLegend();
        }
        for(int m=0;m<sources.Length;m++) { int index=m; var pick=(CheckBox)motorPicker.Children[m]; pick.IsCheckedChanged+=(_,_)=>{selectedMotors[index]=pick.IsChecked==true; Update();}; }
        Update(); foreach(var c in sources)c.DataUpdated+=Update;
        Closed+=(_,_)=>{foreach(var c in sources)c.DataUpdated-=Update;};
    }
}
