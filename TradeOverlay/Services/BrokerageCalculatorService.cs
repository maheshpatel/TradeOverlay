using TradeOverlay.Models;

namespace TradeOverlay.Services;

public interface IBrokerageCalculatorService
{
    BrokerageSummary Calculate(IEnumerable<KiteOrder> orders);
    BrokerageSummary CalculateSingleTrade(string product, string exchange, decimal tradeValue, string transactionType = "BUY");
}

/// <summary>
/// Zerodha brokerage calculator for NON-PIS NRI account.
/// Ref: https://zerodha.com/charges#tab-equities
///
/// NRI Non-PIS Brokerage:
///   All segments  : 0.5% or ₹50 per executed order, whichever is lower
///
/// STT (same as resident):
///   CNC            : 0.1% on buy and sell
///   MIS / F&O      : 0.025% on sell side only (equity intraday)
///                    0.02% on sell side (futures)
///                    0.1% on sell side premium (options)
///
/// Transaction charges (NSE):
///   Equity CNC/MIS : 0.00307%
///   F&O Futures    : 0.00183%
///   F&O Options    : 0.03553% on premium
///
/// GST            : 18% on (brokerage + SEBI charges + transaction charges)
/// SEBI charges   : ₹10 per crore
/// Stamp charges  :
///   CNC buy        : 0.015%
///   MIS buy        : 0.003%
///   F&O buy        : 0.003%
/// </summary>
public class BrokerageCalculatorService : IBrokerageCalculatorService
{
    // ── Shared constants ──────────────────────────────────────────────────────
    private const decimal GstRate          = 0.18m;
    private const decimal SebiRatePerCrore = 10m;       // ₹10 per crore
    private const decimal OneCrore         = 10_000_000m;

    // ── NRI Non-PIS brokerage ─────────────────────────────────────────────────
    private const decimal NriBrokerageRate = 0.005m;    // 0.5%
    private const decimal NriMaxBrokerage  = 50m;       // ₹50 cap per order

    // ── STT rates ─────────────────────────────────────────────────────────────
    private const decimal CncSttRate       = 0.001m;    // 0.1% both sides
    private const decimal MisSttRate       = 0.00025m;  // 0.025% sell side only
    private const decimal FutSttRate       = 0.0002m;   // 0.02% sell side
    private const decimal OptSttRate       = 0.001m;    // 0.1% on premium, sell side

    // ── Transaction charges ───────────────────────────────────────────────────
    private const decimal EqTxnNse         = 0.0000307m;  // 0.00307% equity
    private const decimal FutTxnNse        = 0.0000183m;  // 0.00183% futures
    private const decimal OptTxnNse        = 0.0003553m;  // 0.03553% options premium
    private const decimal EqTxnBse         = 0.0000375m;  // 0.00375% BSE equity

    // ── Stamp charges ─────────────────────────────────────────────────────────
    private const decimal CncStampBuy      = 0.00015m;  // 0.015%
    private const decimal MisStampBuy      = 0.00003m;  // 0.003%
    private const decimal FnoStampBuy      = 0.00003m;  // 0.003%

    // ── Public ────────────────────────────────────────────────────────────────

    public BrokerageSummary Calculate(IEnumerable<KiteOrder> orders)
    {
        var summary = new BrokerageSummary();

        foreach (var order in orders.Where(o => o.Status == "COMPLETE" && o.FilledQuantity > 0))
        {
            var tradeValue = order.AveragePrice * order.FilledQuantity;
            var single = CalculateSingleTrade(
                order.Product, order.Exchange, tradeValue, order.TransactionType);

            summary.Brokerage          += single.Brokerage;
            summary.Stt                += single.Stt;
            summary.TransactionCharges += single.TransactionCharges;
            summary.SebiCharges        += single.SebiCharges;
            summary.StampCharges       += single.StampCharges;
            summary.Cgst               += single.Cgst;
            summary.Sgst               += single.Sgst;
            summary.Igst               += single.Igst;
        }

        return summary;
    }

