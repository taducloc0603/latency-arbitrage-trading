using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using LatencyArbTool.App.Services;
using LatencyArbTool.Core.Models;
using LatencyArbTool.Core.Services;

namespace LatencyArbTool.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly SharedMemoryTickReader _reader = new();
    private readonly SharedMemoryTradeReader _tradeReader = new();
    private readonly SharedMemoryHistoryReader _historyReader = new();
    private readonly RollingGapStats _stats = new();
    private readonly LeadFollowSignalEngine _signalEngine = new();
    private readonly DryRunClusterEngine _clusterEngine = new();
    private readonly Mt5Engine _mt5Engine = new();
    private readonly Mt5TradeExecutor _tradeExecutor;
    private readonly FeedFreshnessTracker _feedAFreshness = new();
    private readonly FeedFreshnessTracker _feedBFreshness = new();
    private readonly DispatcherTimer _timer;
    private CsvLogger? _csvLogger;
    private bool _isRunning;
    private bool _loggedInvalidLatencyA;
    private bool _loggedInvalidLatencyB;
    private bool _loggedBTradeDisconnected;
    private bool _loggedBHistoryDisconnected;
    private long _nextBPositionReadTickCountMs;
    private string _chartHwndText = string.Empty;
    private string _tradeHwndText = string.Empty;
    private string _liveStatus = "Live mode on; waiting for valid HWND";
    private string _mapNameA = @"Local\MT_A_Tick";
    private string _mapNameB = @"Local\MT_B_Tick";
    private string _statusA = "Disconnected";
    private string _statusB = "Disconnected";
    private string _statusBTrade = "Disconnected";
    private string _statusBHistory = "Disconnected";
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
    private string _bTradeSummary = "-";
    private string _bHistorySummary = "-";
    private int _gapBuy;
    private int _gapSell;
    private int _openBuyThreshold = StrategyDefaults.FixedOpenBuyFallback;
    private int _openSellThreshold = StrategyDefaults.FixedOpenSellFallback;
    private int _sampleCount;
    private string _aVolatility = "-";
    private string _thresholdMode = "Warmup";
    private string _botState = LatencyArbTool.Core.Models.BotState.Idle.ToString();
    private string _clusterSide = "-";
    private int _orderCount;
    private string _floatingPnl = "0";
    private string _peakTrough = "-";

    public MainViewModel()
    {
        _tradeExecutor = new Mt5TradeExecutor(_mt5Engine);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => Poll();

        CheckMapsCommand = new RelayCommand(CheckMaps);
        StartCommand = new RelayCommand(Start, () => !IsRunning);
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        ResetCommand = new RelayCommand(ResetLiveState);
    }

    public string MapNameA
    {
        get => _mapNameA;
        set => SetProperty(ref _mapNameA, value);
    }

    public string MapNameB
    {
        get => _mapNameB;
        set
        {
            if (SetProperty(ref _mapNameB, value))
            {
                OnPropertyChanged(nameof(MapNameBTrade));
                OnPropertyChanged(nameof(MapNameBHistory));
            }
        }
    }

    public string MapNameBTrade => SharedMemoryMapNames.TradeFromTick(MapNameB);

    public string MapNameBHistory => SharedMemoryMapNames.HistoryFromTick(MapNameB);

    public string ChartHwndText
    {
        get => _chartHwndText;
        set
        {
            if (SetProperty(ref _chartHwndText, value))
            {
                UpdateLiveStatus();
            }
        }
    }

    public string TradeHwndText
    {
        get => _tradeHwndText;
        set
        {
            if (SetProperty(ref _tradeHwndText, value))
            {
                UpdateLiveStatus();
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
            }
        }
    }

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
    public string BTradeSummary { get => _bTradeSummary; private set => SetProperty(ref _bTradeSummary, value); }
    public string BHistorySummary { get => _bHistorySummary; private set => SetProperty(ref _bHistorySummary, value); }
    public int GapBuy { get => _gapBuy; private set => SetProperty(ref _gapBuy, value); }
    public int GapSell { get => _gapSell; private set => SetProperty(ref _gapSell, value); }
    public int OpenBuyThreshold { get => _openBuyThreshold; private set => SetProperty(ref _openBuyThreshold, value); }
    public int OpenSellThreshold { get => _openSellThreshold; private set => SetProperty(ref _openSellThreshold, value); }
    public int SampleCount { get => _sampleCount; private set => SetProperty(ref _sampleCount, value); }
    public string AVolatility { get => _aVolatility; private set => SetProperty(ref _aVolatility, value); }
    public string ThresholdMode { get => _thresholdMode; private set => SetProperty(ref _thresholdMode, value); }
    public string BotState { get => _botState; private set => SetProperty(ref _botState, value); }
    public string ClusterSide { get => _clusterSide; private set => SetProperty(ref _clusterSide, value); }
    public int OrderCount { get => _orderCount; private set => SetProperty(ref _orderCount, value); }
    public string FloatingPnl { get => _floatingPnl; private set => SetProperty(ref _floatingPnl, value); }
    public string PeakTrough { get => _peakTrough; private set => SetProperty(ref _peakTrough, value); }
    public string LiveStatus { get => _liveStatus; private set => SetProperty(ref _liveStatus, value); }
    public ObservableCollection<string> Logs { get; } = [];
    public RelayCommand CheckMapsCommand { get; }
    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ResetCommand { get; }

    private void CheckMaps()
    {
        StatusA = _reader.MapExists(MapNameA) ? "Connected" : "Disconnected";
        StatusB = _reader.MapExists(MapNameB) ? "Connected" : "Disconnected";
        StatusBTrade = _tradeReader.MapExistsForTickMap(MapNameB) ? "Connected" : "Disconnected";
        StatusBHistory = _historyReader.MapExistsForTickMap(MapNameB) ? "Connected" : "Disconnected";
        AddLog($"map check: A={StatusA}, B={StatusB}, BTrade={StatusBTrade}, BHistory={StatusBHistory}");
    }

    private void Start()
    {
        _csvLogger?.Dispose();
        _csvLogger = new CsvLogger(AppContext.BaseDirectory);
        IsRunning = true;
        _timer.Start();
        UpdateLiveStatus();
        AddLog("start live mode");
    }

    private void Stop()
    {
        _timer.Stop();
        _csvLogger?.Flush();
        IsRunning = false;
        AddLog("stop live mode");
    }

    private void ResetLiveState()
    {
        _signalEngine.Reset();
        _clusterEngine.Reset();
        _feedAFreshness.Reset();
        _feedBFreshness.Reset();
        UpdateClusterUi();
        AddLog("reset live state");
    }

    private void Poll()
    {
        UpdateBPositionMaps(Environment.TickCount64);

        var tickA = _reader.TryRead(MapNameA);
        var tickB = _reader.TryRead(MapNameB);
        var nowTickCountMs = Environment.TickCount64;
        StatusA = tickA.Success ? "Connected" : $"Disconnected: {tickA.Error}";
        StatusB = tickB.Success ? "Connected" : $"Disconnected: {tickB.Error}";

        if (tickA.Tick is null || tickB.Tick is null)
        {
            return;
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var (gapBuy, gapSell) = GapCalculator.Calculate(tickA.Tick, tickB.Tick);
        var feedASilenceMs = _feedAFreshness.Observe(tickA.Tick.EaTickCountMs, nowTickCountMs);
        var feedBSilenceMs = _feedBFreshness.Observe(tickB.Tick.EaTickCountMs, nowTickCountMs);
        var snapshot = new MarketSnapshot(tickA.Tick, tickB.Tick, nowMs, gapBuy, gapSell, nowTickCountMs, feedASilenceMs, feedBSilenceMs);

        _stats.Add(nowMs, gapBuy, gapSell, tickB.Tick.Spread, (tickA.Tick.Bid + tickA.Tick.Ask) / 2.0);
        var thresholds = _stats.GetThresholds();
        var signal = _signalEngine.Evaluate(snapshot, thresholds);
        var events = _clusterEngine.Step(snapshot, thresholds, signal);

        UpdateMarketUi(snapshot, thresholds);
        LogInvalidLatencyWarnings(snapshot);
        UpdateClusterUi();
        _csvLogger?.LogTick(snapshot, thresholds);

        foreach (var dryRunEvent in events)
        {
            AddLog($"{dryRunEvent.Decision}: {dryRunEvent.Reason}");
            _csvLogger?.LogDecision(dryRunEvent, snapshot, thresholds);
            ExecuteLiveIfEnabled(dryRunEvent);
        }
    }

    private void ExecuteLiveIfEnabled(DryRunEvent dryRunEvent)
    {
        var bTrades = _tradeReader.TryReadForTickMap(MapNameB);
        var bHistory = _historyReader.TryReadForTickMap(MapNameB);
        UpdateBTradeUi(bTrades, logTransitions: true);
        UpdateBHistoryUi(bHistory, logTransitions: true);

        var result = _tradeExecutor.Execute(dryRunEvent, ChartHwndText, TradeHwndText, bTrades);
        if (!result.Attempted)
        {
            return;
        }

        var prefix = result.Success ? "live ok" : "live failed";
        LiveStatus = $"{prefix}: {result.Message}";
        AddLog($"{prefix}: {result.Message}");

        if (result.Success)
        {
            AddLog($"live audit: {FormatTradeAudit(bTrades)}; {FormatHistoryAudit(bHistory)}");
        }

        if (dryRunEvent.Decision == "live close")
        {
            AddLog("live warning: close maps to MT5 row 0");
        }
    }

    private void UpdateBPositionMaps(long nowTickCountMs)
    {
        if (nowTickCountMs < _nextBPositionReadTickCountMs)
        {
            return;
        }

        _nextBPositionReadTickCountMs = nowTickCountMs + 1000;
        UpdateBTradeUi(_tradeReader.TryReadForTickMap(MapNameB), logTransitions: true);
        UpdateBHistoryUi(_historyReader.TryReadForTickMap(MapNameB), logTransitions: true);
    }

    private void UpdateBTradeUi(TradeReadResult result, bool logTransitions)
    {
        StatusBTrade = result.Success ? $"Connected: {result.Count} open" : $"Disconnected: {result.Error}";
        BTradeSummary = FormatTradeSummary(result);

        if (!logTransitions)
        {
            return;
        }

        if (!result.Success && !_loggedBTradeDisconnected)
        {
            AddLog($"B trade map disconnected: map={result.MapName}, error={result.Error}");
            _loggedBTradeDisconnected = true;
        }
        else if (result.Success && _loggedBTradeDisconnected)
        {
            AddLog($"B trade map recovered: map={result.MapName}, openTrades={result.Count}");
            _loggedBTradeDisconnected = false;
        }
    }

    private void UpdateBHistoryUi(HistoryReadResult result, bool logTransitions)
    {
        StatusBHistory = result.Success ? $"Connected: {result.Count} history" : $"Disconnected: {result.Error}";
        BHistorySummary = FormatHistorySummary(result);

        if (!logTransitions)
        {
            return;
        }

        if (!result.Success && !_loggedBHistoryDisconnected)
        {
            AddLog($"B history map disconnected: map={result.MapName}, error={result.Error}");
            _loggedBHistoryDisconnected = true;
        }
        else if (result.Success && _loggedBHistoryDisconnected)
        {
            AddLog($"B history map recovered: map={result.MapName}, history={result.Count}");
            _loggedBHistoryDisconnected = false;
        }
    }

    private void UpdateMarketUi(MarketSnapshot snapshot, GapThresholds thresholds)
    {
        SymbolA = snapshot.A.Symbol;
        BidA = F(snapshot.A.Bid);
        AskA = F(snapshot.A.Ask);
        SpreadA = F(snapshot.A.Spread);
        LatencyA = FormatLatency(snapshot.FeedALatencyMs);

        SymbolB = snapshot.B.Symbol;
        BidB = F(snapshot.B.Bid);
        AskB = F(snapshot.B.Ask);
        SpreadB = F(snapshot.B.Spread);
        LatencyB = FormatLatency(snapshot.FeedBLatencyMs);

        GapBuy = snapshot.GapBuy;
        GapSell = snapshot.GapSell;
        OpenBuyThreshold = thresholds.OpenBuy;
        OpenSellThreshold = thresholds.OpenSell;
        SampleCount = thresholds.SampleCount;
        AVolatility = thresholds.ARangePoints == int.MaxValue ? "-" : thresholds.ARangePoints.ToString(CultureInfo.InvariantCulture);
        ThresholdMode = thresholds.IsWarmup ? "Warmup" : "Dynamic";
    }

    private void UpdateClusterUi()
    {
        BotState = _clusterEngine.State.ToString();
        var cluster = _clusterEngine.CurrentCluster;
        ClusterSide = cluster?.Side.ToString() ?? "-";
        OrderCount = cluster?.Orders.Count ?? 0;
        FloatingPnl = F(cluster?.FloatingPnlRaw ?? 0);
        PeakTrough = cluster is null ? "-" : $"{F(cluster.PeakAskA)} / {F(cluster.TroughBidA)}";
    }

    private void LogInvalidLatencyWarnings(MarketSnapshot snapshot)
    {
        if (!snapshot.HasValidFeedALatency && !_loggedInvalidLatencyA)
        {
            AddLog($"feed A invalid tick latency: eaTickCountMs={snapshot.A.EaTickCountMs}, tickTimeMsc={snapshot.A.TickTimeMsc}, latencyResolved={FormatNullableLatency(snapshot.FeedALatencyMs)}, source={snapshot.FeedALatency.Source}");
            _loggedInvalidLatencyA = true;
        }
        else if (snapshot.HasValidFeedALatency)
        {
            if (_loggedInvalidLatencyA)
            {
                AddLog($"feed A latency recovered: eaTickCountMs={snapshot.A.EaTickCountMs}, tickTimeMsc={snapshot.A.TickTimeMsc}, latencyResolved={FormatNullableLatency(snapshot.FeedALatencyMs)}, source={snapshot.FeedALatency.Source}");
            }
            _loggedInvalidLatencyA = false;
        }

        if (!snapshot.HasValidFeedBLatency && !_loggedInvalidLatencyB)
        {
            AddLog($"feed B invalid tick latency: eaTickCountMs={snapshot.B.EaTickCountMs}, tickTimeMsc={snapshot.B.TickTimeMsc}, latencyResolved={FormatNullableLatency(snapshot.FeedBLatencyMs)}, source={snapshot.FeedBLatency.Source}");
            _loggedInvalidLatencyB = true;
        }
        else if (snapshot.HasValidFeedBLatency)
        {
            if (_loggedInvalidLatencyB)
            {
                AddLog($"feed B latency recovered: eaTickCountMs={snapshot.B.EaTickCountMs}, tickTimeMsc={snapshot.B.TickTimeMsc}, latencyResolved={FormatNullableLatency(snapshot.FeedBLatencyMs)}, source={snapshot.FeedBLatency.Source}");
            }
            _loggedInvalidLatencyB = false;
        }
    }

    private void AddLog(string message)
    {
        Logs.Insert(0, $"{DateTime.Now:HH:mm:ss.fff} {message}");
        while (Logs.Count > 300)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private void UpdateLiveStatus()
    {
        var chartOk = HwndParser.TryParse(ChartHwndText, out var chartHwnd, out var chartError);
        var tradeOk = HwndParser.TryParse(TradeHwndText, out var tradeHwnd, out var tradeError);

        LiveStatus = (chartOk, tradeOk) switch
        {
            (true, true) => $"Live armed: chart 0x{chartHwnd:X}, trade 0x{tradeHwnd:X}",
            (false, true) => $"Live blocked: chart {chartError}",
            (true, false) => $"Live blocked: trade {tradeError}",
            _ => $"Live blocked: chart {chartError}; trade {tradeError}"
        };
    }

    private static string F(double value)
    {
        return value.ToString("0.#####", CultureInfo.InvariantCulture);
    }

    private static string FormatLatency(long? latencyMs)
    {
        return latencyMs is null ? "unknown" : $"{latencyMs.Value} ms";
    }

    private static string FormatTradeSummary(TradeReadResult result)
    {
        if (!result.Success)
        {
            return "-";
        }

        if (result.Count == 0)
        {
            return "0 open";
        }

        var trade = result.Trades[0];
        return $"{result.Count} open: #{trade.Ticket} {trade.Side} {F(trade.Lot)} pnl={F(trade.Profit)}";
    }

    private static string FormatHistorySummary(HistoryReadResult result)
    {
        if (!result.Success)
        {
            return "-";
        }

        if (result.Count == 0)
        {
            return "0 history";
        }

        var history = result.History[^1];
        return $"{result.Count} history: #{history.Ticket} {history.Side} pnl={F(history.Profit)}";
    }

    private static string FormatTradeAudit(TradeReadResult result)
    {
        return result.Success ? $"B trades count={result.Count}, first={FormatFirstTrade(result)}" : $"B trades unavailable: {result.Error}";
    }

    private static string FormatHistoryAudit(HistoryReadResult result)
    {
        return result.Success ? $"B history count={result.Count}, last={FormatLastHistory(result)}" : $"B history unavailable: {result.Error}";
    }

    private static string FormatFirstTrade(TradeReadResult result)
    {
        if (result.Count == 0)
        {
            return "none";
        }

        var trade = result.Trades[0];
        return $"#{trade.Ticket}/{trade.Side}/lot={F(trade.Lot)}/pnl={F(trade.Profit)}";
    }

    private static string FormatLastHistory(HistoryReadResult result)
    {
        if (result.Count == 0)
        {
            return "none";
        }

        var history = result.History[^1];
        return $"#{history.Ticket}/{history.Side}/pnl={F(history.Profit)}";
    }

    private static string FormatNullableLatency(long? latencyMs)
    {
        return latencyMs?.ToString(CultureInfo.InvariantCulture) ?? "null";
    }

    public void Dispose()
    {
        _timer.Stop();
        _csvLogger?.Dispose();
        _mt5Engine.Dispose();
    }
}
