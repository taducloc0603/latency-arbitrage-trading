namespace LatencyArbTool.Core.Services;

public interface IMt5TradeGateway
{
    bool IsAvailable(out string error);
    bool IsValidWindow(ulong hwnd, out string error);
    bool ClickBuy(ulong chartHwnd, out string error);
    bool ClickSell(ulong chartHwnd, out string error);
    bool EnsureContextFromParent(ulong parentHwnd, out string error);
    bool ClosePositionMt5(int rowIndex, out string error);
}

