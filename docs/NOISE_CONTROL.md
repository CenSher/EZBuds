# 噪声控制

入口位于主面板电量区下方、已配对设备列表上方。使用原生 ComboBox，提供“降噪 / 关闭 / 透传”。处于降噪模式且型号已适配强度控制时，在下方显示“降噪强度”下拉框。不改变面板窗口尺寸，设备列表保留滚动区域；不支持的控件折叠。

## 显示与写入条件

- 使用保存的型号配置进行能力判断，不以可随意修改的蓝牙显示名称猜测。
- 首次必须收到当前设备有效的模式回读，才显示切换控件。断开或切换耳机时清除确认状态；失败时不默认显示“关闭”。
- 只有完整界面刷新才会对已适配型号读取模式；托盘后台电量刷新不查询或修改噪声控制。
- 用户选择后先重新读当前状态，再发送至多一次切换指令，随后最多两次查询确认结果。有无独立应答都会回读，以状态为准；不会因为失败自动重发写入，不为切换模式主动重连。
- 控件始终显示已回读状态；失败会显示提示，状态未知时禁用，等待主动刷新或下一次可见界面刷新。
- 下拉框展开期间沿用菜单弹出层的焦点保护，暂停自动收回及定时数据刷新；收起后恢复。

## 已实现的协议范围

现代 Huawei ANC：FreeBuds 4i / 5i / 6i，Pro / Pro 2 / Pro 3 / Pro 4 / Pro 5，SE 4 ANC，Studio，FreeLace Pro / Pro 2，以及上游映射至 4i 驱动的 HONOR Earbuds 2 / 2 SE / 2 Lite。

FreeBuds 7i 已加入独立型号配置，通道 1 的数据读取依据来自本机使用 6i 兼容配置的日志，模式和强度语义采用 7i 的独立源码参考。旧版保存为 buds6i 且 Windows 名称恰为 HUAWEI FreeBuds 7i 或 FreeBuds 7i 的记录，在启动识别时纠正为 buds7i；其他保存的型号或自定义名称不自动迁移。手动配置也可直接选择 HUAWEI FreeBuds 7i。

FreeClip / FreeClip 2、SE / SE 2 保持隐藏。FreeBuds 3 使用不同的单字节旧协议，当前没有接入可靠状态回读，不显示此现代三模式控件。未知型号不自动开放写入。

## 协议依据

OpenFreebuds 固定提交 `59b77e334f438fb670ba8873e6e721bfabdb7238`：

