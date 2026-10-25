# 小米 / 红米耳机扩展可行性评估

评估日期：2026-10-22。对象：本仓库当前 WinUI 3 / C# 主线及用户指定的三个 GitHub 仓库。

本次为源码和公开文档评估。未修改应用实现、未运行参考项目、未向耳机发送命令，也没有本项目的小米 / 红米实机验收结果。上游测试记录与本项目实测严格分开。

## 结论

可以扩展，已有足够源码支撑实现新的小米 / 红米 RFCOMM 驱动，电量、充电状态以及部分型号的降噪、EQ、低延迟、手势和双连接开关有明确参考。现有托盘、WinUI 界面、配对设备选择、地址绑定和原生 RFCOMM 传输可以继续使用。

不能只向现有 JSON 型号配置添加名称。当前所有型号最终仍使用 HuaweiSession，帧格式、认证和命令都与参考项目的小米协议不同。

对现有核心功能必须分别判断：

| 功能 | 可行性与证据边界 |
|---|---|
| 按地址保存、切换当前管理的耳机 | 现有本地配置及界面可复用；需增加协议驱动路由 |
| 左右耳和盒电量、充电标志 | 较高；BudsLink 已实现解析，部分型号有上游测试记录；盒电量可能只在特定状态报告 |
| 降噪 / 通透、EQ、低延迟等设置 | 较高但按型号开放；许多型号的配置仅存在于注释 |
| 双设备连接开关 | 有现成读写实现；只是开启 / 关闭 multipoint |
| 耳机内部已配对设备表、其他设备连接状态 | 在此次检查的三个公开仓库中未找到等价实现 |
| 从电脑指定耳机连接 / 断开另一台手机 | 未找到可直接移植的接口，需要独立协议研究与实机证据 |
| FreeClip 2 的本次连接锁定及跨设备音频切换逻辑 | 没有小米等价命令证据，不能套用华为命令 |
| 固件升级、Wi-Fi 音频 | 不属于已有 RFCOMM 设置驱动可直接证明的能力，首批不纳入 |

“切换当前管理哪副耳机”与“让耳机切到另一台手机 / 电脑”是两项不同能力。

## 三个参考仓库的用途

### BudsLink：主要协议参考

已检查 redmiBudsConfig、redmiBudsSocket、redmiBudsAuthenticator、redmiBudsDevice、profileManager 和全部 32 个实际导入的型号配置（排除模板）。

- 经典蓝牙 RFCOMM，使用服务 UUID `0000fd2d-0000-1000-8000-00805f9b34fb`。
- 帧头 `FE DC BA`，帧尾 `EF`，两字节大端载荷长度；请求与响应包含不同的状态 / 序列号布局。不能使用本项目的 `5A` + CRC16 华为帧解析器。
- 包含 `0x50/0x51` challenge / confirm 认证流程和 16 字节挑战响应算法；此算法可用 C# 实现。上游使用 Gio 读取 /dev/urandom，本项目应使用 Windows / .NET 随机数接口。
- 设备信息读取为 `0x02`，运行信息为 `0x09`，配置读写为 `0xF3/0xF2`，通知为 `0xF4`，能力按型号配置启用。
- 电量字节低 7 位为数值，高位为充电标志；`0xFF` 在上游表示断开 / 无有效读数。本项目应映射为未知，不显示为 0%。
- `DOUBLE_CONNECTION=0x04` 的解析和写入仅处理一个布尔开关，没有设备 MAC 列表、指定目标连接命令或锁定接口。
- Linux 的 BlueZ / Gio / GObject、文件描述符及设置存储不能原样搬到 WinUI。可移植的是协议算法和型号能力，连接与界面继续使用本项目的 Windows 实现。
- 文档说明控制通道可能被其他电脑程序或手机官方伴侣应用占用；盒电量仅在耳机报告时可显示。

上游文档明确列出测试成功的 Redmi Buds 6 Play、Redmi Buds 5 Pro、Redmi Buds 8 Lite。这是上游测试记录，不代表 Windows C# 移植已通过验证。

### MiBudsClient：Redmi Buds 6 Play 的第二参考

README 明确限定控制支持 Redmi Buds 6 Play，设备目录识别更多型号不等于支持控制更多型号。

