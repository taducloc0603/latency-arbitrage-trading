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
    private readonly SharedMemoryCommandWriter _commandWriter = new();
    private readonly Dictionary<ulong, FillEvent> _openFills = new();
    private readonly Dictionary<ulong, FillEvent> _closeFills = new();
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
    private string _statusA = "Not started";
    private string _statusB = "Not started";
    private string _statusBTrade = "Not started";
    private string _statusBHistory = "Not started";
    // Session-closed rows accumulate in BHistory (append-only, keyed by ticket).
    // The EA re-baselines the history map at Start (reset command below), so the
    // map only ever contains this session's deals — no time filtering here. We
    // still accumulate so rows survive being evicted from the bounded map.
    private readonly HashSet<ulong> _sessionTickets = new();
    private const int MaxHistoryRows = 500;

    // History-reset handshake with the EA. Rows are not shown until the EA acks
    // the reset (so pre-session deals never flash in), with a timeout fallback
    // in case an old EA build doesn't understand the command.
    private int _historyResetSeq;
    private bool _historyResetConfirmed;
    private long _historyResetSentTick;
    private const long HistoryResetTimeoutMs = 3000;

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
    private long _lastSnapshotMs;

    // No re-open until this UTC ms (set after every close: cooldown against
    // instantly re-entering the same adverse condition).
    private long _reopenBlockedUntilMs;

    // Hard-SL command awaiting the EA's ack (0 = none pending).
    private int _pendingSlCmdSeq;
    private ulong _pendingSlTicket;

    public MainViewModel()
    {
        _tradeExecutor = new Mt5TradeExecutor(_mt5Engine);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        _timer.Tick += (_, _) => Poll();

        LoadConfigCommand = new RelayCommand(() => _ = LoadConfigAsync(), () => !IsRunning);
        CheckMapsCommand = new RelayCommand(CheckMaps);
        CheckHwndCommand = new RelayCommand(CheckHwnd);
        SaveConfigCommand = new RelayCommand(() => _ = SaveConfigAsync(), () => !IsRunning && _config is not null);
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
                SaveConfigCommand.RaiseCanExecuteChanged();
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
    public RelayCommand SaveConfigCommand { get; }
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
            SaveConfigCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            ConfigStatus = $"Config load failed: {ex.Message}";
        }
    }

    private async Task SaveConfigAsync()
    {
        if (_config is not { } config)
        {
            LiveStatus = "Save: no config loaded";
            return;
        }

        var url = Environment.GetEnvironmentVariable("SUPABASE_URL");
        var key = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY");
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            LiveStatus = "Save failed: missing SUPABASE_URL / SUPABASE_ANON_KEY";
            AddLog(LiveStatus);
            return;
        }

        try
        {
            var repo = new SupabaseConfigRepository(url, key, SharedHttp);
            var error = await repo.UpdateHwndAndMapsAsync(config.Id, MapNameA, MapNameB, ChartHwndText, TradeHwndText)
                .ConfigureAwait(true);
            if (error is null)
            {
                _config = config with { MapA = MapNameA, MapB = MapNameB, ChartHwndB = ChartHwndText, TradeHwndB = TradeHwndText };
                LiveStatus = "Saved config to DB";
                AddLog($"saved config to Supabase (group '{config.GroupName}')");
            }
            else
            {
                LiveStatus = $"Save failed: {error}";
                AddLog(LiveStatus);
            }
        }
        catch (Exception ex)
        {
            LiveStatus = $"Save failed: {ex.Message}";
            AddLog(LiveStatus);
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
        if (_config is not { } config)
        {
            AddLog("cannot start: no config loaded");
            return;
        }

        var problems = ValidateForStart(config);
        if (problems.Count > 0)
        {
            var msg = "cannot start: " + string.Join("; ", problems);
            LiveStatus = msg;
            AddLog(msg);
            return;
        }

        _csvLogger?.Dispose();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var logsDirectory = Path.Combine(desktop, "arb-log");
        _csvLogger = new CsvLogger(logsDirectory);

        _signalEngine.Reset();
        _fillTracker.Reset();
        _lastSnapshotMs = 0;
        _reopenBlockedUntilMs = 0;
        _pendingSlCmdSeq = 0;

        // Start = a fresh session: drop everything from the previous one and ask
        // the EA to re-baseline the history map so only new closes are exported.
        BHistory.Clear();
        _sessionTickets.Clear();
        _openFills.Clear();
        _closeFills.Clear();
        RequestHistoryReset();

        IsRunning = true;
        _timer.Start();
        AddLog($"start; logs at {logsDirectory}");
    }

    // Pre-flight checks: only allow Start when params, maps and HWNDs are valid.
    private List<string> ValidateForStart(StrategyConfig config)
    {
        var problems = new List<string>();

        if (config.Point <= 0) problems.Add("point<=0");
        if (config.OpenHoldConfirmMs <= 0) problems.Add("y(open_hold_confirm_ms)<=0");
        if (config.StopLossPoint <= 0) problems.Add("stop_loss_point<=0");
        if (config.TrailingStartPoint <= 0) problems.Add("trailing_start_point<=0");
        if (config.TrailingStepPoint <= 0) problems.Add("trailing_step_point<=0");

        if (!_reader.MapExists(MapNameA)) problems.Add("map A missing");
        if (!_reader.MapExists(MapNameB)) problems.Add("map B missing");
        if (!_tradeReader.MapExistsForTickMap(MapNameB)) problems.Add("B trade map missing");

        if (!HwndParser.TryParse(ChartHwndText, out var chart, out _) || !_mt5Engine.IsValidWindow(chart, out _))
            problems.Add("chart HWND invalid/not found");
        if (!HwndParser.TryParse(TradeHwndText, out var trade, out _) || !_mt5Engine.IsValidWindow(trade, out _))
            problems.Add("trade HWND invalid/not found");

        return problems;
    }

    private void Stop()
    {
        _timer.Stop();
        _csvLogger?.Flush();
        IsRunning = false;

        // Reset to idle so stale Connected/data isn't mistaken for a live state.
        StatusA = "Not started";
        StatusB = "Not started";
        StatusBTrade = "Not started";
        StatusBHistory = "Not started";
        BTrades.Clear();
        BHistory.Clear();
        _sessionTickets.Clear();
        _openFills.Clear();
        _closeFills.Clear();

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

        // B quotes are required for everything (opens fill on B, closes price on
        // B). Feed A is only needed to compute the gap for OPENS — when it is
        // down, close management (SL/trailing) keeps running on B alone.
        if (tickB.Tick is null)
        {
            UpdateBHistoryUi(bHistory, config.Point);
            return;
        }

        var a = tickA.Tick;
        var b = tickB.Tick;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nowTick = Environment.TickCount64;

        var gapBuy = 0;
        var gapSell = 0;
        SignalSide? signal = null;
        if (a is not null)
        {
            (gapBuy, gapSell) = GapCalculator.Calculate(a, b, config.Point);

            // Signal only opens; it is ignored by the engine while a position is
            // held, and suppressed while a feed is stale or a cooldown is active.
            signal = FilterSignal(_signalEngine.Evaluate(gapBuy, gapSell, nowMs, config), a, b, nowTick, nowMs, config);
        }

        var events = _trailingEngine.Step(b.Bid, b.Ask, signal, nowMs, config);

        UpdateMarketUi(a, b, gapBuy, gapSell, config);

        // ~1s market/gap/window snapshot for offline analysis (logged even when idle).
        if (a is not null && nowMs - _lastSnapshotMs >= 1000)
        {
            _lastSnapshotMs = nowMs;
            var w = _signalEngine.CurrentWindow(nowMs);
            _csvLogger?.LogSnapshot(nowMs, a, b, gapBuy, gapSell, w.State, w.DurationMs, w.Min, w.Max, w.Count);
        }

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
                // Kept until the close is confirmed so retry events log the same
                // entry context.
                gapAtOpen = _openGapAtOpen ?? 0;
                entryPoint = _openEntryPoint ?? 0;
            }
            else
            {
                gapAtOpen = 0;
                entryPoint = 0;
            }

            AddLog(DescribeEvent(e, gapBuy, gapSell, window));
            _csvLogger?.LogEvent(e, gapAtOpen, entryPoint, window);

            var closeTicket = _trailingEngine.Current?.Ticket;
            var result = ExecuteLive(e, bTrades, closeTicket, b.Symbol);
            var side = e.Side ?? DryRunSide.BuyB;

            if (e.Decision == "live open")
            {
                if (result.Success)
                {
                    _fillTracker.RecordOpenClick(new ClickContext(nowMs, nowTick, gapAtOpen, e.OpenPrice, side, e.ClusterId, "live open"));
                }
                else if (e.ClusterId is { } cid && _trailingEngine.AbortOpen(cid))
                {
                    // No broker position was created; drop the phantom so the
                    // engine can act on the next confirmed signal.
                    AddLog($"open click failed -> rolled back (cluster {cid})");
                }

                // The attempt consumed the signal either way: a retry (after a
                // failed click) must earn a fresh confirm window rather than
                // firing every tick the gap stays extreme.
                _signalEngine.Reset();
            }
            else if (e.Decision == "live close" && result.Success)
            {
                var closedTicket = closeTicket ?? FindSymbolTicket(bTrades, b.Symbol);
                if (closedTicket != 0)
                {
                    _fillTracker.RecordCloseClick(closedTicket,
                        new ClickContext(nowMs, nowTick, gapAtOpen, e.ClosePrice, side, e.ClusterId, "live close"));
                }

                if (e.ClusterId is { } cid)
                {
                    _trailingEngine.ConfirmClose(cid);
                }

                BeginReopenCooldown(nowMs, config);
                _openGapAtOpen = null;
                _openEntryPoint = null;
            }
            // Close click failed: the engine keeps the position and re-emits the
            // close on its retry cadence — no orphaned broker position.
        }

        // Observe fills first so _openFills/_closeFills are populated before the
        // history UI rebuild — ensures SlipClose is available on the same tick the
        // trade closes and the history count increases.
        foreach (var fill in _fillTracker.Observe(bTrades, bHistory, gapBuy, gapSell, nowMs))
        {
            if (fill.IsClose)
                _closeFills[fill.Ticket] = fill;
            else
                _openFills[fill.Ticket] = fill;

            AddLog(DescribeFill(fill, config.Point));
            _csvLogger?.LogFill(fill);

            // Re-anchor SL/trailing to the broker's real fill price once known,
            // then arm the broker-side hard SL on the real ticket.
            if (!fill.IsClose && fill.ClusterId is { } cid
                && _trailingEngine.ApplyOpenFill(cid, fill.Ticket, fill.FillPrice, config.Point))
            {
                AddLog($"entry corrected -> {F(fill.FillPrice)} ({GapCalculator.ToPoints(fill.FillPrice, config.Point)}pt)");
                RequestHardSl(fill, config);
            }
        }

        ReconcileExternalClose(bTrades, b.Symbol, nowMs, config);
        PollHardSlAck();

        UpdateBHistoryUi(bHistory, config.Point);
    }

    // Suppresses an open signal when a feed is stale or the post-close cooldown
    // is active. Also resets the confirm window so the signal must be re-earned
    // once conditions are healthy again (prevents an instant stale refire).
    private SignalSide? FilterSignal(SignalSide? signal, TickRecord a, TickRecord b, long nowTick, long nowMs, StrategyConfig config)
    {
        if (signal is null)
        {
            return null;
        }

        var silenceA = nowTick - a.EaTickCountMs;
        var silenceB = nowTick - b.EaTickCountMs;
        var stale = silenceA < 0 || silenceA > config.MaxFeedSilenceMs
                    || silenceB < 0 || silenceB > config.MaxFeedSilenceMs;
        if (stale)
        {
            _signalEngine.Reset();
            AddLog($"open blocked: feed stale (A={silenceA}ms B={silenceB}ms, max={config.MaxFeedSilenceMs}ms)");
            return null;
        }

        if (nowMs < _reopenBlockedUntilMs)
        {
            _signalEngine.Reset();
            AddLog($"open blocked: cooldown {_reopenBlockedUntilMs - nowMs}ms left");
            return null;
        }

        return signal;
    }

    private void BeginReopenCooldown(long nowMs, StrategyConfig config)
    {
        _signalEngine.Reset();
        if (config.ReopenCooldownMs > 0)
        {
            _reopenBlockedUntilMs = nowMs + config.ReopenCooldownMs;
        }
    }

    // An open click that "succeeded" can still produce no position (order
    // rejected by the broker). If no ticket showed up on our symbol this long
    // after the open, the position is a phantom and is dropped.
    private const long PhantomOpenTimeoutMs = 10_000;

    // The broker can close our position without us clicking (hard SL hit, manual
    // close). When the engine's ticket vanishes from the trades map, go flat so
    // the engine doesn't manage (or retry-close) a position that no longer exists.
    private void ReconcileExternalClose(TradeReadResult bTrades, string symbol, long nowMs, StrategyConfig config)
    {
        if (_trailingEngine.Current is not { } pos || !bTrades.Success)
        {
            return;
        }

        if (pos.Ticket is { } ticket)
        {
            foreach (var t in bTrades.Trades)
            {
                if (t.Ticket == ticket)
                {
                    return;
                }
            }

            AddLog($"position #{ticket} closed externally (hard SL / manual) -> engine flat");
            GoFlat(pos, nowMs, config);
            return;
        }

        // No fill was ever observed: if nothing is open on our symbol well after
        // the click, the open never actually filled — drop the phantom so the
        // engine doesn't hold (and retry-close) a position that never existed.
        if (nowMs - pos.OpenedAtMs < PhantomOpenTimeoutMs)
        {
            return;
        }

        foreach (var t in bTrades.Trades)
        {
            if (string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        AddLog($"open never filled (no position {PhantomOpenTimeoutMs / 1000}s after click) -> engine flat");
        GoFlat(pos, nowMs, config);
    }

    private void GoFlat(Position pos, long nowMs, StrategyConfig config)
    {
        _trailingEngine.ConfirmClose(pos.ClusterId);
        BeginReopenCooldown(nowMs, config);
        _openGapAtOpen = null;
        _openEntryPoint = null;
    }

    // Broker-side hard SL: soft SL (StopLossPoint) + buffer, placed on the real
    // ticket via the EA command map. It is the last-resort stop when the app,
    // feed or close click fails; the soft stop still closes first normally.
    private void RequestHardSl(FillEvent fill, StrategyConfig config)
    {
        if (config.HardSlBufferPt <= 0)
        {
            return;
        }

        var distance = (config.StopLossPoint + config.HardSlBufferPt) / (double)config.Point;
        var sl = fill.Side == DryRunSide.BuyB ? fill.FillPrice - distance : fill.FillPrice + distance;
        var mapName = SharedMemoryMapNames.CmdFromTick(MapNameB);

        if (_commandWriter.TryWriteSetSl(mapName, fill.Ticket, sl, out var seq, out var error))
        {
            _pendingSlCmdSeq = seq;
            _pendingSlTicket = fill.Ticket;
            AddLog($"hard SL request #{fill.Ticket} sl={F(sl)} ({config.StopLossPoint}+{config.HardSlBufferPt}pt)");
        }
        else
        {
            AddLog($"hard SL request FAILED #{fill.Ticket}: {error}");
        }
    }

    private void PollHardSlAck()
    {
        if (_pendingSlCmdSeq == 0)
        {
            return;
        }

        var ack = _commandWriter.TryReadAck(SharedMemoryMapNames.CmdFromTick(MapNameB));
        if (ack is not { } a || a.Seq != _pendingSlCmdSeq)
        {
            return;
        }

        AddLog(a.Ok
            ? $"hard SL set on #{_pendingSlTicket}"
            : $"hard SL FAILED on #{_pendingSlTicket} (retcode={a.Retcode}) — check AutoTrading is enabled");
        _pendingSlCmdSeq = 0;
    }

    private static ulong FindSymbolTicket(TradeReadResult bTrades, string symbol)
    {
        if (!bTrades.Success)
        {
            return 0;
        }

        foreach (var t in bTrades.Trades)
        {
            if (string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            {
                return t.Ticket;
            }
        }

        return 0;
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

    private LiveTradeResult ExecuteLive(DryRunEvent e, TradeReadResult bTrades, ulong? closeTicket, string symbol)
    {
        var result = _tradeExecutor.Execute(e, ChartHwndText, TradeHwndText, bTrades, closeTicket, symbol);
        if (result.Attempted)
        {
            var prefix = result.Success ? "live ok" : "live failed";
            LiveStatus = $"{prefix}: {result.Message}";
            AddLog($"{prefix}: {result.Message}");
        }

        return result;
    }

    private void UpdateMarketUi(TickRecord? a, TickRecord b, int gapBuy, int gapSell, StrategyConfig config)
    {
        var nowTickCountMs = Environment.TickCount64;
        if (a is not null)
        {
            SymbolA = a.Symbol;
            BidA = F(a.Bid);
            AskA = F(a.Ask);
            SpreadA = F(a.Spread);
            LatencyA = FormatLatency(nowTickCountMs, a.EaTickCountMs);
        }
        else
        {
            LatencyA = "-";
        }

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
        // The stop trails the best price since open in both phases.
        TrailingState = pos.TrailingActive
            ? $"active, ref={(pos.Side == SignalSide.BuyB ? pos.HighestPoint : pos.LowestPoint)}"
            : $"SL@{(pos.Side == SignalSide.BuyB ? pos.HighestPoint - config.StopLossPoint : pos.LowestPoint + config.StopLossPoint)}";
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

    // Asks the EA to re-baseline the history map to now (new session). Until the
    // EA acks, the grid is held empty so pre-session deals never flash in.
    private void RequestHistoryReset()
    {
        _historyResetConfirmed = false;
        _historyResetSentTick = Environment.TickCount64;
        var mapName = SharedMemoryMapNames.CmdFromTick(MapNameB);
        if (_commandWriter.TryWriteResetHistory(mapName, out var seq, out var error))
        {
            _historyResetSeq = seq;
        }
        else
        {
            // No command channel (EA not running / old build): fall back to
            // showing everything rather than a permanently empty grid.
            _historyResetSeq = 0;
            _historyResetConfirmed = true;
            AddLog($"history reset request failed: {error} — showing all deals in map");
        }
    }

    // True once the EA has re-baselined the map (or the timeout fallback fired).
    private bool HistoryResetReady()
    {
        if (_historyResetConfirmed)
        {
            return true;
        }

        var ack = _commandWriter.TryReadAck(SharedMemoryMapNames.CmdFromTick(MapNameB));
        if (ack is { } a && a.Seq == _historyResetSeq)
        {
            _historyResetConfirmed = true;
            return true;
        }

        if (Environment.TickCount64 - _historyResetSentTick > HistoryResetTimeoutMs)
        {
            _historyResetConfirmed = true;
            AddLog("history reset not acknowledged — recompile/re-attach EA DataExporter");
            return true;
        }

        return false;
    }

    private void UpdateBHistoryUi(HistoryReadResult r, int point)
    {
        if (!r.Success)
        {
            StatusBHistory = $"Disconnected: {r.Error}";
            return;
        }

        // Hold the grid empty until the EA confirms the session reset so the map
        // (which still holds old deals for a tick or two) doesn't pollute it.
        if (!HistoryResetReady())
        {
            StatusBHistory = "Connected: resetting session…";
            return;
        }

        // The map is session-only after the EA reset; add each ticket once. Rows
        // stay even after the record is evicted from the bounded map.
        foreach (var h in r.History) // oldest-first, so inserts keep newest on top
        {
            if (!_sessionTickets.Add(h.Ticket))
            {
                continue;
            }

            BHistory.Insert(0, BuildHistoryRow(h, point));
            while (BHistory.Count > MaxHistoryRows)
            {
                BHistory.RemoveAt(BHistory.Count - 1);
            }
        }

        RefreshLateFillColumns(point);

        StatusBHistory = $"Connected: {_sessionTickets.Count} session closed";
    }

    private BHistoryRow BuildHistoryRow(HistoryRecord h, int point)
    {
        _openFills.TryGetValue(h.Ticket, out var openFill);
        _closeFills.TryGetValue(h.Ticket, out var closeFill);
        var displayOpenPrice = h.OpenPrice > 0 ? h.OpenPrice : (openFill?.FillPrice ?? 0);
        return new BHistoryRow(
            h.Ticket,
            h.Side.ToString(),
            h.Volume,
            displayOpenPrice,
            h.ClosePrice,
            h.StopLoss,
            h.TakeProfit,
            h.Commission,
            h.Profit,
            FormatTime(h.OpenTimeMsc),
            FormatTime(h.CloseTimeMsc),
            h.CloseEaTimeLocal,
            h.Symbol,
            GapOpen:  openFill is not null ? openFill.DecideGap : null,
            SlipOpen:  openFill is not null ? GapCalculator.ToPoints(openFill.SlippagePrice, point) : null,
            SlipClose: closeFill is not null && h.ClosePrice > 0
                ? GapCalculator.ToPoints(h.ClosePrice - closeFill.DecidePrice, point)
                : null);
    }

    // Fills can be observed after the history record is first shown (FillTracker
    // waits up to a few seconds for the lagging history map), so backfill the
    // Gap/Slip columns onto rows that were added without them.
    private void RefreshLateFillColumns(int point)
    {
        for (var i = 0; i < BHistory.Count; i++)
        {
            var row = BHistory[i];
            var updated = row;

            if (updated.SlipClose is null && updated.ClosePrice > 0
                && _closeFills.TryGetValue(updated.Ticket, out var closeFill))
            {
                updated = updated with
                {
                    SlipClose = GapCalculator.ToPoints(updated.ClosePrice - closeFill.DecidePrice, point),
                };
            }

            if (updated.GapOpen is null && _openFills.TryGetValue(updated.Ticket, out var openFill))
            {
                updated = updated with
                {
                    GapOpen = openFill.DecideGap,
                    SlipOpen = GapCalculator.ToPoints(openFill.SlippagePrice, point),
                    OpenPrice = updated.OpenPrice > 0 ? updated.OpenPrice : openFill.FillPrice,
                };
            }

            if (!ReferenceEquals(updated, row))
            {
                BHistory[i] = updated;
            }
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
