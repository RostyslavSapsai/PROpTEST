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
    public ChartRangeBar(Func<IReadOnlyList<TelemetryChart>> charts)
    {
        this.charts = charts; Spacing = 4;
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = "Діапазон часу", FontWeight = FontWeight.SemiBold, FontSize = 14 });
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        StackPanel Field(string label, NumericUpDown input)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(0, 0, 12, 4) };
            group.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }); group.Children.Add(input);
            return group;
        }
        row.Children.Add(Field("Від, с", From)); row.Children.Add(Field("До, с", To));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(0,0,0,4) };
        var apply = new Button { Content = "Застосувати", Height = 36, MinHeight = 0, Padding = new(12,6) };
        var reset = new Button { Content = "Весь діапазон", Name = "ResetRangeButton", Height = 36, MinHeight = 0, Padding = new(12,6) };
        ToolTip.SetTip(reset, "Показати весь запис і автоматично враховувати нові виміри");
        apply.Click += (_, _) => Apply(); reset.Click += (_, _) => Reset();
        actions.Children.Add(apply); actions.Children.Add(reset); row.Children.Add(actions);
        content.Children.Add(row);
        content.Children.Add(new TextBlock { Text = "Показати весь часовий діапазон. Дані запису зберігаються.", FontSize = 11, Foreground = Brush.Parse("#66717C"), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(error);
        Children.Add(new Border { Child = content, Padding = new(12), CornerRadius = new(8), BorderThickness = new(1), BorderBrush = Brush.Parse("#DCE1E6"), Background = Brushes.White });
    }
    public void Reset()
    {
        var targets = charts();
        foreach (var chart in targets) chart.ResetView();
        From.Value = 0;
        To.Value = (decimal)Math.Min(7200, targets.Count == 0 ? 10 : targets.Max(chart => chart.ViewRange.End));
        error.IsVisible = false;
    }

    public bool Apply()
    {
        if (From.Value is not { } start || To.Value is not { } end || start < 0 || end > 7200 || end - start < .05m)
        { error.Text = "Вкажіть час 0–7200 с. «До» має бути більше «Від» мінімум на 0,05 с."; error.IsVisible = true; return false; }
        foreach (var chart in charts()) chart.SetTimeRange((double)start, (double)end);
        error.IsVisible = false; return true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Source is Button && e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control)
        { foreach (var chart in charts()) chart.UndoView(); e.Handled = true; }
    }
}