- 连接代码固定使用 RFCOMM channel 6；可为该型号的初次验证提供参考，不能推导所有小米型号都在通道 6。
- 包含电量查询和低延迟模式命令；新 rcsp.py 有按长度重组分片 / 连包及 VID/PID 读取，可交叉核对协议。
- 原有 BudsProtocol 电量解析仍依赖字节模式搜索；低延迟 ACK 识别依赖接收长度 14。扩展驱动应改用完整帧、序列号、状态及实际回读判断，不能直接采用这些简化判断。
- 当前电量处理直接读取原始字节；通用充电位和未知值应以完整协议及实机结果处理。
- 即使仓库包含大量型号和官方应用功能列表，也不能自动据此启用配置写入。

### MiBudsM8：型号与区域识别参考

公开仓库内容为固件、型号资料、图片 / 动画及翻译；没有公开的应用协议驱动源码。它适合交叉核对 VID/PID、区域版和产品代码，不能承担主协议实现。

例如 Redmi Buds 6 Pro 国行为 `2717:509D`、国际版为 `2717:509E`、电竞版为 `2717:50AF`。Xiaomi Buds 5 Pro 普通 / Wi-Fi 及国行 / 国际版也有不同 PID；不能仅按蓝牙名称推断全部能力。

图片和固件资源的存在也不等于已经完成本项目的移植或资源授权确认。

## 型号范围：必须读取有效代码，排除注释

以下表格来自 BudsLink 固定版本中 32 个配置文件的有效代码，已排除块注释及行注释。列出的功能表示“配置与通用处理器有对应入口”，不是本项目或所有固件的验收结论。

| FlipBudsPro | 0x0000, 0x0001 | 左/右/盒 | 无 |
| RedmiAirDots3Pro | 0xEA03, 0xEA0D | 左/右/盒 | 无 |
| RedmiBuds3 | 0x5027, 0x502A, 0x5066 | 左/右/盒 | 无 |
| RedmiBuds3Pro | 0xEA0A, 0xEA0B | 左/右/盒 | 无 |
| RedmiBuds4 | 0x5037, 0x505D | 左/右/盒 | 无 |
| RedmiBuds4Active | 0x5069, 0x505F | 左/右/盒 | 无 |
| RedmiBuds4Pro | 0xEA0E, 0xEA0F | 左/右/盒 | 无 |
| RedmiBuds5 | 0x506A, 0x506B, 0x5075 | 左/右/盒 | 无 |
| RedmiBuds5Pro | 0x506C, 0x506D, 0x506F | 左/右/盒 | 降噪/通透、EQ、双连接开关、低延迟、响铃、手势 |
| RedmiBuds6 | 0x509F, 0x50A0 | 左/右/盒 | 无 |
| RedmiBuds6Active | 0x5088, 0x5089 | 左/右/盒 | 无 |
| RedmiBuds6Lite | 0x508A, 0x508B | 左/右/盒 | 无 |
| RedmiBuds6Play | 0x509B | 左/右/盒 | EQ、低延迟、响铃、手势 |
| RedmiBuds6Pro | 0x509D, 0x509E, 0x50AF | 左/右/盒 | 降噪/通透、EQ、双连接开关、低延迟、响铃、手势 |
| RedmiBuds6s | 0x5095 | 左/右/盒 | 无 |
| RedmiBuds7s | 0x50B9 | 未启用分项电量 | 无 |
| RedmiBuds8 | 0x50F2 | 左/右/盒 | 无 |
| RedmiBuds8Active | 0x50E1, 0x50E2 | 左/右/盒 | 无 |
| RedmiBuds8Lite | 0x50EE, 0x50EF | 左/右/盒 | 降噪/通透、EQ、双连接开关、响铃、手势 |
| RedmiBuds8Pro | 0x50E3, 0x50E5 | 左/右/盒 | 降噪/通透、EQ、双连接开关、响铃、手势 |
| RedmiBudsSE | 0x509A | 左/右/盒 | 无 |
| XiaomiAirSE | 0x509C | 左/右/盒 | 无 |
| XiaomiBuds3 | 0x5026, 0x502B, 0x5066, 0x505E, 0x505A | 左/右/盒 | 无 |
| XiaomiBuds3Pro | 0x5025 | 左/右/盒 | 无 |
| XiaomiBuds3TPro | 0x502D | 左/右/盒 | 无 |
| XiaomiBuds4 | 0x5044 | 左/右/盒 | 无 |
| XiaomiBuds4Pro | 0x5035, 0x2717, 0x503B | 左/右/盒 | 无 |
| XiaomiBuds5 | 0x5081, 0x5082 | 左/右/盒 | 无 |
| XiaomiBuds5Pro | 0x50AD, 0x50B4, 0x50AB, 0x50AC | 左/右/盒 | 无 |
| XiaomiBuds6 | 0x50EA | 左/右/盒 | 无 |
| XiaomiOpenWear | 0x50B8, 0x50D7 | 左/右/盒 | 无 |
| XiaomiOpenWearPro | 0x507F, 0x5080 | 左/右/盒 | 无 |

