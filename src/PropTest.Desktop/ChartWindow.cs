using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PropTest.Desktop;

public sealed class ChartWindow : Window
{
    public TelemetryChart Chart { get; }
    public ChartWindow(TelemetryChart source, bool live, string sourceLabel, Action stop, bool compact = false, string? motorName = null)
    {
        var title = source.Channel switch
        {
            ChartChannel.Current => "Струм, А",
            ChartChannel.Vibration => "Вібрація · прискорення XYZ, g",
            ChartChannel.Sound => source.SoundUnit == "dB*" ? "Звук · стара умовна шкала dB* (не SPL)" : "Звук · амплітуда ADC (не dB SPL)",
            _ => "Тяга, г"
        };
        Title = "PROpTEST · " + (motorName is null ? "" : motorName + " · ") + title;
        Width = compact ? 620 : 1100; Height = compact ? 520 : 720; MinWidth = 480; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Chart = new TelemetryChart { Channel = source.Channel };
        Chart.Update(source.Samples);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20) };
        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(new TextBlock { Text = (motorName is null ? "" : motorName + " · ") + title, FontSize = 22, FontWeight = FontWeight.SemiBold });
        header.Children.Add(new TextBlock { Text = (live ? "Живий перегляд · " : "Збережений запис · ") + sourceLabel, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new ChartRangeBar(() => [Chart]));
        root.Children.Add(header); Grid.SetRow(Chart, 1); root.Children.Add(Chart);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(new TextBlock { Text = "ЛКМ — виділити ділянку · ПКМ — пересунути\nCtrl + прокручування миші / touchpad — масштаб · Ctrl+Z — попередній масштаб · подвійний клік — авто", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var stopButton = new Button { Content = "■ STOP · зупинити", Name = "DetailStopButton" }; stopButton.Classes.Add("stop"); stopButton.Click += (_, _) => stop();
        Grid.SetColumn(stopButton, 1); footer.Children.Add(stopButton); Grid.SetRow(footer, 2); root.Children.Add(footer);
        Content = root;
        if (live)
        {
            void Update() => Chart.Update(source.Samples);
            source.DataUpdated += Update;
            Closed += (_, _) => source.DataUpdated -= Update;
        }
    }
}





