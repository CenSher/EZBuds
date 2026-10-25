# HUAWEI FreeClip 2 — 控制协议摘要（Windows / SPP）

本文是 2026-10-20 在 FreeClip 2（`C0:DA:5E:**:**:**`）上实测并对照 OpenFreebuds 源码整理的协议笔记，供自研 Windows 客户端使用。

> 这是非官方协议，可能随固件变化。命令含义来自公开开源实现与真机抓包，不保证对所有型号/固件成立。

---

## 1. 传输层

| 项 | 值 |
|---|---|
| 类型 | Bluetooth Classic SPP（RFCOMM） |
| 通道 | **1**（FreeClip 2） |
| 服务 UUID | `00001101-0000-1000-8000-00805F9B34FB` |
| 配对 | Windows 设置中普通蓝牙配对一次 |
| 权限 | 普通用户即可，无需管理员/驱动 |

Windows 上可用：

- C#：`32feet.NET` 的 `BluetoothClient.Connect(new BluetoothEndPoint(addr, 1))`；
- C#：WinRT `RfcommDeviceService` + `StreamSocket`；
- Python：`socket.AF_BLUETOOTH` + `BTPROTO_RFCOMM`（3.11+）。

---

## 2. 帧格式

```text
+------+--------+------+--------+---------------------------+--------+
| 0x5A | LEN(2) | 0x00 | CMD(2) | TLV 参数 ...              | CRC(2) |
+------+--------+------+--------+---------------------------+--------+
```

- `LEN`：大端 16 位，等于 `len(body) + 1`，其中 `body = CMD + TLV`。
- **整帧长度 = LEN + 5**（1 字节 `5A` + 2 字节 LEN + 1 字节 `00` + body + 2 字节 CRC）。
- TLV：`param_id(1) | length(1) | value(length)`。
- `CRC`：CRC-16/XMODEM（poly `0x1021`，init `0`），覆盖除最后 2 字节外的全部字节，大端存放。

### 2.1 CRC 实现

```python
def crc16_xmodem(data: bytes) -> bytes:
    crc = 0
    for byte in data:
        crc ^= byte << 8
        for _ in range(8):
            crc = ((crc << 1) ^ 0x1021) & 0xFFFF if crc & 0x8000 else (crc << 1) & 0xFFFF
    return crc.to_bytes(2, "big")
```

### 2.2 组包

```python
body = cmd + b"".join(bytes([pid, len(val)]) + val for pid, val in params)
packet = b"Z" + (len(body) + 1).to_bytes(2, "big") + b"\x00" + body
packet += crc16_xmodem(packet)
```

### 2.3 收包（重要）

```python
total = int.from_bytes(buffer[1:3], "big") + 5   # 不是 +4
```

如果按 `LEN + 4` 收包，会少读 1 字节 CRC，导致：

- 第一帧还能解析（因为 CRC 不参与字段解析）；
- 但多设备枚举时，后续帧会被截断/错位，**只能看到第一台设备**。

这是本次第一版 PoC 只读到 PC、漏掉手机和平板的原因。

---

## 3. 命令表

| 命令 | 方向 | 参数 | 用途 |
|---|---|---|---|
| `0x0108` | 读 | `1,2,3`（空值） | 读取电量 |
| `0x0127` | 通知 | — | 电量变化推送 |
| `0x0107` | 读 | 0..31（空值） | 设备信息（型号/固件/序列号，可选） |
| `0x2b2f` | 读 | `1` | 读取双连接总开关 |
| `0x2b2e` | 写 | `1 = 1/0` | 设置双连接总开关 |
| `0x2b31` | 读 | `1`（空值） | 枚举设备槽位 |
| `0x2b32` | 写 | `1 = MAC(6B)` 或 `000000000000` | 设置/清除首选设备 |
| `0x2b33` | 写 | `id = 1..5`，值 = `MAC(6B)` | 连接/断开/取消配对/自动连接 |
| `0x2b36` | 通知 | — | 双连接状态变化事件 |

`0x2b33` 的 `id`：

| id | 动作 |
|---|---|
| 1 | CONNECT（连接指定设备） |
| 2 | DISCONNECT（断开指定设备） |
| 3 | UNPAIR（从耳机侧取消配对） |
| 4 | ENABLE_AUTO（开启该设备自动连接） |
| 5 | DISABLE_AUTO（关闭该设备自动连接） |

---

## 4. 电量

### 4.1 请求

```text
5a 0009 00 0108 01 00 02 00 03 00 fbb9
```

即：`0x0108`，参数 1、2、3，值为空。

### 4.2 响应（真机）

```text
5a 001b 00 0108
   01 01 60
   02 03 63 60 45
   03 03 00 00 00
   04 02 14 0a
   05 02 00 00
   06 01 0a
   cb fb
```

| 参数 | 值 | 含义 |
|---|---|---|
| `p1` | `0x60` | 综合电量 96% |
| `p2` | `63 60 45` | 左 99%、右 96%、仓 69% |
| `p3` | `00 00 00` | 左/右/仓充电标志（`0x01` = 充电） |
| `p4` | `14 0a` | 未知（可能为计数器/时间戳） |
| `p5` | `00 00` | 未知 |
| `p6` | `0a` | 未知 |

