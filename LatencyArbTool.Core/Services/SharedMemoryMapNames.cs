namespace LatencyArbTool.Core.Services;

public static class SharedMemoryMapNames
{
    public static string TradeFromTick(string tickMapName)
    {
        return ReplaceTickSuffix(tickMapName, "Trade");
    }

    public static string TradesFallbackFromTick(string tickMapName)
    {
        return ReplaceTickSuffix(tickMapName, "Trades");
    }

    public static string HistoryFromTick(string tickMapName)
    {
        return ReplaceTickSuffix(tickMapName, "History");
    }

    private static string ReplaceTickSuffix(string tickMapName, string suffix)
    {
        if (string.IsNullOrWhiteSpace(tickMapName))
        {
            return string.Empty;
        }

        return tickMapName.EndsWith("Tick", StringComparison.OrdinalIgnoreCase)
            ? string.Concat(tickMapName.AsSpan(0, tickMapName.Length - "Tick".Length), suffix)
            : $"{tickMapName}_{suffix}";
    }
}
