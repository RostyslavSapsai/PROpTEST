using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PropTest.Desktop;
using PropTest.Infrastructure;
using Xunit;

namespace PropTest.Tests;
public sealed class BoardPickerTests
{
    [AvaloniaFact]
    public async Task OnlyAvailableBoardsAppearAndDetailsRequireClick()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dir = Path.Combine(Path.GetTempPath(), "proptest-picker-" + Guid.NewGuid());
        var store = new SavedBoardStore(Path.Combine(dir, "boards-v1.json"));
        store.Remember("proptest-v2-111111.local", "", "test");
        bool available = false;
        var vm = new MainViewModel(Path.Combine(dir, "test.db"), false, false, () => [],
            (_, _, _) => available ? Task.CompletedTask : Task.FromException(new IOException("Offline")));
        var window = new MainWindow(vm); window.Show();
        try {
            window.FindControl<TabControl>("Tabs")!.SelectedIndex = 2;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var picker = window.FindControl<BoardPickerControl>("BoardsPanel")!;
            picker.IsExpanded = true; await vm.ScanSavedBoardsAsync();
            IEnumerable<T> All<T>() => picker.GetLogicalDescendants().OfType<T>().Distinct();
            Button Button(string label) => All<Button>().Single(b => b.Content?.ToString() == label);
            void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            void Capture(string name) {
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var output = Environment.GetEnvironmentVariable("PROPTEST_ARTIFACTS");
                if (output is null) return;
                Directory.CreateDirectory(output);
                using var image = new RenderTargetBitmap(new PixelSize(1220,850), new Vector(96,96));
                image.Render(window); image.Save(Path.Combine(output, name + ".png"));
            }
            Assert.Empty(All<CheckBox>());
            Assert.Contains(All<TextBlock>(), b => b.Text?.Contains("Доступних пристроїв поки немає") == true);
            Capture("picker-empty");
            Click(Button("Збережені мережі"));
            Assert.Contains(All<TextBlock>(), b => b.Text == "PROpTEST · 111111");
            Assert.Empty(All<CheckBox>());
            Click(Button("← Доступні пристрої"));
            available = true; await vm.ScanSavedBoardsAsync();
            Assert.Contains(All<TextBlock>(), b => b.Text == "PROpTEST · 111111");
            Assert.Empty(All<CheckBox>());
            var card = All<Button>().Single(b => b.Content is StackPanel);
            Click(card);
            Assert.Single(All<CheckBox>());
            Assert.NotNull(Button("Підключити"));
            Capture("picker-expanded");
            Click(All<Button>().Single(b => b.Content is StackPanel));
            Assert.Empty(All<CheckBox>());
            available = false; await vm.ScanSavedBoardsAsync();
            Assert.DoesNotContain(All<TextBlock>(), b => b.Text == "PROpTEST · 111111");
            Click(Button("Збережені мережі"));
            Assert.Contains(All<TextBlock>(), b => b.Text == "PROpTEST · 111111");
        } finally { window.Close(); }
    }
}

