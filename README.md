# Latency Arb Live

Tool theo doi 2 feed gia tren MT (A = san nhanh / lead, B = san cham / lag). Khi
gap A-B mo rong va duy tri du lau, bot mo lenh tren B theo huong B se catch-up A.
Dong lenh bang Stop-Loss, chuyen sang Trailing-Stop khi da co lai du nguong.

---

## 1. Cong thuc GAP (same-side A - B; don vi point; `point` tu config, vd 100)

```
GapBuy  = (int)(A.Bid * point) - (int)(B.Bid * point)
GapSell = (int)(A.Ask * point) - (int)(B.Ask * point)
```

- A cao hon B -> `GapBuy > 0`  -> BUY B.
- A thap hon B -> `GapSell < 0` -> SELL B.
- OpenSignalEngine: BUY khi `GapBuy >= x` ; SELL khi `GapSell <= -x`.

Vi du (`point=1`): A=4200, B=4100 -> GapBuy=+100 -> BUY B ; A=4100, B=4200 -> GapSell=-100 -> SELL B.

Luu y: gap nay KHONG tru spread (gom ca chenh spread giua A va B).

Code: [GapCalculator.cs](LatencyArbTool.Core/Services/GapCalculator.cs).

---

## 2. Dieu kien MO lenh (chi khi co signal)

Tham so: `x = open_pts`, `y = open_hold_confirm_ms`, `z = open_confirm_gap_pts`.
Signal phat ra khi (xem [OpenSignalEngine.cs](LatencyArbTool.Core/Services/OpenSignalEngine.cs)):

```
BUY  : GapBuy  >= z  giu lien tuc >= y ms,  va  GapBuy  cuoi >= x
SELL : GapSell <= -z giu lien tuc >= y ms,  va  GapSell cuoi <= -x
```

1. **Sustain (z)**: moi tick trong window phai co `|gap| >= z`. Tut xuong duoi z -> reset window.
2. **Confirm (y)**: dieu kien sustain giu lien tuc du `y` ms.
3. **Final (x)**: gap cuoi `|gap| >= x` thi ban signal.

> RULE BAT BUOC (khong duoc pha khi sua): viec **mo lenh chi thuc hien khi co
> signal**. **Dong lenh KHONG can signal** (SL/trailing chay moi tick). Mo & dong
> tuc thi, khong holding / cooldown. Moi thoi diem chi giu **mot** lenh (no stacking).

---

## 3. Dieu kien DONG lenh — Trailing Stop bam DINH (MaxPrice)

Tham so: `stop_loss_point`, `trailing_start_point`, `trailing_step_point`.
Thuan theo gia B. Quy uoc gia (theo MT5 thuc):

```
Entry   = gia fill   : BUY = B.Ask, SELL = B.Bid
Current = gia dong   : BUY = B.Bid, SELL = B.Ask
```
Tat ca quy ve point (`gia * point`). Xem [TrailingStopEngine.cs](LatencyArbTool.Core/Services/TrailingStopEngine.cs).

> ⚠️ **BUSINESS LOGIC — KHONG tu y doi.** Stop bam vao **MaxPrice** (dinh, BUY) /
> **MinPrice** (day, SELL) tinh **tu luc mo lenh** — KE CA khi chua kich hoat
> trailing. Truoc khi active, stop van truot theo dinh (`MaxPrice - stop_loss_point`),
> **KHONG** co dinh o Entry. Moi thay doi (doi moc tham chieu ve Entry, bo cap nhat
> Max khi chua active, doi thu tu cac buoc) phai duoc **chu du an xac nhan**. Test
> doi chung: `TrailingStopEngineTests` (`Buy_FullSequencePerSpec`,
> `Buy_StopTrailsMaxBeforeActivation`, `Sell_StopTrailsMinBeforeActivation`).

### Thuat toan (BUY; SELL guong voi MinPrice)
```
Ngay sau khi vao lenh: MaxPrice = Entry ; TrailingActive = false

Moi tick:
  1) MaxPrice = max(MaxPrice, Current)              // LUON cap nhat, ke ca chua active
  2) Neu Current >= Entry + trailing_start_point -> TrailingActive = true
  3) StopPrice = TrailingActive ? MaxPrice - trailing_step_point
                                : MaxPrice - stop_loss_point
  4) Neu Current <= StopPrice -> dong lenh
        reason = TrailingActive ? "trailing stop" : "stop loss"
```
SELL: MinPrice = min(MinPrice, Current); active khi `Current <= Entry - trailing_start_point`;
`StopPrice = MinPrice + (TrailingActive ? trailing_step_point : stop_loss_point)`; dong khi `Current >= StopPrice`.

