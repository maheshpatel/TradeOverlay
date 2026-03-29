using Newtonsoft.Json;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using TradeOverlay.Config;

namespace TradeOverlay.Services;

public interface IKiteAuthService
{
    /// <summary>
    /// Full OAuth flow:
    /// 1. Opens browser to Kite login page
    /// 2. Starts local HTTP listener on localhost:{port}
    /// 3. Catches the redirect with request_token
    /// 4. Exchanges request_token + api_secret for access_token
    /// 5. Saves access_token to settings
    /// </summary>
    Task<string> LoginAsync(CancellationToken ct = default);
}

public class KiteAuthService : IKiteAuthService
{
    private const string KiteLoginBase = "https://kite.zerodha.com/connect/login";
    private const string KiteSessionUrl = "https://api.kite.trade/session/token";

    private readonly AppSettings _settings;
    private readonly HttpClient _http;

    public KiteAuthService(AppSettings settings, HttpClient http)
    {
        _settings = settings;
        _http = http;
    }

    public async Task<string> LoginAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            throw new InvalidOperationException("API Key is not set. Please enter it in Settings first.");
        if (string.IsNullOrWhiteSpace(_settings.ApiSecret))
            throw new InvalidOperationException("API Secret is not set. Please enter it in Settings first.");

        var redirectUrl = $"http://localhost:{_settings.CallbackPort}/callback";
        var loginUrl = $"{KiteLoginBase}?api_key={_settings.ApiKey}&v=3";

        // ── Step 1: Start local listener BEFORE opening the browser ──────────
        var requestToken = await ListenForCallbackAsync(redirectUrl, ct);

        if (string.IsNullOrWhiteSpace(requestToken))
            throw new KiteAuthException("Did not receive a request_token from Kite. Login may have been cancelled.");

        // ── Step 2: Exchange request_token for access_token ───────────────────
        var accessToken = await ExchangeTokenAsync(requestToken);

        // ── Step 3: Persist ───────────────────────────────────────────────────
        _settings.AccessToken = accessToken;
        _settings.Save();

        return accessToken;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts an HttpListener, opens the browser, then waits for the callback.
    /// Returns the request_token extracted from the redirect URL.
    /// </summary>
    private async Task<string> ListenForCallbackAsync(string redirectUrl, CancellationToken ct)
    {
        var prefix = $"http://localhost:{_settings.CallbackPort}/callback/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new KiteAuthException(
                $"Could not start local listener on port {_settings.CallbackPort}. " +
                $"Is another app using that port? ({ex.Message})", ex);
        }

        // Open browser after listener is ready
        var loginUrl = $"https://kite.zerodha.com/connect/login?api_key={_settings.ApiKey}&v=3";
        OpenBrowser(loginUrl);

        // Wait for the callback (with cancellation support)
        HttpListenerContext context;
        try
        {
            var getContextTask = listener.GetContextAsync();
            var cancelTask = Task.Delay(Timeout.Infinite, ct);
            var completed = await Task.WhenAny(getContextTask, cancelTask);

            if (completed == cancelTask)
            {
                listener.Stop();
                throw new OperationCanceledException("Login was cancelled.");
            }

            context = await getContextTask;
        }
        catch (OperationCanceledException)
        {
            listener.Stop();
            throw;
        }

        // Parse request_token from query string
        // Kite redirects to: http://localhost:5000/callback?request_token=xxx&action=login&status=success
        var query = context.Request.QueryString;
        var requestToken = query["request_token"] ?? string.Empty;
        var status = query["status"] ?? string.Empty;

        // Send a nice HTML response back to the browser
        var responseHtml = status == "success"
            ? "<html><body style='font-family:sans-serif;text-align:center;margin-top:80px'>" +
              "<h2 style='color:#00E676'>✅ Login successful!</h2>" +
              "<p>You can close this tab and return to Trade Overlay.</p></body></html>"
            : "<html><body style='font-family:sans-serif;text-align:center;margin-top:80px'>" +
              "<h2 style='color:#FF1744'>❌ Login failed.</h2>" +
              "<p>Please try again from Trade Overlay settings.</p></body></html>";

        var buffer = Encoding.UTF8.GetBytes(responseHtml);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer, ct);
        context.Response.Close();

        listener.Stop();

        if (status != "success" || string.IsNullOrWhiteSpace(requestToken))
            throw new KiteAuthException($"Kite login failed. Status: '{status}'");

        return requestToken;
    }

    /// <summary>
    /// POST /session/token — exchanges request_token + checksum for access_token.
    /// Checksum = SHA256(api_key + request_token + api_secret)
    /// </summary>
    private async Task<string> ExchangeTokenAsync(string requestToken)
    {
        var checksum = ComputeChecksum(_settings.ApiKey, requestToken, _settings.ApiSecret);

        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("api_key", _settings.ApiKey),
            new KeyValuePair<string, string>("request_token", requestToken),
            new KeyValuePair<string, string>("checksum", checksum)
        });

        // Session endpoint uses api_key only in the Authorization header (no access_token yet)
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("token", $"{_settings.ApiKey}:");
        _http.DefaultRequestHeaders.Accept
             .Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _http.PostAsync(KiteSessionUrl, formData);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new KiteAuthException($"Token exchange failed ({(int)response.StatusCode}): {json}");

        var result = JsonConvert.DeserializeObject<KiteSessionResponse>(json);

        if (result?.Status != "success" || result.Data == null)
            throw new KiteAuthException($"Unexpected response from Kite: {json}");

        return result.Data.AccessToken;
    }

    /// <summary>SHA256(api_key + request_token + api_secret) as lowercase hex.</summary>
    private static string ComputeChecksum(string apiKey, string requestToken, string apiSecret)
    {
        var raw = apiKey + requestToken + apiSecret;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // If shell execute fails, try explicit chrome/edge
            try { Process.Start("explorer.exe", url); } catch { /* ignore */ }
        }
    }
}

// ── Kite session response models ──────────────────────────────────────────────

public class KiteSessionResponse
{
    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("data")]
    public KiteSessionData? Data { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }
}

public class KiteSessionData
{
    [JsonProperty("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonProperty("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonProperty("user_name")]
    public string UserName { get; set; } = string.Empty;

    [JsonProperty("login_time")]
    public string LoginTime { get; set; } = string.Empty;
}

public class KiteAuthException : Exception
{
    public KiteAuthException(string message) : base(message) { }
    public KiteAuthException(string message, Exception inner) : base(message, inner) { }
}
