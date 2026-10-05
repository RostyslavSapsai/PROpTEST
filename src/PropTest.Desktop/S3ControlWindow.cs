using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PropTest.Infrastructure;

namespace PropTest.Desktop;

public sealed class S3ControlWindow : Window
{
    readonly string address;
    readonly Func<string, string, S3ControlClient> createClient;
    readonly string accessKey;
    readonly Button connect = new() { Content = "Підключитися" };
    readonly Button arm = new() { Content = "Підготувати тест", IsEnabled = false };
    readonly Button apply = new() { Content = "Задати газ", IsEnabled = false };
    readonly Button stop = new() { Content = "■ STOP · усі мотори", Background = Brush.Parse("#B72521"), Foreground = Brushes.White };
    readonly Button update = new() { Content = "Обрати прошивку .ptfw", IsEnabled = false };
    readonly NumericUpDown gas = new() { Minimum = 0, Maximum = 10, Increment = .1m, ShowButtonSpinner = false, VerticalContentAlignment = VerticalAlignment.Center, FormatString = "0.0", Value = 0, Width = 140, IsEnabled = false };
    readonly ComboBox motorPicker=new() {ItemsSource=new[]{"Мотор 1","Мотор 2","Мотор 3","Мотор 4"},SelectedIndex=0,Name="ControlMotorPicker",MinWidth=160};
    readonly Slider slider=new() {Minimum=0,Maximum=10,Value=0,IsEnabled=false,Name="MotorThrottleSlider"};
    readonly NumericUpDown limit=new() {Minimum=0,Maximum=10,Value=10,Increment=1,Width=90,ShowButtonSpinner=false,FormatString="0",VerticalContentAlignment=VerticalAlignment.Center,Name="MotorThrottleLimit"};
    readonly TextBlock allReadings=new() {TextWrapping=TextWrapping.Wrap};
    readonly bool requireFour;
    bool syncing;
    readonly decimal[] requested=new decimal[4], limits=[10,10,10,10];
    readonly TextBlock reading = new() { Text = "Вихід ще не перевірений", FontSize = 24, FontWeight = FontWeight.SemiBold };
    readonly TextBlock message = new() { Text = "Підключення не запускає мотор.", TextWrapping = TextWrapping.Wrap };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly CancellationTokenSource lifetime = new();
    S3ControlClient? client;
    S3ControlState? state;
    string token = "";
    bool busy, polling, closed, stopping;
    int epoch;
    long lastPoll;

