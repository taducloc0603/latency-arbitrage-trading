using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows.Threading;
using LatencyArbTool.App.Services;
using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private static readonly HttpClient SharedHttp = new();

    private readonly SharedMemoryTickReader _reader = new();
    private readonly SharedMemoryTradeReader _tradeReader = new();
    private readonly SharedMemoryHistoryReader _historyReader = new();
    private readonly Mt5Engine _mt5Engine = new();
    private readonly Mt5TradeExecutor _tradeExecutor;
    private readonly OpenSignalEngine _signalEngine = new();
    private readonly TrailingStopEngine _trailingEngine = new();
    private readonly FillTracker _fillTracker = new();
    private readonly DispatcherTimer _timer;
    private CsvLogger? _csvLogger;

    private StrategyConfig? _config;
    private bool _isRunning;

    private string _chartHwndText = string.Empty;
    private string _tradeHwndText = string.Empty;
    private string _mapNameA = StrategyConfig.Default.MapA;
    private string _mapNameB = StrategyConfig.Default.MapB;
    private string _configStatus = "No config loaded. Click Load Config.";
    private string _configSummary = "-";
    private string _statusA = "Disconnected";
    private string _statusB = "Disconnected";
    private string _statusBTrade = "Disconnected";
    private string _statusBHistory = "Disconnected";
    private int _lastHistoryCount = -1;
    private string _symbolA = "-";
    private string _symbolB = "-";
    private string _bidA = "-";
    private string _askA = "-";
    private string _spreadA = "-";
    private string _latencyA = "-";
    private string _bidB = "-";
    private string _askB = "-";
    private string _spreadB = "-";
    private string _latencyB = "-";
    private int _gapBuy;
    private int _gapSell;
    private string _positionSide = "Flat";
    private string _entryPoint = "-";
    private string _currentPoint = "-";
    private string _trailingState = "-";
    private string _liveStatus = "Idle";
    private string _hwndStatus = "-";

    // Captured at open so the matching close row can log the position's entry context.
    private int? _openGapAtOpen;
    private int? _openEntryPoint;

    public MainViewModel()
    {
        _tradeExecutor = new Mt5TradeExecutor(_mt5Engine);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        _timer.Tick += (_, _) => Poll();

        LoadConfigCommand = new RelayCommand(() => _ = LoadConfigAsync(), () => !IsRunning);
        CheckMapsCommand = new RelayCommand(CheckMaps);
        CheckHwndCommand = new RelayCommand(CheckHwnd);
        StartCommand = new RelayCommand(Start, () => !IsRunning && _config is not null);
        StopCommand = new RelayCommand(Stop, () => IsRunning);
    }

    public string MapNameA { get => _mapNameA; set => SetProperty(ref _mapNameA, value); }
    public string MapNameB { get => _mapNameB; set => SetProperty(ref _mapNameB, value); }
    public string ChartHwndText { get => _chartHwndText; set => SetProperty(ref _chartHwndText, value); }
    public string TradeHwndText { get => _tradeHwndText; set => SetProperty(ref _tradeHwndText, value); }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                LoadConfigCommand.RaiseCanExecuteChanged();
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ConfigStatus { get => _configStatus; private set => SetProperty(ref _configStatus, value); }
    public string ConfigSummary { get => _configSummary; private set => SetProperty(ref _configSummary, value); }
    public string StatusA { get => _statusA; private set => SetProperty(ref _statusA, value); }
    public string StatusB { get => _statusB; private set => SetProperty(ref _statusB, value); }
    public string StatusBTrade { get => _statusBTrade; private set => SetProperty(ref _statusBTrade, value); }
    public string StatusBHistory { get => _statusBHistory; private set => SetProperty(ref _statusBHistory, value); }
    public string SymbolA { get => _symbolA; private set => SetProperty(ref _symbolA, value); }
    public string SymbolB { get => _symbolB; private set => SetProperty(ref _symbolB, value); }
    public string BidA { get => _bidA; private set => SetProperty(ref _bidA, value); }
    public string AskA { get => _askA; private set => SetProperty(ref _askA, value); }
    public string SpreadA { get => _spreadA; private set => SetProperty(ref _spreadA, value); }
    public string LatencyA { get => _latencyA; private set => SetProperty(ref _latencyA, value); }
    public string BidB { get => _bidB; private set => SetProperty(ref _bidB, value); }
    public string AskB { get => _askB; private set => SetProperty(ref _askB, value); }
    public string SpreadB { get => _spreadB; private set => SetProperty(ref _spreadB, value); }
    public string LatencyB { get => _latencyB; private set => SetProperty(ref _latencyB, value); }
    public int GapBuy { get => _gapBuy; private set => SetProperty(ref _gapBuy, value); }
    public int GapSell { get => _gapSell; private set => SetProperty(ref _gapSell, value); }
    public string PositionSide { get => _positionSide; private set => SetProperty(ref _positionSide, value); }
    public string EntryPoint { get => _entryPoint; private set => SetProperty(ref _entryPoint, value); }
    public string CurrentPoint { get => _currentPoint; private set => SetProperty(ref _currentPoint, value); }
    public string TrailingState { get => _trailingState; private set => SetProperty(ref _trailingState, value); }
    public string LiveStatus { get => _liveStatus; private set => SetProperty(ref _liveStatus, value); }
    public string HwndStatus { get => _hwndStatus; private set => SetProperty(ref _hwndStatus, value); }

    public ObservableCollection<string> Logs { get; } = [];
    public ObservableCollection<BTradeRow> BTrades { get; } = [];
    public ObservableCollection<BHistoryRow> BHistory { get; } = [];
    public RelayCommand LoadConfigCommand { get; }
    public RelayCommand CheckMapsCommand { get; }
    public RelayCommand CheckHwndCommand { get; }
    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }

    private async Task LoadConfigAsync()
    {
        var hostname = Environment.MachineName;
        ConfigStatus = $"Loading config for {hostname}...";
        try
        {
            var url = Environment.GetEnvironmentVariable("SUPABASE_URL");
            var key = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY");
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            {
                ConfigStatus = "Missing SUPABASE_URL / SUPABASE_ANON_KEY env vars.";
                return;
            }

            var repo = new SupabaseConfigRepository(url, key, SharedHttp);
            var config = await repo.LoadForHostAsync(hostname).ConfigureAwait(true);
            if (config is null)
            {
                ConfigStatus = $"No active config row for hostname '{hostname}'.";
                return;
            }

            _config = config;
            MapNameA = config.MapA;
            MapNameB = config.MapB;
            ChartHwndText = config.ChartHwndB ?? string.Empty;
            TradeHwndText = config.TradeHwndB ?? string.Empty;
            ConfigStatus = $"Loaded '{config.GroupName}' for {hostname}.";
            ConfigSummary =
                $"point={config.Point}  x(open)={config.OpenPts}  y(ms)={config.OpenHoldConfirmMs}  z(sustain)={config.OpenConfirmGapPts}  " +
                $"SL={config.StopLossPoint}  trailStart={config.TrailingStartPoint}  trailStep={config.TrailingStepPoint}";
            StartCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            ConfigStatus = $"Config load failed: {ex.Message}";
        }
    }

    private void CheckMaps()
    {
        StatusA = _reader.MapExists(MapNameA) ? "Connected" : "Disconnected";
        StatusB = _reader.MapExists(MapNameB) ? "Connected" : "Disconnected";
        StatusBTrade = _tradeReader.MapExistsForTickMap(MapNameB) ? "Connected" : "Disconnected";
        AddLog($"map check: A={StatusA}, B={StatusB}, BTrade={StatusBTrade}");
    }

    private void CheckHwnd()
    {
        var chart = ValidateHwnd(ChartHwndText, "Chart");
        var trade = ValidateHwnd(TradeHwndText, "Trade");
        HwndStatus = $"{chart}; {trade}";
        AddLog($"hwnd check: {HwndStatus}");
    }

    private string ValidateHwnd(string text, string label)
    {
        if (!HwndParser.TryParse(text, out var hwnd, out var parseError))
        {
            return $"{label} invalid: {parseError}";
        }

        return _mt5Engine.IsValidWindow(hwnd, out var error)
            ? $"{label} 0x{hwnd:X} OK"
            : $"{label} 0x{hwnd:X} NOT found: {error}";
    }

    private void Start()
    {
        if (_config is null)
        {
            AddLog("cannot start: no config loaded");
            return;
        }

        _csvLogger?.Dispose();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var logsDirectory = Path.Combine(desktop, "arb-log");
        _csvLogger = new CsvLogger(logsDirectory);

        _signalEngine.Reset();
        _fillTracker.Reset();
        IsRunning = true;
        _timer.Start();
        AddLog($"start; logs at {logsDirectory}");
    }

    private void Stop()
    {
        _timer.Stop();
        _csvLogger?.Flush();
        IsRunning = false;
        AddLog("stop");
    }

    private void Poll()
    {
        if (_config is not { } config)
        {
            return;
        }

        var tickA = _reader.TryRead(MapNameA);
        var tickB = _reader.TryRead(MapNameB);
        StatusA = tickA.Success ? "Connected" : $"Disconnected: {tickA.Error}";
        StatusB = tickB.Success ? "Connected" : $"Disconnected: {tickB.Error}";

        var bTrades = _tradeReader.TryReadForTickMap(MapNameB);
        var bHistory = _historyReader.TryReadForTickMap(MapNameB);
        UpdateBTradesUi(bTrades);
        UpdateBHistoryUi(bHistory);

        if (tickA.Tick is null || tickB.Tick is null)
        {
            return;
        }

        var a = tickA.Tick;
        var b = tickB.Tick;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var (gapBuy, gapSell) = GapCalculator.Calculate(a, b, config.Point);

        // Signal only opens; it is ignored by the engine while a position is held.
        var signal = _signalEngine.Evaluate(gapBuy, gapSell, nowMs, config);
        var events = _trailingEngine.Step(b.Bid, b.Ask, signal, nowMs, config);

        UpdateMarketUi(a, b, gapBuy, gapSell, config);

        foreach (var e in events)
        {
            int gapAtOpen;
            int entryPoint;
            SignalWindow? window = null;
            if (e.Decision == "live open")
            {
                gapAtOpen = e.Side == DryRunSide.BuyB ? gapBuy : gapSell;
                entryPoint = GapCalculator.ToPoints(e.OpenPrice, config.Point);
                window = _signalEngine.LastWindow;
                _openGapAtOpen = gapAtOpen;
                _openEntryPoint = entryPoint;
            }
            else if (e.Decision == "live close")
            {
                gapAtOpen = _openGapAtOpen ?? 0;
                entryPoint = _openEntryPoint ?? 0;
                _openGapAtOpen = null;
                _openEntryPoint = null;
            }
            else
            {
                gapAtOpen = 0;
                entryPoint = 0;
            }

            AddLog(DescribeEvent(e, gapBuy, gapSell, window));
            _csvLogger?.LogEvent(e, gapAtOpen, entryPoint, window);

            var result = ExecuteLive(e, bTrades);
            if (result.Success)
            {
                var side = e.Side ?? DryRunSide.BuyB;
                if (e.Decision == "live open")
                {
                    _fillTracker.RecordOpenClick(new ClickContext(nowMs, gapAtOpen, e.OpenPrice, side, e.ClusterId, "live open"));
                }
                else if (e.Decision == "live close" && bTrades.Success && bTrades.Trades.Count > 0)
                {
                    _fillTracker.RecordCloseClick(bTrades.Trades[0].Ticket,
                        new ClickContext(nowMs, gapAtOpen, e.ClosePrice, side, e.ClusterId, "live close"));
                }
            }

            // A fresh open consumes the signal: reset so re-entry requires a new
            // confirm window rather than firing every tick the gap stays extreme.
            if (e.Decision == "live open")
            {
                _signalEngine.Reset();
            }
        }

        // Observe broker fills every tick (slippage arrives a few ticks after the
        // click) — log/recheck only, never feeds the strategy.
        foreach (var fill in _fillTracker.Observe(bTrades, bHistory, gapBuy, gapSell))
        {
            AddLog(DescribeFill(fill, config.Point));
            _csvLogger?.LogFill(fill);
        }
    }

    private static string DescribeEvent(DryRunEvent e, int gapBuy, int gapSell, SignalWindow? window)
    {
        var side = e.Side?.ToString() ?? string.Empty;
        return e.Decision switch
        {
            "live open" =>
                $"live open {side} entry={F(e.OpenPrice)} gap={(e.Side == DryRunSide.BuyB ? gapBuy : gapSell)}{DescribeWindow(window)}",
            "live close" =>
                $"live close {side} {e.Reason} exit={F(e.ClosePrice)} " +
                $"pnl={Signed((int)e.PnlRaw)}pt " +
                $"hold={e.HoldMs / 1000.0:0.0}s",
            _ => $"{e.Decision}: {e.Reason}",
        };
    }

    private static string DescribeWindow(SignalWindow? w)
    {
        if (w is null || w.Count == 0)
        {
            return string.Empty;
        }

        return $" win[n={w.Count} min={w.Min} max={w.Max} first={w.First} last={w.Last} " +
               $"avg={w.Avg.ToString("0", CultureInfo.InvariantCulture)} z={w.Z} x={w.X} dur={w.DurationMs}ms]";
    }

    private static string DescribeFill(FillEvent f, int point)
    {
        var slipPt = GapCalculator.ToPoints(f.SlippagePrice, point);
        if (f.IsClose)
        {
            return $"fill close #{f.Ticket} closePrice={F(f.FillPrice)} " +
                   $"realizedUsd={f.RealizedUsd.ToString("0.##", CultureInfo.InvariantCulture)} " +
                   $"comm={f.Commission.ToString("0.##", CultureInfo.InvariantCulture)} " +
                   $"slip={Signed(slipPt)}pt latency={f.SlippageMs}ms";
        }

        var gapDrift = f.DecideGap - f.FillObservedGap;
        return $"fill open #{f.Ticket} fillPrice={F(f.FillPrice)} " +
               $"slip={Signed(slipPt)}pt gapDrift={Signed(gapDrift)} latency={f.SlippageMs}ms";
    }

    private static string Signed(int value) => value.ToString("+0;-0;0", CultureInfo.InvariantCulture);

    // Display-only latency: the EA stamps ea_ms from Windows GetTickCount64 (same
    // clock as Environment.TickCount64). Valid range 0..24h, else "unknown". Not
    // used by the strategy.
    private static string FormatLatency(long nowTickCountMs, long eaTickCountMs)
    {
        var latency = nowTickCountMs - eaTickCountMs;
        return latency is >= 0 and <= 86_400_000 ? $"{latency} ms" : "unknown";
    }

    private LiveTradeResult ExecuteLive(DryRunEvent e, TradeReadResult bTrades)
    {
        var result = _tradeExecutor.Execute(e, ChartHwndText, TradeHwndText, bTrades);
        if (result.Attempted)
        {
            var prefix = result.Success ? "live ok" : "live failed";
            LiveStatus = $"{prefix}: {result.Message}";
            AddLog($"{prefix}: {result.Message}");
        }

        return result;
    }

    private void UpdateMarketUi(TickRecord a, TickRecord b, int gapBuy, int gapSell, StrategyConfig config)
    {
        var nowTickCountMs = Environment.TickCount64;
        SymbolA = a.Symbol;
        BidA = F(a.Bid);
        AskA = F(a.Ask);
        SpreadA = F(a.Spread);
        LatencyA = FormatLatency(nowTickCountMs, a.EaTickCountMs);
        SymbolB = b.Symbol;
        BidB = F(b.Bid);
        AskB = F(b.Ask);
        SpreadB = F(b.Spread);
        LatencyB = FormatLatency(nowTickCountMs, b.EaTickCountMs);
        GapBuy = gapBuy;
        GapSell = gapSell;

        var pos = _trailingEngine.Current;
        if (pos is null)
        {
            PositionSide = "Flat";
            EntryPoint = "-";
            CurrentPoint = "-";
            TrailingState = "-";
            return;
        }

        var current = pos.Side == SignalSide.BuyB
            ? GapCalculator.ToPoints(b.Bid, config.Point)
            : GapCalculator.ToPoints(b.Ask, config.Point);

        PositionSide = pos.Side.ToString();
        EntryPoint = pos.EntryPoint.ToString(CultureInfo.InvariantCulture);
        CurrentPoint = current.ToString(CultureInfo.InvariantCulture);
        TrailingState = pos.TrailingActive
            ? $"active, ref={(pos.Side == SignalSide.BuyB ? pos.HighestPoint : pos.LowestPoint)}"
            : $"SL@{(pos.Side == SignalSide.BuyB ? pos.EntryPoint - config.StopLossPoint : pos.EntryPoint + config.StopLossPoint)}";
    }

    private void UpdateBTradesUi(TradeReadResult r)
    {
        StatusBTrade = r.Success ? $"Connected: {r.Count} open" : $"Disconnected: {r.Error}";
        if (!r.Success)
        {
            BTrades.Clear();
            return;
        }

        // Drop rows whose ticket is gone.
        var tickets = r.Trades.Select(t => t.Ticket).ToHashSet();
        for (var i = BTrades.Count - 1; i >= 0; i--)
        {
            if (!tickets.Contains(BTrades[i].Ticket))
            {
                BTrades.RemoveAt(i);
            }
        }

        // Add new tickets; update Profit in place for existing ones (live, no flicker).
        foreach (var t in r.Trades)
        {
            var row = BTrades.FirstOrDefault(x => x.Ticket == t.Ticket);
            if (row is null)
            {
                BTrades.Add(new BTradeRow
                {
                    Ticket = t.Ticket,
                    Side = t.Side.ToString(),
                    Lot = t.Lot,
                    Price = t.Price,
                    StopLoss = t.StopLoss,
                    TakeProfit = t.TakeProfit,
                    Profit = t.Profit,
                    Time = FormatTime(t.TimeMsc),
                    OpenEaTimeLocal = t.OpenEaTimeLocal,
                    Symbol = t.Symbol,
                });
            }
            else
            {
                row.Profit = t.Profit;
            }
        }
    }

    private void UpdateBHistoryUi(HistoryReadResult r)
    {
        StatusBHistory = r.Success ? $"Connected: {r.Count} closed" : $"Disconnected: {r.Error}";
        if (!r.Success || r.Count == _lastHistoryCount)
        {
            return;
        }

        _lastHistoryCount = r.Count;
        BHistory.Clear();
        // Newest first, cap to keep the grid light.
        foreach (var h in r.History.Reverse().Take(200))
        {
            BHistory.Add(new BHistoryRow(
                h.Ticket,
                h.Side.ToString(),
                h.Volume,
                h.OpenPrice,
                h.ClosePrice,
                h.StopLoss,
                h.TakeProfit,
                h.Commission,
                h.Profit,
                FormatTime(h.OpenTimeMsc),
                FormatTime(h.CloseTimeMsc),
                h.CloseEaTimeLocal,
                h.Symbol));
        }
    }

    private static string FormatTime(ulong epochMs)
    {
        if (epochMs == 0)
        {
            return "-";
        }

        return DateTimeOffset.FromUnixTimeMilliseconds((long)epochMs)
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private void AddLog(string message)
    {
        Logs.Insert(0, $"{DateTime.Now:HH:mm:ss.fff} {message}");
        while (Logs.Count > 300)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private static string F(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        _timer.Stop();
        _csvLogger?.Dispose();
        _mt5Engine.Dispose();
    }
}
