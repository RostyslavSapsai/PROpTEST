using System.Globalization;
using System.IO.Ports;
using PropTest.Core;

namespace PropTest.Infrastructure;

public interface ISerialConnection : IDisposable
{
    void Open();
    void WriteLine(string command);
    string ReadAvailable();
    void ClearInput();
}

sealed class SerialConnection(string portName) : ISerialConnection
{
    readonly SerialPort port = new(portName, 115200, Parity.None, 8, StopBits.One)
    { NewLine = "\n", DtrEnable = false, RtsEnable = false, WriteTimeout = 200, ReadTimeout = 200 };
    public void Open() => port.Open();
    public void WriteLine(string command) => port.WriteLine(command);
    public string ReadAvailable() => port.ReadExisting();
    public void ClearInput() => port.DiscardInBuffer();
    public void Dispose() => port.Dispose();
}

// Adapter for the inspected PT:1.0 firmware, not an assertion about the firmware flashed on a board.
public sealed class SerialTestDevice(Func<string, ISerialConnection>? factory = null, int probeTimeoutMs = 7000) : ITestDevice
{
    readonly Func<string, ISerialConnection> create = factory ?? (name => new SerialConnection(name));
    readonly TelemetryFrameDecoder decoder = new();
    ISerialConnection? connection;
    Guid runId;
    RunSettings? settings;
    long started, sequence, lastZeroRequest;
    int requested;
    long lastHeartbeat, deviceSequence;
    public bool HasWatchdog { get; private set; }
    public bool UsesSoundEnvelope { get; private set; }
    public bool SupportsFineThrottle { get; private set; }
    public bool IsS3 { get; private set; }
    public bool SupportsBoardWeb { get; private set; }
    public bool SupportsHomeWifi { get; private set; }
    public string HomeNetworkStatus { get; private set; } = "";
    public string HomeNetworkAddress { get; private set; } = "";
    public string NetworkHostname { get; private set; } = "";
    public bool CanRunTests => !IsS3;
    public string BoardInfo { get; private set; } = "";
    public string WifiNetwork { get; private set; } = "";
    public string WifiKey { get; private set; } = "";
    public bool NetworkOnly { get; init; }
    long lastPong, lastPing;
    public string ProtocolVersion => SupportsFineThrottle ? "2.2" : UsesSoundEnvelope ? "2.1" : HasWatchdog ? "2.0" : "1.0";
    public string PortName { get; set; } = "";
    public string Name => IsS3 ? $"PROpTEST v2 · S3-Connect {(SupportsBoardWeb ? "0.3.0" : SupportsHomeWifi ? "0.2.0" : "0.1.0")} · {PortName}" : $"COM / {PortName} / PT:{(HasWatchdog ? $"{ProtocolVersion} / cap30 / thrust-polarity=-1 / sound={(UsesSoundEnvelope ? "mean50ms-P2P" : "instant-ADC")} ADC / HX historical inverse 0.0014577025 / nominal-ACS20A-A3 / MMA8452Q-8g-or-MPU6050-2g" : "1.0 / uncalibrated")}";

