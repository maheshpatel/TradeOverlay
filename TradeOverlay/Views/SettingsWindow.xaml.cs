using System.Windows;
using TradeOverlay.ViewModels;

namespace TradeOverlay.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel settingsViewModel)
    {
        InitializeComponent();
        _vm = settingsViewModel;
        DataContext = _vm;
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _vm.Reload();
        // Seed PasswordBoxes (don't support two-way binding)
        ApiSecretBox.Password = _vm.ApiSecret;
        AccessTokenBox.Password = _vm.AccessToken;
    }

    private void ApiSecretBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _vm.ApiSecret = ApiSecretBox.Password;

    private void AccessTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _vm.AccessToken = AccessTokenBox.Password;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _vm.Save();
        Hide();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