具体注意事项：

1. 32 个配置中，31 个启用分项电量，RedmiBuds7s 的分项电量和设置配置仍被注释。
2. 有有效高级设置配置的是 RedmiBuds5Pro、RedmiBuds6Play、RedmiBuds6Pro、RedmiBuds8Lite、RedmiBuds8Pro；其余多数仅启用电量和图标。
3. Xiaomi Buds 4 Pro / 5 / 5 Pro、Redmi Buds 3 Pro / 4 Pro / 5 / 6 等文件中的降噪、EQ、双连接配置被注释，不能当作完整支持证据。
4. RedmiBuds8Pro.js 的显示名称仍写着 Redmi Buds 6 Pro；文档注明该 8 Pro 未测试。目录不能机械导入。
5. 文档对 8 Lite 列出的已测试功能多于当前有效型号配置，例如低延迟。发生冲突时按有效代码和实际回读收紧能力范围。
6. VID/PID 表存在需要交叉核对的条目：BudsLink 的 Redmi Buds 4 表未包含 MiBudsM8 国行表的 PID 5034；其 Redmi Buds 3 与 Xiaomi Buds 3 还共享 5066。不能用首次匹配就覆盖用户选择，也不能自动修正未知型号。

## 当前项目需要改动的位置

| 当前位置 | 已核对现状 | 建议改动 |
|---|---|---|
| FreeClipControl.Shell/EarphonePanel.xaml.cs:19 | 直接创建 HuaweiBluetoothDataSource | 创建统一的数据源路由器，按选定协议委托给驱动 |
| FreeClipControl.Shell/HeadsetProfiles.cs:12 | 配置只有通道、电量布局、设备管理、锁定和等待时间 | 增加品牌、协议、服务 UUID / 通道策略、VID/PID 及独立能力；保持旧配置可加载 |
| FreeClipControl.Shell/HuaweiBluetoothDataSource.cs:329 | 会话始终为 HuaweiSession | 保留华为驱动，新增 Xiaomi 数据源与独立会话 |
| FreeClipControl.Shell/HuaweiProtocol.cs | 5A 帧、CRC16、华为 TLV | 新增 RCSP 帧重组、长度边界、响应匹配、认证状态机和通知处理 |
| FreeClipControl.Shell/BluetoothNative.cs:175 | 已有 Windows RFCOMM 字节传输，按数字通道连接 | 复用底层收发、超时、取消；补充服务发现 / UUID 定位，固定通道仅限已验证配置 |
| FreeClipControl.Shell/Models.cs:105、127 | 电量快照通用，但接口及 DeviceInfo 带华为设备管理语义 | 区分本地选耳机、multipoint 开关、耳机设备表、远程连接、锁定；不可共用一个能力开关 |
| FreeClipControl.Shell/HeadsetCatalog.cs | 更新器只解析 OpenFreebuds 华为驱动 | 为小米目录单独解析、校验和迁移；未知协议直接拒绝发送 |
| FreeClipControl.Shell/AbstractCopy.cs:8 | 品牌枚举只有 Huawei/Honor，未知品牌回退华为 | 补充 Xiaomi/Redmi 品牌与文案设置、标题、默认提示 |

现有 UI 没有降噪、EQ、手势等设置面板。这些功能还需要新增界面及读写后的状态确认，不属于只接入电量就自然完成的内容。

驱动身份必须由已保存的型号 / 协议和实际设备信息共同约束。不能向未确认协议的设备依次试发华为与小米命令。名称用于推荐，用户明确保存的地址和型号优先。

## 建议落地顺序

