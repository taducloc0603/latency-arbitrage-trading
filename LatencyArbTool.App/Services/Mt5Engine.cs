using System.Runtime.InteropServices;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.App.Services;

public sealed class Mt5Engine : IMt5TradeGateway, IDisposable
{
    private IntPtr _ctx;

    public bool IsAvailable(out string error)
    {
        error = string.Empty;
        try
        {
            _ = Mt5Native.mt_is_valid_window(0);
            return true;
        }
        catch (Exception ex) when (IsNativeLoadException(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    public bool IsValidWindow(ulong hwnd, out string error)
    {
        error = string.Empty;
        try
        {
            if (Mt5Native.mt_is_valid_window(hwnd) == 1)
            {
                return true;
            }

            error = $"window 0x{hwnd:X} is not valid";
            return false;
        }
        catch (Exception ex) when (IsNativeLoadException(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    public bool CreateContextFromParent(ulong parentHwnd)
    {
        DisposeContext();
        _ctx = Mt5Native.mt_create_context_from_parent(parentHwnd);
        return _ctx != IntPtr.Zero;
    }

    public bool CreateContext(ulong listViewHwnd)
    {
        DisposeContext();
        _ctx = Mt5Native.mt_create_context(listViewHwnd);
        return _ctx != IntPtr.Zero;
    }

    public bool EnsureContextFromParent(ulong parentHwnd, out string error)
    {
        error = string.Empty;
        try
        {
            if (_ctx != IntPtr.Zero)
            {
                return true;
            }

            if (CreateContextFromParent(parentHwnd))
            {
                return true;
            }

            error = $"could not create context from parent HWND 0x{parentHwnd:X}";
            return false;
        }
        catch (Exception ex) when (IsNativeLoadException(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    public int UpdateRowCount()
    {
        return _ctx == IntPtr.Zero ? 0 : Mt5Native.mt_update_row_count(_ctx);
    }

    public bool ClosePosition(int rowIdx)
    {
        return _ctx != IntPtr.Zero && Mt5Native.mt_close_position_mt5(_ctx, rowIdx) == 1;
    }

    public bool ClosePositionMt5(int rowIndex, out string error)
    {
        error = string.Empty;
        try
        {
            if (_ctx == IntPtr.Zero)
            {
                error = "context is not initialized";
                return false;
            }

            if (Mt5Native.mt_close_position_mt5(_ctx, rowIndex) == 1)
            {
                return true;
            }

            error = $"native close row {rowIndex} returned 0";
            return false;
        }
        catch (Exception ex) when (IsNativeLoadException(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    public bool ClickBuy(ulong chartHwnd)
    {
        return Mt5Native.mt_click_buy(chartHwnd) == 1;
    }

    public bool ClickBuy(ulong chartHwnd, out string error)
    {
        error = string.Empty;
        try
        {
            if (ClickBuy(chartHwnd))
            {
                return true;
            }

            error = $"native click buy returned 0 for HWND 0x{chartHwnd:X}";
            return false;
        }
        catch (Exception ex) when (IsNativeLoadException(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    public bool ClickSell(ulong chartHwnd)
    {
        return Mt5Native.mt_click_sell(chartHwnd) == 1;
    }

    public bool ClickSell(ulong chartHwnd, out string error)
    {
        error = string.Empty;
        try
        {
            if (ClickSell(chartHwnd))
            {
                return true;
            }

            error = $"native click sell returned 0 for HWND 0x{chartHwnd:X}";
            return false;
        }
        catch (Exception ex) when (IsNativeLoadException(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        DisposeContext();
    }

    private void DisposeContext()
    {
        if (_ctx == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Mt5Native.mt_destroy_context(_ctx);
        }
        catch (Exception ex) when (IsNativeLoadException(ex))
        {
            // The process is exiting or the DLL is unavailable; there is no useful recovery here.
        }
        finally
        {
            _ctx = IntPtr.Zero;
        }
    }

    private static bool IsNativeLoadException(Exception ex)
    {
        return ex is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or SEHException;
    }
}
