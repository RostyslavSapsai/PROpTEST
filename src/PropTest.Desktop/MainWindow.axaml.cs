using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PropTest.Core;
using Avalonia.Platform.Storage;

namespace PropTest.Desktop;
public partial class MainWindow : Window
{
    readonly DispatcherTimer timer;
    NetworkStatusWindow? networkWindow;
    S3ControlWindow? motorControl;
    public ErrorWindow? ActiveError { get; private set; }
    public MainViewModel ViewModel { get; }
    public MainWindow() : this(new MainViewModel(autoConnect: true)) { }
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent(); ViewModel = vm; DataContext = vm;
        MotorsOverview.Stop = StopAll;
        MotorsOverview.ControlRequested = OpenMotorControl;
        ChartRangeHost.Content = new ChartRangeBar(() => [ThrustChart, CurrentChart, VibrationChart, SoundChart]);
        ArchiveRangeHost.Content = new ChartRangeBar(() => [ArchiveThrust, ArchiveCurrent, ArchiveVibration, ArchiveSound]);
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += async (_, _) => { RefreshCharts(); await ViewModel.CheckPortsAsync(); }; timer.Start();
        Closed += (_, _) => { timer.Stop(); ActiveError?.Close(); networkWindow?.Close(); motorControl?.Close(); vm.Dispose(); };
    }
    public void RefreshCharts()
    {

        ViewModel.Refresh(); MotorsOverview.UpdateLegacy(ViewModel.Samples, ViewModel.SourceLabel, ViewModel.IsRunning); ThrustChart.Update(ViewModel.Samples); CurrentChart.Update(ViewModel.Samples); VibrationChart.Update(ViewModel.Samples); SoundChart.Update(ViewModel.Samples);
        var saved = ViewModel.SelectedData?.Samples ?? Array.Empty<Measurement>(); ArchiveThrust.Update(saved); ArchiveCurrent.Update(saved); ArchiveVibration.Update(saved); ArchiveSound.Update(saved);
        if (IsVisible) while (ViewModel.TakeError() is { } error)
        {
            if (ActiveError is { } existing) { existing.Add(error); existing.Activate(); }
            else { var dialog = new ErrorWindow(error); ActiveError = dialog; dialog.Closed += (_, _) => ActiveError = null; dialog.Show(this); }
        }
    }
    TelemetryChart? ChartFrom(object? sender) => sender is Button { Tag: string name } ? this.FindControl<TelemetryChart>(name) : null;
    void OnResetChart(object? sender, RoutedEventArgs e) => ChartFrom(sender)?.ResetView();
    void OnOpenChart(object? sender, RoutedEventArgs e)
    {
        if (ChartFrom(sender) is { } chart) OpenChart(chart);
    }
    public Window OpenChart(TelemetryChart chart)
    {
        bool live = chart == ThrustChart || chart == CurrentChart || chart == VibrationChart || chart == SoundChart;
        var detail = new ChartWindow(chart, live, live ? ViewModel.SourceLabel : ViewModel.ArchiveSummary, StopAll);
        detail.Show(this); return detail;
    }
    async void OnConnect(object? sender, RoutedEventArgs e) => await ViewModel.ConnectAsync();
    Window OpenMotorControl(int motor) {
        if(motorControl is not null){motorControl.SelectMotor(motor);motorControl.Activate();return motorControl;}
        bool connected=ViewModel.CanOpenBoardWeb;
        motorControl=new S3ControlWindow(connected?ViewModel.BoardWebAddress:"http://127.0.0.1/",connected?ViewModel.WifiPassword:"",motor:motor,requireFour:true,showGraphs:i=>MotorsOverview.OpenMotor(i));
        motorControl.Closed+=(_,_)=>motorControl=null;motorControl.Show(this);return motorControl;
    }
    void StopAll(){ViewModel.Stop();if(motorControl is {} control)_=control.StopAllAsync();}
    void OnOpenS3Control(object? sender,RoutedEventArgs e)=>OpenMotorControl(0);
    async void OnCopyWifiPassword(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is null || !ViewModel.HasWifiPassword) return;
        try
        {
            await Clipboard.SetTextAsync(ViewModel.WifiPassword);
            if (sender is Button button) button.Content = "Пароль скопійовано";
        }
        catch (Exception ex) { new ErrorWindow("Не вдалося скопіювати пароль: " + ex.Message).Show(this); }
    }

    async void OnCopyNetworkAddress(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is null || !ViewModel.HasHomeNetworkAddress) return;
        try { await Clipboard.SetTextAsync(ViewModel.HomeNetworkAddress); if (sender is Button b) b.Content = "Адресу скопійовано"; }
        catch (Exception ex) { new ErrorWindow("Не вдалося скопіювати адресу: " + ex.Message).Show(this); }
    }
    void OnConfigureHomeWifi(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.ConfigureHomeWifi()) return;
        if (networkWindow is not null) { networkWindow.Activate(); return; }
        networkWindow = new NetworkStatusWindow(ViewModel);
        networkWindow.Closed += (_, _) => networkWindow = null;
        networkWindow.Show(this);
    }
    async void OnRenameBoard(object? sender, RoutedEventArgs e) => await new BoardNameWindow(ViewModel).ShowDialog(this);
    async void OnConnectionHelp(object? sender, RoutedEventArgs e) => await new ConnectionHelpWindow().ShowDialog(this);
    void OnStart(object? sender, RoutedEventArgs e) => ViewModel.Start();
    void OnStartThrottle(object? sender, RoutedEventArgs e) { if (ViewModel.CanSetStartThrottle) ViewModel.Throttle = 6; }
    void OnStop(object? sender, RoutedEventArgs e) => StopAll();
    void OnDisconnect(object? sender, RoutedEventArgs e) => ViewModel.Disconnect();
    void OnPorts(object? sender, RoutedEventArgs e) => ViewModel.ScanPorts();
    void OnBreak(object? sender, RoutedEventArgs e) => ViewModel.BreakLink();
    void OnArchive(object? sender, RoutedEventArgs e) => ViewModel.RefreshArchive();
    bool exporting;
    async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (exporting) return;
        exporting = true;
        IStorageFile? selectedFile = null;
        try
        {
            await ViewModel.ExportSelectedAsync(async suggested =>
            {
                selectedFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Зберегти експеримент як CSV", SuggestedFileName = suggested,
                    DefaultExtension = "csv", ShowOverwritePrompt = true,
                    FileTypeChoices = [new FilePickerFileType("CSV — таблиця вимірів") { Patterns = ["*.csv"] }]
                });
                if (selectedFile is null) return null;
                return (await selectedFile.OpenWriteAsync(), selectedFile.TryGetLocalPath() ?? selectedFile.Name);
            });
            RefreshCharts();
        }
        finally { selectedFile?.Dispose(); exporting = false; }
    }
}







