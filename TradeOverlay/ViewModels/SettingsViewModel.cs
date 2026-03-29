using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TradeOverlay.Config;
using TradeOverlay.Services;

namespace TradeOverlay.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly MainViewModel _mainVm;
    private readonly IKiteAuthService _authService;

    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _apiSecret = string.Empty;
    [ObservableProperty] private string _accessToken = string.Empty;
    [ObservableProperty] private int _maxTradesPerDay;
    [ObservableProperty] private decimal _maxDailyLossRupees;
    [ObservableProperty] private int _pollingIntervalSeconds;
    [ObservableProperty] private decimal _defaultTradeValue;
    [ObservableProperty] private double _windowOpacity;
    [ObservableProperty] private int _callbackPort;

    [ObservableProperty] private string _loginStatus = string.Empty;
    [ObservableProperty] private bool _isLoggingIn;
    [ObservableProperty] private bool _loginSuccess;

    public SettingsViewModel(AppSettings settings, MainViewModel mainVm, IKiteAuthService authService)
    {
        _settings = settings;
        _mainVm = mainVm;
        _authService = authService;
        Reload();
    }

    public void Reload()
    {
        ApiKey = _settings.ApiKey;
        ApiSecret = _settings.ApiSecret;
        AccessToken = _settings.AccessToken;
        MaxTradesPerDay = _settings.MaxTradesPerDay;
        MaxDailyLossRupees = _settings.MaxDailyLossRupees;
        PollingIntervalSeconds = _settings.PollingIntervalSeconds;
        DefaultTradeValue = _settings.DefaultTradeValue;
        WindowOpacity = _settings.WindowOpacity;
        CallbackPort = _settings.CallbackPort;
        LoginStatus = string.Empty;
        LoginSuccess = false;
    }

    [RelayCommand]
    public async Task LoginWithKiteAsync()
    {
        // Save key/secret before login attempt
        _settings.ApiKey = ApiKey.Trim();
        _settings.ApiSecret = ApiSecret.Trim();
        _settings.CallbackPort = CallbackPort;
        _settings.Save();

        IsLoggingIn = true;
        LoginSuccess = false;
        LoginStatus = "Opening browser… waiting for Kite login…";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var token = await _authService.LoginAsync(cts.Token);

            AccessToken = token;
            LoginStatus = "✅ Login successful! Access token saved.";
            LoginSuccess = true;

            _mainVm.ApplySettings();
            _ = _mainVm.TriggerRefreshAsync();
        }
        catch (OperationCanceledException)
        {
            LoginStatus = "⚠ Login timed out (3 min). Please try again.";
        }
        catch (KiteAuthException ex)
        {
            LoginStatus = $"❌ {ex.Message}";
        }
        catch (Exception ex)
        {
            LoginStatus = $"❌ Unexpected error: {ex.Message}";
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    [RelayCommand]
    public void Save()
    {
        _settings.ApiKey = ApiKey.Trim();
        _settings.ApiSecret = ApiSecret.Trim();
        _settings.AccessToken = AccessToken.Trim();
        _settings.MaxTradesPerDay = MaxTradesPerDay;
        _settings.MaxDailyLossRupees = MaxDailyLossRupees;
        _settings.PollingIntervalSeconds = Math.Max(5, PollingIntervalSeconds);
        _settings.DefaultTradeValue = DefaultTradeValue;
        _settings.WindowOpacity = Math.Clamp(WindowOpacity, 0.3, 1.0);
        _settings.CallbackPort = CallbackPort;
        _settings.Save();

        _mainVm.ApplySettings();
        _ = _mainVm.TriggerRefreshAsync();
    }
}