### Vi du (Entry=1000, StopLoss=80, TrailingStart=50, TrailingStep=50)
```
BUY:
  1000 -> 1040 -> 1030          : Max=1040, stop=960, 1030>960 -> chua dong
  1040 -> 980 -> 960            : Max=1040, stop=960, 960<=960 -> dong (stop loss)
  1000 -> 990 -> 995            : gia chua vuot Entry, Max=1000, stop=920 -> chua dong
  1050 (active) -> 1120 -> 1070 : active tai 1050; Max=1120, stop=1120-50=1070 -> dong (trailing stop)
```
Diem mau chot: sau khi len 1040 roi quay dau, stop la **Max-80 = 960** (KHONG phai Entry-80 = 920).

PnL khi dong (point): `BUY = Current - Entry`, `SELL = Entry - Current`.

> **Hard SL broker-side (TRAILING).** App day SL that xuong broker (qua EA
> `PositionModify`) va **cap nhat lien tuc khi soft-stop truot len**. Broker kiem
> SL tren TUNG tick server-side nen chan duoc buoc nhay gia (gap) giua 2 lan app
> poll. Muc: `broker_SL = soft_stop ∓ hard_sl_buffer_pt` (BUY tru, SELL cong) —
> nam sau soft-stop dung `hard_sl_buffer_pt` diem, nen app van dong truoc trong
> nhip thuong, broker chi bat cac gap vuot qua. **Giam `hard_sl_buffer_pt` de siet
> gap sat hon** (0 = broker trung soft-stop). Chi day lai khi stop siet >= 10pt de
> gioi han so lan modify. Yeu cau EA bat AutoTrading.

---

## 4. Config tu Supabase

Tat ca tham so nam o bang `public.configs` ([db/schema.sql](db/schema.sql)). Moi PC
nap row moi nhat co `is_active = true` va `hostname = Environment.MachineName`
(phan biet HOA/thuong). Map cot:

| Config field | Cot DB |
|---|---|
| point (nhan gia) | `point` |
| x — gap cuoi | `open_pts` |
| y — confirm ms | `open_hold_confirm_ms` |
| z — sustain floor | `open_confirm_gap_pts` |
| StopLoss | `stop_loss_point` |
| TrailingStart | `trailing_start_point` |
| TrailingStep | `trailing_step_point` |
| Map tick A / B | `map_a` / `map_b` |
| HWND chart B / trade B | `chart_hwnd_b` / `trade_hwnd_b` |

Code nap: [SupabaseConfigRepository.cs](LatencyArbTool.Core/Services/SupabaseConfigRepository.cs)
(goi PostgREST `?hostname=eq.<host>&is_active=eq.true&order=created_at.desc&limit=1`).
UI nap khi bam **Load Config**; co the nap lai runtime.

### Setup Supabase (1 lan)

1. **Tao bang**: SQL Editor -> chay [db/schema.sql](db/schema.sql).
2. **Mo quyen doc + ghi cho anon** (neu bang da bat RLS). Doc de Load Config; update
   de nut **Save Config** ghi nguoc HWND / map names:
   ```sql
   create policy "anon read configs"
     on public.configs for select
     to anon using (true);
   create policy "anon update configs"
     on public.configs for update
     to anon using (true) with check (true);
   ```
3. **Insert row** cho tung may (lay hostname bang `echo %COMPUTERNAME%`):
   ```sql
   insert into public.configs
     (group_name, hostname, point, open_pts, open_hold_confirm_ms, open_confirm_gap_pts,
      stop_loss_point, trailing_start_point, trailing_step_point,
      map_a, map_b, chart_hwnd_b, trade_hwnd_b)
   values
     ('LAP 1', 'DESKTOP-XXXX', 100, 80, 1000, 30,
      50, 200, 30,
      'Local\MT_A_Tick', 'Local\MT_B_Tick', '0x00180070', '0x0085089A');
   ```

### Credentials (bien moi truong)

App doc 2 bien moi truong (KHONG commit key vao repo):

```
SUPABASE_URL       = https://<project>.supabase.co     (domain goc, KHONG kem /rest/v1)
SUPABASE_ANON_KEY  = <anon key>
```

---

## 5. Cau truc

- [LatencyArbTool.Core](LatencyArbTool.Core) — logic (gap, signal, trailing, config, readers). net10.0.
- [LatencyArbTool.App](LatencyArbTool.App) — WPF UI + orchestration ([MainViewModel.cs](LatencyArbTool.App/ViewModels/MainViewModel.cs)). net10.0-windows.
- [LatencyArbTool.Tests](LatencyArbTool.Tests) — xUnit.
- `DataExporter/MQ5` — EA MT5 ghi tick/trade/history vao shared memory (giu nguyen).
- `native/mt5engine-capi` — DLL C/C++ click/close MT5 (giu nguyen).

