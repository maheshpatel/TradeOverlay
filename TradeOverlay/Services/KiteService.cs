using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using TradeOverlay.Config;
using TradeOverlay.Models;

namespace TradeOverlay.Services;

public interface IKiteService
{
    Task<List<KiteOrder>> GetTodaysOrdersAsync();
    Task<KitePositionsResponse> GetPositionsAsync();
    Task<BrokerageSummary?> GetChargesAsync(List<KiteOrder> completedOrders);
    void UpdateCredentials(string apiKey, string accessToken);
}

public class KiteService : IKiteService
{
    private const string BaseUrl = "https://api.kite.trade";

    private static readonly string DebugLogPath =
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TradeOverlay", "debug.log");

    private readonly HttpClient _http;
    private readonly AppSettings _settings;

    public KiteService(HttpClient http, AppSettings settings)
    {
        _http = http;
        _settings = settings;
        _http.BaseAddress = new Uri(BaseUrl);
        // Clear default Accept headers — Kite doesn't require them and they
        // bleed into POST requests causing "invalid json" on /charges/orders
        _http.DefaultRequestHeaders.Accept.Clear();
    }

    public void UpdateCredentials(string apiKey, string accessToken)
    {
        _settings.ApiKey = apiKey;
        _settings.AccessToken = accessToken;
    }

    /// <summary>GET /orders</summary>
    public async Task<List<KiteOrder>> GetTodaysOrdersAsync()
    {
        var json = await SendGetAsync("/orders");
        var result = JsonConvert.DeserializeObject<KiteResponse<List<KiteOrder>>>(json);
        if (result?.Status == "success" && result.Data != null)
            return result.Data;
        throw new KiteServiceException($"Orders API error: {result?.Message ?? "Unknown"}");
    }

    /// <summary>GET /positions</summary>
    public async Task<KitePositionsResponse> GetPositionsAsync()
    {
        var json = await SendGetAsync("/portfolio/positions");
        Log("POSITIONS RAW", json);

        try
        {
            var jobj = JObject.Parse(json);
            var dayArr = jobj["data"]?["day"] as JArray;
            if (dayArr != null && dayArr.Count > 0)
                Log("POSITIONS FIRST DAY ENTRY",
                    string.Join("\n", ((JObject)dayArr[0]).Properties().Select(p => $"  {p.Name} = {p.Value}")));
            else
                Log("POSITIONS DAY ARRAY", "Empty — no positions today");
        }
        catch (Exception ex) { Log("POSITIONS DEBUG ERROR", ex.Message); }

        var result = JsonConvert.DeserializeObject<KiteResponse<KitePositionsResponse>>(json);
        if (result?.Status == "success" && result.Data != null)
            return result.Data;
        throw new KiteServiceException($"Positions API error: {result?.Message ?? "Unknown"}");
    }

    /// <summary>POST /charges/orders — get charges from Kite API.</summary>
    public async Task<BrokerageSummary?> GetChargesAsync(List<KiteOrder> completedOrders)
    {
        if (completedOrders.Count == 0)
            return new BrokerageSummary { IsFromApi = true };

        var ordersArray = completedOrders.Select(o =>
        {
            var req = KiteChargesOrderRequest.FromOrder(o);
            return new
            {
                order_id         = req.OrderId,
                exchange         = req.Exchange,
                tradingsymbol    = req.TradingSymbol,
                transaction_type = req.TransactionType,
                variety          = req.Variety,
                product          = req.Product,
                order_type       = req.OrderType,
                quantity         = req.Quantity,
                average_price    = req.AveragePrice
            };
        }).ToList();

        var body = JsonConvert.SerializeObject(ordersArray);
        Log("CHARGES REQUEST JSON", body);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/charges/orders");
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey) &&
            !string.IsNullOrWhiteSpace(_settings.AccessToken))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("token",
                    $"{_settings.ApiKey}:{_settings.AccessToken}");
        }
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        Log($"CHARGES RESPONSE ({(int)response.StatusCode})", json);

        if (!response.IsSuccessStatusCode)
        {
            var detail = TryParseError(json) ?? $"HTTP {(int)response.StatusCode}";
            throw new KiteServiceException($"Charges API error: {detail}");
        }

        var result = JsonConvert.DeserializeObject<KiteResponse<List<KiteOrderCharges>>>(json);
        if (result?.Status == "success" && result.Data != null)
            return BrokerageSummary.FromApiResponse(result.Data);

        throw new KiteServiceException($"Charges API error: {result?.Message ?? "Unknown"}");
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task<string> SendGetAsync(string path)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            AddAuthHeaders(request);
            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var detail = TryParseError(json) ?? $"HTTP {(int)response.StatusCode}";
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    throw new KiteServiceException(
                        $"Authentication failed — token expired. Login again via Settings. ({detail})");
                throw new KiteServiceException($"API error: {detail}");
            }
            return json;
        }
        catch (KiteServiceException) { throw; }
        catch (HttpRequestException ex)
        {
            throw new KiteServiceException($"Network error ({ex.Message})", ex);
        }
        catch (JsonException ex)
        {
            throw new KiteServiceException($"Failed to parse API response: {ex.Message}", ex);
        }
    }

    private void AddAuthHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey) &&
            !string.IsNullOrWhiteSpace(_settings.AccessToken))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("token",
                    $"{_settings.ApiKey}:{_settings.AccessToken}");
        }
        // No Accept header — Kite doesn't need it and it conflicts with POST /charges/orders
    }

    private static string? TryParseError(string json)
    {
        try
        {
            var err = JsonConvert.DeserializeObject<KiteResponse<object>>(json);
            return err?.Message ?? err?.ErrorType;
        }
        catch { return null; }
    }

    public static void Log(string section, string content)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DebugLogPath)!);
            var line = $"[{DateTime.Now:HH:mm:ss}] [{section}]\n{content}\n{new string('-', 60)}\n";
            System.IO.File.AppendAllText(DebugLogPath, line);
        }
        catch { }
    }
}

public class KiteServiceException : Exception
{
    public KiteServiceException(string message) : base(message) { }
    public KiteServiceException(string message, Exception inner) : base(message, inner) { }
}
