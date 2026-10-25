# FreeClip 2 锁定电脑后音频异常

核查日期：2026-10-22（北京时间）。耳机 HUAWEI FreeClip 2，电脑 DESKTOP-XXXXXXX。固件版本尚未核实；本记录不是硬件故障鉴定。

本机只读查询：Windows 11 家庭版中文版，版本/内部版本 `10.0.26300`；Intel 无线 Bluetooth（USB VID_8087/PID_0026），Intel 驱动 `24.20.0.3`，驱动日期 2025-12-18，INF `oem28.inf`。本轮没有更新或回滚驱动，也没有核实其是否为最新版本。原始查询结果位于抓取目录的 `windows-version.json` 和 `bluetooth-driver-versions.json`。

## 当前结论

手机官方智慧音频锁定电脑，也会出现与电脑控制软件相同的故障：播放卡顿，暂停后无声，Windows 耳机输出消失，而蓝牙连接仍保留。因此不能只归因于电脑软件的小锁按钮、当次锁定报文或操作后补读。

完整抓取显示，故障处出现耳机侧发往电脑的 AVDTP Close 请求，Windows 返回接受；耳机侧随后发出两个 L2CAP Disconnect 请求，Windows 应答，音频输出成为 UNPLUGGED。证据支持沿耳机固件与 Windows 蓝牙音频兼容性继续调查，不足以唯一判定固件、驱动或物理硬件故障。合法 Close 报文本身也不能解释为什么随后不能恢复播放。

## 官方应用对照

1. 此前仅重启 Windows Audio 服务，用户确认声音恢复。
2. 电脑软件保持托盘驻留，用户通过手机智慧音频开启电脑的“本次连接锁定”。
3. 持续播放后暂停约 5 秒，再尝试播放。用户确认锁定成功、先卡顿、暂停后无声且 Windows 输出消失。
4. 电脑日志从本次抓取开始至故障时仅有托盘心跳，没有锁定写入或打开面板；首次故障后的面板展开为 11:50:37，晚于端点消失约 18 秒。

本轮排除了电脑软件在故障时发送锁定或即时补读，不代表已排除所有此前系统状态的影响。没有退出软件、重启系统后重新进行完全隔离的对照，不应扩大结论。

## 关键时间线及原始报文

抓取目录：`.scratch/audio-lock-phone-control-20261022-114758`。

微软原版 BluetoothStack WPR 记录：11:48:20—11:51:20，180 秒，793951 事件、0 丢失。跟踪已自动停止。ETL 文件约 62 MiB；原始记录、XML、音频事件、设备状态及软件日志均保留。

| 电脑本地时间 | 证据 |
|---|---|
| 11:49:08.506 | BthA2dp: A2dpStreamingStart，耳机地址 ***（C0:DA:5E:**:**:**） |
| 11:49:19 | 只读端点状态 ACTIVE，默认输出为耳机，未静音，音量约 35%，采样峰值约 0.228 |
| 11:50:19.353686 | 耳机侧 → 电脑，L2CAP CID 0x0041，AVDTP `60 08 dc`：单包、事务 6、Command、Close、ACP SEID 55 |
| 11:50:19.353831 | BthA2dp: IsConnected=false、IsSepOpen=false、IsStreaming=false，AvrcpRegistered=true |
| 11:50:19.353867 | 电脑 → 耳机侧，CID 0x00f1，AVDTP `62 08`：同事务 ResponseAccept |
| 11:50:19.378728 | 耳机侧发出 L2CAP Disconnect Request，通道 0x0045/0x00f5；电脑应答 |
| 11:50:19.382467 | 耳机侧发出 L2CAP Disconnect Request，通道 0x0041/0x00f1；电脑应答 |
| 11:50:19.4089689 | Windows Audio 事件 65：FreeClip 输出 NewState=8（UNPLUGGED） |

原始 HCI ACL 负载（包含 ACL 与 L2CAP 头）：

```
Close request: 00210700030041006008dc
Close accept:  000106000200f1006208
Disconnect:   00210c0008000100060f04004500f500
Response:     00010c0008000100070f04004500f500
Disconnect:   00210c0008000100061004004100f100
Response:     00010c0008000100071004004100f100
```

方向依据：本次 Windows 事件的 BIP_Type=4 与电脑发出的 SPP 电量/设备查询对应，BIP_Type=3 与耳机返回对应；同一方向映射还与电脑开始播放时的 AVDTP Start 请求/接受一致。这里没有把 BIP_Type 数值直接当作标准 H4 类型；没有观察本次链路建立，不能仅凭 ACL handle 推断远端身份，需结合 BthA2dp 地址和 SPP 设备信息。事件 XML 的时区尾缀被 tracerpt 输出为 +07:59，本表按与 Windows Audio 事件相符的本地时间字段对齐，不据此计算跨设备 UTC 亚秒顺序。

协议解释依据：Wireshark 上游 AVDTP 解码定义 `0x08=Close`、`0x0a=Abort`，二者不能混用；`0=Command`、`2=ResponseAccept`。

## 恢复与限制

- 先前一轮：手机解锁不能恢复；重开 Windows 蓝牙只恢复输出设备，播放仍失败；随后经用户授权重启 Audiosrv，用户确认声音恢复。
- 本轮完整故障抓取保存后，经用户指示继续恢复，11:53:39 仅重启 Audiosrv；输出仍显示断开。用户随后按提示在手机解除锁定、重开 Windows 蓝牙并重新播放，确认“现在恢复了”。没有要求再次锁定，没有自动发出任何耳机控制指令。
- 两种恢复顺序均出现过，但没有验证每次都可恢复，不能把它包装为锁定功能的修复。
- 本轮未修改/发布电脑端锁定实现，未更改驱动、音频格式、独占模式或耳机固件。正常启动仍仅驻留托盘。
- 下一阶段先核实耳机固件与电脑蓝牙驱动版本，必要时在另一台电脑做同样的官方应用对照，以区分固件共性与当前电脑兼容性；不再重复本机同一故障。

参考：

- 微软蓝牙抓取：https://github.com/microsoft/busiotools/blob/master/bluetooth/tracing/readme.md
- Windows 音频状态：https://learn.microsoft.com/en-us/windows/win32/coreaudio/device-state-xxx-constants
- AVDTP 解码定义：https://github.com/wireshark/wireshark/blob/master/epan/dissectors/packet-btavdtp.c
