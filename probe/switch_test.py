import socket
import time

ADDR = "AA:BB:CC:DD:EE:FF"
PC_NAME = "DESKTOP-XXXXXXX"


def crc16(data):
    crc = 0
    for b in data:
        crc ^= b << 8
        for _ in range(8):
            crc = ((crc << 1) ^ 0x1021) & 0xFFFF if crc & 0x8000 else (crc << 1) & 0xFFFF
    return crc.to_bytes(2, "big")


def pkg(cmd, params=()):
    body = cmd
    for pid, val in params:
        body += bytes([pid, len(val)]) + val
    raw = b"Z" + (len(body) + 1).to_bytes(2, "big") + b"\x00" + body
    return raw + crc16(raw)


def parse(data):
    if len(data) < 6 or data[0] != 0x5A:
        return None
    length = int.from_bytes(data[1:3], "big")
    cmd, params, pos = data[4:6], {}, 6
    while pos < length + 3 and pos + 1 < len(data):
        size = data[pos + 1]
        params[data[pos]] = data[pos + 2:pos + 2 + size]
        pos += size + 2
    return cmd, params


def recv(sock, seconds):
    end, buf, out = time.time() + seconds, b"", []
    while time.time() < end:
        sock.settimeout(max(0.2, end - time.time()))
        try:
            chunk = sock.recv(4096)
        except socket.timeout:
            break
        if not chunk:
            print("  socket closed by peer")
            break
        buf += chunk
        while len(buf) >= 4 and buf[0] == 0x5A:
            total = int.from_bytes(buf[1:3], "big") + 5
            if len(buf) < total:
                break
            out.append(buf[:total])
            buf = buf[total:]
    return out


def enumerate_rows(sock):
    sock.sendall(pkg(b"\x2b\x31", [(1, b"")]))
    rows = {}
    for raw in recv(sock, 1.5):
        parsed = parse(raw)
        if not parsed or parsed[0] != b"\x2b\x31":
            continue
        p = parsed[1]
        if len(p.get(4, b"")) < 6:
            continue
        rows[p[3][0]] = {
            "name": p.get(9, b"").decode("utf-8", "ignore"),
            "raw_mac": p[4],
            "connected": p.get(5, b"\x00")[0] > 0,
        }
    return rows


s = socket.socket(socket.AF_BLUETOOTH, socket.SOCK_STREAM, socket.BTPROTO_RFCOMM)
s.settimeout(10)
s.connect((ADDR, 1))
print("connected")
time.sleep(0.4)
rows = enumerate_rows(s)
for idx, row in rows.items():
    print(f"  slot {idx}: {row['name']} connected={row['connected']} raw={row['raw_mac'].hex()}")

target = next((r for r in rows.values() if r["name"] == PC_NAME), None)
if target is None:
    print("PC row not found; abort")
    s.close()
    raise SystemExit(1)

print(f"sending DISCONNECT for {PC_NAME} ({target['raw_mac'].hex()})")
s.sendall(pkg(b"\x2b\x33", [(2, target["raw_mac"])]))
for raw in recv(s, 2.0):
    parsed = parse(raw)
    print("  RX", parsed[0].hex() if parsed else raw.hex())
s.close()
print("socket closed locally")

# wait, then cold-reconnect over RFCOMM and verify the link is back
for attempt in range(1, 5):
    time.sleep(1.0)
    print(f"reconnect attempt {attempt} ...")
    t0 = time.time()
    try:
        s2 = socket.socket(socket.AF_BLUETOOTH, socket.SOCK_STREAM, socket.BTPROTO_RFCOMM)
        s2.settimeout(8)
        s2.connect((ADDR, 1))
        print(f"  reconnected in {time.time()-t0:.2f}s")
        time.sleep(0.3)
        rows2 = enumerate_rows(s2)
        for idx, row in rows2.items():
            print(f"  slot {idx}: {row['name']} connected={row['connected']}")
        s2.close()
        if any(r["name"] == PC_NAME and r["connected"] for r in rows2.values()):
            print("RESULT: disconnect command worked, PC link restored")
            raise SystemExit(0)
        print("RESULT: reconnected but PC row not marked connected")
        raise SystemExit(2)
    except OSError as exc:
        print(f"  failed: {exc}")
print("RESULT: could not reconnect")
raise SystemExit(3)
