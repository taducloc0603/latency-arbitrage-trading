using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
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
    private readonly Mt5Engine _mt5Engine = new();
    private readonly Mt5TradeExecutor _tradeExecutor;
    private readonly OpenSignalEngine _signalEngine = new();
    private readonly TrailingStopEngine _trailingEngine = new();
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
    private string _symbolA = "-";
    private string _symbolB = "-";
    private string _bidA = "-";
    private string _askA = "-";
    private string _bidB = "-";
    private string _askB = "-";
    private int _gapBuy;
    private int _gapSell;
    private string _positionSide = "Flat";
    private string _entryPoint = "-";
    private string _currentPoint = "-";
    private string _trailingState = "-";
    private string _liveStatus = "Idle";

    public MainViewModel()
    {
        _tradeExecutor = new Mt5TradeExecutor(_mt5Engine);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        _timer.Tick += (_, _) => Poll();

        LoadConfigCommand = new RelayCommand(() => _ = LoadConfigAsync(), () => !IsRunning);
        CheckMapsCommand = new RelayCommand(CheckMaps);
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
    public string SymbolA { get => _symbolA; private set => SetProperty(ref _symbolA, value); }
    public string SymbolB { get => _symbolB; private set => SetProperty(ref _symbolB, value); }
    public string BidA { get => _bidA; private set => SetProperty(ref _bidA, value); }
    public string AskA { get => _askA; private set => SetProperty(ref _askA, value); }
    public string BidB { get => _bidB; private set => SetProperty(ref _bidB, value); }
    public string AskB { get => _askB; private set => SetProperty(ref _askB, value); }
    public int GapBuy { get => _gapBuy; private set => SetProperty(ref _gapBuy, value); }
    public int GapSell { get => _gapSell; private set => SetProperty(ref _gapSell, value); }
    public string PositionSide { get => _positionSide; private set => SetProperty(ref _positionSide, value); }
    public string EntryPoint { get => _entryPoint; private set => SetProperty(ref _entryPoint, value); }
    public string CurrentPoint { get => _currentPoint; private set => SetProperty(ref _currentPoint, value); }
    public string TrailingState { get => _trailingState; private set => SetProperty(ref _trailingState, value); }
    public string LiveStatus { get => _liveStatus; private set => SetProperty(ref _liveStatus, value); }

    public ObservableCollection<string> Logs { get; } = [];
    public RelayCommand LoadConfigCommand { get; }
    public RelayCommand CheckMapsCommand { get; }
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
        StatusBTrade = bTrades.Success ? $"Connected: {bTrades.Count} open" : $"Disconnected: {bTrades.Error}";

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
            AddLog($"{e.Decision}: {e.Reason}");
            _csvLogger?.LogEvent(e);
            ExecuteLive(e, bTrades);

            // A fresh open consumes the signal: reset so re-entry requires a new
            // confirm window rather than firing every tick the gap stays extreme.
            if (e.Decision == "live open")
            {
                _signalEngine.Reset();
            }
        }
    }

    private void ExecuteLive(DryRunEvent e, TradeReadResult bTrades)
    {
        var result = _tradeExecutor.Execute(e, ChartHwndText, TradeHwndText, bTrades);
        if (!result.Attempted)
        {
            return;
        }

        var prefix = result.Success ? "live ok" : "live failed";
        LiveStatus = $"{prefix}: {result.Message}";
        AddLog($"{prefix}: {result.Message}");
    }

    private void UpdateMarketUi(TickRecord a, TickRecord b, int gapBuy, int gapSell, StrategyConfig config)
    {
        SymbolA = a.Symbol;
        BidA = F(a.Bid);
        AskA = F(a.Ask);
        SymbolB = b.Symbol;
        BidB = F(b.Bid);
        AskB = F(b.Ask);
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
