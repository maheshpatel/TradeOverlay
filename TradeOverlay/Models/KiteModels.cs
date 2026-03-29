using Newtonsoft.Json;

namespace TradeOverlay.Models;

// ── Kite Connect v3 response wrapper ─────────────────────────────────────────

public class KiteResponse<T>
{
    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("data")]
    public T? Data { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    [JsonProperty("error_type")]
    public string? ErrorType { get; set; }
}

// ── Order ─────────────────────────────────────────────────────────────────────

public class KiteOrder
{
    [JsonProperty("order_id")]
    public string OrderId { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("product")]
    public string Product { get; set; } = string.Empty;

    [JsonProperty("transaction_type")]
    public string TransactionType { get; set; } = string.Empty;

    [JsonProperty("tradingsymbol")]
    public string TradingSymbol { get; set; } = string.Empty;

    [JsonProperty("quantity")]
    public int Quantity { get; set; }

    [JsonProperty("average_price")]
    public decimal AveragePrice { get; set; }

    [JsonProperty("filled_quantity")]
    public int FilledQuantity { get; set; }

    [JsonProperty("order_timestamp")]
    public DateTime? OrderTimestamp { get; set; }

    [JsonProperty("exchange")]
    public string Exchange { get; set; } = string.Empty;

    [JsonProperty("order_type")]
    public string OrderType { get; set; } = string.Empty;

    [JsonProperty("variety")]
    public string Variety { get; set; } = "regular";
}

// ── Position ──────────────────────────────────────────────────────────────────

public class KitePosition
{
    [JsonProperty("tradingsymbol")]
    public string TradingSymbol { get; set; } = string.Empty;

    [JsonProperty("exchange")]
    public string Exchange { get; set; } = string.Empty;

    [JsonProperty("product")]
    public string Product { get; set; } = string.Empty;

    /// <summary>Net quantity held. Non-zero = position still open.</summary>
    [JsonProperty("quantity")]
    public int Quantity { get; set; }

    [JsonProperty("average_price")]
    public decimal AveragePrice { get; set; }

    /// <summary>
    /// Total P&L for this position (sell_value - buy_value).
    /// This is the authoritative field — always correct for both open and closed positions.
    /// </summary>
    [JsonProperty("pnl")]
    public decimal Pnl { get; set; }

    /// <summary>
    /// Mark-to-market unrealised P&L on the open portion.
    /// Only meaningful when Quantity != 0 (position still open).
    /// Kite sets this equal to pnl for closed positions after market hours — do NOT use directly.
    /// </summary>
    [JsonProperty("unrealised")]
    public decimal UnrealisedRaw { get; set; }

    /// <summary>
    /// Realised P&L on the closed portion.
    /// Kite sets this to 0 for closed MIS positions after market hours — do NOT use directly.
    /// </summary>
    [JsonProperty("realised")]
    public decimal RealisedRaw { get; set; }

    /// <summary>Buy value (cost of all buy legs).</summary>
    [JsonProperty("buy_value")]
    public decimal BuyValue { get; set; }

    /// <summary>Sell value (proceeds of all sell legs).</summary>
    [JsonProperty("sell_value")]
    public decimal SellValue { get; set; }

    /// <summary>True when position is fully closed today.</summary>
    public bool IsClosed => Quantity == 0;

    /// <summary>
    /// Actual realised P&L = sell_value - buy_value for closed positions.
    /// For open positions, use RealisedRaw from Kite.
    /// </summary>
    public decimal Realised => IsClosed ? SellValue - BuyValue : RealisedRaw;

    /// <summary>
    /// Unrealised P&L = only meaningful for open positions.
    /// Zero for closed positions (pnl IS the realised in that case).
    /// </summary>
    public decimal Unrealised => IsClosed ? 0 : UnrealisedRaw;
}

public class KitePositionsResponse
{
    [JsonProperty("net")]
    public List<KitePosition>? Net { get; set; }

    [JsonProperty("day")]
    public List<KitePosition>? Day { get; set; }
}

// ── POST /charges/orders ──────────────────────────────────────────────────────

