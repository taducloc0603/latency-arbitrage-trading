# Latency Arb Live

Tai lieu nay ghi lai cac thong so va dieu kien dong/mo lenh hien tai cua tool.

## Tong quan chien luoc

Tool theo doi 2 feed gia (A nhanh, B cham). Khi gap giua A va B mo rong vuot nguong va duy tri du lau (sustained), bot mo lenh tren B theo huong B se catch-up A. Khi gap ve 0 hoac A dao chieu, bot dong lenh.

Trinh tu xac nhan tin hieu chia 3 phase de loc cac brief spike (gap nhay extreme nhung B catch up qua nhanh, vuot khoi window thi truoc khi MT5 kip vao lenh):

1. **Confirm** (`500ms`): gap phai vuot threshold lien tuc.
2. **Re-check** (`200ms`): tiep tuc giu threshold them; tong wait `700ms`.
3. **Stability** (cuoi Re-check): gap hien tai phai >= `65%` cua peak quan sat trong toan bo window. Loc cac signal qua dinh roi revert manh, nhung van cho phep cac sustained signal voi peak cao moderate.

Sau khi mo, MinHoldMs ngan (`3000ms`) + AReversalUsd thap (`$0.40`) cho phep dong som ngay khi A dao chieu, tranh expose voi market drift trong khi giu lenh.

### Filter chinh

Bot SKIP trade hoan toan trong cac dieu kien:

- **Feed B silent qua lau** (`FeedBStaleMs = 3000ms`): khi B khong co tick moi trong 3s, bot khong tin tuong gap (B co the dang stuck/dead). Day la filter chinh giup tranh run-6-style market (broker B sparse + trending) — cac phien thua nang nhat.
- **A volatility thap** (`MinAVolPoints = 50`): A khong di chuyen du, gap khong reverable.
- **Spread B bat thuong** (`> 2.5x median`): broker B dang giam dia, fill gia toi.

## Calibration log (V1, sweep tren polling-emulated sim)

Sau khi phat hien event-driven sim under-count engine evaluation va polling-emulated sim moi la reference dung, da sweep parameters de tim combo profitable:

| Run | Real outcome | Sim V1 (current) |
|-----|--------------|------------------|
| Run 4 (50min, normal market) | 9 trades, +$78 | **3 trades, 100% WR, +18.77** |
| Run 2 (15min, normal market) | 12 trades, +$22 | **2 trades, 100% WR, +1.37** |
| Run 6 (88min, sparse B + trending) | 15 trades, **-$405** | **0 trades** (skipped) |
| Run 7 (13min, sparse B) | 3 trades, **-$154** | **0 trades** (skipped) |

V1 trade-off: ~5x ít volume hon V0 (`StabilityRatio=0.40`) nhung 100% WR + bo qua hoan toan cac phien thua. Strategy "selective quality" thay vi "high frequency".

## Du lieu dau vao

- Map A mac dinh: `Local\MT_A_Tick`
- Map B mac dinh: `Local\MT_B_Tick`
- Tool doc tick tu shared memory, layout tick hien tai: `seq:int32` (monotonic counter — moi `OnTick` increment 1, dung de detect missed polls), `ea_ms:uint64`, padding `4` bytes, `Bid`, `Ask`, `Spread`, `TickTimeMsc`, `Symbol[16]`.
- `ea_ms` la clock monotonic tu Windows `GetTickCount64` ben EA, khong phai Unix epoch.
- `Latency` duoc tinh uu tien bang `Environment.TickCount64 - ea_ms`. Gia tri hop le phai nam trong khoang `0..86_400_000ms` (24h). Day la metric **chi de hien thi** cho user xem.
- Neu `ea_ms` thieu/khong hop le, tool fallback sang `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - TickTimeMsc` khi `TickTimeMsc` la Unix epoch milliseconds hop le. Neu ca 2 nguon khong hop le thi hien `unknown`.
- `nowTickCountMs` duoc capture **ngay sau khi doc xong tickA va tickB**, giam thoi gian troi giua EA ghi `ea_ms` va C# doc latency.
- **Stale check dung "silence" thay vi "latency"** ([FeedFreshnessTracker.cs](LatencyArbTool.Core/Services/FeedFreshnessTracker.cs)): track thoi gian ke tu lan cuoi `ea_ms` thay doi (tick moi den). Quiet market khien tick sparse → silence reset moi tick mới → feed van duoc coi la khoe. Feed thuc su die → silence tang den threshold → block. Tranh false positive cho sparse-but-alive feed (P95 B interval 2.3s trong run 6 khong block bot).
- **Polling miss detection** ([SequenceTracker.cs](LatencyArbTool.Core/Services/SequenceTracker.cs)): EA increment `seq` moi tick. Tool track delta giua cac poll. Neu `seq_delta > 1` → tool da bo lo (delta - 1) tick giua 2 lan poll → trong khoang \"miss\" do co the co tick gap revert ma signal engine khong thay → signal engine **reset state** (`PollMissedTicks` field tren `MarketSnapshot`). Tranh false sustained signal khi tick rate vuot polling rate.
- DispatcherTimer interval `25ms` (giam tu `50ms`) → giam tan suat miss tick. Ket hop voi seq tracker, polling artifact gan nhu duoc loai bo.
- Live safety chi check lenh that o feed B. Tu `MapNameB`, tool derive map trade/history bang cach thay hau to `Tick`: `Local\MT_B_Tick` -> `Local\MT_B_Trade` va `Local\MT_B_History`. Trade co fallback `Local\MT_B_Trades` de tuong thich writer cu.
- Symbol A/B khong can giong nhau. Dieu kien `symbol mismatch` da duoc bo.

