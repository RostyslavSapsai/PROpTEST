using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

namespace PropTest.Desktop;

public sealed class NetworkStatusWindow : Window
{
    public NetworkStatusWindow(MainViewModel viewModel)
    {
        Title = "PROpTEST · Підключення до роутера";
        Width = 580; Height = 440; MinWidth = 420; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DataContext = viewModel;
        var status = new SelectableTextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 17 };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(MainViewModel.HomeNetworkStatus)));
        var address = new SelectableTextBlock { FontSize = 26, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        address.Bind(TextBlock.TextProperty, new Binding(nameof(MainViewModel.HomeNetworkAddress)));
        var copy = new Button { Content = "Скопіювати IP-адресу" };
        copy.Bind(IsEnabledProperty, new Binding(nameof(MainViewModel.HasHomeNetworkAddress)));
        copy.Click += async (_, _) =>
        {
            if (Clipboard is null || !viewModel.HasHomeNetworkAddress) return;
            try { await Clipboard.SetTextAsync(viewModel.HomeNetworkAddress); copy.Content = "Адресу скопійовано"; }
            catch (Exception ex) { new ErrorWindow("Не вдалося скопіювати адресу: " + ex.Message).Show(this); }
        };
        var close = new Button { Content = "Зрозуміло" }; close.Click += (_, _) => Close();
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 14,
                Children =
                {
                    new TextBlock { Text = "З’єднання з роутером", FontSize = 23, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    status, address,
                    new SelectableTextBlock { Text = "Результат оновлюється тут автоматично. Якщо плата отримала адресу, можна перейти на Wi-Fi у головному вікні. Інтернет ноутбука залишається у вашій мережі.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new WrapPanel { Orientation = Orientation.Horizontal, Children = { copy, close } }
                }
            }
        };
        close.Margin = new Thickness(12, 0, 0, 0);
    }
}
