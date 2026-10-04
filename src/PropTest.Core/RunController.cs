namespace PropTest.Core;

public sealed class RunController(ITestDevice device, IRunRepository repository) : IDisposable
{
    readonly object gate = new();
    readonly List<Measurement> samples = new();
    readonly TelemetryFrameDecoder decoder = new();
    RunState state;
    string message = "Пристрій не підключено. Нулі на панелі означають відсутність даних.";
    RunInfo? run;
    long lastReceived, started, lastSequence;
    double requested;
    ThrustGuard thrustGuard = new();
    public SessionSnapshot Snapshot() { lock (gate) return new(state, message, run?.Id, requested, samples.ToArray()); }
    public void Connect()
    {
        lock (gate)
        {
            if (state is RunState.Running or RunState.Connecting) throw new InvalidOperationException("Спочатку зупиніть тест або дочекайтеся підключення.");
            state = RunState.Connecting; message = "Перевірка пристрою…";
        }
        try { device.Connect(); lock (gate) { samples.Clear(); state = RunState.Ready; message = "Підключено: " + device.Name; } }
        catch { lock (gate) { state = RunState.Disconnected; message = "Не вдалося підключитися."; } throw; }
    }
    public void Start(RunSettings settings, long nowMs)
    {
        lock (gate)
        {
            if (state != RunState.Ready) throw new InvalidOperationException("Спочатку підключіть пристрій.");
            if (!device.CanRunTests) throw new InvalidOperationException("Ця прошивка не передає виміри датчиків. Запис експерименту недоступний; короткий тест GPIO14 у версії 0.3 відкривається окремою кнопкою на вкладці «Підключення».");
            settings.Validate();
            var next = new RunInfo(Guid.NewGuid(), DateTimeOffset.UtcNow, settings, device.Name, "Running", 0);
            repository.Begin(next);
            run = next; samples.Clear(); decoder.Reset(); thrustGuard = new(); requested = 0; lastSequence = 0; started = lastReceived = nowMs;
            state = RunState.Running; message = "Запис триває. Газ регулюється повзунком.";
            try { device.Begin(next.Id, settings, nowMs); }
            catch (Exception ex) { End("StartFailed", true, $"Не вдалося почати тест: {ex.Message}"); }
        }
    }
    public void SetThrottle(double value)
    {
        lock (gate)
        {
            if (state != RunState.Running || run is null) return;
            if (!double.IsFinite(value) || value < 0 || value > run.Settings.ThrottleLimit) throw new ArgumentOutOfRangeException(nameof(value));
            try { device.SetThrottle(value); requested = value; }
            catch (Exception ex) { End("DeviceError", true, ex.Message); }
        }
    }
    public void ConfigureWifi(string ssid, string password)
    {
        lock (gate)
        {
            if (state != RunState.Ready) throw new InvalidOperationException("Спочатку підключіть ESP32 через USB.");
            device.ConfigureWifi(ssid, password);
        }
    }
    public void Tick(long nowMs)
    {
        lock (gate)
        {
            if (state == RunState.Ready)
            {
                try { device.CheckConnection(nowMs); }
                catch (Exception ex)
                {
                    try { device.Disconnect(); } catch { }
                    state = RunState.Faulted; message = "Втрачено зв’язок із платою. " + ex.Message;
                }
                return;
            }
            if (state != RunState.Running || run is null) return;
            try
            {
                // Poll before renewal: a late host cannot revive an expired device lease.
                var frames = device.Poll(nowMs).SelectMany(decoder.Push).ToArray();
                if (nowMs - lastReceived > Math.Max(2000, run.Settings.SampleIntervalMs * 3))
                { End("TelemetryLost", true, "Втрачено телеметрію. Тест перервано; потрібне повторне підключення."); return; }
                foreach (var frame in frames)
                {
                    if (!TelemetryProtocol.TryParse(frame, out var s) || s is null || s.RunId != run.Id || s.Sequence <= lastSequence) continue;
                    if ((samples.Count > 0 && s.ElapsedMs < samples[^1].ElapsedMs) || s.ElapsedMs > nowMs - started) continue;
                    if (s.ThrottlePercent > run.Settings.ThrottleLimit)
                    { End("LimitExceeded", true, "Пристрій повідомив перевищення ліміту газу."); return; }
                    repository.Append(s); samples.Add(s); lastSequence = s.Sequence; lastReceived = nowMs;
                    if (thrustGuard.Observe(s, requested, run.Settings))
                    { End("ThrustAnomaly", true, "Аварійна зупинка за тягою завершила тест: два послідовні виміри різко відрізняються від попередньої тяги при сталому газі. Перевірте стенд і пороги контролю перед повторним запуском."); return; }
                }
                if (nowMs - started >= run.Settings.DurationSeconds * 1000L)
                { End("TimeLimit", false, "Завершено за лімітом часу. Запис збережено."); return; }
                device.Heartbeat(nowMs);
            }
            catch (Exception ex) { End("Fault", true, $"Тест перервано: {ex.Message}"); }
        }
    }
    public void Stop()
    {
        lock (gate)
        {
            if (state == RunState.Connecting) return;
            var text = state == RunState.Running ? "Зупинено користувачем. Запис збережено." : "Команду Stop надіслано.";
            End("UserStopped", state == RunState.Faulted, text);
        }
    }
    public void Disconnect()
    {
        lock (gate)
        {
            if (state == RunState.Connecting) return;
            End("Disconnected", false, "Відключено. Даних немає.");
            try { device.Disconnect(); }
            finally { state = RunState.Disconnected; samples.Clear(); }
        }
    }
    void End(string outcome, bool fault, string text)
    {
        try { device.Stop(); }
        catch (Exception ex) { fault = true; text += $" Команда Stop не підтверджена: {ex.Message}"; }
        if (state == RunState.Running && run is not null)
        {
            try { repository.Finish(run.Id, outcome); }
            catch (Exception ex) { fault = true; text += $" Помилка збереження: {ex.Message}"; }
        }
        requested = 0; state = fault ? RunState.Faulted : state == RunState.Disconnected ? state : RunState.Ready; message = text;
    }
    public void Dispose() { lock (gate) { End("ApplicationClosed", false, "Закрито."); device.Dispose(); } }
}

