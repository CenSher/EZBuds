"""Explicit, single-shot FreeClip 2 connection-lock experiment.

Only replays the observed 0x2b33 TLV 8/9 for the enumerated PC MAC.
Does not clear preferred devices, disconnect devices, or retry mutations.
"""
import argparse
import datetime
import json
from pathlib import Path
import socket
import time

from freeclip2_probe import build_packet, crc16_xmodem

EARBUD = "AA:BB:CC:DD:EE:FF"
PC = "11:22:33:44:55:66"


def decode(raw):
    if len(raw) < 8 or raw[0] != 0x5A or raw[3] != 0:
        return None
    if len(raw) != int.from_bytes(raw[1:3], "big") + 5 or crc16_xmodem(raw[:-2]) != raw[-2:]:
        return None
    pos, fields = 6, {}
    while pos < len(raw) - 2:
        if pos + 2 > len(raw) - 2:
            return None
        tag, size = raw[pos:pos+2]
        if pos + 2 + size > len(raw) - 2:
            return None
        fields[tag] = raw[pos+2:pos+2+size]
        pos += 2 + size
    return raw[4:6], fields


class Session:
    def __init__(self, sock, log):
        self.sock, self.log, self.buffer = sock, log, b""

    def send(self, cmd, fields):
        raw = build_packet(cmd, fields)
        self.sock.sendall(raw)
        self.log.append({"direction": "tx", "utc": datetime.datetime.now(datetime.timezone.utc).isoformat(), "hex": raw.hex()})

    def receive(self, seconds):
        until, out = time.monotonic()+seconds, []
        while time.monotonic() < until:
            self.sock.settimeout(max(0.05, until-time.monotonic()))
            try:
                chunk = self.sock.recv(4096)
            except socket.timeout:
                break
            if not chunk:
                raise ConnectionError("Earbud closed SPP")
            self.buffer += chunk
            while self.buffer:
                start = self.buffer.find(b"\x5a")
                if start < 0:
                    self.buffer = b""; break
                self.buffer = self.buffer[start:]
                if len(self.buffer) < 4:
                    break
                total = int.from_bytes(self.buffer[1:3], "big")+5
                if total < 8 or self.buffer[3] != 0:
                    self.buffer = self.buffer[1:]; continue
                if len(self.buffer) < total:
                    break
                raw = self.buffer[:total]
                parsed = decode(raw)
                if parsed is None:
                    self.buffer = self.buffer[1:]; continue
                self.buffer = self.buffer[total:]
                cmd, fields = parsed
                row = {"direction": "rx", "utc": datetime.datetime.now(datetime.timezone.utc).isoformat(), "hex": raw.hex(), "command": cmd.hex(), "fields": {str(k): v.hex() for k,v in fields.items()}}
                self.log.append(row); out.append((cmd, fields))
        return out

    def devices(self):
        self.send(b"\x2b\x31", [(1,b"")])
        rows = []
        for cmd, fields in self.receive(1.5):
            if cmd != b"\x2b\x31" or len(fields.get(4,b"")) != 6:
                continue
            rows.append({"name": fields.get(9,b"").decode("utf-8","replace"),
                         "mac": ":".join("%02X"%v for v in fields[4][::-1]),
                         "connected": bool(fields.get(5,b"\0")[0]),
                         "fields": {str(k):v.hex() for k,v in fields.items()}})
        return rows


def self_test():
    mac = bytes.fromhex("97258d1760e4")
    assert build_packet(b"\x2b\x33", [(8,mac)]).hex() == "5a000b002b33080697258d1760e4f32d"
    assert build_packet(b"\x2b\x33", [(9,mac)]).hex() == "5a000b002b33090697258d1760e4b4fe"
    reply = bytes.fromhex("5a0009002b331e01081f01001742")
    assert decode(reply) == (b"\x2b\x33", {30:b"\x08",31:b"\0"})
    assert decode(reply[:-1]+b"\0") is None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("status","on","off"), nargs="?", default="status")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    self_test()
    if args.self_test:
        print("Captured on/off frames and strict reply validation: PASS"); return 0
    result = {"action": args.action, "earbud": EARBUD, "target": PC, "traffic": []}
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    output = Path(__file__).resolve().parent.parent/".scratch"/("connection-lock-%s-%s.json"%(args.action,stamp))
    output.parent.mkdir(parents=True,exist_ok=True)
    try:
        with socket.socket(socket.AF_BLUETOOTH,socket.SOCK_STREAM,socket.BTPROTO_RFCOMM) as sock:
            sock.settimeout(8); sock.connect((EARBUD,1))
            session = Session(sock,result["traffic"])
            session.receive(0.4)
            result["before"] = session.devices()
            if args.action != "status":
                target = next((r for r in result["before"] if r["mac"] == PC and r["connected"]),None)
                if target is None:
                    raise RuntimeError("Target PC not enumerated as connected; no mutation sent")
                if args.action == "on" and sum(r["connected"] for r in result["before"]) < 2:
                    raise RuntimeError("Need phone and PC connected; no mutation sent")
                operation = 8 if args.action == "on" else 9
                session.send(b"\x2b\x33",[(operation,bytes.fromhex(target["fields"]["4"]))])
                result["mutation_sent"] = True
                replies = session.receive(1.2)
                result["observed_matching_reply"] = any(cmd==b"\x2b\x33" and p.get(30)==bytes([operation]) and p.get(31)==b"\0" for cmd,p in replies)
                result["after"] = session.devices()
            result["result"] = "completed"
    except Exception as error:
        result["result"], result["error"] = "failed", repr(error)
    finally:
        output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
    print(json.dumps({k:v for k,v in result.items() if k != "traffic"},ensure_ascii=True,indent=2))
    print("Evidence: "+str(output))
    return 0 if result["result"] == "completed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
