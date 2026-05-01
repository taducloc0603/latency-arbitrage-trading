# Latency Arb Dry-Run

Tai lieu nay ghi lai cac thong so va dieu kien dong/mo lenh hien tai cua tool.

## Du lieu dau vao

- Map A mac dinh: `Local\MT_A_Tick`
- Map B mac dinh: `Local\MT_B_Tick`
- Tool doc tick tu shared memory, layout tick hien tai: `count:int32`, `ea_ms:uint64`, padding `4` bytes, `Bid`, `Ask`, `Spread`, `TickTimeMsc`, `Symbol[16]`.
- `ea_ms` la clock monotonic tu Windows `GetTickCount64` ben EA, khong phai Unix epoch.
- `Latency` duoc tinh uu tien bang `Environment.TickCount64 - ea_ms`. Gia tri hop le phai nam trong khoang `0..86_400_000ms` (24h).
- Neu `ea_ms` thieu/khong hop le, tool chi fallback sang `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - TickTimeMsc` khi `TickTimeMsc` la Unix epoch milliseconds hop le. Neu ca 2 nguon khong hop le thi hien `unknown`.
- Symbol A/B khong can giong nhau. Dieu kien `symbol mismatch` da duoc bo.

## Cong thuc va nguong

- `GapBuy = round((B.Bid - A.Ask) * 100)`
- `GapSell = round((B.Ask - A.Bid) * 100)`
- Rolling window: `5` phut.
- Can toi thieu `500` samples de dung nguong dong.
- Khi chua du `500` samples:
  - `OpenBuyThreshold = -50`
  - `OpenSellThreshold = 30`
- Khi du samples:
  - `OpenBuyThreshold = median(GapBuy) - 2.5 * std(GapBuy)`
  - `OpenSellThreshold = median(GapSell) + 2.5 * std(GapSell)`
- Nguong dong co dinh:
  - `CloseBuyRevert = -15`
  - `CloseSellRevert = 20`
- Feed stale:
  - Feed A stale neu latency khong hop le hoac `> 5000ms`.
  - Feed B stale neu latency khong hop le hoac `> 3000ms`.
- Spread B bat thuong neu `MedianSpreadB > 0` va `SpreadB > MedianSpreadB * 2.5`.

## Dieu kien mo lenh

Tool chi mo lenh moi khi:

- Dang khong co cluster/lenh dang giu.
- Bot khong o trang thai `Emergency`.
- Feed A khong stale.
- Feed B khong stale.
- Spread B khong bat thuong.
- Tin hieu duoc xac nhan lien tuc toi thieu `1000ms`.

Mo `BuyB` khi:

- `GapBuy <= OpenBuyThreshold` lien tuc toi thieu `1000ms`.
- Gia mo lenh la `B.Ask`.

Mo `SellB` khi:

- `GapSell >= OpenSellThreshold` lien tuc toi thieu `1000ms`.
- Gia mo lenh la `B.Bid`.

Neu ca Buy va Sell cung du dieu kien, tool chon ben manh hon theo do lech so voi median/std:

- Score Buy = `abs(GapBuy - MedianBuy) / StdBuy`
- Score Sell = `abs(GapSell - MedianSell) / StdSell`
- Score nao lon hon thi chon ben do. Neu bang nhau thi chon `BuyB`.

## Dieu kien dong lenh

Lenh dang giu se khong dong theo revert/reversal truoc `5000ms`, tru khi bi emergency. Thoi gian giu toi da la `90000ms`.

Dong `BuyB` khi mot trong cac dieu kien sau dung:

- Feed A stale/invalid hoac bot vao `Emergency`: dong ngay tai `B.Bid`.
- Da giu toi thieu `5000ms` va `A.Ask <= PeakAskA - 0.30`: dong tai `B.Bid`.
- Da giu toi thieu `5000ms` va `GapBuy >= -15`: dong tai `B.Bid`.
- Da giu `>= 90000ms`: dong tai `B.Bid`.

Dong `SellB` khi mot trong cac dieu kien sau dung:

- Feed A stale/invalid hoac bot vao `Emergency`: dong ngay tai `B.Ask`.
- Da giu toi thieu `5000ms` va `A.Bid >= TroughBidA + 0.30`: dong tai `B.Ask`.
- Da giu toi thieu `5000ms` va `GapSell <= 20`: dong tai `B.Ask`.
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
