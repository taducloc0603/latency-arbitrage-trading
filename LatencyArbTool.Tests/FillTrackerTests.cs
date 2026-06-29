using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class FillTrackerTests
{
    [Fact]
    public void OpenFill_MatchesNewTicket_ComputesSlippage()
    {
        var ft = new FillTracker();
        ft.Observe(Trades(), Histories(), 0, 0); // seed (no pre-existing tickets)
        ft.RecordOpenClick(new ClickContext(
            DecideTimeMs: 1000, DecideGap: 120, DecidePrice: 4100.25,
            Side: DryRunSide.BuyB, ClusterId: 1, Decision: "live open"));

        var fills = ft.Observe(Trades(Trade(50, TradeSide.Buy, 4100.30, 1330)), Histories(), gapBuy: 112, gapSell: 0);

        var f = Assert.Single(fills);
        Assert.False(f.IsClose);
        Assert.Equal(50ul, f.Ticket);
        Assert.Equal(4100.30, f.FillPrice, 5);
        Assert.Equal(0.05, f.SlippagePrice, 5);
        Assert.Equal(330, f.SlippageMs);
        Assert.Equal(112, f.FillObservedGap);
    }

    [Fact]
    public void CloseFill_MatchesDisappearedTicket_ReadsHistory()
    {
        var ft = new FillTracker();
        ft.Observe(Trades(Trade(50, TradeSide.Buy, 4100.30, 1330)), Histories(), 0, 0); // seed with the open ticket
        ft.RecordCloseClick(50, new ClickContext(2000, 120, 4102.40, DryRunSide.BuyB, 1, "live close"));

        var fills = ft.Observe(
            Trades(),
            Histories(Hist(50, TradeSide.Buy, 4102.45, profit: 17.6, comm: -1.2, closeMsc: 2410)),
            0, 0);

        var f = Assert.Single(fills);
        Assert.True(f.IsClose);
        Assert.Equal(4102.45, f.FillPrice, 5);
        Assert.Equal(17.6, f.RealizedUsd, 5);
        Assert.Equal(-1.2, f.Commission, 5);
        Assert.Equal(410, f.SlippageMs);
    }

    private static TradeRecord Trade(ulong ticket, TradeSide side, double price, ulong timeMsc) =>
        new(ticket, side, 1.0, price, 0, 0, 0, timeMsc, 0, "XAUUSD");

    private static HistoryRecord Hist(ulong ticket, TradeSide side, double closePrice, double profit, double comm, ulong closeMsc) =>
        new(ticket, side, 1.0, 0, closePrice, 0, 0, comm, profit, 0, closeMsc, 0, "XAUUSD");

    private static TradeReadResult Trades(params TradeRecord[] t) => TradeReadResult.Ok("m", 0, t);

    private static HistoryReadResult Histories(params HistoryRecord[] h) => HistoryReadResult.Ok("m", 0, h);
}
