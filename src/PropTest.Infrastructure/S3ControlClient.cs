using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PropTest.Infrastructure;

public sealed record S3MotorState(int Pin, int Target, int Applied);

public sealed record S3ControlState(string Version, int Pin, int Max, bool Armed, bool Ready,
    int Target, int Applied, string Reason, bool Pending, string Slot, uint Uptime, string? Token, S3MotorState[]? Motors = null);

/// <summary>Authenticated motor-check API, separate from sensor recording.</summary>
public sealed class S3ControlClient : IDisposable
{
    readonly HttpClient http;
    readonly SemaphoreSlim gate = new(1);
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public S3ControlClient(string address, string password, HttpMessageHandler? handler = null)
    {
        var uri = new Uri(address);
        if (uri.Scheme != "http" || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0)
            throw new ArgumentException("Перевірте адресу ESP32.");
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.BaseAddress = uri; http.Timeout = Timeout.InfiniteTimeSpan;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("proptest:" + password)));
        http.DefaultRequestHeaders.Add("X-Proptest", "1");
    }
    public Task<S3ControlState> ReadDiscoveryStatusAsync(CancellationToken cancellation = default) => SendCoreAsync("/status", null, cancellation, 5000);
    public Task<S3ControlState> SendAsync(string path, Dictionary<string, string>? values = null, CancellationToken cancellation = default)
        => SendCoreAsync(path, values, cancellation, 800);
    async Task<S3ControlState> SendCoreAsync(string path, Dictionary<string, string>? values, CancellationToken cancellation, int timeoutMs)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(timeoutMs);
        await gate.WaitAsync(timeout.Token);
        try
        {
            using var request = new HttpRequestMessage(values is null ? HttpMethod.Get : HttpMethod.Post, path);
            if (values is not null) request.Content = new FormUrlEncodedContent(values);
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new IOException(response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "Пароль плати не підійшов. Це пароль PROpTEST, а не вашого роутера."
                : await response.Content.ReadAsStringAsync(timeout.Token));
            var state = await response.Content.ReadFromJsonAsync<S3ControlState>(Json, timeout.Token) ?? throw new IOException("Порожня відповідь плати.");
            bool legacy = state.Version.StartsWith("S3-Connect 0.3.", StringComparison.Ordinal) && state.Pin == 14 && state.Motors is null;
            bool four = state.Version.StartsWith("S3-Connect 0.4.", StringComparison.Ordinal) && state.Pin == 10
                && state.Motors is {Length:4} && state.Motors.Select((m,i)=>m is not null && m.Pin==10+i && m.Target is >=0 and <=100 && m.Applied is >=0 and <=100).All(v=>v)
                && state.Target==state.Motors[0].Target && state.Applied==state.Motors[0].Applied;
            if ((!legacy && !four) || state.Max != 100 || state.Applied is <0 or >100 || state.Target is <0 or >100)
                throw new IOException("Непідтримувана конфігурація плати. Очікуються GPIO10–13 (S3-Connect 0.4) або старий GPIO14 (0.3), ліміт 10%.");
            if (path == "/stop" && (state.Armed || state.Applied != 0 || state.Target != 0 || state.Motors?.Any(m=>m.Target!=0 || m.Applied!=0)==true))
                throw new IOException("Плата не підтвердила нульовий вихід після Stop.");
            return state;
        }
        finally { gate.Release(); }
    }
    public async Task UploadAsync(Stream package, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        await gate.WaitAsync(timeout.Token);
        try
        {
            using var body = new MultipartFormDataContent();
            body.Add(new StreamContent(package), "firmware", "firmware.ptfw");
            using var response = await http.PostAsync("/update", body, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new IOException(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        finally { gate.Release(); }
    }
    public void Dispose() => http.Dispose();
}
