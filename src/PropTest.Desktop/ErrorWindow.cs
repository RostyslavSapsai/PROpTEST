using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace PropTest.Desktop;

public sealed class ErrorWindow : Window
{
    readonly TextBlock heading;
    readonly StackPanel messages = new() { Spacing = 18 };
    readonly HashSet<string> displayed = new();
    public ErrorWindow(string text)
    {
        Title = "PROpTEST · Помилка"; Width = 600; Height = 410; MinWidth = 420; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24) };
        heading = new TextBlock { FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#B42318"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16) };
        root.Children.Add(heading);
        var scroll = new ScrollViewer { Content = messages }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var close = new Button { Content = "Зрозуміло", Name = "DismissErrorButton", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,16,0,0) };
        close.Click += (_, _) => Close(); Grid.SetRow(close, 2); root.Children.Add(close);
        Content = root; Add(text);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Opened += (_, _) => close.Focus();
    }
    public void Add(string text)
    {
        if (!displayed.Add(text)) return;
        var explanation = ErrorExplanation.From(text);
        heading.Text = displayed.Count == 1 ? explanation.Title : "Потрібно перевірити кілька речей";
        var section = new StackPanel { Spacing = 10 };
        section.Children.Add(new TextBlock { Text = explanation.Title, FontWeight = FontWeight.SemiBold, IsVisible = displayed.Count > 1 });
        section.Children.Add(new TextBlock { Text = explanation.Guidance, TextWrapping = TextWrapping.Wrap, FontSize = 15 });
        section.Children.Add(new Expander { Header = "Технічні деталі", HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 } });
        messages.Children.Add(section);
    }
}
