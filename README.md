# Latency Arb Live

Tai lieu nay ghi lai cac thong so va dieu kien dong/mo lenh hien tai cua tool.

## Du lieu dau vao

- Map A mac dinh: `Local\MT_A_Tick`
- Map B mac dinh: `Local\MT_B_Tick`
- Tool doc tick tu shared memory, layout tick hien tai: `count:int32`, `ea_ms:uint64`, padding `4` bytes, `Bid`, `Ask`, `Spread`, `TickTimeMsc`, `Symbol[16]`.
- `ea_ms` la clock monotonic tu Windows `GetTickCount64` ben EA, khong phai Unix epoch.
- `Latency` duoc tinh uu tien bang `Environment.TickCount64 - ea_ms`. Gia tri hop le phai nam trong khoang `0..86_400_000ms` (24h).
- Neu `ea_ms` thieu/khong hop le, tool chi fallback sang `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - TickTimeMsc` khi `TickTimeMsc` la Unix epoch milliseconds hop le. Neu ca 2 nguon khong hop le thi hien `unknown`.
- Live safety chi check lenh that o feed B. Tu `MapNameB`, tool derive map trade/history bang cach thay hau to `Tick`: `Local\MT_B_Tick` -> `Local\MT_B_Trade` va `Local\MT_B_History`. Rieng trade co fallback `Local\MT_B_Trades` de tuong thich writer cu.
- Symbol A/B khong can giong nhau. Dieu kien `symbol mismatch` da duoc bo.

## Cong thuc va nguong

- `GapBuy = round((B.Bid - A.Ask) * 100)`
- `GapSell = round((B.Ask - A.Bid) * 100)`
- Rolling window: `5` phut.
- Can toi thieu `500` samples de dung nguong dong.
- Khi chua du `500` samples:
  - `OpenBuyThreshold = -50`
  - `OpenSellThreshold = 35`
- Khi du samples:
  - `OpenBuyThreshold = min(median(GapBuy) - 3.0 * std(GapBuy), -50)`
  - `OpenSellThreshold = max(median(GapSell) + 3.0 * std(GapSell), 35)`
  - Dynamic chi duoc dung khi extreme hon fallback. Neu market calm khien `median ± 3*std` chua du extreme, bot van dung `-50 / +35` lam san. Tranh viec mo lenh tren noise nho khi market it bien dong.
- Nguong dong co dinh:
  - `CloseBuyRevert = 0`
  - `CloseSellRevert = 0`
- Feed stale:
  - Feed A stale neu latency khong hop le hoac `> 5000ms`.
  - Feed B stale neu latency khong hop le hoac `> 3000ms`.
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
- Tin hieu duoc xac nhan theo 2 phase:
  - Phase 1 (Confirm): gap phai duoi/tren threshold lien tuc toi thieu `350ms`.
  - Phase 2 (Re-check): sau Confirm, cho them `150ms`. Trong suot 2 phase, gap khong duoc roi khoi vung threshold.
  - Phase 3 (Stability): cuoi Re-check, kiem tra gap hien tai phai con >= `70%` cua peak gap quan sat duoc trong 2 phase. Filter cac signal "qua dinh" (peak roi nhung dang revert), chi giu signal sustained.
- B trade map phai doc duoc va khong co lenh B dang mo; neu khong verify duoc thi live open bi chan.

Mo `BuyB` khi:

- `GapBuy <= OpenBuyThreshold` lien tuc qua Confirm + Re-check (`350+150=500ms`) va stability check pass (current >= 70% peak).
- Gia mo lenh la `B.Ask`.

Mo `SellB` khi:

- `GapSell >= OpenSellThreshold` lien tuc qua Confirm + Re-check (`350+150=500ms`) va stability check pass.
- Gia mo lenh la `B.Bid`.

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

Live close chi duoc gui neu B trade map doc duoc, co it nhat mot lenh dang mo o row 0, va side cua row 0 khop voi side strategy can dong.

Dong `SellB` khi mot trong cac dieu kien sau dung:

- Feed A stale/invalid hoac bot vao `Emergency`: dong ngay tai `B.Ask`.
- Da giu toi thieu `3000ms` va `A.Bid >= TroughBidA + 0.40`: dong tai `B.Ask`.
- Da giu toi thieu `3000ms` va `GapSell <= 0`: dong tai `B.Ask`.
- Da giu `>= 90000ms`: dong tai `B.Ask`.

