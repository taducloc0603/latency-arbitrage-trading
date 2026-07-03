// CommandMemory.mqh
// Kênh lệnh app -> EA qua shared memory. App ghi payload + opcode rồi tăng
// cmd_seq; EA thấy cmd_seq mới thì xử lý theo opcode và ghi ack cho app đọc.
//   opcode 1 = set hard SL cho position theo ticket (PositionModify)
//   opcode 2 = reset baseline history (bắt đầu phiên mới, xoá deal cũ khỏi map)
//
// Phụ thuộc thứ tự include: DataExporter.mq5 include HistoryMemory.mqh TRƯỚC
// header này nên CHistoryMemory đã có định nghĩa khi Process() được biên dịch.
#property strict

#include "SharedMemoryBase.mqh"
#include "BinaryHelper.mqh"

#define CMD_MEMORY_SIZE 64
#define CMD_OP_SET_SL 1
#define CMD_OP_RESET_HISTORY 2
// Layout:
//   0  int32  cmd_seq     (app tăng dần, 0 = chưa có lệnh)
//   4  int32  opcode      (1 = set SL, 2 = reset history)
//   8  ulong  ticket
//   16 double sl_price    (0 = xóa SL)
//   24 int32  ack_seq     (EA ghi = cmd_seq sau khi xử lý)
//   28 int32  ack_result  (1 = OK, 0 = fail)
//   32 int32  ack_retcode (retcode từ server, để debug)
#define CMD_HEADER_READ_SIZE 24
#define CMD_ACK_OFFSET 24

class CCommandMemory : public CSharedMemoryBase {
private:
   int m_lastSeq;

public:
   CCommandMemory(string memory_name) : CSharedMemoryBase(memory_name, CMD_MEMORY_SIZE) {
      m_lastSeq = 0;
   }

   // Gọi sau Init(): bỏ qua lệnh cũ còn sót trong map từ phiên trước.
   void SeedFromExisting() {
      if(!IsValid()) return;
      uchar buf[];
      ArrayResize(buf, CMD_HEADER_READ_SIZE);
      RtlMoveMemory(buf, m_pMem, CMD_HEADER_READ_SIZE);
      m_lastSeq = CBinaryHelper::UnpackInt32(buf, 0);
   }

   void Process(CHistoryMemory *history) {
      if(!IsValid()) return;

      uchar buf[];
      ArrayResize(buf, CMD_HEADER_READ_SIZE);
      RtlMoveMemory(buf, m_pMem, CMD_HEADER_READ_SIZE);

      int seq = CBinaryHelper::UnpackInt32(buf, 0);
      if(seq == 0 || seq == m_lastSeq) return;
      m_lastSeq = seq;

      int opcode = CBinaryHelper::UnpackInt32(buf, 4);

      if(opcode == CMD_OP_RESET_HISTORY) {
         if(history != NULL) history.ResetSession();
         WriteAck(seq, history != NULL, 0);
         PrintFormat("[Cmd] reset history -> %s", history != NULL ? "OK" : "FAIL");
         return;
      }

      // opcode CMD_OP_SET_SL (mặc định, gồm cả 0 cho tương thích ngược).
      ulong ticket = CBinaryHelper::UnpackUInt64(buf, 8);
      double sl = CBinaryHelper::UnpackDouble(buf, 16);

      int retcode = 0;
      bool ok = ApplySl(ticket, sl, retcode);
      WriteAck(seq, ok, retcode);
      PrintFormat("[Cmd] set SL ticket=%I64u sl=%.5f -> %s (retcode=%d)",
                  ticket, sl, ok ? "OK" : "FAIL", retcode);
   }

private:
   bool ApplySl(ulong ticket, double sl, int &retcode) {
      if(!PositionSelectByTicket(ticket)) {
         retcode = -1; // position không tồn tại (đã đóng?)
         return false;
      }

      string symbol = PositionGetString(POSITION_SYMBOL);
      int digits = (int)SymbolInfoInteger(symbol, SYMBOL_DIGITS);

      MqlTradeRequest req;
      MqlTradeResult res;
      ZeroMemory(req);
      ZeroMemory(res);
      req.action = TRADE_ACTION_SLTP;
      req.position = ticket;
      req.symbol = symbol;
      req.sl = NormalizeDouble(sl, digits);
      req.tp = PositionGetDouble(POSITION_TP);

      bool sent = OrderSend(req, res);
      retcode = (int)res.retcode;
      return sent && res.retcode == TRADE_RETCODE_DONE;
   }

   void WriteAck(int seq, bool ok, int retcode) {
      uchar buf[];
      ArrayResize(buf, 12);
      ArrayInitialize(buf, 0);
      CBinaryHelper::PackInt32(buf, 0, seq);
      CBinaryHelper::PackInt32(buf, 4, ok ? 1 : 0);
      CBinaryHelper::PackInt32(buf, 8, retcode);
      RtlMoveMemory(m_pMem + CMD_ACK_OFFSET, buf, 12);
   }
};
