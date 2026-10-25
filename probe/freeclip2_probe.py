#!/usr/bin/env python3
"""HUAWEI FreeClip 2 - read-only Windows probe.

Reads, over Bluetooth Classic SPP (RFCOMM channel 1):
  * global / left / right / charging-case battery percentages
  * dual-connect slot table (name, MAC, connected, playing, preferred, auto-connect)

No settings are changed. Tested with CPython 3.11 on Windows 11
(socket.AF_BLUETOOTH / BTPROTO_RFCOMM).

Protocol reference: OpenFreebuds (GPL-3.0), docs/devices/HUAWEI_FreeClip_2.md.
"""
import argparse
import json
import socket
import sys
import time

DEFAULT_ADDRESS = "AA:BB:CC:DD:EE:FF"
DEFAULT_CHANNEL = 1

CMD_BATTERY_READ = b"\x01\x08"
CMD_DC_ENUMERATE = b"\x2b\x31"
CMD_DC_ENABLED_READ = b"\x2b\x2f"


def crc16_xmodem(data: bytes) -> bytes:
    crc = 0
    for byte in data:
        crc ^= byte << 8
        for _ in range(8):
            crc = ((crc << 1) ^ 0x1021) & 0xFFFF if crc & 0x8000 else (crc << 1) & 0xFFFF
    return crc.to_bytes(2, "big")


def build_packet(cmd: bytes, params=()) -> bytes:
    body = cmd
    for param_id, value in params:
        body += bytes([param_id, len(value)]) + value
    packet = b"Z" + (len(body) + 1).to_bytes(2, "big") + b"\x00" + body
    return packet + crc16_xmodem(packet)


def parse_packet(data: bytes):
    if len(data) < 6 or data[0] != 0x5A or data[3] != 0:
        return None
    length = int.from_bytes(data[1:3], "big")
    cmd, params, pos = data[4:6], {}, 6
    while pos < length + 3 and pos + 1 < len(data):
        size = data[pos + 1]
        params[data[pos]] = data[pos + 2:pos + 2 + size]
        pos += size + 2
    return cmd, params


def receive(sock, seconds):
    end, buffer, packets = time.time() + seconds, b"", []
    while time.time() < end:
        sock.settimeout(max(0.2, end - time.time()))
        try:
            chunk = sock.recv(4096)
        except socket.timeout:
            break
        if not chunk:
            break
        buffer += chunk
        while len(buffer) >= 4 and buffer[0] == 0x5A:
            # LEN counts body+1; total frame = 1 + 2 + 1 + body + 2 = LEN + 5.
            total = int.from_bytes(buffer[1:3], "big") + 5
            if len(buffer) < total:
                break
            packets.append(buffer[:total])
            buffer = buffer[total:]
    return packets


def request_all(sock, cmd, params, response_cmd, seconds=3.0):
    sock.sendall(build_packet(cmd, params))
    out = []
    for raw in receive(sock, seconds):
        parsed = parse_packet(raw)
        if parsed and parsed[0] == response_cmd:
            out.append(parsed[1])
    return out


def request(sock, cmd, params, response_cmd, seconds=3.0):
    responses = request_all(sock, cmd, params, response_cmd, seconds)
    return responses[0] if responses else None


def mac_from_le(raw: bytes) -> str:
    return ":".join(f"{byte:02X}" for byte in reversed(raw))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--address", default=DEFAULT_ADDRESS)
    parser.add_argument("--channel", type=int, default=DEFAULT_CHANNEL)
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args()

    sock = socket.socket(socket.AF_BLUETOOTH, socket.SOCK_STREAM, socket.BTPROTO_RFCOMM)
    sock.settimeout(10)
    try:
        sock.connect((args.address, args.channel))
    except OSError as exc:
        print(f"无法连接 {args.address} (channel {args.channel}): {exc}", file=sys.stderr)
        print("请确认耳机已从充电盒取出、处于开机状态。", file=sys.stderr)
        return 1
    try:
        receive(sock, 0.6)  # drain initial 0x0106 / 0x2bac pushes
        battery_params = request(sock, CMD_BATTERY_READ, [(1, b""), (2, b""), (3, b"")], CMD_BATTERY_READ)
        if battery_params is None:
            print("未收到电量响应", file=sys.stderr)
            return 2
        levels = battery_params.get(2, b"")
        battery = {
            "global": battery_params.get(1, b"\x00")[0],
            "left": levels[0] if len(levels) >= 3 else None,
            "right": levels[1] if len(levels) >= 3 else None,
            "case": levels[2] if len(levels) >= 3 else None,
            "charging": b"\x01" in battery_params.get(3, b""),
        }

        slots, devices, enabled = None, {}, None
        for _ in range(3):
            for params in request_all(sock, CMD_DC_ENUMERATE, [(1, b"")], CMD_DC_ENUMERATE, seconds=2.0):
                if len(params.get(4, b"")) < 6:
                    continue
                slots = int.from_bytes(params.get(2, b"\x00"), "big")
                index = int.from_bytes(params.get(3, b"\x00"), "big")
                state = params.get(5, b"\x00")[0]
                devices[index] = {
                    "index": index,
                    "name": params.get(9, b"").decode("utf-8", "ignore"),
                    "mac": mac_from_le(params[4]),
                    "connected": state > 0,
                    "playing": state == 9,
                    "preferred": params.get(7, b"\x00")[0] == 1,
                    "auto_connect": params.get(8, b"\x00")[0] == 1,
                }
            if slots is not None and len(devices) >= slots:
                break

        enabled_params = request(sock, CMD_DC_ENABLED_READ, [(1, b"")], CMD_DC_ENABLED_READ, seconds=2.0)
        if enabled_params is not None and len(enabled_params.get(1, b"")) == 1:
            enabled = enabled_params[1][0] == 1

        result = {
            "address": args.address,
            "battery": battery,
            "dual_connect": {
                "enabled": enabled,
                "slots": slots,
                "devices": [devices[key] for key in sorted(devices)],
            },
        }
        if args.json:
            print(json.dumps(result, ensure_ascii=False, indent=2))
        else:
            print(f"电量: 左 {battery['left']}% / 右 {battery['right']}% / 仓 {battery['case']}% "
                  f"(综合 {battery['global']}%, 充电: {'是' if battery['charging'] else '否'})")
            print(f"双连接: {'开启' if enabled else '关闭/未知'}, 槽位 {slots}")
            for device in result["dual_connect"]["devices"]:
                flags = []
                if device["connected"]:
                    flags.append("已连接")
                if device["playing"]:
                    flags.append("播放中")
                if device["preferred"]:
                    flags.append("首选")
                if device["auto_connect"]:
                    flags.append("自动连接")
                print(f"  [{device['index']}] {device['name']} ({device['mac']}) {' '.join(flags)}")
        return 0
    finally:
        sock.close()


if __name__ == "__main__":
    sys.exit(main())