## 6. Build & test (dev)

```
dotnet build LatencyArbTool.sln
dotnet test LatencyArbTool.Tests/LatencyArbTool.Tests.csproj
```

---

## 7. Build & deploy tren VPS (Windows)

### CI tu dong (khuyen nghi)

Workflow [build-release-windows.yml](.github/workflows/build-release-windows.yml) chay
khi push len branch `dev`: build solution (Release) -> test -> build native DLL
(MSVC) -> publish self-contained win-x64 -> tao **GitHub Release** `v1.0.<run_number>`
voi asset:
- `LatencyArbTool.App-<ver>.exe` (single-file self-contained)
- `LatencyArbTool.App-<ver>-portable-win-x64.zip` (folder portable, kem `mt5engine-capi.dll`)

> Luu y: moi push len `dev` deu cat 1 release moi. Neu chua muon phat hanh ban dang
> do, push vao branch khac roi mo PR.

### Trien khai len VPS bang script

VPS **khong can .NET SDK** (ban self-contained). Dung [deploy/update.ps1](deploy/update.ps1):

```powershell
# Lay <ver> = 1.0.<run_number> tu trang Releases tren GitHub.
# Lan dau: set luon Supabase creds.
.\update.ps1 -Version 1.0.34 `
  -SupabaseUrl "https://<project>.supabase.co" `
  -SupabaseAnonKey "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...."

# Cac lan sau (env da luu) chi can:
.\update.ps1 -Version 1.0.35
```

Script se: stop app dang chay -> backup `C:\LatencyArbTool\app` -> tai zip release ->
giai nen -> validate co `LatencyArbTool.App.exe` + `mt5engine-capi.dll` -> set env
Supabase (neu truyen) -> tao shortcut Desktop -> start app.

### Chuan bi MT5 tren VPS

1. Mo MT5, mo chart san A va san B.
2. Attach EA `DataExporter` ([DataExporter/MQ5](DataExporter/MQ5)) len tung chart de ghi
   shared memory: `Local\MT_A_Tick`, `Local\MT_B_Tick` (+ map trade/history cua B).
3. Lay **HWND** cua chart B (de click buy/sell) va panel trade B (de close), dien vao
   row config (`chart_hwnd_b`, `trade_hwnd_b`).
4. **Lot size set thu cong tren chart MT5** — tool khong set lot.

### Chay lan dau

1. Mo app (shortcut) -> **Load Config** (status bao `Loaded '<group>' for <host>`).
2. **Check Maps** -> A / B / BTrade = Connected.
3. **Start** -> theo doi Event Log: mo lenh khi du signal (x/y/z), dong bang SL/trailing.
4. **Test tren tai khoan DEMO truoc** khi chay that.

### Build tu source tren VPS (tuy chon)

Neu muon build truc tiep (khong qua release):

```powershell
# 1. Cai .NET 10 SDK
winget install --id Microsoft.DotNet.SDK.10 -e

# 2. Native DLL can Build Tools for Visual Studio (workload C++).
#    Trong "x64 Native Tools Command Prompt for VS":
cl /nologo /std:c++17 /EHsc /DWIN32 /D_WINDOWS /D_USRDLL /D_WINDLL /DUNICODE /D_UNICODE ^
   /I native\mt5engine-capi /c native\mt5engine-capi\engine.cpp /Fo:native-build\engine.obj
cl /nologo /std:c++17 /EHsc /DWIN32 /D_WINDOWS /D_USRDLL /D_WINDLL /DUNICODE /D_UNICODE ^
   /I native\mt5engine-capi /c native\mt5engine-capi\c_api.cpp /Fo:native-build\c_api.obj
link /nologo /DLL /OUT:native-build\mt5engine-capi.dll native-build\engine.obj ^
   native-build\c_api.obj user32.lib kernel32.lib comctl32.lib

# 3. Publish app self-contained
dotnet publish LatencyArbTool.App\LatencyArbTool.App.csproj -c Release -r win-x64 `
  --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish
Copy-Item native-build\mt5engine-capi.dll publish\mt5engine-capi.dll -Force

# 4. Set env Supabase (User scope) roi chay publish\LatencyArbTool.App.exe
setx SUPABASE_URL "https://<project>.supabase.co"
setx SUPABASE_ANON_KEY "eyJ...."
```
