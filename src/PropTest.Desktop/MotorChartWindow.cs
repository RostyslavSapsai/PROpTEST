using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PropTest.Desktop;

public sealed class MotorChartPanel : UserControl
{
    public TelemetryChart Chart { get; } = new() { Combined = true };
    public bool IsLegacy { get; private set; }
    public MotorChartPanel(TelemetryChart source, int motor, int? metric, string sourceLabel, Action stop, TelemetryChart[]? motorSources = null)
    {
        var sources=motorSources ?? new[]{source};
        var selectedMotors=Enumerable.Range(0,sources.Length).Select(m=>motorSources is null || m==motor-1).ToArray();

        for(int i=0;i<32;i++) Chart.VisibleSeries[i]=metric is null || (metric switch { 0=>i%8==0,1=>i%8==1,2=>i%8 is >=2 and <=4,3=>i%8==5,4=>i%8==6,_=>i%8==7 });
        var root=new Grid { RowDefinitions=new("Auto,Auto,Auto,*,Auto"), Margin=new(18) };
        var header=new Grid { ColumnDefinitions=new("*,Auto"), Margin=new(0,0,0,12) };
        header.Children.Add(new StackPanel { Children = { new TextBlock { Text="Графіки моторів", FontSize=22 }, new TextBlock { Text=sourceLabel, FontSize=11, TextWrapping=TextWrapping.Wrap, Foreground=Brush.Parse("#66717C") } } });
        var toggle=new Button { Content="Окремі графіки", Name="ToggleChartLayout" }; Grid.SetColumn(toggle,1); header.Children.Add(toggle); root.Children.Add(header);
        var legacyByMotor=sources.Select(_=>Enum.GetValues<ChartChannel>().Select(c=>new TelemetryChart { Channel=c, Height=240 }).ToArray()).ToArray();
        var legacyCharts=legacyByMotor.SelectMany(c=>c).ToArray();
        var range=new ChartRangeBar(()=>legacyCharts.Append(Chart).ToArray()); Grid.SetRow(range,1); root.Children.Add(range);
        var combined=new Grid { ColumnDefinitions=new("240,*"), Margin=new(0,14,0,0) };
        var legend=new StackPanel { Spacing=6, Margin=new(0,0,12,0) };
        var selection=new WrapPanel { Orientation=Orientation.Horizontal };
        selection.Children.Add(new TextBlock { Text="Вибір моторів", FontWeight=FontWeight.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,16,0) });
        var motorPicker=new WrapPanel();
        for(int m=0;m<sources.Length;m++) {
            int index=m; var pick=new CheckBox { Content=$"M{m+1}", IsChecked=selectedMotors[m], Margin=new(0,0,8,4), Name=$"MotorToggle{m}" };
            motorPicker.Children.Add(pick);
        }
        selection.Children.Add(motorPicker);
        var selectAll=new Button { Content="Усі", Name="SelectAllMotors", Padding=new(10,5), Margin=new(8,0,4,0) };
        selectAll.Click+=(_,_)=>{ foreach(var pick in motorPicker.Children.OfType<CheckBox>()) pick.IsChecked=true; };
        selection.Children.Add(selectAll);
        var resetSelection=new Button {Content="Скинути",Name="ResetMotorSelection",Padding=new(10,5)};
        ToolTip.SetTip(resetSelection,"Зняти вибір усіх моторів; загальні показники залишаться");
        resetSelection.Click+=(_,_)=>{foreach(var pick in motorPicker.Children.OfType<CheckBox>())pick.IsChecked=false;};
        selection.Children.Add(resetSelection);
        var selectionBox=new Border {Child=selection,Padding=new(10),Margin=new(0,10,0,0),CornerRadius=new(8),Background=Brushes.White,BorderBrush=Brush.Parse("#DCE1E6"),BorderThickness=new(1)};
        Grid.SetRow(selectionBox,2);root.Children.Add(selectionBox);
        var groups=Enumerable.Range(0,4).Select(m=>new StackPanel { Spacing=4 }).ToArray();
        var sections=groups.Select((g,m)=>new Expander { Header=$"Мотор {m+1}", Content=g, IsExpanded=true, HorizontalAlignment=HorizontalAlignment.Stretch }).ToArray();
        foreach(var section in sections) legend.Children.Add(section);
        var shared=new StackPanel { Spacing=4 };
        legend.Children.Insert(0,new Expander { Header="Загальні показники",Content=shared,IsExpanded=true,HorizontalAlignment=HorizontalAlignment.Stretch });
        var buttons=new Button[32]; var labels=new TextBlock[32];
        for(int i=0;i<32;i++) {
            int axis=i;
            labels[i]=new TextBlock { FontSize=11, Width=174, Height=18, TextWrapping=TextWrapping.NoWrap };
            buttons[i]=new Button { Name=$"SeriesToggle{i}", Content=labels[i], HorizontalAlignment=HorizontalAlignment.Stretch, HorizontalContentAlignment=HorizontalAlignment.Left, Padding=new(10,6) };
            buttons[i].Click+=(_,_)=>{ Chart.SetSeriesVisible(axis,!Chart.VisibleSeries[axis]); RefreshLegend(); };
            if(i%8 is 1 or 5) { if(i<8)shared.Children.Add(buttons[i]); else Chart.VisibleSeries[i]=false; }
            else groups[i/8].Children.Add(buttons[i]);
        }
        for(int m=0;m<4;m++) {
            int motorIndex=m;
            var actions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=4};
            foreach(bool visible in new[]{true,false}) {
                var action=new Button {Content=visible?"Усі":"Приховати",Name=$"MotorSeries{m}{(visible?"All":"None")}",FontSize=11,Padding=new(8,4)};
                action.Click+=(_,_)=>{for(int c=0;c<8;c++)if(c is not (1 or 5))Chart.SetSeriesVisible(motorIndex*8+c,visible);RefreshLegend();};
                actions.Children.Add(action);
            }
            groups[m].Children.Insert(0,actions);
        }
        combined.Children.Add(new ScrollViewer { Content=legend });
        var plot = new Grid { RowDefinitions=new("Auto,*") };
        plot.Children.Add(new TextBlock { Text="Незалежні масштаби · межі — у підказках легенди · значення при наведенні", FontSize=11, TextWrapping=TextWrapping.Wrap, Foreground=Brush.Parse("#66717C"), Margin=new(0,0,0,6) });
        Grid.SetRow(Chart,1); plot.Children.Add(Chart); Grid.SetColumn(plot,1); combined.Children.Add(plot); Grid.SetRow(combined,3); root.Children.Add(combined);
        var old=new StackPanel { Spacing=10 };
        var sharedOld=new StackPanel { Spacing=8 };
        foreach(int c in new[]{1,3}) sharedOld.Children.Add(new Expander {Header=ChartUi.ChannelHeading(c==1?"Загальний струм, А":"Звук стенда",c==1?1:5),Content=legacyByMotor[0][c],IsExpanded=true,HorizontalAlignment=HorizontalAlignment.Stretch});
        old.Children.Add(new Expander {Header="Загальні показники",Content=sharedOld,IsExpanded=true,HorizontalAlignment=HorizontalAlignment.Stretch});
        var oldGroups=new Expander[sources.Length];
        for(int m=0;m<sources.Length;m++) {
            var group=new StackPanel { Spacing=8 };
            for(int c=0;c<4;c++) if(c is 0 or 2) group.Children.Add(new Expander { Header=ChartUi.ChannelHeading(new[]{"Тяга, г","Струм, А","Вібрація XYZ, g","Звук"}[c],new[]{0,1,2,5}[c]), Content=legacyByMotor[m][c], IsExpanded=true, HorizontalAlignment=HorizontalAlignment.Stretch });
            foreach(var name in new[]{"Температура, °C","Оберти, RPM"}) group.Children.Add(new Expander { Header=ChartUi.ChannelHeading(name,name.StartsWith("Температура")?6:7),Content=new TextBlock {Text="Немає даних",Margin=new(12)},HorizontalAlignment=HorizontalAlignment.Stretch });
            var groupActions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=6};
            foreach(bool expanded in new[]{true,false}) {
                var action=new Button {Content=expanded?"Розгорнути всі":"Згорнути",FontSize=11,Padding=new(8,4)};
                action.Click+=(_,_)=>{foreach(var section in group.Children.OfType<Expander>())section.IsExpanded=expanded;};
                groupActions.Children.Add(action);
            }
            group.Children.Insert(0,groupActions);
            oldGroups[m]=new Expander { Name=$"SeparateMotor{m}",Header=$"Мотор {m+1}",Content=group,IsExpanded=true,HorizontalAlignment=HorizontalAlignment.Stretch }; old.Children.Add(oldGroups[m]);
        }
        var legacy=new ScrollViewer { Content=old, IsVisible=false, Margin=new(0,14,0,0) }; Grid.SetRow(legacy,3); root.Children.Add(legacy);
        toggle.Click+=(_,_)=> { IsLegacy=!IsLegacy; legacy.IsVisible=IsLegacy; combined.IsVisible=!IsLegacy; toggle.Content=IsLegacy?"Спільний графік":"Окремі графіки"; };
        var footer=new Grid { ColumnDefinitions=new("*,Auto"), Margin=new(0,12,0,0) };
        footer.Children.Add(new TextBlock { Text="Ctrl + прокручування — масштаб · Ctrl+Z — назад\nЛКМ — ділянка · ПКМ — пересування", FontSize=11, TextWrapping=TextWrapping.Wrap, VerticalAlignment=VerticalAlignment.Center });
        var stopButton=new Button { Content="■ STOP · зупинити" }; stopButton.Classes.Add("stop"); stopButton.Click+=(_,_)=>stop(); Grid.SetColumn(stopButton,1); footer.Children.Add(stopButton); Grid.SetRow(footer,4); root.Children.Add(footer);
        Content=root;
        void RefreshLegend() {
            for(int i=0;i<32;i++) {
                bool active=Chart.VisibleSeries[i], has=Chart.HasSeries(i); var scale=Chart.SeriesScale(i); var value=Chart.LatestValue(i);
                labels[i].Text=$"{(active?"●":"○")} {(i%8==1?"Загальний струм":i%8==5?"Звук стенда":TelemetryChart.SeriesNames[i%8])}  "+(has?$"{value:0.##} {Chart.SeriesUnit(i)}":"немає даних");
                ToolTip.SetTip(buttons[i],$"Масштаб: {scale.Min:0.##}…{scale.Max:0.##} {Chart.SeriesUnit(i)}");
                labels[i].Foreground=Brush.Parse(active?"#FFFFFF":"#59616B");
                buttons[i].Background=Brush.Parse(active?TelemetryChart.SeriesColors[i%8]:"#E4E7EB");
            }
        }
        void Update() {
            Chart.UpdateMotors(sources.Select((c,m)=>(IReadOnlyList<PropTest.Core.Measurement>)(selectedMotors[m]?c.Samples:m==0?c.Samples.Select(s=>s with { ThrustGrams=null,VibrationX=null,VibrationY=null,VibrationZ=null }).ToArray():Array.Empty<PropTest.Core.Measurement>())).ToArray());
            for(int m=0;m<4;m++) sections[m].IsVisible=m<sources.Length && selectedMotors[m];
            for(int m=0;m<sources.Length;m++) { foreach(var c in legacyByMotor[m])c.Update(sources[m].Samples); oldGroups[m].IsVisible=selectedMotors[m]; } RefreshLegend();
        }
        for(int m=0;m<sources.Length;m++) { int index=m; var pick=(CheckBox)motorPicker.Children[m]; pick.IsCheckedChanged+=(_,_)=>{selectedMotors[index]=pick.IsChecked==true; Update();}; }
        Update();
        AttachedToVisualTree+=(_,_)=>{foreach(var c in sources)c.DataUpdated+=Update; Update();};
        DetachedFromVisualTree+=(_,_)=>{foreach(var c in sources)c.DataUpdated-=Update;};
    }
}

public sealed class MotorChartWindow : Window
{
    readonly MotorChartPanel panel;
    public TelemetryChart Chart => panel.Chart;
    public bool IsLegacy => panel.IsLegacy;
    public MotorChartWindow(TelemetryChart source,int motor,int? metric,string sourceLabel,Action stop,TelemetryChart[]? motorSources=null) {
        Title=$"PROpTEST · Мотор {motor}"; Width=1000; Height=720; MinWidth=760; MinHeight=600;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        panel=new MotorChartPanel(source,motor,metric,sourceLabel,stop,motorSources); Content=panel;
    }
}
