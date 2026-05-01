using System.Runtime.InteropServices;

namespace LatencyArbTool.App.Services;

internal static class Mt5Native
{
    private const string DllName = "mt5engine-capi.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mt_is_valid_window(ulong hwnd);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong mt_find_list_view(ulong parentHwnd);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mt_create_context(ulong listViewHwnd);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mt_create_context_from_parent(ulong parentHwnd);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mt_update_row_count(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mt_close_position_mt5(IntPtr ctx, int rowIdx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mt_close_position_mt4(IntPtr ctx, int rowIdx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mt_destroy_context(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mt_click_buy(ulong chartHwnd);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mt_click_sell(ulong chartHwnd);
}