电量是 0–100 的独立字节，支持 1% 精度。不是所有型号都返回 `p2`；老型号可能只有 `p1` 综合值。

---

## 5. 双连接设备表

### 5.1 枚举请求

```text
5a 0005 00 2b31 01 00 f1ec
```

### 5.2 单条记录

以 PC 行为例：

```text
5a 003a 00 2b31
   02 01 03                  ; 槽位总数 = 3
   03 01 01                  ; 当前记录 index = 1
   04 06 xx xx xx 17 60 e4   ; MAC（小端字节序）
   05 01 01                  ; 连接状态 = 1
   06 01 04                  ; 未知
   07 01 00                  ; 首选 = 0
   08 01 01                  ; 自动连接 = 1
   09 0f "DESKTOP-XXXXXXX"   ; UTF-8 名称，长度 0x0f
   0a 01 01
   0b 01 00
   0c 01 00
   0d 01 00
   db c6
```

| 参数 | 含义 |
|---|---|
| `p2` | 槽位总数（本次 3） |
| `p3` | 记录索引（0..N-1） |
| `p4` | 设备 MAC，**小端**（发送命令时原样回传） |
| `p5` | 连接状态：`1` 已连接，`9` 正在播放；其他值未知 |
| `p7` | 是否首选设备（1/0） |
| `p8` | 是否自动连接（1/0） |
| `p9` | UTF-8 名称，长度由 TLV 给出 |
| `p10`–`p13` | 未知标志（本次均为 01/00/00/00） |

### 5.3 MAC 字节序

耳机返回的 `p4` 是 Windows 显示地址的**逆序**：

```text
显示：E4:60:17:**:**:**
原始：xx xx xx 17 60 E4
```

发送 `0x2b33` / `0x2b32` 时，直接使用 `p4` 的 6 个原始字节，**不要**把显示字符串再转一次。

### 5.4 真机完整结果

| 槽位 | 名称 | MAC | 已连接 | 自动连接 | 首选 |
|---|---|---|---|---|---|
| 0 | Karthy的MatePad Air | `34:D6:93:**:**:**` | 否 | 是 | 否 |
| 1 | DESKTOP-XXXXXXX | `E4:60:17:**:**:**` | 是 | 是 | 否 |
| 2 | HGAOZU**** | `70:A7:03:**:**:**` | 是 | 是 | 否 |

一次枚举请求会连续返回多条 `0x2b31` 帧，必须循环读到超时，不能只取第一帧。

---

## 6. 切换命令

### 6.1 断开本机（已实测）

```text
命令：0x2b33
参数：id = 2（DISCONNECT），值 = xx xx xx 17 60 e4
```

发送后耳机立即关闭 SPP 链路；0.70 秒后重新打开 RFCOMM channel 1 即恢复，手机连接未受影响。

### 6.2 连接手机

```text
命令：0x2b33
参数：id = 1（CONNECT），值 = be 1a 21 03 a7 70
```

### 6.3 设置首选设备

```text
命令：0x2b32
参数：1 = 6 字节 MAC；清除则用 00 00 00 00 00 00
```

### 6.4 自动连接

```text
命令：0x2b33
参数：id = 4（开启）或 5（关闭），值 = 6 字节 MAC
```

---

## 7. 实现注意事项

1. **帧长按 `LEN+5`**，否则多设备响应会被截断。
2. **校验 CRC**，不要解析长度不合法/CRC 错误的包。
3. 连接后耳机会主动推送 `0x0106` 和 `0x2bac`，忽略未知命令即可。
4. 一次请求可能返回多帧；按命令 ID 和 3–5 秒超时收集。
5. 设备名称是 UTF-8，长度可变；不要按固定长度解析。
6. 同一时间只能有一个 SPP 客户端；打开前先关闭旧 socket。
7. 耳机在充电盒内/关机时，RFCOMM 连接会超时；提示用户取出耳机。
8. Windows 休眠/蓝牙关闭后要重新枚举并重连。
9. 该协议没有应用层认证，安全性依赖蓝牙配对；不要暴露给不可信设备。
10. 命令 id 和字段含义来自参考实现，固件升级后需重新抓包验证。

---

## 8. 参考

- OpenFreebuds（GPL-3.0）：<https://github.com/melianmiko/OpenFreebuds>
- FreeClip 2 支持说明：<https://github.com/melianmiko/OpenFreebuds/blob/main/docs/devices/HUAWEI_FreeClip_2.md>
- 电量 handler：<https://github.com/melianmiko/OpenFreebuds/blob/main/openfreebuds/driver/huawei/handler/battery.py>
- 双连接 handler：<https://github.com/melianmiko/OpenFreebuds/blob/main/openfreebuds/driver/huawei/handler/dual_connect/handler.py>
- 命令常量：<https://github.com/melianmiko/OpenFreebuds/blob/main/openfreebuds/driver/huawei/constants/spp_commands.py>
- 组包/解析：<https://github.com/melianmiko/OpenFreebuds/blob/main/openfreebuds/driver/huawei/package.py>
- Windows 后端：<https://github.com/melianmiko/OpenFreebuds/blob/main/openfreebuds_backend/windows/bt_win32.py>
