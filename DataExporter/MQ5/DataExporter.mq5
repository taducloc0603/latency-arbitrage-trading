#property strict
#property copyright "2026 Hoang Anh"
#property description "Lấy Tick, Trades, History từ MT5"

#include "Configs.mqh"
#include "TradesMemory.mqh"
#include "HistoryMemory.mqh"
#include "TickMemory.mqh"
#include "ControlMemory.mqh"

CTradesMemory* g_trades = NULL;
CHistoryMemory* g_history = NULL;
CTickMemory* g_tick = NULL;
CControlMemory* g_ctrl = NULL;

// Update định kỳ để map không bị stale khi miss event OnTrade
// (và để Profit của trades chạy theo giá).
#define TRADES_REFRESH_MS 100
#define HISTORY_REFRESH_MS 500
ulong g_lastTradesRefreshMs = 0;
ulong g_lastHistoryRefreshMs = 0;

input string EA_CHANNEL_ID = "A";
input int UPDATE_INTERVAL_MS = 1;  // cập nhật mỗi 1ms (1000 = 1s)

string TRADES_MEMORY_NAME = StringFormat("Local\\MT_%s_Trades", EA_CHANNEL_ID);
string HISTORY_MEMORY_NAME = StringFormat("Local\\MT_%s_History", EA_CHANNEL_ID);
string TICK_MEMORY_NAME = StringFormat("Local\\MT_%s_Tick", EA_CHANNEL_ID);
string CONTROL_MEMORY_NAME = StringFormat("Local\\MT_%s_Ctrl", EA_CHANNEL_ID);

int OnInit() {

   g_trades = new CTradesMemory(TRADES_MEMORY_NAME);
   if(!g_trades.Init()) {
      Print(StringFormat("[X] Tạo Trade memory thất bại: %s ", TRADES_MEMORY_NAME));
      delete g_trades;
      return INIT_FAILED;
   }
   
   g_history = new CHistoryMemory(HISTORY_MEMORY_NAME);
   if(!g_history.Init()) {
      Print(StringFormat("[X] Tạo History memory thất bại: %s ", HISTORY_MEMORY_NAME));
      delete g_trades;
      delete g_history;
      return INIT_FAILED;
   }

   g_tick = new CTickMemory(TICK_MEMORY_NAME);
   if(!g_tick.Init()) {
      Print(StringFormat("[X] Tạo Tick memory thất bại: %s ", TICK_MEMORY_NAME));
      delete g_trades;
      delete g_history;
      delete g_tick;
      return INIT_FAILED;
   }

   g_ctrl = new CControlMemory(CONTROL_MEMORY_NAME);
   if(!g_ctrl.Init()) {
      Print(StringFormat("[X] Tạo Control memory thất bại: %s ", CONTROL_MEMORY_NAME));
      delete g_trades;
      delete g_history;
      delete g_tick;
      delete g_ctrl;
      return INIT_FAILED;
   }
   g_ctrl.Seed();

   g_trades.Update();
   g_history.Update();
   
   EventSetMillisecondTimer(UPDATE_INTERVAL_MS);
   Print("[OK] Khởi động thành công. Channel ID: ", EA_CHANNEL_ID);
   return INIT_SUCCEEDED;
}

void OnTick() {
   g_tick.Update();
}

void OnTrade() {
   g_trades.Update();
   g_history.Update();
}

void OnTimer() {
   // App nhấn Start -> re-baseline history để map chỉ còn deal của phiên mới.
   if(g_ctrl.ResetRequested()) g_history.ResetSession();

   ulong now = GetTickCount64();
   if(now - g_lastTradesRefreshMs >= TRADES_REFRESH_MS) {
      g_lastTradesRefreshMs = now;
      g_trades.Update();
   }
   if(now - g_lastHistoryRefreshMs >= HISTORY_REFRESH_MS) {
      g_lastHistoryRefreshMs = now;
      g_history.Update();
   }
}

void OnDeinit(const int reason) {
   EventKillTimer();

   if(g_trades) delete g_trades;
   if(g_history) delete g_history;
   if(g_tick) delete g_tick;
   if(g_ctrl) delete g_ctrl;

   Print("[OK] Dọn dẹp xong.");
}