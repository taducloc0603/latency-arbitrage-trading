// ControlMemory.mqh
// Kênh điều khiển tối giản app -> EA (KHÔNG phải lệnh giao dịch, không cần
// AutoTrading). Hiện chỉ 1 tín hiệu: app tăng resetSeq khi nhấn Start để EA
// re-baseline history (map chỉ còn deal của phiên mới).
#property strict

#include "SharedMemoryBase.mqh"
#include "BinaryHelper.mqh"

#define CTRL_MEMORY_SIZE 16   // int32 resetSeq @0 (app tăng dần; 0 = chưa có)

class CControlMemory : public CSharedMemoryBase {
private:
   int m_lastResetSeq;

public:
   CControlMemory(string memory_name) : CSharedMemoryBase(memory_name, CTRL_MEMORY_SIZE) {
      m_lastResetSeq = 0;
   }

   // Gọi sau Init(): bỏ qua seq cũ còn sót trong map từ phiên trước.
   void Seed() {
      m_lastResetSeq = ReadSeq();
   }

   // True đúng một lần mỗi khi app tăng resetSeq (nhấn Start).
   bool ResetRequested() {
      int seq = ReadSeq();
      if(seq != 0 && seq != m_lastResetSeq) {
         m_lastResetSeq = seq;
         return true;
      }
      return false;
   }

private:
   int ReadSeq() {
      if(!IsValid()) return 0;
      uchar buf[];
      ArrayResize(buf, 4);
      RtlMoveMemory(buf, m_pMem, 4);
      return CBinaryHelper::UnpackInt32(buf, 0);
   }
};
