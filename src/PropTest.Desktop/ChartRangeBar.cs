using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace PropTest.Desktop;

public sealed class ChartRangeBar : StackPanel
{
    public NumericUpDown From { get; } = new() { Width = 100, TextAlignment = TextAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, ShowButtonSpinner = false, Minimum = 0, Maximum = 7200, Increment = .1m, FormatString = "0.##", Value = 0 };
    public NumericUpDown To { get; } = new() { Width = 100, TextAlignment = TextAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, ShowButtonSpinner = false, Minimum = 0, Maximum = 7200, Increment = .1m, FormatString = "0.##", Value = 120 };
    readonly TextBlock error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    readonly Func<IReadOnlyList<TelemetryChart>> charts;
    public ChartRangeBar(Func<IReadOnlyList<TelemetryChart>> charts)
    {
        this.charts = charts; Spacing = 4;
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        StackPanel Field(string label, NumericUpDown input)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(0, 0, 20, 0) };
            group.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }); group.Children.Add(input);
            return group;
        }
        row.Children.Add(Field("Від, с", From)); row.Children.Add(Field("До, с", To));
        var apply = new Button { Content = "Показати" };
        apply.Click += (_, _) => Apply(); row.Children.Add(apply);
        Children.Add(row); Children.Add(error);
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