    public void Connect()
    {
        Disconnect();
        SupportsBoardWeb = SupportsHomeWifi = IsS3 = HasWatchdog = UsesSoundEnvelope = SupportsFineThrottle = false;
        BoardInfo = WifiKey = WifiNetwork = HomeNetworkStatus = HomeNetworkAddress = NetworkHostname = "";
        if (string.IsNullOrWhiteSpace(PortName)) throw new InvalidOperationException("Оберіть COM-порт у вкладці «Підключення».");
        var candidate = create(PortName);
        try
        {
            candidate.Open(); candidate.ClearInput(); decoder.Reset();
            var deadline = Environment.TickCount64 + probeTimeoutMs;
            long lastProbe = 0;
            while (Environment.TickCount64 < deadline)
            {
                if (Environment.TickCount64 - lastProbe >= 400) { candidate.WriteLine("P"); lastProbe = Environment.TickCount64; }
                foreach (var line in decoder.Push(candidate.ReadAvailable()))
                {
                    var identity = line.Trim();
                    if (identity is "PT:S3:0.1" or "PT:S3:0.2" or "PT:S3:0.3")
                    {
                        connection = candidate; IsS3 = true; SupportsBoardWeb = identity == "PT:S3:0.3"; SupportsHomeWifi = identity != "PT:S3:0.1"; decoder.Reset();
                        lastPong = Environment.TickCount64; lastPing = 0;
                        candidate.WriteLine("I");
                        return;
                    }
                    if (NetworkOnly) continue;
                    if (identity.Equals("PT:1.0", StringComparison.OrdinalIgnoreCase) || identity is "PT:2.0" or "PT:2.1" or "PT:2.2")
                    {
                        HasWatchdog = identity is "PT:2.0" or "PT:2.1" or "PT:2.2"; UsesSoundEnvelope = identity is "PT:2.1" or "PT:2.2"; SupportsFineThrottle = identity == "PT:2.2"; connection = candidate; decoder.Reset();
                        if (HasWatchdog) SendAcknowledged("G", "ACK:G:0"); else candidate.WriteLine("G");
                        return;
                    }
                }
                Thread.Sleep(20);
            }
            if (NetworkOnly) throw new IOException("Wi-Fi: за цією адресою не відповіла прошивка S3-Connect. Перевірте мережу PROpTEST-v2 та адресу плати.");
            throw new InvalidOperationException($"Порт не відповів PROpTEST (PT:1.0 / PT:2.0 / PT:2.1 / PT:2.2 / PT:S3:0.1). {PortName} відкрито на 115200 бод, але прошивка не підтвердила підтримку PROpTEST. Перевірте прошивку та її запуск; назва COM-порту не підтверджує готовність плати.");
        }
        catch { connection = null; candidate.Dispose(); throw; }
    }
    public void Begin(Guid id, RunSettings configuration, long nowMs)
    {
        if (IsS3) throw new InvalidOperationException("S3-Connect не передає виміри датчиків; запис експерименту недоступний.");
        if (connection is null) throw new InvalidOperationException("COM-порт не підключено.");
        configuration.Validate();
        if (HasWatchdog && configuration.ThrottleLimit > 30) throw new InvalidOperationException("Прошивка цієї плати обмежує газ до 30%. Встановіть ліміт не вище 30%.");
        Stop(); connection.ClearInput(); decoder.Reset();
        runId = id; settings = configuration; started = lastZeroRequest = nowMs; sequence = 0; requested = 0;
        connection.WriteLine($"M:{configuration.ThrottleLimit}");
        connection.WriteLine($"F:{configuration.SampleIntervalMs}");
        if (HasWatchdog) { SendAcknowledged("B", "ACK:B:0"); deviceSequence = 0; lastHeartbeat = nowMs; }
        connection.WriteLine("W:0");
    }
    public void SetThrottle(double percent)
    {
        if (connection is null || settings is null) throw new InvalidOperationException("Тест не запущено.");
        if (!double.IsFinite(percent) || percent < 0 || percent > settings.ThrottleLimit) throw new ArgumentOutOfRangeException(nameof(percent));
        if (!SupportsFineThrottle && Math.Abs(percent - Math.Round(percent)) > .00001)
            throw new InvalidOperationException("Для газу з кроком 0,1% потрібна прошивка PT:2.2. Поточна прошивка підтримує лише цілі відсотки.");
        requested = (int)Math.Round(percent);
        if (SupportsFineThrottle) { connection.WriteLine($"U:{(int)Math.Round(percent * 10)}"); return; }
        // Firmware applies floor(W * ceiling / 100). Keep M as a device-side ceiling.
        var command = HasWatchdog ? requested : (int)Math.Ceiling(requested * 100.0 / settings.ThrottleLimit);
        connection.WriteLine($"W:{command}");
    }
    public IReadOnlyList<string> Poll(long nowMs)
    {
        if (settings is null || connection is null) return Array.Empty<string>();
        var result = new List<string>();
        foreach (var line in decoder.Push(connection.ReadAvailable()))
        {
            if (HasWatchdog && (line.StartsWith("FAULT:") || line.StartsWith("ERR:") || line.StartsWith("PT:2.")))
                throw new InvalidOperationException(DescribeDeviceFault(line));
            Measurement? sample;
            if (HasWatchdog ? TryParseCurrent(line, runId, nowMs - started, out sample) : TryParseLegacy(line, runId, sequence + 1, nowMs - started, out sample))
            {
                if (HasWatchdog && sample!.Sequence <= deviceSequence) continue;
                if (HasWatchdog) deviceSequence = sample!.Sequence;
                sequence++; result.Add(TelemetryProtocol.Encode(sample!) + "\n");
            }
        }
        // Legacy firmware only streams continuously at nonzero throttle.
        if (!HasWatchdog && requested == 0 && nowMs - lastZeroRequest >= settings.SampleIntervalMs)
        { connection.WriteLine("W:0"); lastZeroRequest = nowMs; }
        return result;
    }
    public static bool TryParseLegacy(string line, Guid id, long sequence, long elapsed, out Measurement? sample)
    {
        sample = null;
        var parts = line.Split('|');
        if (parts.Length != 7 || line.Length > TelemetryProtocol.MaxFrameLength) return false;
        var v = new double[7];
        for (int i = 0; i < v.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || !double.IsFinite(v[i])) return false;
        if (v[6] is < 0 or > 100) return false;
        sample = new(id, sequence, elapsed, v[6], v[0], v[4])
        { VibrationX = v[1], VibrationY = v[2], VibrationZ = v[3], SoundDb = v[5] };
        return true;
    }
    public static bool TryParseCurrent(string line, Guid id, long elapsed, out Measurement? sample)
    {
        sample = null;
        var p = line.Split('|');
        if (line.Length > TelemetryProtocol.MaxFrameLength || p.Length is not (11 or 12) || p[0] != "D" || p[10] != "1"
            || !long.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seq) || seq < 1
            || !uint.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out _)) return false;
        var v = new double?[7];
        for (int i = 0; i < 7; i++)
        {
            if (p[i + 3].Length == 0 && i > 0) continue;
            if (!double.TryParse(p[i + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) return false;
            v[i] = value;
        }
        if (v[0] is < 0 or > 30 || v[6] is < 0 or > 1023) return false;
        double? thrust = null;
        if (p.Length == 12 && p[11].Length > 0)
        {
            if (!double.TryParse(p[11], NumberStyles.Float, CultureInfo.InvariantCulture, out var grams) || !double.IsFinite(grams) || v[1] is null) return false;
            // User confirmed this stand's propeller/load direction: positive thrust is negative sensor force.
            thrust = -grams;
        }
        sample = new(id, seq, elapsed, v[0]!.Value, thrust, v[2])
        { ThrustRaw = v[1], VibrationX = v[3], VibrationY = v[4], VibrationZ = v[5], SoundAdc = v[6] };
        return true;
    }
    static string DescribeDeviceFault(string line) => line switch
    {
        "FAULT:CURRENT" => "Arduino зупинила газ: сигнал датчика струму відсутній або оцінка струму досягла 8 А. Перевірте живлення, GND і сигнальний провід ACS712 до A3. Значення струму ще не каліброване.",
        "FAULT:HEARTBEAT" => "Arduino зупинила газ: протягом 1 секунди не отримувала heartbeat від програми. Підключіться повторно перед новим тестом.",
        "ERR:STARTUP" => "Arduino ще запускається. Зачекайте 3 секунди після подачі живлення та підключіться повторно.",
        _ => "Arduino відхилила команду або перезапустилася: " + line
    };
    void SendAcknowledged(string command, string expected)
    {
        connection!.WriteLine(command);
        var deadline = Environment.TickCount64 + 600;
        while (Environment.TickCount64 < deadline)
        {
            foreach (var line in decoder.Push(connection.ReadAvailable()))
            {
                if (line == expected) return;
                if (line.StartsWith("ERR:")) throw new InvalidOperationException(DescribeDeviceFault(line));
            }
            Thread.Sleep(10);
        }
        throw new IOException($"Arduino не підтвердила команду {command}. Очікувалось {expected}.");
    }
    public void Heartbeat(long nowMs)
    {
        if (HasWatchdog && settings is not null && nowMs - lastHeartbeat >= 200)
        { connection!.WriteLine("H"); lastHeartbeat = nowMs; }
    }
    public void ConfigureWifi(string ssid, string password)
    {
        if (connection is null || !SupportsHomeWifi || NetworkOnly) throw new InvalidOperationException("Потрібна прошивка S3-Connect 0.2.0 та USB-підключення.");
        var name = System.Text.Encoding.UTF8.GetBytes(ssid);
        var key = System.Text.Encoding.UTF8.GetBytes(password);
        if (name.Length is < 1 or > 32 || ssid.Contains('\0') || key.Length is < 8 or > 63 || password.Any(c => c < 32 || c > 126))
            throw new ArgumentException("Назва мережі: 1–32 байти; пароль WPA2: 8–63 латинські символи, цифри або знаки.");
        HomeNetworkAddress = "";
        HomeNetworkStatus = "Надсилаємо налаштування на плату…";
        connection.WriteLine($"NET:{Convert.ToHexString(name)}:{Convert.ToHexString(key)}");
    }
    public void CheckConnection(long nowMs)
    {
        if (!IsS3 || connection is null) return;
        foreach (var line in decoder.Push(connection.ReadAvailable()))
        {
            if (line == "PONG") lastPong = nowMs;
            else if (line.StartsWith("INFO|"))
            {
                BoardInfo = line[5..].Replace("|", " · ");
                var fields = line.Split('|');
                if (fields.Length == 8) WifiNetwork = fields[5];
            }
            else if (line.StartsWith("NETWORK|"))
            {
                var fields = line.Split('|');
                if (fields.Length == 4)
                {
                    NetworkHostname = fields[3].ToLowerInvariant();
                    HomeNetworkAddress = fields[1] == "CONNECTED" ? fields[2] : "";
                    HomeNetworkStatus = fields[1] switch
                    {
                        "CONNECTED" => $"Плата в мережі роутера. Адреса: {fields[2]} · {fields[3]}",
                        "CONNECTING" => "Плата підключається до роутера…",
                        "NOT_CONFIGURED" => "Мережу роутера ще не налаштовано.",
                        _ => "Не підключено. Перевірте назву, пароль та наявність мережі 2,4 ГГц."
                    };
                }
            }
            else if (line.StartsWith("ERR:NET:")) HomeNetworkStatus = "Плата відхилила налаштування мережі. Перевірте поля та USB-підключення.";
            else if (line.StartsWith("WIFIKEY|")) WifiKey = line[8..];
        }
        if (nowMs - lastPong > 3500) throw new IOException("ESP32-S3 не відповідає понад 3,5 секунди.");
        if (nowMs - lastPing >= 750) { connection.WriteLine("H"); lastPing = nowMs; }
    }
    public void Stop()
    {
        settings = null; requested = 0;
        if (connection is null) return;
        if (HasWatchdog || SupportsBoardWeb) SendAcknowledged("G", "ACK:G:0"); else connection.WriteLine("G");
    }
    public void Disconnect()
    {
        try { Stop(); }
        finally { connection?.Dispose(); connection = null; decoder.Reset(); }
    }
    public void Dispose()
    {
        try { Disconnect(); }
        catch (IOException) { }
        catch (InvalidOperationException) { }
        catch (TimeoutException) { }
        catch (System.Net.Sockets.SocketException) { }
    }
}

