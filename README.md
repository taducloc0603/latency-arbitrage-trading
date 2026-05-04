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
  - `OpenBuyThreshold = -80`
  - `OpenSellThreshold = 60`
- Khi du samples:
  - `OpenBuyThreshold = median(GapBuy) - 3.0 * std(GapBuy)`
  - `OpenSellThreshold = median(GapSell) + 3.0 * std(GapSell)`
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
- Tin hieu duoc xac nhan lien tuc toi thieu `300ms`.
- B trade map phai doc duoc va khong co lenh B dang mo; neu khong verify duoc thi live open bi chan.

Mo `BuyB` khi:

- `GapBuy <= OpenBuyThreshold` lien tuc toi thieu `300ms`.
- Gia mo lenh la `B.Ask`.

Mo `SellB` khi:

- `GapSell >= OpenSellThreshold` lien tuc toi thieu `300ms`.
- Gia mo lenh la `B.Bid`.

Neu ca Buy va Sell cung du dieu kien, tool chon ben manh hon theo do lech so voi median/std:

- Score Buy = `abs(GapBuy - MedianBuy) / StdBuy`
- Score Sell = `abs(GapSell - MedianSell) / StdSell`
- Score nao lon hon thi chon ben do. Neu bang nhau thi chon `BuyB`.

## Dieu kien dong lenh

Lenh dang giu se khong dong theo revert/reversal truoc `15000ms`, tru khi bi emergency. Thoi gian giu toi da la `90000ms`.

Dong `BuyB` khi mot trong cac dieu kien sau dung:

- Feed A stale/invalid hoac bot vao `Emergency`: dong ngay tai `B.Bid`.
- Da giu toi thieu `15000ms` va `A.Ask <= PeakAskA - 0.80`: dong tai `B.Bid`.
- Da giu toi thieu `15000ms` va `GapBuy >= 0`: dong tai `B.Bid`.
- Da giu `>= 90000ms`: dong tai `B.Bid`.

Live close chi duoc gui neu B trade map doc duoc, co it nhat mot lenh dang mo o row 0, va side cua row 0 khop voi side strategy can dong.

Dong `SellB` khi mot trong cac dieu kien sau dung:

- Feed A stale/invalid hoac bot vao `Emergency`: dong ngay tai `B.Ask`.
- Da giu toi thieu `15000ms` va `A.Bid >= TroughBidA + 0.80`: dong tai `B.Ask`.
- Da giu toi thieu `15000ms` va `GapSell <= 0`: dong tai `B.Ask`.
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
- `ConfirmMs` (mac dinh `300`): thoi gian tin hieu phai duy tri lien tuc truoc khi mo lenh.
- `MinHoldMs` / `MaxHoldMs` (mac dinh `15000` / `90000`): thoi gian giu lenh toi thieu va toi da.
- `FixedOpenBuyFallback` / `FixedOpenSellFallback` (mac dinh `-80` / `60`): nguong open khi chua du `WarmupMinSamples` mau.
- `CloseBuyRevertFallback` / `CloseSellRevertFallback` (mac dinh `0` / `0`): nguong gap revert de dong lenh.
- `AReversalUsd` (mac dinh `0.80`): muc retrace cua A truoc khi chot.
- `KStd` (mac dinh `3.0`): he so nhan std cho threshold dong sau warmup.
- `FeedAStaleMs` / `FeedBStaleMs` (mac dinh `5000` / `3000`): thoi gian latency cho phep cho tung feed.
- `SpreadBMaxMultiplier` (mac dinh `2.5`): bo qua tin hieu khi spread B vuot `MedianSpreadB * he so` nay.
- `AVolWindowMs` / `MinAVolPoints` (mac dinh `60000` / `50`): cua so do volatility cua A va nguong toi thieu.

Sau khi sua, build lai (`dotnet build`) va chay tests (`dotnet test LatencyArbTool.Tests/LatencyArbTool.Tests.csproj`) de chac chan khong vo logic.

## Vi du tu anh chup

Voi thong so:

- `GapBuy = -27`
- `GapSell = 17`
- `OpenBuyThreshold = -78`
- `OpenSellThreshold = 68`
- `Bot state = Emergency`
- `Latency = unknown`

Ket luan:

- Chua the mo `BuyB` vi `-27` chua `<= -78`.
- Chua the mo `SellB` vi `17` chua `>= 68`.
- Bot dang `Emergency`, nen khong mo lenh moi.
- `Latency unknown` lam feed bi xem la invalid/stale.
