using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LatencyArbTool.Core.Models;

namespace LatencyArbTool.Core.Services;

// Loads a StrategyConfig row from Supabase via its PostgREST endpoint. Picks the
// most recent active row whose `hostname` matches the running machine.
public sealed class SupabaseConfigRepository
{
    private readonly HttpClient _http;
    private readonly string _restBase; // e.g. https://xxxx.supabase.co/rest/v1
    private readonly string _table;

    public SupabaseConfigRepository(string supabaseUrl, string apiKey, HttpClient? http = null, string table = "configs")
    {
        if (string.IsNullOrWhiteSpace(supabaseUrl))
            throw new ArgumentException("supabaseUrl is required", nameof(supabaseUrl));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("apiKey is required", nameof(apiKey));

        _restBase = supabaseUrl.TrimEnd('/') + "/rest/v1";
        _table = table;
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.Remove("apikey");
        _http.DefaultRequestHeaders.Add("apikey", apiKey);
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<StrategyConfig?> LoadForHostAsync(string hostname, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(hostname);
        var url = $"{_restBase}/{_table}" +
                  $"?hostname=eq.{encoded}&is_active=eq.true&order=created_at.desc&limit=1";

        var rows = await _http.GetFromJsonAsync<List<ConfigRow>>(url, JsonOpts, ct).ConfigureAwait(false);
        var row = rows is { Count: > 0 } ? rows[0] : null;
        return row is null ? null : Map(row);
    }

    private static StrategyConfig Map(ConfigRow r) => new(
        GroupName: r.GroupName ?? string.Empty,
        Hostname: r.Hostname ?? string.Empty,
        Point: r.Point,
        OpenPts: r.OpenPts,
        OpenHoldConfirmMs: r.OpenHoldConfirmMs,
        OpenConfirmGapPts: r.OpenConfirmGapPts,
        StopLossPoint: r.StopLossPoint,
        TrailingStartPoint: r.TrailingStartPoint,
        TrailingStepPoint: r.TrailingStepPoint,
        MapA: string.IsNullOrWhiteSpace(r.MapA) ? StrategyConfig.Default.MapA : r.MapA!,
        MapB: string.IsNullOrWhiteSpace(r.MapB) ? StrategyConfig.Default.MapB : r.MapB!,
        ChartHwndB: r.ChartHwndB,
        TradeHwndB: r.TradeHwndB);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private sealed class ConfigRow
    {
        [JsonPropertyName("group_name")] public string? GroupName { get; set; }
        [JsonPropertyName("hostname")] public string? Hostname { get; set; }
        [JsonPropertyName("point")] public int Point { get; set; }
        [JsonPropertyName("open_pts")] public int OpenPts { get; set; }
        [JsonPropertyName("open_hold_confirm_ms")] public int OpenHoldConfirmMs { get; set; }
        [JsonPropertyName("open_confirm_gap_pts")] public int OpenConfirmGapPts { get; set; }
        [JsonPropertyName("stop_loss_point")] public int StopLossPoint { get; set; }
        [JsonPropertyName("trailing_start_point")] public int TrailingStartPoint { get; set; }
        [JsonPropertyName("trailing_step_point")] public int TrailingStepPoint { get; set; }
        [JsonPropertyName("map_a")] public string? MapA { get; set; }
        [JsonPropertyName("map_b")] public string? MapB { get; set; }
        [JsonPropertyName("chart_hwnd_b")] public string? ChartHwndB { get; set; }
        [JsonPropertyName("trade_hwnd_b")] public string? TradeHwndB { get; set; }
    }
}
