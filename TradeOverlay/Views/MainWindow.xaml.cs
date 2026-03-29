using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Input;
using TradeOverlay.ViewModels;

namespace TradeOverlay.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = _vm;

        // Restore last saved position
        var (left, top) = _vm.GetWindowPosition();
        Left = left;
        Top = top;

        Loaded += async (_, _) => await _vm.RefreshAsync();
    }

    // ── Drag to move ──────────────────────────────────────────────────────────
    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            _vm.SaveWindowPosition(Left, Top);
        }
    }

    // ── Toolbar buttons ───────────────────────────────────────────────────────
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var sp = ((App)Application.Current).Services;
        var settingsWindow = sp.GetRequiredService<SettingsWindow>();
        settingsWindow.Owner = this;
        settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        settingsWindow.Show();
        settingsWindow.Activate();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _vm.SaveWindowPosition(Left, Top);
        Application.Current.Shutdown();
    }
}
