using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PropTest.Desktop;

public sealed class MotorChartWindow : Window
{
    public TelemetryChart Chart { get; } = new() { Combined = true };
    public bool IsLegacy { get; private set; }
    public MotorChartWindow(TelemetryChart source, int motor, int? metric, string sourceLabel, Action stop)
    {
        Title = $"PROpTEST · Мотор {motor}"; Width=1000; Height=720; MinWidth=760; MinHeight=600;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        for(int i=0;i<8;i++) Chart.VisibleSeries[i]=metric is null || (metric switch { 0=>i==0,1=>i==1,2=>i is >=2 and <=4,3=>i==5,4=>i==6,_=>i==7 });
        var root=new Grid { RowDefinitions=new("Auto,Auto,*,Auto"), Margin=new(18) };
        var header=new Grid { ColumnDefinitions=new("*,Auto"), Margin=new(0,0,0,12) };
        header.Children.Add(new StackPanel { Children = { new TextBlock { Text=$"Мотор {motor} · графіки", FontSize=22 }, new TextBlock { Text=sourceLabel, FontSize=11, TextWrapping=TextWrapping.Wrap, Foreground=Brush.Parse("#66717C") } } });
        var toggle=new Button { Content="Попередній вигляд", Name="ToggleChartLayout" }; Grid.SetColumn(toggle,1); header.Children.Add(toggle); root.Children.Add(header);
        var legacyCharts=Enum.GetValues<ChartChannel>().Select(c=>new TelemetryChart { Channel=c, Height=240 }).ToArray();
        var range=new ChartRangeBar(()=>IsLegacy?legacyCharts:[Chart]); Grid.SetRow(range,1); root.Children.Add(range);
        var combined=new Grid { ColumnDefinitions=new("190,*"), Margin=new(0,14,0,0) };
        var legend=new StackPanel { Spacing=6, Margin=new(0,0,12,0) };
        legend.Children.Add(new TextBlock { Text="Канали", FontWeight=FontWeight.SemiBold });
        var buttons=new Button[8]; var labels=new TextBlock[8];
        for(int i=0;i<8;i++) {
            int axis=i;
            labels[i]=new TextBlock { FontSize=11, Width=156, Height=32, TextWrapping=TextWrapping.NoWrap };
            buttons[i]=new Button { Name=$"SeriesToggle{i}", Content=labels[i], HorizontalAlignment=HorizontalAlignment.Stretch, HorizontalContentAlignment=HorizontalAlignment.Left, Padding=new(10,6) };
            buttons[i].Click+=(_,_)=>{ Chart.SetSeriesVisible(axis,!Chart.VisibleSeries[axis]); RefreshLegend(); };
            legend.Children.Add(buttons[i]);
        }
        combined.Children.Add(new ScrollViewer { Content=legend });
        var plot = new Grid { RowDefinitions=new("Auto,*") };
        plot.Children.Add(new TextBlock { Text="Незалежні масштаби · межі й одиниці в легенді · значення при наведенні", FontSize=11, TextWrapping=TextWrapping.Wrap, Foreground=Brush.Parse("#66717C"), Margin=new(0,0,0,6) });
        Grid.SetRow(Chart,1); plot.Children.Add(Chart); Grid.SetColumn(plot,1); combined.Children.Add(plot); Grid.SetRow(combined,2); root.Children.Add(combined);
        var old=new StackPanel { Spacing=10 };
        for(int i=0;i<legacyCharts.Length;i++) { old.Children.Add(new TextBlock { Text=new[]{"Тяга, г","Струм, А","Вібрація XYZ, g","Звук"}[i], FontWeight=FontWeight.SemiBold }); old.Children.Add(legacyCharts[i]); }
        var legacy=new ScrollViewer { Content=old, IsVisible=false, Margin=new(0,14,0,0) }; Grid.SetRow(legacy,2); root.Children.Add(legacy);
        toggle.Click+=(_,_)=> { IsLegacy=!IsLegacy; legacy.IsVisible=IsLegacy; combined.IsVisible=!IsLegacy; toggle.Content=IsLegacy?"Спільний графік":"Попередній вигляд"; };
        var footer=new Grid { ColumnDefinitions=new("*,Auto"), Margin=new(0,12,0,0) };
        footer.Children.Add(new TextBlock { Text="Ctrl + прокручування — масштаб · Ctrl+Z — назад\nЛКМ — ділянка · ПКМ — пересування", FontSize=11, TextWrapping=TextWrapping.Wrap, VerticalAlignment=VerticalAlignment.Center });
        var stopButton=new Button { Content="■ STOP · зупинити" }; stopButton.Classes.Add("stop"); stopButton.Click+=(_,_)=>stop(); Grid.SetColumn(stopButton,1); footer.Children.Add(stopButton); Grid.SetRow(footer,3); root.Children.Add(footer);
        Content=root;
        void RefreshLegend() {
            for(int i=0;i<8;i++) {
                bool active=Chart.VisibleSeries[i], has=Chart.HasSeries(i); var scale=Chart.SeriesScale(i);
                labels[i].Text=$"{(active?"●":"○")} {TelemetryChart.SeriesNames[i]}\n"+(has?$"{scale.Min:0.##}…{scale.Max:0.##} {Chart.SeriesUnit(i)}":"немає даних");
                labels[i].Foreground=Brush.Parse(active&&has?TelemetryChart.SeriesColors[i]:"#727B86");
                buttons[i].Background=Brush.Parse(active?"#F0F4F8":"#F6F6F6");
            }
        }
        void Update() { Chart.Update(source.Samples); foreach(var c in legacyCharts)c.Update(source.Samples); RefreshLegend(); }
        Update(); source.DataUpdated+=Update; Closed+=(_,_)=>source.DataUpdated-=Update;
    }
}
