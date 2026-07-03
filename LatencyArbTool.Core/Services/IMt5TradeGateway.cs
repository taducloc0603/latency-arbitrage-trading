namespace LatencyArbTool.Core.Services;

public interface IMt5TradeGateway
{
    bool IsAvailable(out string error);
    bool IsValidWindow(ulong hwnd, out string error);
    bool ClickBuy(ulong chartHwnd, out string error);
    bool ClickSell(ulong chartHwnd, out string error);
    bool EnsureContextFromParent(ulong parentHwnd, out string error);

    // Drop the cached context and build a fresh one from the parent HWND. Used
    // when a close fails: the cached context may point at a dead window (e.g.
    // MT5 was restarted).
    bool RecreateContextFromParent(ulong parentHwnd, out string error);

    bool ClosePositionMt5(int rowIndex, out string error);
}