    public BrokerageSummary CalculateSingleTrade(
        string product,
        string exchange,
        decimal tradeValue,
        string transactionType = "BUY")
    {
        var side = transactionType.ToUpper();
        var exch = exchange.ToUpper();

        return product.ToUpper() switch
        {
            "CNC"  => CalculateDelivery(tradeValue, side, exch),
            "MIS"  => exch == "NFO" || exch == "BFO"
                        ? CalculateFno(tradeValue, side, exch)
                        : CalculateIntraday(tradeValue, side, exch),
            "NRML" => CalculateFno(tradeValue, side, exch),
            _      => CalculateIntraday(tradeValue, side, exch)
        };
    }

    // ── Equity Delivery (CNC) ─────────────────────────────────────────────────

    private static BrokerageSummary CalculateDelivery(
        decimal tradeValue, string side, string exchange)
    {
        var r = new BrokerageSummary();

        // NRI Non-PIS: 0.5% or ₹50 whichever lower
        r.Brokerage = Math.Min(tradeValue * NriBrokerageRate, NriMaxBrokerage);
        r.Stt       = tradeValue * CncSttRate;   // both sides

        r.TransactionCharges = exchange == "BSE"
            ? tradeValue * EqTxnBse
            : tradeValue * EqTxnNse;

        r.SebiCharges  = tradeValue / OneCrore * SebiRatePerCrore;
        r.StampCharges = side == "BUY" ? tradeValue * CncStampBuy : 0m;
        var gstTotal = (r.Brokerage + r.SebiCharges + r.TransactionCharges) * GstRate;
        r.Cgst = Math.Round(gstTotal / 2, 2);
        r.Sgst = Math.Round(gstTotal / 2, 2);
        r.Igst = 0m;

        return r;
    }

    // ── Equity Intraday (MIS on NSE/BSE) ─────────────────────────────────────

    private static BrokerageSummary CalculateIntraday(
        decimal tradeValue, string side, string exchange)
    {
        var r = new BrokerageSummary();

        r.Brokerage = Math.Min(tradeValue * NriBrokerageRate, NriMaxBrokerage);
        r.Stt       = side == "SELL" ? tradeValue * MisSttRate : 0m;

        r.TransactionCharges = exchange == "BSE"
            ? tradeValue * EqTxnBse
            : tradeValue * EqTxnNse;

        r.SebiCharges  = tradeValue / OneCrore * SebiRatePerCrore;
        r.StampCharges = side == "BUY" ? tradeValue * MisStampBuy : 0m;

        var gstTotal = (r.Brokerage + r.SebiCharges + r.TransactionCharges) * GstRate;
        r.Cgst = Math.Round(gstTotal / 2, 2);
        r.Sgst = Math.Round(gstTotal / 2, 2);
        r.Igst = 0m;

        return r;
    }

    // ── F&O (MIS/NRML on NFO/BFO) ────────────────────────────────────────────

    private static BrokerageSummary CalculateFno(
        decimal tradeValue, string side, string exchange)
    {
        var r = new BrokerageSummary();

        r.Brokerage = Math.Min(tradeValue * NriBrokerageRate, NriMaxBrokerage);
        r.Stt = side == "SELL" ? tradeValue * OptSttRate : 0m;

        r.TransactionCharges = exchange == "BFO"
            ? tradeValue * 0.000325m
            : tradeValue * OptTxnNse;

        r.SebiCharges  = tradeValue / OneCrore * SebiRatePerCrore;
        r.StampCharges = side == "BUY" ? tradeValue * FnoStampBuy : 0m;

        var gstTotal = (r.Brokerage + r.SebiCharges + r.TransactionCharges) * GstRate;
        r.Cgst = Math.Round(gstTotal / 2, 2);
        r.Sgst = Math.Round(gstTotal / 2, 2);
        r.Igst = 0m;

        return r;
    }
}
