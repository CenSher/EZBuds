# FreeClip 2 Windows Probe

只读验证脚本：通过 Windows 蓝牙 SPP（RFCOMM channel 1）读取 HUAWEI FreeClip 2 的电量和双连接设备表。

## 环境

- Windows 10/11
- Python 3.11+（Windows 版需支持 `socket.AF_BLUETOOTH`）
- FreeClip 2 已在 Windows 设置中完成蓝牙配对
- 耳机已从充电盒取出、处于开机状态

## 运行

```powershell
# 人类可读
py -3.11 probe\freeclip2_probe.py

# JSON（方便接入其他程序）
py -3.11 probe\freeclip2_probe.py --json
```

如果设备地址不是默认值：

```powershell
py -3.11 probe\freeclip2_probe.py --address AA:BB:CC:DD:EE:FF --channel 1
```

## 预期输出

```json
{
  "address": "C0:DA:5E:**:**:**",
  "battery": {
    "global": 96,
    "left": 99,
    "right": 96,
    "case": 69,
    "charging": false
  },
  "dual_connect": {
    "enabled": true,
    "slots": 3,
    "devices": [
      {"index": 0, "name": "Karthy的MatePad Air", "mac": "34:D6:93:**:**:**",
       "connected": false, "playing": false, "preferred": false, "auto_connect": true},
      {"index": 1, "name": "DESKTOP-XXXXXXX", "mac": "E4:60:17:**:**:**",
       "connected": true, "playing": false, "preferred": false, "auto_connect": true},
      {"index": 2, "name": "HGAOZU****", "mac": "70:A7:03:**:**:**",
       "connected": true, "playing": false, "preferred": false, "auto_connect": true}
    ]
  }
}
```

## 切换测试（可选，会断开本机）

```powershell
py -3.11 probe\switch_test.py
```

该脚本只对 `DESKTOP-XXXXXXX` 这个槽位发送 `DISCONNECT`，然后立即通过 RFCOMM 重连并确认恢复；**不会断开手机**。运行时会短暂断开 PC 的蓝牙链路（约 1–2 秒）。

## 常见问题

| 现象 | 原因/处理 |
|---|---|
| `WinError 10060` 连接超时 | 耳机在充电盒里/关机；取出耳机后重试 |
| `WinError 10049` / 地址错误 | 检查 MAC 格式，必须是 `AA:BB:CC:DD:EE:FF` |
| 能连上但读不到电量 | 固件/型号不支持该命令；抓包确认命令 ID |
| 只看到一台设备 | 收包长度按 `LEN+4` 截断了；必须按 `LEN+5` |
| 中文名称乱码 | 控制台编码问题；用 `--json` 或 `chcp 65001` |

协议细节见 [../PROTOCOL.md](../PROTOCOL.md)，可行性结论见 [../FEASIBILITY.md](../FEASIBILITY.md)。
