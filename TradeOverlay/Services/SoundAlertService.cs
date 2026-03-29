using System.Media;
using System.Runtime.InteropServices;

namespace TradeOverlay.Services;

public interface ISoundAlertService
{
    void PlayLossAlert();
    void PlayTradeAlert();
}

/// <summary>
/// Plays system sounds for breach alerts.
/// Uses Windows MessageBeep via P/Invoke for zero-dependency beeps.
/// </summary>
public class SoundAlertService : ISoundAlertService
{
    // Windows MessageBeep types
    private const uint MB_ICONERROR       = 0x00000010; // critical stop — for loss alert
    private const uint MB_ICONEXCLAMATION = 0x00000030; // exclamation — for trade limit
    private const uint MB_OK              = 0x00000000; // simple beep

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MessageBeep(uint uType);

    private DateTime _lastLossAlert   = DateTime.MinValue;
    private DateTime _lastTradeAlert  = DateTime.MinValue;

    // Throttle — don't spam alerts more than once every 10 seconds
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromSeconds(10);

    /// <summary>Plays a critical alert sound when daily loss limit is breached.</summary>
    public void PlayLossAlert()
    {
        if (DateTime.Now - _lastLossAlert < AlertCooldown) return;
        _lastLossAlert = DateTime.Now;

        Task.Run(() =>
        {
            // Play 3 rapid critical beeps for maximum attention
            for (int i = 0; i < 3; i++)
            {
                MessageBeep(MB_ICONERROR);
                Thread.Sleep(300);
            }
        });
    }

    /// <summary>Plays a warning sound when trade count limit is breached.</summary>
    public void PlayTradeAlert()
    {
        if (DateTime.Now - _lastTradeAlert < AlertCooldown) return;
        _lastTradeAlert = DateTime.Now;

        Task.Run(() =>
        {
            MessageBeep(MB_ICONEXCLAMATION);
            Thread.Sleep(300);
            MessageBeep(MB_ICONEXCLAMATION);
        });
    }
}
