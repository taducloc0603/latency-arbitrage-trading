import mmap
import struct
import os
import time as time_mod
import ctypes
from datetime import datetime, timezone

channelID = input("channelID: ")
get_tick_count = ctypes.windll.kernel32.GetTickCount64


def read_trades_realtime():
    last_ts = 0
    with mmap.mmap(-1, 4096, f"Local\\MT_{channelID}_Trades") as shm:
        while True:
            try:
                shm.seek(0)
                count = struct.unpack('i', shm.read(4))[0]
                ea_ms = struct.unpack('Q', shm.read(8))[0]
                shm.read(4)

                if ea_ms == last_ts:
                    time_mod.sleep(0.01)
                    continue
                last_ts = ea_ms
                now_ms = get_tick_count()

                trades = []
                for _ in range(count):
                    ticket = struct.unpack('Q', shm.read(8))[0]
                    lot = struct.unpack('d', shm.read(8))[0]
                    price = struct.unpack('d', shm.read(8))[0]
                    sl = struct.unpack('d', shm.read(8))[0]
                    tp = struct.unpack('d', shm.read(8))[0]
                    profit = struct.unpack('d', shm.read(8))[0]
                    trade_type = struct.unpack('i', shm.read(4))[0]
                    time_msc = struct.unpack('Q', shm.read(8))[0]
                    open_ea_time_local = struct.unpack('Q', shm.read(8))[0]
                    symbol = shm.read(32).decode('utf-8', errors='ignore').rstrip('\x00')
                    trades.append({
                        'ticket': ticket,
                        'type': 'BUY' if trade_type == 0 else 'SELL',
                        'lot': lot, 'price': price,
                        'sl': sl, 'tp': tp,
                        'profit': profit, 'time_msc': time_msc,
                        'open_ea_time_local': open_ea_time_local,
                        'symbol': symbol
                    })

                os.system('cls')
                print(f"{'='*70}")
                print(f"  EA: {ea_ms}  |  Py: {now_ms}  |  Delay: {now_ms - ea_ms} ms  |  Trades: {count}")
                print(f"{'='*70}")
                if trades:
                    print(f"  {'Symbol':<10} {'Ticket':<12} {'Type':<6} {'Lot':<8} {'Price':<12} {'SL':<12} {'TP':<12} {'Profit':<10} {'Time':<26} {'EA Open (ms)'}")
                    print(f"  {'─'*115}")
                    for t in trades:
                        time_str = datetime.fromtimestamp(t['time_msc'] / 1000, tz=timezone.utc).strftime('%Y-%m-%d %H:%M:%S.') + f"{t['time_msc'] % 1000:03d}"
                        print(f"  {t['symbol']:<10} {t['ticket']:<12} {t['type']:<6} {t['lot']:<8.2f} {t['price']:<12.5f} {t['sl']:<12.5f} {t['tp']:<12.5f} {t['profit']:<10.2f} {time_str:<26} {t['open_ea_time_local']}")
                else:
                    print("  No open trades")
                print(f"{'='*70}")
                time_mod.sleep(0.05)
            except KeyboardInterrupt:
                print("\nStopped.")
                break
            except Exception as e:
                print(f"Error: {e}")
                time_mod.sleep(0.5)