    public S3ControlWindow(string address, string key, Func<string, string, S3ControlClient>? createClient = null, int motor = 0, bool requireFour = false, Action<int>? showGraphs = null)
    {
        this.requireFour=requireFour;motorPicker.SelectedIndex=Math.Clamp(motor,0,3);
        this.address = address; this.createClient = createClient ?? ((host, secret) => new S3ControlClient(host, secret));
        Title = "PROpTEST · Керування моторами"; Width = 660; Height = 780; MinWidth = 500; MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#EEF2F6"); accessKey = key;
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = "Керування моторами", FontSize = 26, FontWeight = FontWeight.Bold });
        body.Children.Add(new SelectableTextBlock { Text = key.Length>0?$"Wi-Fi · {new Uri(address).Host} · до 10%":"Підключіть плату у вкладці «Підключення»" });
        body.Children.Add(new TextBlock { Text = "Короткий тест до 30 секунд. Без запису датчиків. Першу перевірку нового підключення проводьте без пропелера.", TextWrapping = TextWrapping.Wrap });
        var credentials = new StackPanel { Spacing = 8 };
        credentials.Children.Add(new TextBlock { Text = key.Length > 0 ? "Доступ до плати збережено. Пароль вводити не потрібно." : "Один раз підключіть плату через USB, щоб запам’ятати доступ.", TextWrapping = TextWrapping.Wrap });
        credentials.Children.Add(connect);
        connect.IsEnabled = key.Length > 0;
        if (key.Length > 0) Opened += async (_, _) => await ConnectAsync();
        body.Children.Add(new Expander {Header="Підключення та доступ",Content=credentials,HorizontalAlignment=HorizontalAlignment.Stretch});
        var motorRow=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12};motorRow.Children.Add(motorPicker);
        if(showGraphs is not null) {var graphs=new Button {Content="Графіки мотора"};graphs.Click+=(_,_)=>showGraphs(motorPicker.SelectedIndex);motorRow.Children.Add(graphs);}
        body.Children.Add(motorRow);body.Children.Add(allReadings);
        body.Children.Add(reading);
        body.Children.Add(new StackPanel {Orientation=Orientation.Horizontal,Spacing=10,Children={new TextBlock {Text="Ліміт цього мотора, %",VerticalAlignment=VerticalAlignment.Center},limit}});
        body.Children.Add(slider);
        motorPicker.SelectionChanged+=(_,_)=>{if(state is {} current)Render(current);SyncInputs();if(state?.Motors is not null)message.Text=$"Обрано мотор {motorPicker.SelectedIndex+1}. Перемикання не змінює газ інших моторів.";};
        slider.PropertyChanged+=(_,e)=>{if(e.Property==Slider.ValueProperty && !syncing){gas.Value=(decimal)Math.Round(slider.Value,1);}};
        gas.ValueChanged+=(_,_)=>{if(!syncing){requested[motorPicker.SelectedIndex]=gas.Value??0;syncing=true;slider.Value=(double)(gas.Value??0);syncing=false;}};
        limit.ValueChanged+=(_,_)=>{if(syncing)return;limits[motorPicker.SelectedIndex]=limit.Value??0;SyncInputs();};
        var stopMotor=new Button {Content="Зупинити цей мотор",Name="StopSelectedMotor"};
        stopMotor.Click+=async (_,_)=>{if(client is not null && token.Length>0)await RunAsync(()=>client.SendAsync("/gas",GasCommand(0),lifetime.Token),"Мотору задано нульовий газ.");requested[motorPicker.SelectedIndex]=0;SyncInputs();};
        ToolTip.SetTip(stopMotor,"Нульова команда лише вибраному мотору; інші зберігають свій газ");
        body.Children.Add(stopMotor);
        body.Children.Add(new TextBlock { Text = "Заданий газ, %" });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        controls.Children.Add(gas); controls.Children.Add(apply); body.Children.Add(controls);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        actions.Children.Add(arm); body.Children.Add(actions);
        body.Children.Add(new Border { Background = Brush.Parse("#E1E7EF"), Padding = new Thickness(14), CornerRadius = new CornerRadius(8), Child = message });
        var updates = new StackPanel { Spacing = 10 };
        updates.Children.Add(new TextBlock { Text = "Мотор має бути зупинений. Залиште плату ввімкненою до завершення оновлення.", TextWrapping = TextWrapping.Wrap });
        updates.Children.Add(update);
        body.Children.Add(new Expander { Header = "Оновлення прошивки через Wi-Fi", HorizontalAlignment = HorizontalAlignment.Stretch, Content = updates });
        var root=new Grid {RowDefinitions=new("*,Auto")};root.Children.Add(new ScrollViewer {Content=body});
        stop.HorizontalAlignment=HorizontalAlignment.Stretch;stop.HorizontalContentAlignment=HorizontalAlignment.Center;stop.Margin=new(24,8,24,16);
        ToolTip.SetTip(stop,"Зупинити всі чотири мотори");Grid.SetRow(stop,1);root.Children.Add(stop);Content=root;
        connect.Click += async (_, _) => await ConnectAsync();
        arm.Click += async (_, _) => await RunAsync(async () => {
            var s = await client!.SendAsync("/arm", new(), lifetime.Token); return s;
        }, "Готово: газ 0%. Введіть значення й натисніть «Задати газ».");
        apply.Click += async (_, _) => await RunAsync(() => client!.SendAsync("/gas", GasCommand((int)Math.Round((gas.Value ?? 0) * 10)), lifetime.Token), "Команду газу прийнято.");
        stop.Click += async (_, _) => await StopAsync();
        update.Click += async (_, _) => await UpdateAsync();
        timer.Tick += async (_, _) => await PollAsync(); timer.Start();
        Closed += async (_, _) => {
            closed = true; epoch++; token = ""; timer.Stop(); lifetime.Cancel();
            if (client is { } c) { try { await c.SendAsync("/stop", new()); } catch { } c.Dispose(); }
        };
    }
    Dictionary<string,string> GasCommand(int value) {
        var command=new Dictionary<string,string> {{"token",token},{"value",value.ToString(System.Globalization.CultureInfo.InvariantCulture)}};
        if(state?.Motors is not null)command["motor"]=(motorPicker.SelectedIndex+1).ToString();
        return command;
    }
    void SyncInputs() {
        syncing=true;int i=motorPicker.SelectedIndex;
        limit.Value=limits[i];gas.Maximum=limits[i];slider.Maximum=(double)limits[i];
        requested[i]=Math.Min(requested[i],limits[i]);gas.Value=requested[i];slider.Value=(double)requested[i];syncing=false;
    }
    public void SelectMotor(int motor){if(!busy && !stopping)motorPicker.SelectedIndex=Math.Clamp(motor,0,3);}
    public Task StopAllAsync()=>StopAsync();
    void Render(S3ControlState s)
    {
        state = s;
        if (!s.Armed) { token = "";Array.Clear(requested);SyncInputs(); }
        var channel=s.Motors?[motorPicker.SelectedIndex];
        reading.Text = $"Задано {(channel?.Target??s.Target) / 10.0:0.0}% · вихід {(channel?.Applied??s.Applied) / 10.0:0.0}%";
        allReadings.Text=s.Motors is {} motors?string.Join("    ",motors.Select((m,i)=>$"M{i+1}: {m.Applied/10.0:0.0}%")):"Стара прошивка · один вихід GPIO14";
        bool compatible=!requireFour || s.Motors is {Length:4};
        motorPicker.IsEnabled=!busy && !stopping && s.Motors is not null;
        limit.IsEnabled=!s.Armed && !busy && compatible;
        if(!compatible)message.Text="Для моторів M1–M4 оновіть плату пакетом S3-Connect 0.4.0. Керування GPIO14 тут заблоковане.";
        arm.IsEnabled = compatible && !busy && !stopping && s.Ready && !s.Armed && !s.Pending;
        slider.IsEnabled = gas.IsEnabled = apply.IsEnabled = compatible && !busy && !stopping && s.Armed && token.Length > 0;
        update.IsEnabled = !busy && !stopping && !s.Armed && !s.Pending;
        connect.IsEnabled = !busy && !stopping && !s.Armed;
    }
    void Fault(Exception ex)
    {
        token = ""; state = null; arm.IsEnabled = apply.IsEnabled = slider.IsEnabled = gas.IsEnabled = update.IsEnabled = false;
        connect.IsEnabled = true; reading.Text = "Стан виходу не підтверджений";
        message.Text = "Керування припинено. Плата зупиняє тест без команд за 1 с. " + (ex is OperationCanceledException ? "Час відповіді минув." : ex.Message);
    }
    async Task ConnectAsync()
    {
        if (busy || stopping) return;
        epoch++; state = null; token = "";
        try { client?.Dispose(); client = createClient(address, accessKey); }
        catch (Exception ex) { Fault(ex); return; }
        await RunAsync(async () => {
            var s = await client.SendAsync("/status", cancellation: lifetime.Token);
            if (s.Pending) s = await client.SendAsync("/confirm", new(), lifetime.Token);
            return s;
        }, "З’єднання встановлено. Підготовка тесту не подає газ.");
    }
    async Task RunAsync(Func<Task<S3ControlState>> action, string success)
    {
        if (busy || stopping || closed) return;
        busy = true; var generation = epoch; if (state is { } old) Render(old); connect.IsEnabled = false;
        try { var s = await action(); if (!closed && generation == epoch) { state = s; if (s.Armed && s.Token is not null) token = s.Token; message.Text = success; } }
        catch (Exception ex) { if (!closed && generation == epoch) Fault(ex); }
        finally { busy = false; if (!closed && state is { } s) Render(s); else if (!closed) connect.IsEnabled = true; }
    }
    async Task PollAsync()
    {
        if (busy || stopping || polling || closed || state is null || client is null) return;
        if (token.Length == 0 && Environment.TickCount64 - lastPoll < 1000) return;
        polling = true; lastPoll = Environment.TickCount64; var generation = epoch;
        try {
            bool wasArmed = state.Armed;
            var s = await client.SendAsync(token.Length > 0 ? "/lease" : "/status", token.Length > 0 ? new() { ["token"] = token } : null, lifetime.Token);
            if (!closed && generation == epoch) {
                Render(s);
                if (wasArmed && !s.Armed) message.Text = s.Reason == "TIME_LIMIT" ? "Тест завершено: минуло 30 секунд." : "Плата зупинила тест. Повторний запуск — лише вручну.";
            }
        } catch (Exception ex) { if (!closed && generation == epoch) Fault(ex); }
        finally { polling = false; }
    }
    async Task StopAsync()
    {
        if (stopping) return; stopping = true;
        epoch++; token = ""; arm.IsEnabled = apply.IsEnabled = false;
        if (client is null) { stopping = false; return; }
        try { var s = await client.SendAsync("/stop", new()); if (!closed) { Render(s); message.Text = "Зупинено · усі виходи 0%."; } }
        catch (Exception ex) { if (!closed) Fault(ex); }
        finally { stopping = false; if (!closed && state is { } s) Render(s); }
    }
    async Task UpdateAsync()
    {
        if (busy || stopping || state is not { Armed: false } || client is null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Прошивка PROpTEST", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PROpTEST ESP32-S3") { Patterns = ["*.ptfw"] }] });
        if (files.Count == 0 || closed) return;
        busy = true; token = ""; Render(state); string previous = state.Slot;
        try {
            await client.SendAsync("/stop", new(), lifetime.Token);
            message.Text = "Передавання прошивки. Не вимикайте плату…";
            using var file = files[0]; await using var stream = await file.OpenReadAsync();
            await client.UploadAsync(stream, lifetime.Token);
            message.Text = "Файл перевірено. Очікуємо перезапуск…";
            S3ControlState? fresh = null;
            for (int n = 0; n < 20 && !closed; n++) {
                await Task.Delay(1500, lifetime.Token);
                try {
                    var s = await client.SendAsync("/status", cancellation: lifetime.Token);
                    if (s.Slot == previous || s.Uptime > 45000) continue;
                    fresh = s.Pending ? await client.SendAsync("/confirm", new(), lifetime.Token) : s; break;
                } catch (HttpRequestException) { } catch (OperationCanceledException) when (!closed) { }
            }
            if (fresh is null) throw new IOException("Нова версія не підтвердила запуск. Без підтвердження плата повертає попередню прошивку.");
            state = fresh; message.Text = "Оновлення завершено · " + fresh.Version;
        } catch (Exception ex) { if (!closed) Fault(ex); }
        finally { busy = false; if (!closed && state is { } s) Render(s); }
    }
}