1. 先拆出品牌 / 协议路由和能力模型，保留当前华为行为与旧地址绑定。
2. 第一副实物优先 Redmi Buds 6 Play：有两个项目可交叉核对，先完成正确通道、身份、认证、电量、未知盒电量、重连与单耳场景，再接低延迟。
3. 第二副优先 Redmi Buds 5 Pro：上游有明确测试记录，可验证降噪 / 通透、EQ 和双连接开关；Redmi Buds 8 Lite 可作为另一候选。
4. Xiaomi Buds 5 / 5 Pro 等先作为电量读取候选，逐个确认国行 / 国际版、认证、固件和实际回包后，再启用设置。
5. 耳机侧设备列表、指定另一台设备连接 / 断开、连接锁定单独立项；需要官方应用操作前后的实机抓包和回读验证，不能随基础支持一并承诺。

不依赖实物可完成：路由与旧配置兼容、帧解析、认证算法对照向量、分片 / 连包 / 畸形输入检查、能力隔离。
必须依赖实物完成：Windows 服务发现和通道、认证握手、实际电量 / 充电、盒关闭与单耳、控制通道占用、睡眠重连、设置写入回读及对音频播放的影响。
上游的通用型号表不能替代这些验收。

## 来源与版本

本次检查使用仓库默认分支的当前文件，所有 32 个型号配置及四个小米核心文件的 blob SHA 均与下述 BudsLink 树一致。文档在独立 Documentation 分支，其能力表与主线不完全同步。

- BudsLink 主线树：`f3935512ec459229317e8b875c024af08ca7c2e9`
- BudsLink Documentation 树：`07f894663d21dabfe56692c49ee2561d6d94bfa8`
- MiBudsClient 主线树：`4325df178abbf57cc07b8298838967532184e60e`
- MiBudsM8 主线树：`fef47b9cd4deea8d4796a3ce4f6d2746c9fba1ae`

直接源码链接：

- [BudsLink 型号与命令常量](https://github.com/maniacx/BudsLink/blob/f3935512ec459229317e8b875c024af08ca7c2e9/src/lib/devices/redmiBuds/redmiBudsConfig.js)
- [BudsLink 认证算法](https://github.com/maniacx/BudsLink/blob/f3935512ec459229317e8b875c024af08ca7c2e9/src/lib/devices/redmiBuds/redmiBudsAuthenticator.js)
- [BudsLink 帧、认证、电量与设置处理](https://github.com/maniacx/BudsLink/blob/f3935512ec459229317e8b875c024af08ca7c2e9/src/lib/devices/redmiBuds/redmiBudsSocket.js)
- [BudsLink 服务 UUID 与设备能力](https://github.com/maniacx/BudsLink/blob/f3935512ec459229317e8b875c024af08ca7c2e9/src/lib/devices/redmiBuds/redmiBudsDevice.js)
- [BudsLink 型号配置目录](https://github.com/maniacx/BudsLink/tree/f3935512ec459229317e8b875c024af08ca7c2e9/src/lib/devices/redmiBuds/deviceConfigs)
- [BudsLink 兼容性和测试记录](https://github.com/maniacx/BudsLink/blob/07f894663d21dabfe56692c49ee2561d6d94bfa8/docs/Devices/Redmi.md)
- [MiBudsClient 控制支持范围](https://github.com/CesurPolat/MiBudsClient/blob/4325df178abbf57cc07b8298838967532184e60e/README.md)
- [MiBudsClient 帧解析与身份读取](https://github.com/CesurPolat/MiBudsClient/blob/4325df178abbf57cc07b8298838967532184e60e/bluetooth/rcsp.py)
- [MiBudsClient 简化设置 / 电量解析](https://github.com/CesurPolat/MiBudsClient/blob/4325df178abbf57cc07b8298838967532184e60e/bluetooth/protocol.py)
- [MiBudsClient 固定通道](https://github.com/CesurPolat/MiBudsClient/blob/4325df178abbf57cc07b8298838967532184e60e/bluetooth/constants.py)
- [MiBudsM8 国行型号](https://github.com/BurgerZ/MiBudsM8/blob/fef47b9cd4deea8d4796a3ce4f6d2746c9fba1ae/models/MODELS_CHINA.md)
- [MiBudsM8 国际版型号](https://github.com/BurgerZ/MiBudsM8/blob/fef47b9cd4deea8d4796a3ce4f6d2746c9fba1ae/models/MODELS_GLOBAL.md)

BudsLink 和 MiBudsClient 的 LICENSE 文件均标明 GPL v3。实际移植时应记录代码来源和对应许可，分发前核对项目整体许可安排；本评估未进行法律分析。

