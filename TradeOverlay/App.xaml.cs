using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using TradeOverlay.Config;
using TradeOverlay.Services;
using TradeOverlay.ViewModels;
using TradeOverlay.Views;

namespace TradeOverlay;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    /// <summary>Exposed so Windows can resolve transient dependencies via DI.</summary>
    public IServiceProvider Services => _serviceProvider!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Config
        services.AddSingleton<AppSettings>(AppSettings.Load());

        // HTTP + Kite service (HttpClient factory handles lifetime)
        services.AddHttpClient<IKiteService, KiteService>();
        services.AddHttpClient<IKiteAuthService, KiteAuthService>();

        // Services
        services.AddSingleton<IBrokerageCalculatorService, BrokerageCalculatorService>();
        services.AddSingleton<ISoundAlertService, SoundAlertService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
        services.AddSingleton<SettingsWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