## Emergency va resume

- Feed A stale/invalid dua bot vao `Emergency`.
- Neu dang co lenh, bot dong lenh ngay khi vao emergency.
- Bot thoat `Emergency` sau `10` tick A lien tiep co latency `<= 1000ms`.

## Lot va stack

- `MaxStack = 1`, nen hien tai moi cluster chi co toi da 1 order.
- Neu sau nay tang `MaxStack`, stack chi xay ra sau cooldown `1000ms` va gap van con extreme:
  - BuyB stack khi `GapBuy <= OpenBuyThreshold`.
  - SellB stack khi `GapSell >= OpenSellThreshold`.
- Lot theo do lon cua gap:
  - `abs(gap) <= 60`: lot `8.0`
  - `abs(gap) <= 70`: lot `7.0`
  - `abs(gap) > 70`: lot `5.0`

## Noi chinh sua tham so chien luoc

Tat ca cac hang so cau hinh nam tap trung tai [LatencyArbTool.Core/Services/StrategyDefaults.cs](LatencyArbTool.Core/Services/StrategyDefaults.cs). Khi muon tinh chinh chien luoc, sua truc tiep cac hang so trong file nay:

- `MaxStack` (mac dinh `1`): so order toi da trong 1 cluster. Tang len `2` hoac `3` khi muon stack nhieu lenh tren cung tin hieu (sau cooldown va gap van extreme).
- `StackCooldownMs` (mac dinh `1000`): khoang thoi gian toi thieu giua 2 lan stack.
- `LotBandOneMaxGap` / `LotBandTwoMaxGap` (mac dinh `60` / `70`): nguong `abs(gap)` de chia bands lot 8.0 / 7.0 / 5.0. Lot thuc te khi live duoc set san tren chart MT5, day chi anh huong PnL accounting noi bo dry run.
- `ConfirmMs` (mac dinh `350`): Phase 1 - thoi gian tin hieu phai duy tri lien tuc truoc khi vao Phase Re-check.
- `ReCheckMs` (mac dinh `150`): Phase 2 - sau khi Confirm dat, cho them khoang nay roi moi danh gia stability. Tong cong `ConfirmMs + ReCheckMs = 500ms` truoc khi fire signal.
- `StabilityRatio` (mac dinh `0.70`): Phase 3 - tai cuoi Re-check, gap hien tai phai >= `StabilityRatio * |peakGap|` (peak observed trong toan bo confirm + recheck window). Vi du peak=-100 va StabilityRatio=0.7 thi current phai <= -70 moi fire. Filter signal "dinh-roi-revert" — gap nhay extreme nhung dang catch up.
- `MinHoldMs` / `MaxHoldMs` (mac dinh `3000` / `90000`): thoi gian giu lenh toi thieu va toi da. Da giam tu `15000ms` xuong `3000ms` vi gap arb chi keo dai 1-2s; giu lau hon chi expose vao market drift, lam mat winning trades khi A trend nguoc lai.
- `FixedOpenBuyFallback` / `FixedOpenSellFallback` (mac dinh `-50` / `35`): nguong open khi chua du `WarmupMinSamples` mau.
- `CloseBuyRevertFallback` / `CloseSellRevertFallback` (mac dinh `0` / `0`): nguong gap revert de dong lenh.
- `AReversalUsd` (mac dinh `0.40`): muc retrace cua A truoc khi chot. Da giam tu `$0.80` xuong `$0.40` de close som hon khi A bat dau dao chieu, han che loss khi market trend di nguoc.
- `KStd` (mac dinh `3.0`): he so nhan std cho threshold dong sau warmup.
- `FeedAStaleMs` / `FeedBStaleMs` (mac dinh `5000` / `3000`): thoi gian latency cho phep cho tung feed.
- `SpreadBMaxMultiplier` (mac dinh `2.5`): bo qua tin hieu khi spread B vuot `MedianSpreadB * he so` nay.
- `AVolWindowMs` / `MinAVolPoints` (mac dinh `60000` / `50`): cua so do volatility cua A va nguong toi thieu.

Sau khi sua, build lai (`dotnet build`) va chay tests (`dotnet test LatencyArbTool.Tests/LatencyArbTool.Tests.csproj`) de chac chan khong vo logic.