public sealed class SelectableDevice : ITestDevice
{
    public SimulatedDevice Simulator { get; } = new();
    public SerialTestDevice Serial { get; } = new();
    public SerialTestDevice Wifi { get; } = new(host => new TcpConnection(host), 2500) { PortName = "192.168.4.1", NetworkOnly = true };
    public bool IsWifi { get; set; }
    public SerialTestDevice Hardware => IsWifi ? Wifi : Serial;
    public bool IsSimulation { get; set; }
    ITestDevice Current => IsSimulation ? Simulator : Hardware;
    public bool CanRunTests => Current.CanRunTests;
    public void CheckConnection(long nowMs) => Current.CheckConnection(nowMs);
    public string Name => Current.Name;
    public void ConfigureWifi(string ssid, string password) => Current.ConfigureWifi(ssid, password);
    public void Connect() => Current.Connect();
    public void Begin(Guid runId, RunSettings settings, long nowMs) => Current.Begin(runId, settings, nowMs);
    public void SetThrottle(double percent) => Current.SetThrottle(percent);
    public void Heartbeat(long nowMs) => Current.Heartbeat(nowMs);
    public void Stop() => Current.Stop();
    public void Disconnect() => Current.Disconnect();
    public IReadOnlyList<string> Poll(long nowMs) => Current.Poll(nowMs);
    public void Dispose() { Simulator.Dispose(); Serial.Dispose(); Wifi.Dispose(); }
}






