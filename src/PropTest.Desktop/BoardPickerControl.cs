using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace PropTest.Desktop;

public sealed class BoardPickerControl : UserControl
{
    readonly Expander panel = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly StackPanel rows = new() { Spacing = 8 };
    readonly TextBlock search = new() { Foreground = Brush.Parse("#596779"), TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar progress = new() { IsIndeterminate = true, Height = 3, IsVisible = false };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(8) };
    readonly Button saved = new() { Content = "Збережені мережі", FontSize = 12, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0, 6), HorizontalAlignment = HorizontalAlignment.Left };
    bool showSaved;
    string? expandedHost;
    MainViewModel? vm;
    string signature = "";
    public bool IsExpanded { get => panel.IsExpanded; set => panel.IsExpanded = value; }
    public BoardPickerControl()
    {
        var refresh = new Button { Content = "Оновити список", HorizontalAlignment = HorizontalAlignment.Left };
        refresh.Click += async (_, _) => { if (vm is not null) await vm.ScanSavedBoardsAsync(); };
        saved.Click += (_, _) => { showSaved = !showSaved; expandedHost = null; Refresh(); };
        panel.Content = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 0, 0), Children = {
            progress, search, rows, refresh, saved,
            new TextBlock { Text = "Нова плата? Підключіть її один раз через USB. Інструкція — ⓘ.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brush.Parse("#596779") }
        }};
        panel.PropertyChanged += async (_, e) => {
            if (e.Property == Expander.IsExpandedProperty && panel.IsExpanded && vm is not null) await vm.ScanSavedBoardsAsync();
        };
        timer.Tick += async (_, _) => { if (panel.IsExpanded && IsEffectivelyVisible && vm is not null) await vm.ScanSavedBoardsAsync(); };
        DataContextChanged += (_, _) => {
            if (vm is not null) vm.PropertyChanged -= OnChanged;
            vm = DataContext as MainViewModel;
            if (vm is not null) vm.PropertyChanged += OnChanged;
            signature = ""; Refresh();
        };
        AttachedToVisualTree += (_, _) => { if (vm is not null) { vm.PropertyChanged -= OnChanged; vm.PropertyChanged += OnChanged; } Refresh(); timer.Start(); };
        DetachedFromVisualTree += (_, _) => { timer.Stop(); if (vm is not null) vm.PropertyChanged -= OnChanged; };
        Content = new StackPanel { Spacing = 6, Children = {
            new TextBlock { Text = "Вибір пристрою", FontWeight = FontWeight.SemiBold }, panel
        }};
    }
    void OnChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
    void Refresh()
    {
        if (vm is null) return;
        panel.Header = vm.BoardPickerTitle;
        progress.IsVisible = vm.SearchingBoards;
        search.Text = showSaved ? "Збережені мережі" : vm.SearchingBoards ? "Пошук плат у вашій мережі…" : "Доступні пристрої";
        saved.Content = showSaved ? "← Доступні пристрої" : "Збережені мережі";
        string next = string.Join("|", vm.SavedBoards.Select(b => $"{b.Hostname}:{b.Name}:{b.AutoConnect}:{vm.BoardAvailability(b.Hostname)}"))
            + vm.SelectedBoard?.Hostname + vm.CanSelectBoard + vm.CanConnect + vm.IsSelectedBoardConnected + showSaved + expandedHost;
        if (next == signature) return;
        signature = next; rows.Children.Clear();
        var visible = vm.SavedBoards.Where(b => showSaved || vm.IsBoardAvailable(b.Hostname)).ToArray();
        if (visible.Length == 0) rows.Children.Add(new TextBlock { Text = showSaved ? "Збережених плат поки немає." : "Доступних пристроїв поки немає. Пошук продовжується автоматично.", TextWrapping = TextWrapping.Wrap });
        foreach (var board in visible)
        {
            bool selected = board.Hostname == expandedHost && board.Hostname == vm.SelectedBoard?.Hostname;
            var select = new Button {
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(10),
                IsEnabled = vm.CanSelectBoard,
                Content = new StackPanel { Spacing = 4, Children = {
                    new TextBlock { Text = board.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = vm.BoardAvailability(board.Hostname), FontSize = 12, Foreground = Brush.Parse("#596779") }
                }}
            };
            select.Click += (_, _) => { expandedHost = expandedHost == board.Hostname ? null : board.Hostname; vm.SelectedBoard = board; Refresh(); };
            var details = new StackPanel { Spacing = 8, Margin = new Thickness(12, 0, 12, 12) };
            if (selected)
            {
                var auto = new CheckBox { Content = "Підключатися автоматично", IsChecked = board.AutoConnect };
                auto.IsCheckedChanged += (_, _) => vm.BoardAutoConnect = auto.IsChecked == true;
                var rename = new Button { Content = "Змінити назву", FontSize = 12 };
                rename.Click += async (_, _) => { if (TopLevel.GetTopLevel(this) is Window owner) await new BoardNameWindow(vm).ShowDialog(owner); };
                var connect = new Button { Content = vm.IsSelectedBoardConnected ? "Відключити" : "Підключити", IsEnabled = vm.CanConnect };
                connect.Click += async (_, _) => { if (vm.IsSelectedBoardConnected) vm.Disconnect(); else await vm.ConnectAsync(); };
                details.Children.Add(auto);
                details.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { rename, connect } });
            }
            rows.Children.Add(new Border {
                Background = Brush.Parse(selected ? "#EDF0F3" : "#FAFBFC"), BorderBrush = Brush.Parse("#D9DFE5"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
                Child = new StackPanel { Children = { select, details } }
            });
        }
    }
}
