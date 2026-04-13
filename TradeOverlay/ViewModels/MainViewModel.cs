using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using TradeOverlay.Config;
using TradeOverlay.Models;
using TradeOverlay.Services;

namespace TradeOverlay.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IKiteService _kiteService;
    private readonly IBrokerageCalculatorService _brokerageCalc;
    private readonly ISoundAlertService _soundAlert;
    private readonly AppSettings _settings;

    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _blinkTimer;

    private bool _blinkOn;
    private bool _lossBlinkOn;
    private int _lastKnownTradeCount = -1;
    private bool _lossAlertFired;     // prevent repeated alert sounds per session
    private bool _tradeAlertFired;
    private DateTime _tradeLogoutLastFired = DateTime.MinValue;  // cooldown between retries
    private DateTime _lossLogoutLastFired  = DateTime.MinValue;
    private static readonly TimeSpan LogoutCooldown = TimeSpan.FromMinutes(2);
    private string? _logoutStatusMessage; // pinned after logout so normal refresh doesn't overwrite it

    // ── Observable Properties ─────────────────────────────────────────────────

    // Trade counter
    [ObservableProperty] private int _tradeCount;
    [ObservableProperty] private int _closedTradeCount;
    [ObservableProperty] private int _openTradeCount;
    [ObservableProperty] private int _maxTrades;
    [ObservableProperty] private bool _isOverLimit;
    [ObservableProperty] private bool _blinkVisible = true;

    // P&L
    [ObservableProperty] private decimal _realisedPnL;
    [ObservableProperty] private decimal _unrealisedPnL;
    [ObservableProperty] private decimal _totalPnL;
    [ObservableProperty] private bool _isDailyLossBreached;
    [ObservableProperty] private bool _lossBlinkVisible = true;
    [ObservableProperty] private decimal _maxDailyLoss;

    // Brokerage
    [ObservableProperty] private decimal _totalBrokerage;
    [ObservableProperty] private decimal _totalStt;
    [ObservableProperty] private decimal _totalTransactionCharges;
    [ObservableProperty] private decimal _totalCgst;
    [ObservableProperty] private decimal _totalSgst;
    [ObservableProperty] private decimal _totalIgst;
    [ObservableProperty] private decimal _totalSebiCharges;
    [ObservableProperty] private decimal _totalStampCharges;
    [ObservableProperty] private decimal _grandTotalCharges;
    [ObservableProperty] private string _chargesSource = string.Empty;

    // Session slots (bound as collection)
    public ObservableCollection<SessionSlotViewModel> SessionSlots { get; } = new();

    // Status
    [ObservableProperty] private string _statusMessage = "Initialising…";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private DateTime _lastUpdated = DateTime.Now;

    public double WindowOpacity => _settings.WindowOpacity;

    public MainViewModel(
        IKiteService kiteService,
        IBrokerageCalculatorService brokerageCalc,
        ISoundAlertService soundAlert,
        AppSettings settings)
    {
        _kiteService  = kiteService;
        _brokerageCalc = brokerageCalc;
        _soundAlert   = soundAlert;
        _settings     = settings;

        _maxTrades    = settings.MaxTradesPerDay;
        _maxDailyLoss = settings.MaxDailyLossRupees;

        // Initialise session slots
        foreach (var slot in SessionSlot.All)
            SessionSlots.Add(new SessionSlotViewModel(slot.Label));

        // Poll timer
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(settings.PollingIntervalSeconds)
        };
        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        _pollTimer.Start();

        // Blink timer — handles both trade limit and loss limit blinks
        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _blinkTimer.Tick += (_, _) =>
        {
            // Trade limit blink
            if (IsOverLimit)
            {
                _blinkOn = !_blinkOn;
                BlinkVisible = _blinkOn;
            }
            else
                BlinkVisible = true;

            // Loss limit blink
            if (IsDailyLossBreached)
            {
                _lossBlinkOn = !_lossBlinkOn;
                LossBlinkVisible = _lossBlinkOn;
            }
            else
                LossBlinkVisible = true;
        };
        _blinkTimer.Start();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey) ||
            string.IsNullOrWhiteSpace(_settings.AccessToken))
        {
            StatusMessage = "⚠ API credentials not set — open Settings";
            return;
        }

        IsLoading = true;
        StatusMessage = "Refreshing…";

        try
        {
            // ── 1. Orders — source of truth for trade count ────────────────────
            var orders    = await _kiteService.GetTodaysOrdersAsync();
            var completed = orders.Where(o => o.Status == "COMPLETE").ToList();
            int newCount  = completed.Count;

            // TradeCount always comes from completed orders, not positions
            TradeCount  = newCount;
            IsOverLimit = TradeCount > MaxTrades;

            // ── 2. Positions — always refresh every tick for live P&L ──────────
            // Charges only refresh when order count changes (rate limit optimisation)
            if (newCount != _lastKnownTradeCount)
            {
                await RefreshPositionsAndChargesAsync(completed);
                _lastKnownTradeCount = newCount;
            }
            else
            {
                // Always refresh positions for live P&L even with no new orders
                await RefreshPositionsOnlyAsync();
            }

            // ── 3. Session breakdown ───────────────────────────────────────────
            UpdateSessionSlots(completed);

            // ── 4. Trade limit alert + logout ─────────────────────────────────
            if (IsOverLimit && !_tradeAlertFired)
            {
                _soundAlert.PlayTradeAlert();
                _tradeAlertFired = true;
            }
            else if (!IsOverLimit)
                _tradeAlertFired = false;

            if (IsOverLimit && OpenTradeCount == 0 && DateTime.Now - _tradeLogoutLastFired > LogoutCooldown)
            {
                _tradeLogoutLastFired = DateTime.Now;
                await TriggerKiteLogoutAsync("Trade limit exceeded");
            }

            LastUpdated = DateTime.Now;
            // Don't overwrite the logout status — keep it visible until next settings change
            StatusMessage = _logoutStatusMessage ?? $"Updated {LastUpdated:HH:mm:ss}";
        }
        catch (KiteServiceException ex)
        {
            StatusMessage = $"⚠ {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"⚠ Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Opens kite.zerodha.com/logout in the default browser using the existing session.
    /// Because UseShellExecute = true passes the URL to the OS shell, Chrome opens it
    /// in the current profile with existing cookies, triggering Kite's logout flow.
    /// </summary>
    private Task TriggerKiteLogoutAsync(string reason)
    {
        KiteService.Log("BROWSER LOGOUT TRIGGERED", reason);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = "https://kite.zerodha.com/logout",
                UseShellExecute = true
            });
            _logoutStatusMessage = $"⛔ {reason} — Kite logout opened in browser";
        }
        catch (Exception ex)
        {
            KiteService.Log("BROWSER LOGOUT FAILED", ex.Message);
            _logoutStatusMessage = $"⚠ {reason} — could not open logout: {ex.Message}";
        }
        StatusMessage = _logoutStatusMessage;
        return Task.CompletedTask;
    }

    /// <summary>Fetches positions + charges when order count has changed.</summary>
    private async Task RefreshPositionsAndChargesAsync(List<KiteOrder> completed)
    {
        // Try Kite charges API first; fall back to local NRI Non-PIS calculator
        BrokerageSummary brokerage;
        try
        {
            brokerage     = await _kiteService.GetChargesAsync(completed)
                            ?? new BrokerageSummary { IsFromApi = true };
            ChargesSource = "📡 Zerodha (NRI brokerage applied)";
        }
        catch (KiteServiceException ex)
        {
            brokerage     = _brokerageCalc.Calculate(completed);
            ChargesSource = "🧮 NRI Non-PIS (estimated)";
            KiteService.Log("CHARGES FALLBACK", ex.Message);
        }

        // Override brokerage with NRI Non-PIS rate: ₹50 per executed order
        // The API returns resident rates (₹20); local calculator uses premium-based rate
        // Both are wrong for flat-fee NRI — override here with the correct flat rate
        brokerage.Brokerage = completed.Count * 50m;

        // Recalculate GST after brokerage override
        // GST = 18% of (brokerage + SEBI charges + transaction charges)
        // Split equally into CGST (9%) and SGST (9%) for domestic trades
        var gstBase  = brokerage.Brokerage + brokerage.SebiCharges + brokerage.TransactionCharges;
        var gstTotal = Math.Round(gstBase * 0.18m, 2);
        brokerage.Cgst = Math.Round(gstTotal / 2, 2);
        brokerage.Sgst = Math.Round(gstTotal / 2, 2);
        brokerage.Igst = 0m;

        TotalBrokerage          = Math.Round(brokerage.Brokerage, 2);
        TotalStt                = Math.Round(brokerage.Stt, 2);
        TotalTransactionCharges = Math.Round(brokerage.TransactionCharges, 2);
        TotalCgst               = Math.Round(brokerage.Cgst, 2);
        TotalSgst               = Math.Round(brokerage.Sgst, 2);
        TotalIgst               = Math.Round(brokerage.Igst, 2);
        TotalSebiCharges        = Math.Round(brokerage.SebiCharges, 2);
        TotalStampCharges       = Math.Round(brokerage.StampCharges, 2);
        GrandTotalCharges       = Math.Round(brokerage.TotalCharges, 2);

        // Positions
        await RefreshPositionsOnlyAsync();
    }

    /// <summary>Refreshes P&L and open/closed counts from positions endpoint.</summary>
    private async Task RefreshPositionsOnlyAsync()
    {
        try
        {
            var posData = await _kiteService.GetPositionsAsync();
            var dayPos  = posData.Day ?? new List<KitePosition>();

            // Open  = symbols with net quantity != 0 (still holding a position)
            // Closed = total completed orders / 2 (each round trip = 1 buy + 1 sell)
            OpenTradeCount   = dayPos.Count(p => p.Quantity != 0);
            ClosedTradeCount = (TradeCount - OpenTradeCount) / 2;

            // Use Pnl as the single source of truth for total P&L.
            // For closed positions: Realised = sell_value - buy_value, Unrealised = 0
            // For open positions:   Realised = RealisedRaw, Unrealised = UnrealisedRaw
            // Total = sum of pnl (always correct regardless of open/closed state)
            RealisedPnL   = Math.Round(dayPos.Sum(p => p.Realised), 2);
            UnrealisedPnL = Math.Round(dayPos.Sum(p => p.Unrealised), 2);
            TotalPnL      = Math.Round(dayPos.Sum(p => p.Pnl), 2);

            // Daily loss alert
            IsDailyLossBreached = MaxDailyLoss > 0 && TotalPnL < 0
                                  && Math.Abs(TotalPnL) >= MaxDailyLoss;

            if (IsDailyLossBreached && !_lossAlertFired)
            {
                _soundAlert.PlayLossAlert();
                _lossAlertFired = true;
            }
            else if (!IsDailyLossBreached)
                _lossAlertFired = false;

            if (IsDailyLossBreached && OpenTradeCount == 0 && DateTime.Now - _lossLogoutLastFired > LogoutCooldown)
            {
                _lossLogoutLastFired = DateTime.Now;
                await TriggerKiteLogoutAsync("Daily loss limit exceeded");
            }
        }
        catch (KiteServiceException ex)
        {
            KiteService.Log("POSITIONS ERROR", ex.Message);
            // Don't overwrite status — positions errors are secondary to main refresh
        }
        catch (Exception ex)
        {
            KiteService.Log("POSITIONS UNEXPECTED ERROR", $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Counts completed orders into the 3 NSE session windows based on IST timestamp.
    /// </summary>
    private void UpdateSessionSlots(List<KiteOrder> completed)
    {
        var slots = SessionSlot.All;

        for (int i = 0; i < slots.Length; i++)
        {
            var slot  = slots[i];
            int count = completed.Count(o =>
            {
                if (o.OrderTimestamp == null) return false;
                // Kite returns timestamps in IST
                var t = o.OrderTimestamp.Value.TimeOfDay;
                return t >= slot.Start && t < slot.End;
            });
            SessionSlots[i].TradeCount = count;
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void ApplySettings()
    {
        MaxTrades     = _settings.MaxTradesPerDay;
        MaxDailyLoss  = _settings.MaxDailyLossRupees;
        _pollTimer.Interval = TimeSpan.FromSeconds(_settings.PollingIntervalSeconds);
        OnPropertyChanged(nameof(WindowOpacity));
        IsOverLimit   = TradeCount > MaxTrades;
        _kiteService.UpdateCredentials(_settings.ApiKey, _settings.AccessToken);
        _lastKnownTradeCount = -1;
        _lossAlertFired       = false;
        _tradeAlertFired      = false;
        _tradeLogoutLastFired = DateTime.MinValue;
        _lossLogoutLastFired  = DateTime.MinValue;
        _logoutStatusMessage  = null;
    }

    public Task TriggerRefreshAsync() => RefreshAsync();

    public void SaveWindowPosition(double left, double top)
    {
        _settings.WindowLeft = left;
        _settings.WindowTop  = top;
        _settings.Save();
    }

    public (double Left, double Top) GetWindowPosition() =>
        (_settings.WindowLeft, _settings.WindowTop);

    public void Dispose()
    {
        _pollTimer.Stop();
        _blinkTimer.Stop();
    }
}

// ── SessionSlotViewModel ──────────────────────────────────────────────────────

/// <summary>Bindable wrapper for a single session slot shown in the UI.</summary>
public partial class SessionSlotViewModel : ObservableObject
{
    public string Label { get; }
    [ObservableProperty] private int _tradeCount;

    public SessionSlotViewModel(string label) => Label = label;
}