def read_history_realtime():
    last_ts = 0
    with mmap.mmap(-1, 16384, f"Local\\MT_{channelID}_History") as shm:
        while True:
            try:
                shm.seek(0)
                count = struct.unpack('i', shm.read(4))[0]
                ea_ms = struct.unpack('Q', shm.read(8))[0]
                shm.read(4)

                if ea_ms == last_ts:
                    time_mod.sleep(0.01)
                    continue
                last_ts = ea_ms
                now_ms = get_tick_count()

                history = []
                for _ in range(count):
                    ticket = struct.unpack('Q', shm.read(8))[0]
                    trade_type = struct.unpack('i', shm.read(4))[0]
                    volume = struct.unpack('d', shm.read(8))[0]
                    open_price = struct.unpack('d', shm.read(8))[0]
                    close_price = struct.unpack('d', shm.read(8))[0]
                    sl = struct.unpack('d', shm.read(8))[0]
                    tp = struct.unpack('d', shm.read(8))[0]
                    commission = struct.unpack('d', shm.read(8))[0]
                    profit = struct.unpack('d', shm.read(8))[0]
                    open_time_msc = struct.unpack('Q', shm.read(8))[0]
                    close_time_msc = struct.unpack('Q', shm.read(8))[0]
                    close_ea_time_local = struct.unpack('Q', shm.read(8))[0]
                    symbol = shm.read(32).decode('utf-8', errors='ignore').rstrip('\x00')
                    history.append({
                        'ticket': ticket,
                        'symbol': symbol,
                        'type': 'BUY' if trade_type == 0 else 'SELL',
                        'volume': volume,
                        'open_price': open_price,
                        'close_price': close_price,
                        'sl': sl, 'tp': tp,
                        'commission': commission,
                        'profit': profit,
                        'open_time_msc': open_time_msc,
                        'close_time_msc': close_time_msc,
                        'close_ea_time_local': close_ea_time_local
                    })

                os.system('cls')
                print(f"{'='*130}")
                print(f"  EA: {ea_ms}  |  Py: {now_ms}  |  Delay: {now_ms - ea_ms} ms  |  History: {count}")
                print(f"{'='*130}")
                if history:
                    print(f"  {'Symbol':<10} {'Ticket':<12} {'Type':<6} {'Vol':<8} {'Open':<12} {'Close':<12} {'SL':<12} {'TP':<12} {'Comm':<10} {'Profit':<10} {'Open Time':<26} {'Close Time':<26} {'EA Close (ms)'}")
                    print(f"  {'─'*165}")
                    for h in history:
                        ot = datetime.fromtimestamp(h['open_time_msc'] / 1000, tz=timezone.utc).strftime('%Y-%m-%d %H:%M:%S.') + f"{h['open_time_msc'] % 1000:03d}"
                        ct = datetime.fromtimestamp(h['close_time_msc'] / 1000, tz=timezone.utc).strftime('%Y-%m-%d %H:%M:%S.') + f"{h['close_time_msc'] % 1000:03d}"
                        print(f"  {h['symbol']:<10} {h['ticket']:<12} {h['type']:<6} {h['volume']:<8.2f} {h['open_price']:<12.5f} {h['close_price']:<12.5f} {h['sl']:<12.5f} {h['tp']:<12.5f} {h['commission']:<10.2f} {h['profit']:<10.2f} {ot:<26} {ct:<26} {h['close_ea_time_local']}")
                else:
                    print("  No history")
                print(f"{'='*130}")
                time_mod.sleep(0.05)
            except KeyboardInterrupt:
                print("\nStopped.")
                break
            except Exception as e:
                print(f"Error: {e}")
                time_mod.sleep(0.5)


def read_tick_realtime():
    last_ts = 0
    with mmap.mmap(-1, 256, f"Local\\MT_{channelID}_Tick") as shm:
        while True:
            try:
                shm.seek(0)
                count = struct.unpack('i', shm.read(4))[0]
                ea_ms = struct.unpack('Q', shm.read(8))[0]
                shm.read(4)

                if ea_ms == last_ts:
                    time_mod.sleep(0.01)
                    continue
                last_ts = ea_ms
                now_ms = get_tick_count()

                if count == 0:
                    continue

                bid = struct.unpack('d', shm.read(8))[0]
                ask = struct.unpack('d', shm.read(8))[0]
                spread = struct.unpack('d', shm.read(8))[0]
                C = struct.unpack('Q', shm.read(8))[0]
                symbol = shm.read(16).decode('utf-8', errors='ignore').rstrip('\x00')

                os.system('cls')
                print(f"{'='*70}")
                print(f"  EA: {ea_ms}  |  Py: {now_ms}  |  Delay: {now_ms - ea_ms} ms")
                print(f"{'='*70}")
                print(f"  Symbol : {symbol}")
                print(f"  Bid    : {bid}")
                print(f"  Ask    : {ask}")
                print(f"  Spread : {spread}")
                print(f"{'='*70}")
                time_mod.sleep(0.05)
            except KeyboardInterrupt:
                print("\nStopped.")
                break
            except Exception as e:
                print(f"Error: {e}")
                time_mod.sleep(0.5)


print("1. Trades")
print("2. History")
print("3. Tick")
choice = input("Choose: ")

if choice == "1":
    read_trades_realtime()
elif choice == "2":
    read_history_realtime()
elif choice == "3":
    read_tick_realtime()
