# TradeOverlay

An always-on-top Windows desktop overlay for disciplined intraday trading on NSE.  
Built with **C# .NET 8 / WPF**, connects to **Kite Connect v3 API**.

---

## Features

| Feature | Detail |
|---|---|
| Always-on-top overlay | Draggable, transparent, stays above all windows |
| Trade counter | Closed + open split, blinks red when daily limit exceeded |
| Daily loss limit | Configurable ₹ limit — overlay turns red and beeps when breached |
| P&L panel | Live realised + unrealised P&L from Kite positions API |
| Session breakdown | Trade count per NSE session (09:15–11:00, 11:00–13:30, 13:30–15:30) |
| Brokerage calculator | Live from Zerodha `POST /charges/orders`, falls back to local estimate |
| Smart polling | Charges only fetched when trade count changes — conserves API quota |
| OAuth login | One-click browser login — no manual token copy-paste |
| Persistent settings | All config saved to `%AppData%\TradeOverlay\settings.json` |
| Extensible architecture | DI + MVVM — easy to add new panels and features |

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) (Windows only)
- A [Kite Connect](https://developers.kite.trade/) app with `api_key` and `api_secret`

---

## Build & Run

```bash
cd TradeOverlay
dotnet restore
dotnet run
```

> **Tip:** If you get build errors after replacing files, always delete `obj\` and `bin\` first:
> ```bash
> rd /s /q obj && rd /s /q bin && dotnet run
> ```

---

## First-Time Setup

### 1. Configure Kite Connect redirect URL
In the [Kite developer portal](https://developers.kite.trade), set your app's redirect URL to:
```
http://localhost:5000/callback/
```
(trailing slash is required)

### 2. Enter credentials in the overlay
Click **⚙** → enter your **API Key** and **API Secret** → click **🔐 Login with Kite**

The browser opens, you log in with Zerodha credentials, and the token is captured automatically.

### 3. Daily login
Access tokens expire at **6:00 AM IST** every day.  
Each morning, click **⚙ → 🔐 Login with Kite** — takes about 10 seconds.

---

## Settings Reference

| Setting | Default | Description |
|---|---|---|
| API Key | — | Your Kite Connect api_key |
| API Secret | — | Your Kite Connect api_secret |
| Callback Port | 5000 | Must match redirect URL in Kite portal |
| Max Trades Per Day | 10 | Counter blinks red above this number |
| Max Daily Loss (₹) | 5,000 | Overlay turns red + beeps when P&L loss exceeds this |
| Poll Interval (sec) | 15 | How often to refresh from API (min: 5) |
| Opacity | 0.92 | Overlay transparency |

---

## Overlay Sections

### Trade Counter
- Large number = total trades today (open + closed)
- **✅ N closed** = fully exited positions
- **🟡 N open** = positions still holding
- Turns orange when approaching limit, red + blinks when exceeded

### P&L Today
- **Realised** = P&L from closed positions
- **Unrealised** = P&L on open positions (mark-to-market)
- **Total P&L** = Realised + Unrealised
- Turns red and blinks + plays sound when daily loss limit hit

### Session Breakdown (IST)
| Session | Time |
|---|---|
| Morning | 09:15 – 11:00 |
| Midday | 11:00 – 13:30 |
| Afternoon | 13:30 – 15:30 |

### Brokerage
Shows `📡 Live from Zerodha` when using the charges API, or `⚠ Estimated locally` if API fails.
Only recalculated when a new trade executes — not on every poll.

---

## API Usage (at 15-second polling)

| Call | Frequency | Purpose |
|---|---|---|
| `GET /orders` | Every 15s | Trade count + session breakdown |
| `GET /positions` | Every 15s | P&L + open/closed split |
| `POST /charges/orders` | Only on new trade | Exact brokerage breakdown |

Well within Zerodha's 20 GET/sec and 10 POST/sec limits.

---

## Project Structure

```
TradeOverlay/
├── Config/
│   └── AppSettings.cs              # All settings, JSON persistence
├── Converters/
│   └── Converters.cs               # WPF value converters
├── Models/
│   └── KiteModels.cs               # Kite API models + SessionSlot
├── Services/
│   ├── KiteService.cs              # orders, positions, charges
│   ├── KiteAuthService.cs          # OAuth login flow
│   ├── BrokerageCalculatorService.cs  # Local fallback calculator
│   └── SoundAlertService.cs        # Beep alerts (P/Invoke)
├── Themes/
│   └── DarkTheme.xaml              # Colours, brushes, control styles
├── ViewModels/
│   ├── MainViewModel.cs            # All overlay state + logic
│   └── SettingsViewModel.cs        # Settings form
├── Views/
│   ├── MainWindow.xaml/.cs         # Overlay window
│   └── SettingsWindow.xaml/.cs     # Settings dialog
├── App.xaml/.cs                    # DI container
└── TradeOverlay.csproj
```

---

## Extending the App

- **New overlay panel** → Add `UserControl` in `Views/`, bind to new `ObservableObject` VM, add to `MainWindow.xaml`
- **New API data** → Add method to `IKiteService`, implement in `KiteService`
- **New settings** → Add property to `AppSettings`, expose in `SettingsViewModel`, add field to `SettingsWindow.xaml`
- **New alert type** → Add method to `ISoundAlertService`

## Suggested Next Features

- [ ] Position size calculator (capital × risk % ÷ stop distance)
- [ ] Revenge trade detector (trade within 2 min of a loss)
- [ ] Win/loss streak counter
- [ ] System tray icon with trade count badge
- [ ] DPAPI encryption for stored credentials
- [ ] Auto token refresh at 6 AM
