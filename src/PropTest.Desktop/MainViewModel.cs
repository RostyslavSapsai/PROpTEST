using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO.Ports;
using System.Globalization;
using System.Text;
using PropTest.Core;
using PropTest.Infrastructure;

namespace PropTest.Desktop;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    readonly SelectableDevice device = new();
    readonly SqliteRunRepository repository;
    readonly SavedBoardStore boardStore;
    readonly Func<string, string, CancellationToken, Task> probeNetwork;
    SavedBoard? selectedBoard;
    string boardName = "", rememberedKey = "", rememberedUsb = "";
    bool autoSuppressed, networkSearch;
    long nextNetworkSearch;
    int discoveryEpoch;
    public System.Collections.ObjectModel.ObservableCollection<SavedBoard> SavedBoards { get; } = [];
    void RefreshSavedBoards() {
        var selection = selectedBoard;
        selectedBoard = null;
        Changed(nameof(SelectedBoard));
        SavedBoards.Clear();
        foreach (var board in boardStore.Boards) SavedBoards.Add(board);
        selectedBoard = selection;
        Changed(nameof(SelectedBoard));
    }
    public SavedBoard? SelectedBoard {
        get => selectedBoard;
        set {
            if (!CanSelectBoard || value is null || value == selectedBoard) return;
            discoveryEpoch++; selectedBoard = value; boardName = value.Name;
            if (snapshot.State == RunState.Disconnected) device.Wifi.PortName = value.Hostname;
            boardStore.SelectedHostname = value.Hostname;
            try { rememberedKey = LocalBoardSecret.Unprotect(value.ProtectedKey); boardStore.Save(); }
            catch (Exception ex) { rememberedKey = ""; ReportError("Потрібно повторно з’єднати плату через USB: " + ex.Message); }
            autoSuppressed = false; nextNetworkSearch = 0; Changed(string.Empty);
        }
    }
    public bool IsSelectedBoardConnected => IsWifi && snapshot.State == RunState.Ready && selectedBoard?.Hostname == device.Wifi.PortName;
    public string ConnectedHostname => IsWifi && snapshot.State == RunState.Ready ? device.Wifi.PortName : "";
    public string BoardPickerTitle => selectedBoard?.Name ?? "Мої плати";
    public bool SearchingBoards { get; private set; }
    readonly Dictionary<string, bool> boardAvailability = new();
    public bool IsBoardAvailable(string host) => host == ConnectedHostname || boardAvailability.GetValueOrDefault(host);
    public string BoardAvailability(string host) => host == ConnectedHostname ? "Підключено" : boardAvailability.TryGetValue(host, out var available)
        ? available ? "Доступна в мережі" : "Недоступна зараз · збережена" : "Збережена плата";
    public async Task ScanSavedBoardsAsync()
    {
        if (SearchingBoards || disposed || !IsWifi) return;
        SearchingBoards = true; Changed(nameof(SearchingBoards));
        try {
            foreach (var board in boardStore.Boards.ToArray()) {
                if (disposed) break;
                if (board.Hostname == ConnectedHostname) { boardAvailability[board.Hostname] = true; continue; }
                try {
                    await probeNetwork(board.Hostname, LocalBoardSecret.Unprotect(board.ProtectedKey), cancellation.Token);
                    boardAvailability[board.Hostname] = true;
                } catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException or System.Net.Sockets.SocketException or System.Security.Cryptography.CryptographicException or FormatException) {
                    boardAvailability[board.Hostname] = false;
                }
            }
        } finally { SearchingBoards = false; if (!disposed) Changed(nameof(SearchingBoards)); }
    }
    public string BoardName { get => boardName; set { boardName = value; Changed(); } }
    public bool CanSelectBoard => !IsRunning && snapshot.State != RunState.Connecting && connectionTask.IsCompleted;
    public string SelectedBoardStatus => selectedBoard is null ? "Додайте плату через USB за інструкцією ⓘ." :
        IsWifi && snapshot.State == RunState.Ready && device.Wifi.PortName == selectedBoard.Hostname ? "Підключено" : "Збережена плата · натисніть «Підключити»";
    public string ConnectedBoardLabel => IsWifi && snapshot.State == RunState.Ready
        ? "Підключено: " + (boardStore.Boards.Find(b => b.Hostname == device.Wifi.PortName)?.Name ?? device.Wifi.PortName) : Status;
    public bool CanEditWifiHost => CanChooseDevice && !HasSavedBoard;
    public bool HasSavedBoard => selectedBoard is not null;
    public bool BoardAutoConnect {
        get => selectedBoard?.AutoConnect ?? false;
        set {
            if (selectedBoard is null || selectedBoard.AutoConnect == value) return;
            discoveryEpoch++; selectedBoard = selectedBoard with { AutoConnect = value };
            try { boardStore.Replace(selectedBoard); RefreshSavedBoards(); } catch (Exception ex) { ReportError(ex.Message); }
            autoSuppressed = false; nextNetworkSearch = 0; Changed(string.Empty);
        }
    }
    public string SavedAccess => rememberedKey.Length > 0 ? "Доступ збережено на цьому комп’ютері. Вводити пароль не потрібно." : "Для запам’ятовування нової плати один раз підключіть її через USB.";
    public void RenameBoard() {
        if (selectedBoard is null) return;
        if (string.IsNullOrWhiteSpace(BoardName) || BoardName.Trim().Length > 60) { ReportError("Введіть назву плати від 1 до 60 символів."); return; }
        selectedBoard = selectedBoard with { Name = BoardName.Trim() };
        try { boardStore.Replace(selectedBoard); RefreshSavedBoards(); notice = "Назву плати збережено."; } catch (Exception ex) { ReportError(ex.Message); }
        Changed(string.Empty);
    }

    readonly RunController controller;
    readonly CancellationTokenSource cancellation = new();
    readonly Task worker;
    readonly bool autoConnect;
    readonly Func<IReadOnlyList<SerialPortInfo>> listPorts;
    readonly AutoConnectionTracker autoTracker = new();
    IReadOnlyList<SerialPortInfo> portDetails = Array.Empty<SerialPortInfo>();
    long lastPortCheck;
    bool checkingPorts, disposed;
    readonly Queue<string> errors = new();
    public string? TakeError() => errors.TryDequeue(out var error) ? error : null;
    void ReportError(string text) { notice = text; if (!errors.Contains(text)) errors.Enqueue(text); }
    void ReadSnapshot()
    {
        var next = controller.Snapshot();
        if (next.State == RunState.Faulted && snapshot.State != RunState.Faulted) ReportError(next.Message);
        snapshot = next;
    }
    Task connectionTask = Task.CompletedTask;
    bool limitEnabled = true;
    string? selectedPort;
    bool showAllPorts;
    public bool ShowAllPorts { get => showAllPorts; set { showAllPorts = value; Changed(); Changed(nameof(PortNames)); } }
    public IReadOnlyList<string> PortNames => portDetails.Where(p => ShowAllPorts || !OperatingSystem.IsWindows() || p.IsArduino).Select(p => p.PortName).ToArray();
    SessionSnapshot snapshot = new(RunState.Disconnected, "", null, 0, Array.Empty<Measurement>());
    string notice = "", ports = "Натисніть «Переглянути COM». Порти не відкриваються.";
    string testName = "Мотор 2807 · 7×4×3R";
    decimal interval = 200, limit = 30, duration = 60;
    bool thrustGuardEnabled = true;
    decimal thrustJumpGrams = 50, thrustJumpPercent = 30;
    IReadOnlyList<RunInfo> archive = Array.Empty<RunInfo>();
    RunInfo? selectedRun;
    public RunData? SelectedData { get; private set; }

    public MainViewModel(string? databasePath = null, bool runWorker = true, bool autoConnect = false, Func<IReadOnlyList<SerialPortInfo>>? listPorts = null, Func<string, string, CancellationToken, Task>? probeNetwork = null)
    {
        this.autoConnect = autoConnect;
        this.probeNetwork = probeNetwork ?? (async (host, key, token) => {
            using var probe = new S3ControlClient("http://" + host + "/", key);
            await probe.ReadDiscoveryStatusAsync(token);
        });
        this.listPorts = listPorts ?? SerialPortCatalog.List;
        repository = new(databasePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PropTestEngineering", "runs-v1.db"));
        boardStore = new SavedBoardStore(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "boards-v1.json"));
        boardStore.Load();
        RefreshSavedBoards();
        controller = new(device, repository);
        if (boardStore.LoadError.Length > 0) ReportError(boardStore.LoadError);
        var saved = boardStore.Boards.FirstOrDefault(b => b.Hostname == boardStore.SelectedHostname) ?? boardStore.Boards.FirstOrDefault();
        if (saved is not null) { SelectedBoard = saved; device.IsWifi = true; autoSuppressed = false; }

        worker = runWorker ? Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            try { while (await timer.WaitForNextTickAsync(cancellation.Token)) controller.Tick(Environment.TickCount64); }
            catch (OperationCanceledException) { }
        }) : Task.CompletedTask;
        Refresh(); RefreshArchive(); ScanPorts();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public string TestName { get => testName; set { testName = value; Changed(); } }
    public decimal Interval { get => interval; set { interval = value; Changed(); } }
    public decimal Limit { get => limit; set { if (!CanConfigure) return; limit = value; Changed(string.Empty); } }
    public bool LimitEnabled { get => limitEnabled; set { if (!CanConfigure) return; limitEnabled = value; Changed(string.Empty); } }
    public bool ThrustGuardEnabled { get => thrustGuardEnabled; set { if (!CanConfigure) return; thrustGuardEnabled = value; Changed(); } }
    public decimal ThrustJumpGrams { get => thrustJumpGrams; set { if (!CanConfigure) return; thrustJumpGrams = value; Changed(); } }
    public decimal ThrustJumpPercent { get => thrustJumpPercent; set { if (!CanConfigure) return; thrustJumpPercent = value; Changed(); } }
    public bool IsSimulation
    {
        get => device.IsSimulation;
        set { if (!CanChooseDevice || value == device.IsSimulation) return; Disconnect(); device.IsSimulation = value; Changed(string.Empty); }
    }
    public bool IsWifi
    {
        get => device.IsWifi;
        set { if (!CanChooseDevice || device.IsWifi == value) return; discoveryEpoch++; device.IsSimulation = false; device.IsWifi = value; Changed(string.Empty); }
    }
    bool showHomePassword;
    public bool ShowHomePassword { get => showHomePassword; set { showHomePassword = value; Changed(); Changed(nameof(HomePasswordChar)); Changed(nameof(PasswordVisibilityText)); } }
    public char HomePasswordChar => ShowHomePassword ? '\0' : '●';
    public string PasswordVisibilityText => ShowHomePassword ? "Приховати" : "Показати";
    public string HomeNetworkAddress => device.Hardware.HomeNetworkAddress;
    public bool HasHomeNetworkAddress => HomeNetworkAddress.Length > 0;
    string homeSsid = "", homePassword = "";
    public string HomeSsid { get => homeSsid; set { homeSsid = value; Changed(); } }
    public string HomePassword { get => homePassword; set { homePassword = value; Changed(); } }
    public bool CanConfigureNetwork => !IsSimulation && !IsWifi && snapshot.State == RunState.Ready && device.Serial.SupportsHomeWifi;
    public string HomeNetworkStatus => device.Hardware.SupportsHomeWifi ? device.Hardware.HomeNetworkStatus : "Для першого налаштування роутера підключіть ESP32 до ноутбука через USB.";
    public bool ConfigureHomeWifi()
    {
        try
        {
            controller.ConfigureWifi(HomeSsid, HomePassword);
            HomePassword = ""; ShowHomePassword = false; notice = "";
            Changed(string.Empty); return true;
        }
        catch (Exception ex) { ReportError(ex.Message); Changed(string.Empty); return false; }
    }
    public bool UsbSelected { get => !IsWifi; set { if (value) IsWifi = false; } }
    public bool IsNotWifi => !IsWifi;
    public bool IsUsbMode => !IsWifi && !IsSimulation;
    public string WifiHost { get => device.Wifi.PortName; set { if (CanChooseDevice) device.Wifi.PortName = value.Trim(); } }
    public bool CanOpenBoardWeb => !IsSimulation && device.Hardware.SupportsBoardWeb && snapshot.State == RunState.Ready;
    public string BoardWebAddress => "http://" + (SavedBoardStore.ValidHost(device.Hardware.NetworkHostname) ? device.Hardware.NetworkHostname : HasHomeNetworkAddress ? HomeNetworkAddress : IsWifi ? WifiHost : "192.168.4.1") + "/";
    public bool HasFirmwareInfo => !string.IsNullOrEmpty(FirmwareInfo);
    public bool HasSensorStatus => !string.IsNullOrEmpty(SensorStatus);
    public string FirmwareInfo => !IsSimulation && device.Hardware.IsS3 ? device.Hardware.Name : "";
    public bool HasWifiPassword => WifiPassword.Length > 0;
    public string WifiPassword => IsWifi && snapshot.State == RunState.Ready
        ? LocalBoardSecret.Unprotect(boardStore.Boards.Find(b => b.Hostname == device.Wifi.PortName)?.ProtectedKey ?? "")
        : rememberedKey.Length > 0 ? rememberedKey : device.Serial.WifiKey;
    public string ConnectionButtonText => !connectionTask.IsCompleted ? "Перевірка…" : "Підключити";
    public string WifiDetails => device.Serial.WifiKey.Length > 0
        ? $"Мережа плати: {device.Serial.WifiNetwork} · адреса 192.168.4.1\nПароль Wi-Fi: {device.Serial.WifiKey}\nЦе пароль мережі плати, не домашнього Wi-Fi. Він створений один раз і зберігається на ESP32."
        : "Пароль ще не прочитано. Оберіть USB-режим і підключіть ESP32 кабелем до ноутбука — тут з’являться назва мережі та її пароль.";
    public string? SelectedPort { get => selectedPort; set { if (!CanChooseDevice) return; selectedPort = value; device.Serial.PortName = value ?? ""; Changed(); } }
    public bool CanChooseDevice => snapshot.State == RunState.Disconnected && connectionTask.IsCompleted;
    public bool CanConnect => !IsRunning && snapshot.State != RunState.Connecting && connectionTask.IsCompleted;
    public string SourceLabel => IsSimulation ? "SIM · СИНТЕТИЧНІ ДАНІ" : IsWifi ? "Wi-Fi · ESP32-S3" : "USB · РЕАЛЬНИЙ ПРИСТРІЙ";
    public string ConnectionWarning => IsSimulation ? "Симулятор створює умовні дані для перевірки програми." : device.Hardware.SupportsBoardWeb ? "S3-Connect 0.3: оновлення та короткий тест GPIO14 (до 10%) — у вікні «Керування GPIO14 та оновлення». Датчики ще не підключені." : device.Hardware.IsS3 ? "S3-Connect: режим перевірки зв’язку. Датчики та мотор ще не налаштовані; початок тесту заблокований." : snapshot.State == RunState.Disconnected ? "Готовність плати буде перевірено під час підключення." : device.Serial.HasWatchdog
        ? $"PT:{device.Serial.ProtocolVersion} · ліміт плати 30% · heartbeat 1 с · Stop з підтвердженням команди. Струм за номінальною формулою ACS712-20A, ще не калібрований. Відсічення при оцінці 8 А не замінює електричний захист."
        : "PT:1.0: при втраті USB мотор може продовжити працювати. Stop не має підтвердження від плати. Потрібне незалежне відключення живлення ESC.";
    public decimal Duration { get => duration; set { duration = value; Changed(); } }
    public double SliderMaximum => LimitEnabled ? Math.Clamp((double)Limit, 1, 100) : 100;
    public double SliderTickFrequency => SliderMaximum / 10;
    public bool IsRunning => snapshot.State == RunState.Running;
    public bool CanStart => snapshot.State == RunState.Ready && device.CanRunTests;
    public bool CanConfigure => !IsRunning && snapshot.State != RunState.Connecting && connectionTask.IsCompleted;
    public string Status => snapshot.State switch { RunState.Connecting => $"Перевірка плати · {(IsWifi ? WifiHost : SelectedPort)}", RunState.Ready => IsSimulation ? "Симулятор готовий" : $"Підключено · {(IsWifi ? "Wi-Fi" : SelectedPort)}", RunState.Running => "Запис триває", RunState.Faulted => "Збій зв’язку / тесту", _ => "Відключено · даних немає" };
    public string DeviceStatus => IsSimulation ? "Синтетичні дані" : IsWifi ? "ESP32-S3 · Wi-Fi · " + WifiHost :
        portDetails.FirstOrDefault(p => p.PortName == SelectedPort) is { IsArduino: true } board
            ? $"USB: {board.Description}. " + (snapshot.State is RunState.Ready or RunState.Running ? "PROpTEST підтверджено." : "Готовність прошивки ще не підтверджена.")
            : "Підключіть ESP32-S3 або Arduino через USB — програма знайде плату автоматично. Інший порт можна обрати у вкладці «Підключення».";
    public string Message => string.IsNullOrEmpty(notice) ? snapshot.Message : notice;
    public string Ports => ports;
    public string DataPath => repository.DatabasePath;
    public double Throttle
    {
        get => snapshot.RequestedThrottle;
        set { Execute(() => controller.SetThrottle(Math.Clamp(Math.Round(value, 1), 0, SliderMaximum))); }
    }
    public decimal ThrottleEntry { get => (decimal)Throttle; set => Throttle = (double)value; }
    public bool CanSetStartThrottle => IsRunning && SliderMaximum >= 6;
    public string ThrottleText => $"{Throttle:0.#}%";
    bool HasLiveData => snapshot.State is RunState.Running or RunState.Ready && snapshot.Samples.Count > 0;
    public string AppliedText => !HasLiveData ? "0%" : $"{snapshot.Samples[^1].ThrottlePercent:0.0}%";
    public string ThrustText => !HasLiveData ? "0 г" : snapshot.Samples[^1].ThrustGrams is { } thrust ? $"{thrust:0.0} г" : "—";
    public string CurrentText => !HasLiveData ? "0 А" : snapshot.Samples[^1].CurrentAmps is { } current ? $"{current:0.00} А" : "—";
    public string SensorStatus
    {
        get
        {
            if (!IsSimulation && device.Hardware.IsS3) return "Дані датчиків відсутні. Запис експерименту поки недоступний.";
            if (!HasLiveData || IsSimulation || !device.Serial.HasWatchdog) return "";
            var s = snapshot.Samples[^1];
            var hx = s.ThrustRaw is { } raw ? $"HX711: {raw:0} ADC; грами за відновленим старим коефіцієнтом, без нової перевірки еталоном." : "HX711: даних немає.";
            return hx + (s.CurrentAmps is null ? " Струм: сигнал A3 відсутній/за діапазоном; запуск заблоковано." : " Струм: номінальна оцінка.")
                + (s.VibrationX is null ? " Акселерометр: даних немає." : " XYZ містять гравітацію.")
                + $" Мікрофон: {s.SoundAdc:0} ADC{(device.Serial.UsesSoundEnvelope ? " p-p за 50 мс" : " миттєвий")}, не dB SPL.";
        }
    }
    public bool HasSensorSummary => !string.IsNullOrEmpty(SensorSummary);
    public string SensorSummary => !HasLiveData || IsSimulation || !device.Serial.HasWatchdog ? ""
        : snapshot.Samples[^1].CurrentAmps is null ? "Немає сигналу струму. Деталі — у «Підключенні»."
        : "Дані надходять. Калібрування потребує перевірки.";
    public string SoundText => !HasLiveData ? "0 ADC" : snapshot.Samples[^1].SoundDb is { } sound ? $"{sound:0.0} dB*"
        : snapshot.Samples[^1].SoundAdc is { } adc ? $"{adc:0} ADC" : "—";
    public string VibrationText
    {
        get
        {
            if (!HasLiveData) return "0 g";
            var s = snapshot.Samples[^1];
            return s.VibrationX is { } x && s.VibrationY is { } y && s.VibrationZ is { } z
                ? $"{Math.Sqrt(x * x + y * y + z * z):0.00} g" : "—";
        }
    }
    public string CountText => $"{snapshot.Samples.Count:N0} вимірів · {(snapshot.Samples.LastOrDefault()?.ElapsedMs ?? 0) / 1000.0:0.0} с";
    public IReadOnlyList<Measurement> Samples => snapshot.Samples;
    public IReadOnlyList<RunInfo> Archive => archive;
    public RunInfo? SelectedRun
    {
        get => selectedRun;
        set
        {
            selectedRun = value;
            SelectedData = null;
            Execute(() => SelectedData = value is null ? null : repository.Load(value.Id));
            Changed(); Changed(nameof(ArchiveSummary)); Changed(nameof(HasSelection));
        }
    }
    public bool HasSelection => SelectedData is not null;
    public string ArchiveSummary => SelectedData is null ? "Оберіть запис зліва." :
        $"{SelectedData.Info.Settings.Name}\n{SelectedData.Info.StartedUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss} · {SelectedData.Samples.Count} вимірів\n{OutcomeLabel(SelectedData.Info.Outcome)} · {SelectedData.Info.Source}";
    static string OutcomeLabel(string value) => value switch
    { "ThrustAnomaly" => "Зупинено контролем тяги", "Running" => "Триває", "UserStopped" => "Зупинено користувачем", "TimeLimit" => "Ліміт часу", "TelemetryLost" => "Втрачено телеметрію", "Interrupted" => "Перервано закриттям процесу", "ApplicationClosed" => "Закрито програму", _ => value };
    public void Connect() => Execute(controller.Connect);
    public async Task ConnectAsync()
    {
        if (!CanConnect) return;
        if (IsWifi && snapshot.State == RunState.Ready && (selectedBoard is null || device.Wifi.PortName == selectedBoard.Hostname))
        {
            notice = "Wi-Fi підключено. Програма підтримує зв’язок із платою у фоні.";
            Changed(nameof(Message));
            return;
        }
        autoSuppressed = false; discoveryEpoch++;
        if (IsWifi && snapshot.State == RunState.Ready) { controller.Disconnect(); ReadSnapshot(); }
        if (IsWifi && selectedBoard is not null) device.Wifi.PortName = selectedBoard.Hostname;
        autoTracker.Suppress(SelectedPort);
        connectionTask = Task.Run(controller.Connect);
        Changed(string.Empty);
        try { await connectionTask; notice = ""; }
        catch (Exception ex) { ReportError(IsWifi ? "Wi-Fi: " + ex.Message : ex is InvalidOperationException ? ex.Message : "Не вдалося встановити зв’язок із платою.\n" + ex.Message); }
        if (!disposed) Refresh();
    }
    public void Start() => Execute(() => controller.Start(new(TestName.Trim(), (int)Interval, LimitEnabled ? (int)Limit : 100, (int)Duration)
    { ThrustGuardEnabled = ThrustGuardEnabled, ThrustJumpGrams = (double)ThrustJumpGrams, ThrustJumpPercent = (double)ThrustJumpPercent }, Environment.TickCount64));
    public void Stop() { Execute(controller.Stop); RefreshArchive(); }
    public void Disconnect() { autoSuppressed = true; discoveryEpoch++; autoTracker.Suppress(SelectedPort); Execute(controller.Disconnect); RefreshArchive(); }
    public void BreakLink() { if (!IsSimulation) return; device.Simulator.LinkLost = true; notice = "Симульовано обрив. Очікуємо виявлення втрати телеметрії…"; Changed(nameof(Message)); }
    public void ScanPorts() => Execute(() =>
    {
        ApplyPorts(listPorts());
        Changed(string.Empty);
    });
    void ApplyPorts(IReadOnlyList<SerialPortInfo> found)
    {
        portDetails = found; autoTracker.Observe(found);
        if (SelectedPort is null || !PortNames.Contains(SelectedPort)) SelectedPort = found.FirstOrDefault(p => p.IsArduino)?.PortName ?? PortNames.FirstOrDefault();
        ports = found.Count == 0 ? "COM-портів немає." : string.Join(" · ", found.Select(p => p.Description));
    }
    public async Task CheckPortsAsync()
    {
        if (IsWifi) { await CheckSavedNetworkAsync(); return; }
        if (!autoConnect || disposed || checkingPorts || Environment.TickCount64 - lastPortCheck < 1000) return;
        checkingPorts = true; lastPortCheck = Environment.TickCount64;
        try
        {
            if (snapshot.State == RunState.Faulted && device.Hardware.IsS3)
            {
                Execute(controller.Disconnect);
                autoTracker.Retry(SelectedPort);
            }
            var found = await Task.Run(listPorts);
            if (disposed) return;
            if (!IsSimulation && !IsWifi && snapshot.State is RunState.Ready or RunState.Running && !found.Any(p => p.PortName == SelectedPort))
            {
                var lost = SelectedPort;
                Execute(controller.Disconnect); RefreshArchive();
                ReportError($"Втрачено телеметрію. USB-пристрій {lost} від’єднано від Windows.");
            }
            ApplyPorts(found); Changed(string.Empty);
            if (!IsSimulation && !IsWifi && CanChooseDevice && autoTracker.Next(found) is { } candidate)
            {
                SelectedPort = candidate;
                await ConnectAsync();
            }
        }
        catch (Exception ex) { if (!disposed) { notice = "Не вдалося оновити список USB: " + ex.Message; Changed(nameof(Message)); } }
        finally { checkingPorts = false; }
    }
    void RememberUsbBoard()
    {
        if (IsWifi || IsSimulation || snapshot.State != RunState.Ready || !device.Serial.SupportsBoardWeb || device.Serial.WifiKey.Length == 0 || !SavedBoardStore.ValidHost(device.Serial.NetworkHostname)) return;
        string signature = device.Serial.NetworkHostname + "|" + device.Serial.HomeNetworkAddress + "|" + device.Serial.WifiKey;
        if (signature == rememberedUsb) return;
        rememberedUsb = signature;
        try {
            selectedBoard = boardStore.Remember(device.Serial.NetworkHostname, device.Serial.HomeNetworkAddress, device.Serial.WifiKey);
            boardName = selectedBoard.Name; rememberedKey = device.Serial.WifiKey; RefreshSavedBoards();
            notice = "Плату запам’ятовано. Наступні підключення — без введення пароля.";
        } catch (Exception ex) { ReportError(ex.Message); }
    }
    async Task CheckSavedNetworkAsync()
    {
        if (!autoConnect || disposed || networkSearch || autoSuppressed || IsSimulation || !IsWifi || selectedBoard is not { AutoConnect: true } board || rememberedKey.Length == 0 || Environment.TickCount64 < nextNetworkSearch) return;
        if (snapshot.State == RunState.Ready || snapshot.State == RunState.Running || !connectionTask.IsCompleted) return;
        networkSearch = true; nextNetworkSearch = Environment.TickCount64 + 5000;
        int version = discoveryEpoch; string key = rememberedKey;
        try {
            // Read-only discovery: failed probes never change UI state or open a motor session.
            await probeNetwork(board.Hostname, key, cancellation.Token);
            if (disposed || version != discoveryEpoch || autoSuppressed || !IsWifi || selectedBoard?.Hostname != board.Hostname) return;
            if (snapshot.State == RunState.Faulted) { controller.Disconnect(); ReadSnapshot(); }
            await ConnectAsync();
        } catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or System.Net.Sockets.SocketException) { }
        finally { networkSearch = false; }
    }
    public void Refresh()
    {
        var before = snapshot.State;
        ReadSnapshot();
        if (!IsWifi && !IsSimulation && device.Serial.HomeNetworkAddress.Length > 0) device.Wifi.PortName = device.Serial.HomeNetworkAddress;
        if (before == RunState.Running && !IsRunning) { notice = ""; RefreshArchive(); }
        RememberUsbBoard();
        Changed(string.Empty);
    }
    public void RefreshArchive() => Execute(() => { archive = repository.List(); Changed(nameof(Archive)); });
    void Execute(Action action)
    {
        try { action(); notice = ""; }
        catch (Exception ex) { ReportError(ex.Message); }
        ReadSnapshot(); Changed(string.Empty);
    }
    public async Task ExportSelectedAsync(Func<string, Task<(Stream Stream, string Name)?>> selectFile)
    {
        // Keep the selected run stable while the native save dialog is open.
        var data = SelectedData;
        if (data is null) return;
        try
        {
            var invalid = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
            var name = new string(data.Info.Settings.Name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            if (string.IsNullOrEmpty(name)) name = "Експеримент";
            var suggested = $"PROpTEST_{name}_{data.Info.StartedLocal:yyyy-MM-dd_HH-mm-ss}.csv";
            var file = await selectFile(suggested);
            if (file is null) { notice = "Експорт скасовано."; return; }
            await using var stream = file.Value.Stream;
            if (stream.CanSeek) { stream.Position = 0; stream.SetLength(0); }
            var sb = new StringBuilder("run_id,source,sequence,elapsed_ms,throttle_pct,thrust_g,current_a,acc_x_g,acc_y_g,acc_z_g,sound_db_uncalibrated,thrust_raw_adc,sound_raw_adc\n");
            var source = "\"" + data.Info.Source.Replace("\"", "\"\"") + "\"";
            foreach (var s in data.Samples)
                sb.AppendLine(FormattableString.Invariant($"{s.RunId},{source},{s.Sequence},{s.ElapsedMs},{s.ThrottlePercent:R},{s.ThrustGrams:R},{s.CurrentAmps:R},{s.VibrationX:R},{s.VibrationY:R},{s.VibrationZ:R},{s.SoundDb:R},{s.ThrustRaw:R},{s.SoundAdc:R}"));
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true))
                await writer.WriteAsync(sb.ToString());
            notice = "CSV збережено: " + file.Value.Name;
        }
        catch (Exception ex) { ReportError("Не вдалося зберегти CSV: " + ex.Message); }
        finally { Changed(nameof(Message)); }
    }
    public void Dispose()
    {
        disposed = true;
        cancellation.Cancel(); worker.GetAwaiter().GetResult();
        try { connectionTask.GetAwaiter().GetResult(); } catch { }
        controller.Dispose(); cancellation.Dispose();
    }
}