- [模式处理器](https://github.com/melianmiko/OpenFreebuds/blob/59b77e334f438fb670ba8873e6e721bfabdb7238/openfreebuds/driver/huawei/handler/anc.py)
- [型号映射](https://github.com/melianmiko/OpenFreebuds/blob/59b77e334f438fb670ba8873e6e721bfabdb7238/openfreebuds/driver/constants.py)及该提交的 per_model 驱动，用于核对哪些型号启用 ANC handler。FreeClip 的映射不能当作支持降噪的证据，代码明确排除。

HuaweiPods 固定提交 `115f080100c07fb51174b3238473c512a2c6d3b8`：

- [主模式数据包](https://github.com/Nshpiter/HuaweiPods/blob/115f080100c07fb51174b3238473c512a2c6d3b8/app/src/main/java/moe/chenxy/huaweipods/pods/HuaweiAncPackets.kt)
- [对应抓包断言](https://github.com/Nshpiter/HuaweiPods/blob/115f080100c07fb51174b3238473c512a2c6d3b8/app/src/test/java/moe/chenxy/huaweipods/pods/HuaweiAncPacketsTest.kt)
- [回读解析](https://github.com/Nshpiter/HuaweiPods/blob/115f080100c07fb51174b3238473c512a2c6d3b8/app/src/main/java/moe/chenxy/huaweipods/pods/HuaweiRfcommResponseParser.kt)
- 华为[FreeBuds 7i 官方模式说明](https://consumer.huawei.com/cn/support/content/zh-cn16064715/)用于核对产品具备“降噪、关闭、透传”；官方产品描述本身不作为蓝牙指令依据。

独立 C# 实现复用本项目已有 Huawei CRC/TLV 封包，不执行或引入上游代码：

| 操作 | 命令 | TLV 1 值 / 完整请求 |
|---|---|---|
| 读当前模式 | 0x2B2A | 空值；`5A0005002B2A0100427E` |
| 关闭 | 0x2B04 | `00 00`；`5A0007002B0401020000D22D` |
| 降噪 | 0x2B04 | `01 FF`；`5A0007002B04010201FFFFEC` |
| 透传 | 0x2B04 | `02 FF`；`5A0007002B04010202FFAABF` |

状态响应 0x2B2A 的 TLV 1 必须恰为两个字节，依次是子模式和主模式；主模式 0/1/2 分别为关闭/降噪/透传，未知值不接受。普通模式切换保留设备对 FF 的子模式选择，不强行指定强度。

## 降噪强度（2026-10-25）

强度写入仍使用 0x2B04，TLV 1 为 `[01, 档位值]`。写入前确认耳机仍处于降噪模式、当前档位和目标档位均属于该型号的映射；若手机或耳机手势已改变模式，则拒绝这次强度写入并提示刷新。写入后同时核对主模式和档位，不把 ACK 当作修改成功，也不因缺少 ACK 跳过回读，不自动重发写入。

| 型号组 | 智慧动态 | 轻度 | 均衡 | 深度 |
|---|---|---|---|---|
| FreeBuds 5i / 6i / 7i / Pro 5 | 03 | 01 | 00 | 02 |
| FreeBuds Pro 3 | 01 | 00 | 02 | 03 |
| FreeBuds Pro / Studio / FreeLace Pro 2 / SE 4 ANC | 03 | 01 | 00 | 02 |
| FreeBuds Pro 2 / FreeLace Pro | 不提供 | 01 | 00 | 02 |

7i 映射采用用户 2026-10-25 双耳佩戴时的逐项对照：原“智慧动态”(01)实际为轻度，原“轻度”(00)实际为均衡，原“均衡”(02)实际为深度，原“深度”(03)实际为智慧动态。该实测反馈覆盖了 HuaweiPods 固定提交中错误适用于本机 7i 的 Pro 3/7i 分组；只修正 7i，不据此改变 Pro 3。5i/6i/Pro 5 和 Pro 3 仍参考其[按型号档位映射](https://github.com/Nshpiter/HuaweiPods/blob/115f080100c07fb51174b3238473c512a2c6d3b8/app/src/main/java/moe/chenxy/huaweipods/pods/HuaweiAncLevelProfile.kt)。其余组依据 OpenFreebuds 各型号配置；上游实现不等于本项目实机验收。

用户同时反馈单耳佩戴时无法切换。应用不以电量或已连接状态猜测佩戴状态，不强行绕过固件限制；请求降噪却明确回读为其他模式时，提示检查双耳佩戴或手机 App 的单耳降噪设置。不将这一台耳机的反馈泛化为所有型号或固件都不支持单耳降噪。

FreeBuds Pro 4 的主模式可用不等于强度映射已确认，因此当前不展示强度；4i、上述 HONOR Earbuds 2 系列和未知型号同样不展示。关闭、透传模式下隐藏强度。未知档位（包括 FF）不假装成智慧动态，显示“强度未知”并禁用，等待有效回读。

## 验证边界

2026-10-25 修正：09:28 日志记录 profile=buds6i 的降噪写入后，程序因未收到独立 0x2B04 ACK 提前抛出超时，完全没有进行状态回读。这证明了软件确认流程的缺陷，不能单凭日志断言耳机接受或拒绝了写入。新版保留单次写入，允许缺少独立 ACK 时查询 0x2B2A，并记录操作前状态、ACK 是否收到及每次回读的模式/档位。此修改仅针对噪声控制，不放宽 FreeClip 2 连接锁定原有的 ACK 约束。

按用户要求，本次仅编译及源码约定检查，不执行测试程序、不启动应用、不操作真实耳机。源码与上游抓包证据不能替代本机实测。真实切换、回读、固件差异、下拉框视觉与失焦行为待用户验收。
