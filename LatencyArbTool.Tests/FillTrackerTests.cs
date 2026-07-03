using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class FillTrackerTests
{
    [Fact]
    public void OpenFill_MatchesNewTicket_ComputesSlippage()
    {
        var ft = new FillTracker();
        ft.Observe(Trades(), Histories(), 0, 0, nowMs: 1000); // seed (no pre-existing tickets)
        ft.RecordOpenClick(new ClickContext(
            DecideTimeMs: 1000, DecideTickCount: 1000, DecideGap: 120, DecidePrice: 4100.25,
            Side: DryRunSide.BuyB, ClusterId: 1, Decision: "live open"));

        var fills = ft.Observe(Trades(Trade(50, TradeSide.Buy, 4100.30, 1330)), Histories(), gapBuy: 112, gapSell: 0, nowMs: 1330);

        var f = Assert.Single(fills);
        Assert.False(f.IsClose);
        Assert.Equal(50ul, f.Ticket);
        Assert.Equal(4100.30, f.FillPrice, 5);
        Assert.Equal(0.05, f.SlippagePrice, 5);
        Assert.Equal(330, f.SlippageMs);
        Assert.Equal(112, f.FillObservedGap);
    }

    [Fact]
    public void OpenClick_ExpiresAfterTtl_NoStaleMatch()
    {
        var ft = new FillTracker();
        ft.Observe(Trades(), Histories(), 0, 0, nowMs: 1000); // seed
        ft.RecordOpenClick(new ClickContext(1000, 1000, 120, 4100.25, DryRunSide.BuyB, 1, "live open"));

        // A ticket appearing days later must not be matched to the ancient click.
        var muchLater = 1000 + 200_000;
        var fills = ft.Observe(Trades(Trade(50, TradeSide.Buy, 4100.30, 1330)), Histories(), 0, 0, nowMs: muchLater);

        Assert.Empty(fills);
    }

    [Fact]
    public void CloseFill_MatchesDisappearedTicket_ReadsHistory()
    {
        var ft = new FillTracker();
        ft.Observe(Trades(Trade(50, TradeSide.Buy, 4100.30, 1330)), Histories(), 0, 0, nowMs: 1500); // seed with the open ticket
        ft.RecordCloseClick(50, new ClickContext(2000, 2000, 120, 4102.40, DryRunSide.BuyB, 1, "live close"));

        var fills = ft.Observe(
            Trades(),
            Histories(Hist(50, TradeSide.Buy, 4102.45, profit: 17.6, comm: -1.2, closeMsc: 2410)),
            0, 0, nowMs: 2410);

        var f = Assert.Single(fills);
        Assert.True(f.IsClose);
        Assert.Equal(4102.45, f.FillPrice, 5);
        Assert.Equal(17.6, f.RealizedUsd, 5);
        Assert.Equal(-1.2, f.Commission, 5);
        Assert.Equal(410, f.SlippageMs);
    }

    [Fact]
    public void CloseFill_WaitsForLaggingHistoryRecord()
    {
        var ft = new FillTracker();
        ft.Observe(Trades(Trade(50, TradeSide.Buy, 4100.30, 1330)), Histories(), 0, 0, nowMs: 1500);
        ft.RecordCloseClick(50, new ClickContext(2000, 2000, 120, 4102.40, DryRunSide.BuyB, 1, "live close"));

        // Ticket disappeared but the history map has not caught up yet: no event.
        Assert.Empty(ft.Observe(Trades(), Histories(), 0, 0, nowMs: 2400));
        Assert.Empty(ft.Observe(Trades(), Histories(), 0, 0, nowMs: 3000));

        // History record arrives a few polls later -> full close data.
        var fills = ft.Observe(
            Trades(),
            Histories(Hist(50, TradeSide.Buy, 4102.45, profit: 17.6, comm: -1.2, closeMsc: 2410)),
            0, 0, nowMs: 3500);

        var f = Assert.Single(fills);
        Assert.True(f.IsClose);
        Assert.Equal(4102.45, f.FillPrice, 5);
        Assert.Equal(17.6, f.RealizedUsd, 5);
    }

    [Fact]
    public void CloseFill_GraceTimeout_EmitsZeroPricedClose()
    {
        var ft = new FillTracker();
        ft.Observe(Trades(Trade(50, TradeSide.Buy, 4100.30, 1330)), Histories(), 0, 0, nowMs: 1500);
        ft.RecordCloseClick(50, new ClickContext(2000, 2000, 120, 4102.40, DryRunSide.BuyB, 1, "live close"));

        Assert.Empty(ft.Observe(Trades(), Histories(), 0, 0, nowMs: 2400));

        // Past the grace window with still no history record: give up with zeros
        // so the close click is at least recorded.
        var fills = ft.Observe(Trades(), Histories(), 0, 0, nowMs: 2400 + 6_000);

        var f = Assert.Single(fills);
        Assert.True(f.IsClose);
        Assert.Equal(0, f.FillPrice, 5);
        Assert.Equal(0, f.RealizedUsd, 5);
    }

    private static TradeRecord Trade(ulong ticket, TradeSide side, double price, ulong timeMsc) =>
        new(ticket, side, 1.0, price, 0, 0, 0, timeMsc, timeMsc, "XAUUSD");

    private static HistoryRecord Hist(ulong ticket, TradeSide side, double closePrice, double profit, double comm, ulong closeMsc) =>
        new(ticket, side, 1.0, 0, closePrice, 0, 0, comm, profit, 0, closeMsc, closeMsc, "XAUUSD");

    private static TradeReadResult Trades(params TradeRecord[] t) => TradeReadResult.Ok("m", 0, t);

    private static HistoryReadResult Histories(params HistoryRecord[] h) => HistoryReadResult.Ok("m", 0, h);
}
