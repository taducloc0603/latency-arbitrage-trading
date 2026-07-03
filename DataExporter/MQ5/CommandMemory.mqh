// CommandMemory.mqh
// Kênh lệnh app -> EA qua shared memory. Hiện chỉ có 1 loại lệnh: đặt SL cứng
// (hard SL) cho một position theo ticket. App ghi ticket + sl rồi tăng cmd_seq;
// EA thấy cmd_seq mới thì gọi PositionModify và ghi ack lại cho app đọc.
#property strict

#include "SharedMemoryBase.mqh"
#include "BinaryHelper.mqh"

#define CMD_MEMORY_SIZE 64
// Layout:
//   0  int32  cmd_seq     (app tăng dần, 0 = chưa có lệnh)
//   4  int32  reserved
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

   void Process() {
      if(!IsValid()) return;

      uchar buf[];
      ArrayResize(buf, CMD_HEADER_READ_SIZE);
      RtlMoveMemory(buf, m_pMem, CMD_HEADER_READ_SIZE);

      int seq = CBinaryHelper::UnpackInt32(buf, 0);
      if(seq == 0 || seq == m_lastSeq) return;
      m_lastSeq = seq;

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
