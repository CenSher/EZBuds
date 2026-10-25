# 多型号版支持范围

本版在 FreeClip 2 基板上增加耳机选择、型号选择和按经典蓝牙地址记忆配置。
这里的“适配”指电量读取，以及对应型号已提供的耳机侧设备连接管理；不代表已移植 OpenFreebuds 的全部设置项。

## 使用

1. 在 Windows 蓝牙设置中配对并连接耳机。
2. 本次发布打开 `releases/device-config-20261022/FreeClipControl.Shell.exe`，右键托盘图标 → 设置 → 耳机配置。
3. 在“选择耳机和型号”中选择已配对的耳机，再选择实际型号，点击“应用到这副耳机”。
4. 型号保存在 `%LOCALAPPDATA%/HuaweiHeadsetControl/headsets.json`，以耳机地址为键；改名和重启不影响映射。

名称匹配到已知型号时可自动使用对应配置。每次启动优先选择 Windows 当前唯一已连接的可识别耳机；多副同时连接时，仅在上次选择的耳机也已连接时保留它，否则要求手动选择。没有耳机连接时保留原选择。完成启动选择后，运行中不会因连接状态变化而自动改换用户手动选择的设备。
选择型号只保存本地配置；随后仅在 Windows 已连接该耳机时读取，不为选择型号自动连接、配对或发送设置写命令。

## 型号表

| 型号 | RFCOMM 通道 | 电量界面 | 耳机侧设备连接管理 |
|---|---:|---|---|
| HUAWEI FreeClip 2 | 1 | 左 / 右 / 盒 | 支持；提供按设备读取、施加及解除本次连接锁定 |
| HUAWEI FreeClip | 1 | 左 / 右 / 盒 | 支持 |
| HUAWEI FreeBuds 3 | 1 | 分项，按实际返回 | 不提供 |
| HUAWEI FreeBuds 4i | 16 | 左 / 右 / 盒 | 不提供 |
| HUAWEI FreeBuds 5i | 16 | 左 / 右 / 盒 | 支持 |
| HUAWEI FreeBuds 6i | 1 | 左 / 右 / 盒 | 支持 |
| HUAWEI FreeBuds 7i | 1 | 左 / 右 / 盒 | 支持；独立噪声控制档位映射 |
| HUAWEI FreeBuds Pro / Pro 2 / Pro 3 / Pro 4 / Pro 5 | 1 | 左 / 右 / 盒 | 支持 |
| HUAWEI FreeBuds SE | 16 | 分项，按实际返回 | 不提供 |
| HUAWEI FreeBuds SE 2 / SE 4 ANC | 1 | 左 / 右 / 盒 | 不提供 |
| HUAWEI FreeBuds Studio | 1 | 整体 | 不提供 |
| HUAWEI FreeLace Pro | 16 | 整体 | 不提供；每次连接前等待 2 秒 |
| HUAWEI FreeLace Pro 2 | 1 | 整体 | 支持 |
| HONOR Earbuds 2 / 2 SE / 2 Lite | 16 | 左 / 右 / 盒 | 不提供 |

内置共 21 个型号配置，另可导入自定义配置或从 OpenFreebuds 更新。名称按最长关键词匹配，并检查型号后缀，避免把 Pro 4 匹配为 Pro；未匹配时留空。不在型号库中的型号不自动推断为相近型号。FreeBuds 7i 的独立配置及旧兼容映射纠正见 [噪声控制说明](NOISE_CONTROL.md)。
缺少电量字段时显示未知值，不以整体电量伪造左右耳电量。
除 FreeClip 2 外，不解析锁定状态、不显示锁定按钮，也不发送锁定 / 解锁命令；音频占用切换请求也仅保留在已有 FreeClip 2 实现中。

## 验证边界

- 原 FreeClip 2 基板保留在 `baselines/FreeClip2-20261022-154252/`，包含源码、完整发布目录、SHA256 清单。
- 新版的通道选择、20 个型号配置、命令限制、改名后恢复、保存失败和界面数据切换由隔离检查覆盖；这些检查使用模拟传输，不等同于实物适配成功。
- 新增型号尚无对应实物验证。上游部分型号自身也注明未测试，固件差异仍需分别确认。
- 本版新增部分型号的降噪主模式切换，按型号与有效回读显示，具体范围和验证边界见 [噪声控制](NOISE_CONTROL.md)。手势、EQ、语言和固件升级设置仍未接入。
- 当前发布目标为 x64，工程最低 Windows 版本为 10.0.19041。第二台电脑、其他蓝牙适配器及 ARM Windows 尚未验证。

## 上游依据

2026-10-22 读取 OpenFreebuds 源码，核对型号映射、各 `per_model` 驱动、通用 SPP 默认通道、电量处理和双连接处理。

- [型号映射](https://github.com/melianmiko/OpenFreebuds/blob/main/openfreebuds/driver/constants.py)
- [各型号驱动](https://github.com/melianmiko/OpenFreebuds/tree/main/openfreebuds/driver/huawei/driver/per_model)
- [默认通道](https://github.com/melianmiko/OpenFreebuds/blob/main/openfreebuds/driver/generic/spp.py)
- [FreeLace 连接等待](https://github.com/melianmiko/OpenFreebuds/blob/main/openfreebuds/driver/generic/spp_transport.py)
- [电量字段](https://github.com/melianmiko/OpenFreebuds/blob/main/openfreebuds/driver/huawei/handler/battery.py)
- [双连接操作](https://github.com/melianmiko/OpenFreebuds/blob/main/openfreebuds/driver/huawei/handler/dual_connect/handler.py)

各次读取的上游文件内容 SHA 记录在 `openfreebuds-profile-sources.json`。OpenFreebuds 的上游许可证为 GPL-3.0，见其 [LICENSE](https://github.com/melianmiko/OpenFreebuds/blob/main/LICENSE)。
