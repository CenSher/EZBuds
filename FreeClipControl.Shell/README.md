# EZBuds

当前主线在 FreeClip 2 基板上增加 21 个内置型号配置、独立耳机配置页、按地址记忆、设备卡片、菜单内设备切换、自定义型号导入和 OpenFreebuds 更新。支持范围与限制见 [型号支持说明](../docs/MODEL_SUPPORT.md)，导入格式见 [配置说明](../docs/CUSTOM_PROFILES.md)。原可用版本完整保存在 `../baselines/FreeClip2-20261022-154252/`。

## 运行

本次发布入口为根目录 `启动EZBuds.cmd`，指向 `releases/delete-confirm-20261025/EZBuds.exe`；常规构建输出为 `dist-current/`。分发时保留整个目录。先退出旧进程，再打开新版。在设置 → 耳机配置中选择设备和型号；在蓝牙和设备页查看已绑定的卡片。新增型号仍需实机验证，下方 FreeClip 2 的历史实测记录不自动适用于其他型号。

默认启动后仅驻留系统托盘，不弹出面板。启动后及每 30 秒会先检查 Windows 蓝牙连接状态，仅在耳机已经连接时读取电量；不会为更新提示而连接已断开的耳机。主动点击托盘图标才展开面板；再次启动同一程序不强行展开已有实例。托盘注册暂时失败时在后台重试，不自动弹出面板。原 `--show` 参数不再主动展开菜单；`--background` 兼容保留。`--preview` 或 `--no-tray` 用于窗口调试；`--settings` 可在首次启动时直接打开设置窗口。

```powershell
.\build-shell.ps1 -Run
# 依赖已经还原时
.\build-shell.ps1 -NoRestore
```

默认 `build.ps1` 也构建此版本。早期 WinUI 和 WPF 构建入口已移除。

## 结构

