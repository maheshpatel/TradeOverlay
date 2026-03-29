using Newtonsoft.Json;
using System.IO;

namespace TradeOverlay.Config;

/// <summary>
/// Persisted application settings. Extend this class to add new configurable features.
/// </summary>
public class AppSettings
{
    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TradeOverlay", "settings.json");

    // ── Kite API ──────────────────────────────────────────────────────────────
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;

    // ── Auth ──────────────────────────────────────────────────────────────────
    /// <summary>Local port for the OAuth callback listener. Must match redirect URL in Kite developer portal.</summary>
    public int CallbackPort { get; set; } = 5000;

    // ── Trade Limits ──────────────────────────────────────────────────────────
    /// <summary>Maximum trades allowed per day before the counter blinks red.</summary>
    public int MaxTradesPerDay { get; set; } = 10;

    /// <summary>Maximum loss (₹) allowed per day. Alert fires when P&L drops below negative of this value.</summary>
    public decimal MaxDailyLossRupees { get; set; } = 5000m;

    // ── Polling ───────────────────────────────────────────────────────────────
    /// <summary>How often (in seconds) to refresh trade count from the API.</summary>
    public int PollingIntervalSeconds { get; set; } = 30;

    // ── Brokerage Calculator Defaults ─────────────────────────────────────────
    /// <summary>Default trade value (₹) used in brokerage estimate.</summary>
    public decimal DefaultTradeValue { get; set; } = 100000m;

    // ── Window Position ───────────────────────────────────────────────────────
    public double WindowLeft { get; set; } = 100;
    public double WindowTop { get; set; } = 100;

    // ── Overlay Opacity ───────────────────────────────────────────────────────
    public double WindowOpacity { get; set; } = 0.92;

    // ─────────────────────────────────────────────────────────────────────────

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { /* fall through to defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
    }
}
