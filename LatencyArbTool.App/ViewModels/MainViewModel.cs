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
    private readonly RollingGapStats _stats = new();
    private readonly LeadFollowSignalEngine _signalEngine = new();
    private readonly DryRunClusterEngine _clusterEngine = new();
    private readonly Mt5Engine _mt5Engine = new();
    private readonly Mt5TradeExecutor _tradeExecutor;
    private readonly DispatcherTimer _timer;
    private CsvLogger? _csvLogger;
    private bool _isRunning;
    private bool _liveMode;
    private string _chartHwndText = string.Empty;
    private string _liveStatus = "Live mode off";
    private string _mapNameA = "FeedA";
    private string _mapNameB = "FeedB";
    private string _statusA = "Disconnected";
    private string _statusB = "Disconnected";
    private string _symbolA = "-";
    private string _symbolB = "-";
    private string _bidA = "-";
    private string _askA = "-";
    private string _spreadA = "-";
    private string _ageA = "-";
    private string _bidB = "-";
    private string _askB = "-";
    private string _spreadB = "-";
    private string _ageB = "-";
    private int _gapBuy;
    private int _gapSell;
    private int _openBuyThreshold = StrategyDefaults.FixedOpenBuyFallback;
    private int _openSellThreshold = StrategyDefaults.FixedOpenSellFallback;
    private int _sampleCount;
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
        ResetCommand = new RelayCommand(ResetDryRun);
    }

    public string MapNameA
    {
        get => _mapNameA;
        set => SetProperty(ref _mapNameA, value);
    }

    public string MapNameB
    {
        get => _mapNameB;
        set => SetProperty(ref _mapNameB, value);
    }

    public bool LiveMode
    {
        get => _liveMode;
        set
        {
            if (SetProperty(ref _liveMode, value))
            {
                LiveStatus = value ? "Live mode armed; waiting for valid HWND" : "Live mode off";
                AddLog(value
                    ? "live mode armed: MT5 actions may be sent when HWND is valid"
                    : "live mode disabled");
            }
        }
    }

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
    public string SymbolA { get => _symbolA; private set => SetProperty(ref _symbolA, value); }
    public string SymbolB { get => _symbolB; private set => SetProperty(ref _symbolB, value); }
    public string BidA { get => _bidA; private set => SetProperty(ref _bidA, value); }
    public string AskA { get => _askA; private set => SetProperty(ref _askA, value); }
    public string SpreadA { get => _spreadA; private set => SetProperty(ref _spreadA, value); }
    public string AgeA { get => _ageA; private set => SetProperty(ref _ageA, value); }
    public string BidB { get => _bidB; private set => SetProperty(ref _bidB, value); }
    public string AskB { get => _askB; private set => SetProperty(ref _askB, value); }
    public string SpreadB { get => _spreadB; private set => SetProperty(ref _spreadB, value); }
    public string AgeB { get => _ageB; private set => SetProperty(ref _ageB, value); }
    public int GapBuy { get => _gapBuy; private set => SetProperty(ref _gapBuy, value); }
    public int GapSell { get => _gapSell; private set => SetProperty(ref _gapSell, value); }
    public int OpenBuyThreshold { get => _openBuyThreshold; private set => SetProperty(ref _openBuyThreshold, value); }
    public int OpenSellThreshold { get => _openSellThreshold; private set => SetProperty(ref _openSellThreshold, value); }
    public int SampleCount { get => _sampleCount; private set => SetProperty(ref _sampleCount, value); }
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
        AddLog($"map check: A={StatusA}, B={StatusB}");
    }

    private void Start()
    {
        _csvLogger?.Dispose();
        _csvLogger = new CsvLogger(AppContext.BaseDirectory);
        IsRunning = true;
        _timer.Start();
        AddLog("start dry-run");
    }

    private void Stop()
    {
        _timer.Stop();
        _csvLogger?.Flush();
        IsRunning = false;
        AddLog("stop dry-run");
    }

    private void ResetDryRun()
    {
        _signalEngine.Reset();
        _clusterEngine.Reset();
        UpdateClusterUi();
        AddLog("reset dry-run");
    }

    private void Poll()
    {
        var tickA = _reader.TryRead(MapNameA);
        var tickB = _reader.TryRead(MapNameB);
        StatusA = tickA.Success ? "Connected" : $"Disconnected: {tickA.Error}";
        StatusB = tickB.Success ? "Connected" : $"Disconnected: {tickB.Error}";

        if (tickA.Tick is null || tickB.Tick is null)
        {
            return;
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var (gapBuy, gapSell) = GapCalculator.Calculate(tickA.Tick, tickB.Tick);
        var snapshot = new MarketSnapshot(tickA.Tick, tickB.Tick, nowMs, gapBuy, gapSell);

        _stats.Add(nowMs, gapBuy, gapSell, tickB.Tick.Spread);
        var thresholds = _stats.GetThresholds();
        var signal = _signalEngine.Evaluate(snapshot, thresholds);
        var events = _clusterEngine.Step(snapshot, thresholds, signal);

        UpdateMarketUi(snapshot, thresholds);
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
        var result = _tradeExecutor.Execute(dryRunEvent, LiveMode, ChartHwndText);
        if (!result.Attempted)
        {
            return;
        }

        var prefix = result.Success ? "live ok" : "live failed";
        LiveStatus = $"{prefix}: {result.Message}";
        AddLog($"{prefix}: {result.Message}");

        if (dryRunEvent.Decision == "dry close")
        {
            AddLog("live warning: dry close maps to MT5 row 0");
        }
    }

    private void UpdateMarketUi(MarketSnapshot snapshot, GapThresholds thresholds)
    {
        SymbolA = snapshot.A.Symbol;
        BidA = F(snapshot.A.Bid);
        AskA = F(snapshot.A.Ask);
        SpreadA = F(snapshot.A.Spread);
        AgeA = $"{snapshot.FeedAAgeMs} ms";

        SymbolB = snapshot.B.Symbol;
        BidB = F(snapshot.B.Bid);
        AskB = F(snapshot.B.Ask);
        SpreadB = F(snapshot.B.Spread);
        AgeB = $"{snapshot.FeedBAgeMs} ms";

        GapBuy = snapshot.GapBuy;
        GapSell = snapshot.GapSell;
        OpenBuyThreshold = thresholds.OpenBuy;
        OpenSellThreshold = thresholds.OpenSell;
        SampleCount = thresholds.SampleCount;
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
        if (!LiveMode)
        {
            LiveStatus = "Live mode off";
            return;
        }

        LiveStatus = HwndParser.TryParse(ChartHwndText, out var hwnd, out var error)
            ? $"Live mode armed for HWND 0x{hwnd:X}"
            : $"Live mode blocked: {error}";
    }

    private static string F(double value)
    {
        return value.ToString("0.#####", CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        _timer.Stop();
        _csvLogger?.Dispose();
        _mt5Engine.Dispose();
    }
}
