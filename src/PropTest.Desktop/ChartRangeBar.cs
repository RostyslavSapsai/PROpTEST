using Avalonia;
using Avalonia.Threading;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace PropTest.Desktop;

public sealed class ChartRangeBar : StackPanel
{
    public NumericUpDown From { get; } = new() { Width = 80, Height = 36, MinHeight = 0, TextAlignment = TextAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, ShowButtonSpinner = false, Minimum = 0, Maximum = 7200, Increment = .1m, FormatString = "0.##", Value = 0 };
    public NumericUpDown To { get; } = new() { Width = 80, Height = 36, MinHeight = 0, TextAlignment = TextAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, ShowButtonSpinner = false, Minimum = 0, Maximum = 7200, Increment = .1m, FormatString = "0.##", Value = 120 };
    readonly TextBlock error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    readonly Func<IReadOnlyList<TelemetryChart>> charts;
    readonly DispatcherTimer liveUpdate=new() {Interval=TimeSpan.FromMilliseconds(300)};
    bool resetting;
    public ChartRangeBar(Func<IReadOnlyList<TelemetryChart>> charts)
    {
        this.charts = charts; Spacing = 4;
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(ChartUi.Heading("Діапазон часу", "Введіть початок і кінець діапазону. Графік оновиться після короткої паузи; Enter застосовує одразу. «Весь діапазон» повертає повний огляд і враховує нові виміри. Дані запису не видаляються."));
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        StackPanel Field(string label, NumericUpDown input)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(0, 0, 12, 4) };
            group.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }); group.Children.Add(input);
            return group;
        }
        row.Children.Add(Field("Від, с", From)); row.Children.Add(Field("До, с", To));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(0,0,0,4) };
        var reset = new Button { Content = "Весь діапазон", Name = "ResetRangeButton", Height = 36, MinHeight = 0, Padding = new(12,6) };
        ToolTip.SetTip(reset, "Показати весь запис і автоматично враховувати нові виміри");
        reset.Click += (_, _) => Reset();
        actions.Children.Add(reset); row.Children.Add(actions);
        content.Children.Add(row);
        content.Children.Add(error);
        Children.Add(new Border { Child = content, Padding = new(12), CornerRadius = new(8), BorderThickness = new(1), BorderBrush = Brush.Parse("#DCE1E6"), Background = Brushes.White });
        liveUpdate.Tick+=(_,_)=>Apply();
        From.PropertyChanged+=ScheduleUpdate;To.PropertyChanged+=ScheduleUpdate;
        DetachedFromVisualTree+=(_,_)=>liveUpdate.Stop();
    }
    void ScheduleUpdate(object? sender,AvaloniaPropertyChangedEventArgs e)
    {
        if(e.Property==NumericUpDown.TextProperty && e.OldValue is null)return;
        if(resetting || (e.Property!=NumericUpDown.TextProperty && e.Property!=NumericUpDown.ValueProperty))return;
        liveUpdate.Stop();liveUpdate.Start();
    }
    public void Reset()
    {
        liveUpdate.Stop();resetting=true;
        var targets = charts();
        foreach (var chart in targets) chart.ResetView();
        From.Value = 0;
        To.Value = (decimal)Math.Min(7200, targets.Count == 0 ? 10 : targets.Max(chart => chart.ViewRange.End));
        error.IsVisible = false;resetting=false;
    }

    public bool Apply()
    {
        liveUpdate.Stop();
        if (!decimal.TryParse(From.Text,NumberStyles.Number,CultureInfo.CurrentCulture,out var start) || !decimal.TryParse(To.Text,NumberStyles.Number,CultureInfo.CurrentCulture,out var end) || start < 0 || end > 7200 || end - start < .05m)
        { error.Text = "Вкажіть час 0–7200 с. «До» має бути більше «Від» мінімум на 0,05 с."; error.IsVisible = true; return false; }
        foreach (var chart in charts()) if(chart.ViewRange!=((double)start,(double)end)) chart.SetTimeRange((double)start, (double)end);
        error.IsVisible = false; return true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.Key==Key.Enter){Apply();e.Handled=true;return;}
        if (e.Source is Button && e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control)
        { foreach (var chart in charts()) chart.UndoView(); e.Handled = true; }
    }
}


