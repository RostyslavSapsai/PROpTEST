using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PropTest.Desktop;

internal static class ChartUi
{
    public static Grid Heading(string title, string help, double size=14)
    {
        var grid=new Grid {ColumnDefinitions=new("*,Auto")};
        grid.Children.Add(new TextBlock {Text=title,FontSize=size,FontWeight=FontWeight.SemiBold,VerticalAlignment=VerticalAlignment.Center});
        var info=new Button {Content="ⓘ",FontSize=20,Width=28,Height=28,MinHeight=0,Padding=new(0),Background=Brushes.Transparent,
            Flyout=new Flyout {Content=new TextBlock {Text=help,MaxWidth=340,TextWrapping=TextWrapping.Wrap}}};
        AutomationProperties.SetName(info,"Інформація: "+title);
        ToolTip.SetTip(info,"Інформація");Grid.SetColumn(info,1);grid.Children.Add(info);
        return grid;
    }
    public static Border ChannelHeading(string title,int axis) => new() {
        Background=Brush.Parse(TelemetryChart.SeriesColors[axis]),CornerRadius=new(5),Padding=new(10,5),
        Child=new TextBlock {Text=title,Foreground=Brushes.White,FontWeight=FontWeight.SemiBold}
    };
}