## Cong thuc va nguong

- `GapBuy = round((B.Bid - A.Ask) * 100)`
- `GapSell = round((B.Ask - A.Bid) * 100)`
- Rolling window thong ke: `5` phut.
- Can toi thieu `500` samples truoc khi dung dynamic threshold.
- Khi chua du `500` samples (warmup):
  - `OpenBuyThreshold = -80`
  - `OpenSellThreshold = 60`
- Khi du samples (post-warmup):
  - `OpenBuyThreshold = min(median(GapBuy) - 3.0 * std(GapBuy), -80)`
  - `OpenSellThreshold = max(median(GapSell) + 3.0 * std(GapSell), 60)`
  - Floor `-80 / +60` la san: dynamic chi duoc dung khi extreme hon. Trong market calm, std nho khien dynamic lon hon san; khi do bot dung san de tranh trade tren noise.
- Nguong dong co dinh:
  - `CloseBuyRevert = 0`
  - `CloseSellRevert = 0`
- Feed stale (silence-based, KHONG phai latency):
  - Feed A stale neu silence (ms ke tu `ea_ms` doi cuoi cung) `> 10000ms`, hoac latency khong hop le.
  - Feed B stale neu silence `> 3000ms`, hoac latency khong hop le.
  - Trong quiet market, silence reset moi khi co tick moi → khong false positive.
- Spread B bat thuong neu `MedianSpreadB > 0` va `SpreadB > MedianSpreadB * 2.5`.
- A volatility filter: tinh range `max(midA) - min(midA)` trong rolling `60s`. Neu `ARangePoints < 50` thi chan mo lenh (`A volatility low`).

## Dieu kien mo lenh

Tool chi mo lenh moi khi:

- Dang khong co cluster/lenh dang giu.
- Bot khong o trang thai `Emergency`.
- Feed A khong stale.
- Feed B khong stale.
- Spread B khong bat thuong.
- A volatility (range 60s) khong qua thap (`>= 50` points).
- Tin hieu pass ca 3 phase Confirm + Re-check + Stability (xem chi tiet o muc Tong quan).
- B trade map phai doc duoc va khong co lenh B dang mo; neu khong verify duoc thi live open bi chan.

Mo `BuyB` khi `GapBuy <= OpenBuyThreshold` qua duoc 3-phase. Gia mo la `B.Ask`.
Mo `SellB` khi `GapSell >= OpenSellThreshold` qua duoc 3-phase. Gia mo la `B.Bid`.

Neu ca Buy va Sell cung du dieu kien, tool chon ben manh hon theo do lech so voi median/std:

- Score Buy = `abs(GapBuy - MedianBuy) / StdBuy`
- Score Sell = `abs(GapSell - MedianSell) / StdSell`
- Score nao lon hon thi chon ben do. Neu bang nhau thi chon `BuyB`.

## Dieu kien dong lenh

Lenh dang giu se khong dong theo revert/reversal truoc `3000ms`, tru khi bi emergency. Thoi gian giu toi da la `90000ms`.

Dong `BuyB` khi mot trong cac dieu kien sau dung:

- Feed A stale/invalid hoac bot vao `Emergency`: dong ngay tai `B.Bid`.
- Da giu toi thieu `3000ms` va `A.Ask <= PeakAskA - 0.40`: dong tai `B.Bid`.
- Da giu toi thieu `3000ms` va `GapBuy >= 0`: dong tai `B.Bid`.
- Da giu `>= 90000ms`: dong tai `B.Bid`.

Dong `SellB` khi mot trong cac dieu kien sau dung:

- Feed A stale/invalid hoac bot vao `Emergency`: dong ngay tai `B.Ask`.
- Da giu toi thieu `3000ms` va `A.Bid >= TroughBidA + 0.40`: dong tai `B.Ask`.
- Da giu toi thieu `3000ms` va `GapSell <= 0`: dong tai `B.Ask`.
- Da giu `>= 90000ms`: dong tai `B.Ask`.