- `ShellWindow`：托盘开合、失焦收起、定位和窗口生命周期。按下托盘图标时记录开合意图，避免失焦先收起、随后同次点击又打开；已经展开时重复显示不会重播动画。
- `PopupFrame`：真正的 `WS_POPUP` 原生窗口，移除标题栏与可调整边框，接管非客户区计算和绘制，不再调用 `SetBorderAndTitleBar(false, false)`。除 `WM_NCCALCSIZE`、`WM_NCPAINT` 外，直接处理 `WM_NCACTIVATE` 并返回 TRUE，允许激活状态变化但不进入系统标题栏重绘；普通 `WM_ACTIVATE` 继续传递给 WinUI。`ShellWindow` 同时扩展内容到标题栏并将 WinUI 标题栏高度设为 `Collapsed`、隐藏图标和系统菜单。首次拦截激活重绘会记录日志。
- `PopupSurface`：一层圆角面板及软投影，外侧只保留 12 DIP 透明绘制区；原生窗口不绘制额外边框。阴影用的圆角实色形状放在可见裁剪区域外，只提供 alpha 蒙版，不再铺在面板下面遮挡材质。浅色投影强度 22%，深色 30%，高对比度关闭投影。
- `EarphonePanel`：通过 `SystemBackdropElement` 与 `PanelAcrylicBackdrop` 使用原生 `DesktopAcrylicController` 的 `Base` 样式，采用更厚重、通透度更低的系统亚克力，将面板后的桌面与窗口模糊为磨砂背景；不额外修改 TintOpacity 或 LuminosityOpacity，沿用系统着色、浅色 / 深色、高对比度及透明效果回退策略。控制器附加结果和 Active / Fallback 等材质状态写入日志。配对设备单选展开，保留系统蓝色选中指示条和连接 / 断开按钮。
- 浅色背景另加 1/6 不透明度的白色微调层，依据用户在 ChatGPT 背景下报告的 RGB 243 与参考组件 RGB 245 差值计算，使同一采样条件下的背景目标接近 245；此层位于内容下方，不改变文字或图标。深色与高对比度使用透明微调层。由于亚克力采样会随背景改变，这不是固定 RGB 245 的实色背景，实际像素值由用户复测。
- 材质配置遵循微软的自定义 `SystemBackdropConfiguration` 模式：为每个目标创建独立配置，通过 `OnDefaultSystemBackdropConfigurationChanged` 同步系统主题和高对比度；`IsInputActive` 由宿主窗口的打开、激活和隐藏状态驱动，不使用元素默认值。关闭动画结束并隐藏窗口后才置为非激活，避免收起及右键菜单期间闪成回退灰色。没有绕过系统的透明效果或节能策略。依据：[系统材质控制器示例](https://learn.microsoft.com/windows/apps/develop/ui/system-backdrops#advanced-how-to-use-a-system-backdrop-controller)、[自定义配置回调](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.systembackdrop.ondefaultsystembackdropconfigurationchanged)。
- 主面板不再使用外层 ScrollViewer，也不显示更新时间。设备过多或出现较长错误提示时，只允许设备列表内部滚动，头部和底栏固定。正常读取成功时不留状态文字占位，异常信息仍会显示。
- 已连接设备排在前面，其余设备按耳机槽位稳定排序并置灰；行内只显示名称和“已连接 / 播放中 / 已配对”等状态，移除“蓝牙设备”“这台电脑”前缀。
- `ProductIcon`：按照参考图用矢量路径绘制电脑、手机、平板，以及通用未知设备图标。设备图标使用中性灰色与淡灰圆底，已连接为深灰色，未连接保留整行淡化后的浅灰色，不使用蓝色连接状态圆底。左右耳使用镜像的夹式耳机轮廓与 L / R 标记，充电盒使用带盒盖分缝的实心圆角外形，不再借用通用耳罩或电池图标。
- 电量区与已配对设备之间使用与底部工具栏相同的主题分隔线。窗口宽高保留原设计的 94.5%（在 90% 版本上增大 5%），内容自适应排版，不使用 Viewbox 缩小文字。设备列表标题、名称和操作按钮为 14 有效像素，状态与辅助文字为 13，上方标题为 20、电量数字为 24；正文使用 Medium 字重，标题保留 SemiBold。系统 DPI 和辅助功能文字缩放由 WinUI 处理。任务栏边缘裁剪仍按实际窗口尺寸计算。
- 底部右侧仅保留刷新，左侧“蓝牙 + 耳机名称”按钮通过官方 `ms-settings:bluetooth` 入口打开 Windows 蓝牙设置。托盘右键使用独立的原生菜单，依次提供“打开控制面板”“连接到耳机”、分隔线、“设置”“退出”；“设置”紧邻“退出”上方。主题、系统蓝牙设置、刷新设备数据已从托盘菜单移除。面板空白区域右键无操作，仅设备行保留“设备类型”右键菜单；Esc 或收起面板时关闭当前设备菜单。
- `PopupAnimation`：GPU 合成的整块面板边缘滑动，透明度始终为 1。底部任务栏从工作区下边界外滑入、滑回下边界外，固定裁剪在任务栏边缘；其他任务栏位置按对应边缘处理。读取系统控件时长（进入 250ms、退出 167ms），支持反向切换和系统关闭动画。收起和真正退出都等待动画完成；不逐帧移动 HWND。

## 真实蓝牙数据

未连接提示页沿用面板材质和尺寸：顶部「蓝牙」，中部显示「蓝牙已关闭」「耳机未连接」或「蓝牙不可用」及对应说明，底部仅提供「更多蓝牙设置」。此页不放蓝牙开关，不显示上次电量和设备列表。启动、定时刷新和打开面板时，先只读 Windows 无线电与耳机连接状态，未连接时直接显示提示，不为显示页面建立 SPP。Windows 已连接且读取正常后回到原控制页。启动仍不主动弹出面板或菜单。

默认已使用真实数据源，不再提供演示电量或虚构设备槽位。适配 HUAWEI FreeClip 2 的经典蓝牙 SPP / RFCOMM channel 1，使用 Windows 原生蓝牙与 Winsock API，无 Python、32feet.NET 或管理员权限依赖。

1. 在 Windows 蓝牙设置中完成配对，开启蓝牙并取出耳机。
2. 关闭占用同一 SPP 通道的探针、OpenFreebuds 或其他控制软件。
3. 手动启动程序，展开面板后读取左耳、右耳、充电盒电量及各自充电状态，以及耳机侧的配对设备表。
4. 每次打开面板立即检查 Windows 蓝牙连接状态，不受 30 秒缓存间隔限制；只有已连接才读取耳机协议。启动后及每 30 秒在后台检查刷新，面板收起时也更新托盘电量。连接状态通过各本地适配器的 `BluetoothGetDeviceInfo` / `fConnected` 读取，不发起发现或连接；未连接时在创建 RFCOMM 连接之前返回。刷新静默进行，不禁用或淡化设备列表，也不切换为“读取中”标签。右下角刷新始终保持可点击；已有读取或连接操作进行时，重复手动点击直接忽略，不排队、不重发。打开面板或延迟确认产生的刷新请求仍会合并为一次后续读取。主动断开本机或控制链路丢失后仍暂停自动读取。

## 设置窗口与托盘电量

两轮合并修订见 [设置更新与验收](../docs/SETTINGS_COMPLETE_20261022.md)。最新发布位于 `releases/settings-complete-20261022/`，根启动入口已更新。耳机页合并为一张可展开的大卡片，展开区支持切换、修改及删除配置；两个折叠菜单均使用整行高亮标题，箭头只作指示。开关保留完整的原生所需宽度，不再将轨道宽度当作整个控件的宽度。

2026-10-22 设置更新：抽象文案的导入/恢复选项默认折叠，采用原生 Expander 的展开/收起动画；开关使用 SDK 原生 40 DIP 轨道并靠右排列。四个设置页统一使用 28 DIP 上滑进入和淡入效果，关闭系统动画时直接显示。

“个性化 → 系统托盘 → 悬浮查看耳机电量”默认开启并持久保存，兼容旧配置。关闭时托盘仅显示型号名称，跳过启动时的后台读取；当面板与设备页都不可见时停止 30 秒刷新计时器。打开耳机面板或“蓝牙和设备”页仍正常读取，离开这些界面后再次停止；重新开启即可恢复托盘电量显示。减少后台查询有助于节省资源，未量化实际内存降幅。

此次可运行目录为 `dist-current/`，根目录 `启动耳机控制中心.cmd` 指向此目录；独立发布备份为 `releases/settings-20261022/`。编译发布通过；28 项文案/偏好检查、250 项模拟蓝牙检查和 24 项窗口策略检查通过，包含停用后台读取、重新开启及主动面板读取的回归覆盖。最终界面、动画及耳机行为由用户手动验收。

“设置”打开可调整大小的独立 WinUI 3 窗口；重复打开激活已有窗口，最小化后可恢复。关闭设置不退出托盘程序，从托盘退出则同时关闭设置。采用 Mica、原生 TitleBar / NavigationView、主题卡片及滚动内容区域，提供“蓝牙和设备”“个性化”“关于”三页。布局依据 [微软应用设置规范](https://learn.microsoft.com/windows/apps/design/app-settings/guidelines-for-app-settings) 和 [WinUI 桌面窗口结构](https://learn.microsoft.com/windows/apps/develop/ui/windows-app-sdk-app-structure)。

主题移到“个性化”，提供跟随系统 / 浅色 / 深色，即时同步控制面板与设置窗口，并原子保存到 `%LOCALAPPDATA%\FreeClipControl\settings.json`。设备页复用真实数据模型显示分项电量、连接状态和连接操作，没有演示数据。

“个性化 → 主菜单文案”默认开启抽象文案：华为内置 14 条，荣耀内置 8 条，每次重新打开主菜单随机选择且避免连续重复。优先按当前已保存/识别的型号品牌判断，型号未标明品牌时参考耳机名称；切换品牌即时更新，未知品牌沿用华为库。关闭后，已连接页面标题下方显示“已连接到华为耳机”或“已连接到荣耀耳机”。长文案单行省略，鼠标悬浮可查看全文。支持导入 UTF-8 TXT，每行一条，追加到当前耳机品牌的内置库，华为和荣耀的导入内容分别保存，忽略空行和重复项，兼容每行外层中英文双引号；每条最多 200 字符，每个品牌的自定义文案最多 1000 条，文件最大 1 MB。恢复内置文案仅清空当前品牌的已导入项，旧版导入内容归入华为库。开关和导入文案与主题一起保存，兼容旧版仅保存主题的配置。

托盘悬浮提示固定为 `FreeClip2（NUM%）`，NUM 取左右耳电量较小值，不包含充电盒或综合电量。只有两耳均有当前有效读数才显示百分比；启动未读取、离线、读取失败或缺少一耳数据时显示 `FreeClip2（--%）`。更新通过 `NIM_MODIFY` 提交给系统，Explorer 重启后重新注册保留当前提示。

## 连接到耳机

托盘菜单与设备设置页共用同一个显式操作。点击后重新读取耳机状态；本机尚未连接时才发送已知的连接操作，并回读确认。如果连接槽位已满，则提示先断开一台设备，不主动断开其他设备。

仅当设备表完整、所有状态已知、本机已连接且未播放，并且其他设备明确返回“播放中”（状态 9）时，才请求 Windows 耳机音频输出；状态 1 / 3、空闲、本机正在播放或状态不完整时均不请求。不会根据设备名称、上次快照或“已连接”推测占用；其他设备已锁定时提示先解锁。

音频请求按这副耳机的设备容器身份匹配启用的输出端点，使用定向 MediaPlayer 的 2 秒无声流请求音频通道，不修改 Windows 默认输出、系统音量或连接锁定，不向其他设备发送断开命令。端点不唯一或不可用时停止并提示；不退回电脑扬声器。依据：[MediaPlayer 指定音频输出](https://learn.microsoft.com/windows/apps/develop/media-playback/play-audio-and-video-with-mediaplayer#common-mediaplayer-tasks)。请求后回读耳机状态，仅本机播放状态得到确认才显示已切换，否则显示已请求、请开始播放；不自动重试。

验证：`dotnet run --project probe/LockChecks/LockChecks.csproj` 的 57 项隔离检查通过，覆盖条件抢占、连接、电量提示、失败恢复及已有锁定逻辑；不发送真实设备命令。`probe/AudioEndpointCheck` 可只读验证实际耳机身份与音频端点的对应关系，不创建音频流。2026-10-22 已实际打开设置页并读到真实电量，端点检查匹配到“耳机 (HUAWEI FreeClip 2)”。主题交互、原生菜单鼠标操作及跨设备播放中的实际抢占仍待手动验收；隔离检查不代表这些场景已通过实机测试。

设备表来自耳机的 `0x2B31` 响应，并非本机 Windows 的已配对设备列表，名称原样显示耳机返回的 UTF-8 名称。状态 0、1、9 分别显示未连接、已连接、播放中；兼容已在实机抓包中出现的状态 3，按已连接显示，不猜测其具体音频业务。其他状态仍保留为未知，不据此提供切换操作。此前解锁后显示“状态未知（3）”是漏掉此值的解析错误；重开面板重新读到 1/9 才恢复的现象来自这一遗漏。

设备类型仅用于图标，不用于选择控制目标或发送命令：本机以实际适配器 MAC 匹配；其他设备若在 Windows 缓存中有 Class of Device，则参考其电脑 / 手机 / 平板类别；否则对 MatePad、iPad、手机等名称作明确标为推测的识别。MAC/OUI 不能可靠区分手机和平板，协议中含义未知的字段不拿来猜类型。Windows 缓存读取不发起新蓝牙扫描，控制连接仍只选择已配对耳机。

无法判断或判断不符时，右键对应设备 → 设备类型 → 电脑 / 手机 / 平板；选择“自动识别”撤销手动指定。按设备地址保存在 `%LOCALAPPDATA%\FreeClipControl\device-types.json`，鼠标提示说明当前判断来源。

设备区域明确使用普通箭头光标；右键菜单打开时恢复普通箭头，客户端 `WM_SETCURSOR` 同样使用箭头，不以等待光标表示后台读取。

底层按 `LEN + 5` 拼接分片、处理连包并检查 CRC-16/XMODEM 和 TLV 边界，多帧设备枚举最多重试三次。读取不完整时保留已读取的真实记录并标明缺失；未知电量显示 `--`，不显示虚假的零电量。读取失败时，历史数值标注为“上次数据”和“状态待刷新”，界面不显示时间。

点击某一设备的连接 / 断开按钮才发送 `0x2B33`，参数使用耳机回传的 6 字节原始地址。操作前重新读取状态，操作后读取耳机返回值确认；未确认时不改变显示状态来假装成功。不发送取消配对、首选设备、自动连接或双连接开关写命令。

### 本次连接锁定

展开当前电脑的设备卡片后，右上角出现 28 DIP 的小锁按钮。点击后在按钮上方弹出轻量原生 Flyout：一行确认问题和一个「是，锁定到这台电脑」按钮，按用户参考图采用圆角、阴影及普通确认按钮，不遮暗整个面板。点击气泡外、Esc 或关闭面板会取消，只有确认按钮发送指令；解锁沿用相同布局。气泡打开时不因失焦自动收起面板。锁定后图标变为闭锁，状态显示「已锁定」。

小锁图标居中于完整的 28×28 DIP 透明按钮区域，不用负边距移动；图标不单独参与命中测试。面板动画统一使用合成层 Translation，不再同时设置 UIElement.Translation，打开后的位移为零，也不使用 Z 轴透视位移，避免绘制和输入坐标不一致。对应 WinUI 问题记录：[microsoft-ui-xaml #10950](https://github.com/microsoft/microsoft-ui-xaml/issues/10950)。本次按用户要求只编译并打开软件，具体鼠标命中效果由用户验证。

下述取消、锁定/解锁的真机记录属于此前确认框版本；新的 Flyout 样式和点击区域修改已编译，尚未声称通过用户的手动界面验收。

只为耳机侧确认已连接且与本地蓝牙适配器地址匹配的电脑提供此操作，不按设备名称或手动图标类型选择目标。缺少锁定状态、离线或未知状态时不允许操作。底层在写入前重新读取目标，使用 `0x2B33` 的 TLV id `8` / `9` 设置锁定 / 解锁，再从设备表 TLV `0x0D` 回读确认。不会清除首选设备，也不会因结果未确认而重发写入。

锁定、解锁成功只更新行内状态和锁图标，不在列表底部另加成功提示；错误和未确认结果仍会提示。写入后先等待 `0x2B33` 的对应操作确认；缺少确认则暂停读取，不重发。确认成功后在同一条 SPP 会话中接收通知并等待约 1.2 秒，再查询实际状态，不再于操作后 2 秒重新打开控制通道。

2026-10-22 11:05 左右，用户报告首次播放时锁定导致卡顿、左右耳不同步及 Windows 输出消失。Windows 事件确认端点在 11:04:56 变为 UNPLUGGED，11:06:26 恢复 ACTIVE。根因尚未完全确定；之前的短时锁定/解锁成功不能作为播放稳定性的验收。此次补充严格确认时序、同会话等待和只读音频输出诊断，未复现故障，也不声称已解决左右耳同步问题。诊断按设备名称匹配音频输出，只读元数据，不创建音频流、不切换设备、不重置蓝牙。命令后约 6 秒的诊断仅查询音频端点，不打开蓝牙会话。

2026-10-22 已在新界面验证电脑展开位置、原生确认框、取消不发送、确认后真实锁定与解锁状态同步，并恢复解锁。23 项隔离检查覆盖报文一致性、状态缺失、非本机及断开目标、回读不一致、链路丢失和绑定行更新。检查入口：`dotnet run --project probe/LockChecks/LockChecks.csproj`；协议依据见 `probe/CONNECTION_LOCK_TEST.md`。验证记录与日志见 `.scratch/connection-lock-ui.log`。

点击连接其他设备后，从点击时起约 6 秒主动再检查一次状态，即使面板已收起也完成这一次确认。该确认同样先检查 Windows 连接状态，电脑已断开时只显示离线，不重新连接耳机。若原操作仍在进行，则完成后立即补读；后续有新的连接 / 断开操作时，旧的延迟确认失效。程序退出时取消等待，不重发连接命令。

**断开本机会使 SPP 控制链路中断。** 此时暂停定时读取，不后台自动重建链路。再次打开面板只在 Windows 报告耳机已连接时读取；只有右下角主动刷新允许重建控制连接，可能使本机重新连接。主动连接其他设备后的一次延迟确认同样不重连已断开的耳机；其他操作导致控制链路中断时，暂停定时读取并显示结果待确认。

耳机重命名后不包含 `FreeClip 2`，或系统配对了多副同型号时，可指定实际地址：

```powershell
.\dist-shell\FreeClipControl.Shell.exe --bluetooth-address AA:BB:CC:DD:EE:FF
```

只允许连接系统中已经配对的地址，不自动配对。首次识别后固定本次运行的耳机，不会因为另一副耳机连接状态改变而偷偷换目标。

## 手动验收与日志

原演示数据的自动 UI 诊断流程已移除。默认启动仅对已经连接的耳机读取数据，不发送蓝牙设置写命令；本次开发中的锁定/解锁验收使用独立测试窗口和真实耳机，通过确认按钮执行，并恢复解锁。

```powershell
.\dist-shell\FreeClipControl.Shell.exe --diagnose .\shell-diagnostic.log
```

`--diagnose` 仅指定日志路径，程序照常运行，不自动测试或退出。正常日志在程序目录 `startup-error.log`，包含读取失败、协议校验错误、命令发送及进程状态。`--preview` 可用于预览启动，现在仍会失焦收回；显式 `--keep-open` 才关闭自动收回。

原连接/断开接入记录与本次锁定测试应分别判断：新增锁定功能已完成上述新 C# 界面和真机回读检查，不代表其余连接/断开场景已重新完整验收。协议依据见根目录 `PROTOCOL.md`、`probe/`。

## 依据

- [WinUI 标题栏隐藏后仍出现白边的记录](https://github.com/microsoft/microsoft-ui-xaml/issues/9978)
- [DWM 窗口属性](https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
- [窗口样式变更与 SWP_FRAMECHANGED](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowpos)
- [系统材质](https://learn.microsoft.com/windows/apps/develop/ui/system-backdrops)
- [启动系统设置的 URI](https://learn.microsoft.com/windows/apps/develop/launch/launch-settings)
- [圆角遮罩与合成投影](https://learn.microsoft.com/windows/apps/develop/composition/using-the-visual-layer-with-xaml)
- [Windows 蓝牙 Socket](https://learn.microsoft.com/windows/win32/bluetooth/bluetooth-and-socket)
- [SOCKADDR_BTH](https://learn.microsoft.com/windows/win32/api/ws2bth/ns-ws2bth-sockaddr_bth)
- [已知设备枚举参数](https://learn.microsoft.com/windows/win32/api/bluetoothapis/ns-bluetoothapis-bluetooth_device_search_params)
- [OpenFreebuds 协议参考](https://github.com/melianmiko/OpenFreebuds)
- [Bluetooth 设备类别](https://learn.microsoft.com/uwp/api/windows.devices.bluetooth.bluetoothminorclass)
- [原生托盘上下文菜单](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-trackpopupmenuex)