public class KiteChargesOrderRequest
{
    public string OrderId { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string TradingSymbol { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public string Variety { get; set; } = "regular";
    public string Product { get; set; } = string.Empty;
    public string OrderType { get; set; } = "MARKET";
    public int Quantity { get; set; }
    public decimal AveragePrice { get; set; }

    public static KiteChargesOrderRequest FromOrder(KiteOrder o) => new()
    {
        OrderId         = o.OrderId,
        Exchange        = o.Exchange,
        TradingSymbol   = o.TradingSymbol,
        TransactionType = o.TransactionType,
        Variety         = string.IsNullOrWhiteSpace(o.Variety) ? "regular" : o.Variety,
        Product         = o.Product,
        OrderType       = string.IsNullOrWhiteSpace(o.OrderType) ? "MARKET" : o.OrderType,
        Quantity        = o.FilledQuantity > 0 ? o.FilledQuantity : o.Quantity,
        AveragePrice    = o.AveragePrice
    };
}

public class KiteChargesResponse
{
    [JsonProperty("orders")]
    public List<KiteOrderCharges>? Orders { get; set; }
}

public class KiteOrderCharges
{
    [JsonProperty("order_id")]
    public string OrderId { get; set; } = string.Empty;

    [JsonProperty("tradingsymbol")]
    public string TradingSymbol { get; set; } = string.Empty;

    [JsonProperty("exchange")]
    public string Exchange { get; set; } = string.Empty;

    [JsonProperty("transaction_type")]
    public string TransactionType { get; set; } = string.Empty;

    [JsonProperty("quantity")]
    public int Quantity { get; set; }

    [JsonProperty("average_price")]
    public decimal AveragePrice { get; set; }

    [JsonProperty("product")]
    public string Product { get; set; } = string.Empty;

    [JsonProperty("charges")]
    public KiteChargesBreakdown? Charges { get; set; }
}

public class KiteChargesBreakdown
{
    [JsonProperty("transaction_tax")]
    public decimal TransactionTax { get; set; }

    [JsonProperty("transaction_tax_type")]
    public string TransactionTaxType { get; set; } = string.Empty;

    [JsonProperty("exchange_turnover_charge")]
    public decimal ExchangeTurnoverCharge { get; set; }

    [JsonProperty("sebi_turnover_charge")]
    public decimal SebiTurnoverCharge { get; set; }

    [JsonProperty("brokerage")]
    public decimal Brokerage { get; set; }

    [JsonProperty("stamp_duty")]
    public decimal StampDuty { get; set; }

    [JsonProperty("gst")]
    public KiteGst? Gst { get; set; }

    [JsonProperty("total")]
    public decimal Total { get; set; }
}

public class KiteGst
{
    [JsonProperty("igst")]  public decimal Igst { get; set; }
    [JsonProperty("cgst")]  public decimal Cgst { get; set; }
    [JsonProperty("sgst")]  public decimal Sgst { get; set; }
    [JsonProperty("total")] public decimal Total { get; set; }
}

// ── Brokerage summary ─────────────────────────────────────────────────────────

public class BrokerageSummary
{
    public decimal Brokerage { get; set; }
    public decimal Stt { get; set; }
    public decimal TransactionCharges { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Igst { get; set; }
    public decimal Gst => Cgst + Sgst + Igst;
    public decimal SebiCharges { get; set; }
    public decimal StampCharges { get; set; }
    public bool IsFromApi { get; set; }

    public decimal TotalCharges =>
        Brokerage + Stt + TransactionCharges + Cgst + Sgst + Igst + SebiCharges + StampCharges;

    public static BrokerageSummary FromApiResponse(List<KiteOrderCharges> orders)
    {
        var s = new BrokerageSummary { IsFromApi = true };
        foreach (var o in orders.Where(o => o.Charges != null))
        {
            s.Brokerage          += o.Charges!.Brokerage;
            s.Stt                += o.Charges.TransactionTax;
            s.TransactionCharges += o.Charges.ExchangeTurnoverCharge;
            s.SebiCharges        += o.Charges.SebiTurnoverCharge;
            s.StampCharges       += o.Charges.StampDuty;
            s.Cgst               += o.Charges.Gst?.Cgst ?? 0;
            s.Sgst               += o.Charges.Gst?.Sgst ?? 0;
            s.Igst               += o.Charges.Gst?.Igst ?? 0;
        }

        // Kite sometimes returns all GST as IGST even for domestic trades.
        // Redistribute: for domestic (intra-state) it should be CGST + SGST (9% each).
        if (s.Igst > 0 && s.Cgst == 0 && s.Sgst == 0)
        {
            s.Cgst = Math.Round(s.Igst / 2, 2);
            s.Sgst = Math.Round(s.Igst / 2, 2);
            s.Igst = 0;
        }

        return s;
    }
}

// ── Session slot ─────────────────────────────────────────────────────────────

/// <summary>One of the three NSE trading session windows.</summary>
public class SessionSlot
{
    public string Label { get; set; } = string.Empty;
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public int TradeCount { get; set; }

    public static readonly SessionSlot[] All =
    [
        new() { Label = "09:15 – 11:00", Start = new(9,15,0),  End = new(11,0,0)  },
        new() { Label = "11:00 – 13:30", Start = new(11,0,0),  End = new(13,30,0) },
        new() { Label = "13:30 – 15:30", Start = new(13,30,0), End = new(15,30,0) },
    ];
}