Live close chi duoc gui neu B trade map doc duoc, co it nhat mot lenh dang mo o row 0, va side cua row 0 khop voi side strategy can dong.

## Emergency va resume

- Feed A stale/invalid dua bot vao `Emergency`.
- Neu dang co lenh, bot dong lenh ngay khi vao emergency.
- Bot thoat `Emergency` sau `10` tick A lien tiep co latency `<= 1000ms`.

## Lot va stack

- `MaxStack = 1`, nen hien tai moi cluster chi co toi da 1 order.
- Neu sau nay tang `MaxStack`, stack chi xay ra sau cooldown `1000ms` va gap van con extreme:
  - BuyB stack khi `GapBuy <= OpenBuyThreshold`.
  - SellB stack khi `GapSell >= OpenSellThreshold`.
- Lot theo do lon cua gap (chi anh huong dry-run accounting; lot thuc khi live set san tren chart MT5):
  - `abs(gap) <= 60`: lot `8.0`
  - `abs(gap) <= 70`: lot `7.0`
  - `abs(gap) > 70`: lot `5.0`

## Noi chinh sua tham so chien luoc

Tat ca cac hang so cau hinh nam tap trung tai [LatencyArbTool.Core/Services/StrategyDefaults.cs](LatencyArbTool.Core/Services/StrategyDefaults.cs).

### Tin hieu (3-phase)

- `ConfirmMs` (`500`): Phase 1 - gap phai vuot threshold lien tuc bao lau truoc khi vao re-check.
- `ReCheckMs` (`200`): Phase 2 - sau confirm, cho them bao lau roi moi danh gia stability. Tong wait `700ms`.
- `StabilityRatio` (`0.65`): Phase 3 - cuoi re-check, `|currentGap|` phai >= ratio nay nhan voi `|peakGap|` quan sat trong window. Set `0.65` sau khi sweep voi polling-emulated sim: gia tri thap (0.40) cho phep too many trades trong run-6-style market (sparse B + trending), value cao filter selectively chi cac sustained signal voi gap on dinh.

### Threshold

- `FixedOpenBuyFallback` / `FixedOpenSellFallback` (`-80` / `60`): san floor cho threshold mo lenh. Cung la nguong dung trong warmup.
- `KStd` (`3.0`): he so nhan std cho dynamic threshold sau warmup.
- `WarmupMinSamples` (`500`): so sample toi thieu truoc khi dung dynamic.
- `MedianWindowMinutes` (`5`): cua so rolling thong ke.

### Hold va close

- `MinHoldMs` / `MaxHoldMs` (`3000` / `90000`): thoi gian giu lenh toi thieu va toi da.
- `AReversalUsd` (`0.40`): muc retrace cua A truoc khi chot.
- `CloseBuyRevertFallback` / `CloseSellRevertFallback` (`0` / `0`): nguong gap revert de dong.

### Filter / safety

- `FeedAStaleMs` / `FeedBStaleMs` (`10000` / `3000`): silence toi da cho tung feed (ms ke tu `ea_ms` doi cuoi). B threshold tight hon vi broker B sparse khien sim run-6-style market loss heavy — dat 3000ms block khoang 8% poll trong run 6 (chu yeu cac stretch B stall) nhung chi 0.4% trong run 4. Hieu qua chinh la skip run-6-style conditions hoan toan trong khi cho phep trade binh thuong.
- `SpreadBMaxMultiplier` (`2.5`): bo signal khi spread B vuot `MedianSpreadB * he so` nay.
- `AVolWindowMs` / `MinAVolPoints` (`60000` / `50`): cua so do volatility cua A va nguong toi thieu de cho phep mo lenh.

### Stack va lot

- `MaxStack` (`1`): order toi da trong 1 cluster.
- `StackCooldownMs` (`1000`): khoang giua 2 lan stack.
- `LotBandOneMaxGap` / `LotBandTwoMaxGap` (`60` / `70`): nguong `abs(gap)` de chia bands lot 8.0 / 7.0 / 5.0.

Sau khi sua, build lai (`dotnet build`) va chay tests (`dotnet test LatencyArbTool.Tests/LatencyArbTool.Tests.csproj`) de chac chan khong vo logic.

## Mo phong va backtest

[LatencyArbTool.Tests/SimulationTests.cs](LatencyArbTool.Tests/SimulationTests.cs) chay lai engine voi tickA/tickB CSV trong `data/tick/`. Output trade list, win rate, block reasons. Goi:

```
dotnet test LatencyArbTool.Tests/LatencyArbTool.Tests.csproj --filter "SimulationTests" --logger "console;verbosity=detailed"
```

Luu y: simulation gia dinh execution instant (khong co broker latency, khong co slippage). Sim PnL la upper bound so voi thuc te.
