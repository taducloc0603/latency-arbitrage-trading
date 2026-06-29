using System.Net;
using System.Net.Http;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.Tests;

public sealed class SupabaseConfigRepositoryTests
{
    [Fact]
    public async Task LoadForHost_QueriesByHostnameAndMapsRow()
    {
        const string json = """
        [{
          "id": "6a620715-941d-491b-b09a-e3df78f5d7e2",
          "group_name": "LAP 2",
          "hostname": "desktop-ndpzoz8",
          "point": 100,
          "open_pts": 80,
          "open_hold_confirm_ms": 1000,
          "open_confirm_gap_pts": 30,
          "stop_loss_point": 50,
          "trailing_start_point": 200,
          "trailing_step_point": 30,
          "map_a": "Local\\MT_A_Tick",
          "map_b": "Local\\MT_B_Tick",
          "chart_hwnd_b": "0x00180070",
          "trade_hwnd_b": "0x0085089A"
        }]
        """;

        var handler = new StubHandler(json);
        var http = new HttpClient(handler);
        var repo = new SupabaseConfigRepository("https://proj.supabase.co", "anon-key", http);

        var config = await repo.LoadForHostAsync("desktop-ndpzoz8");

        Assert.NotNull(config);
        Assert.Equal("LAP 2", config!.GroupName);
        Assert.Equal(100, config.Point);
        Assert.Equal(80, config.OpenPts);
        Assert.Equal(1000, config.OpenHoldConfirmMs);
        Assert.Equal(30, config.OpenConfirmGapPts);
        Assert.Equal(50, config.StopLossPoint);
        Assert.Equal(200, config.TrailingStartPoint);
        Assert.Equal(30, config.TrailingStepPoint);
        Assert.Equal("0x00180070", config.ChartHwndB);

        Assert.Contains("hostname=eq.desktop-ndpzoz8", handler.LastUrl);
        Assert.Contains("is_active=eq.true", handler.LastUrl);
        Assert.Contains("order=created_at.desc", handler.LastUrl);
        Assert.Contains("limit=1", handler.LastUrl);
    }

    [Fact]
    public async Task LoadForHost_NoRow_ReturnsNull()
    {
        var http = new HttpClient(new StubHandler("[]"));
        var repo = new SupabaseConfigRepository("https://proj.supabase.co", "anon-key", http);

        var config = await repo.LoadForHostAsync("unknown-host");

        Assert.Null(config);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public string LastUrl { get; private set; } = string.Empty;

        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
