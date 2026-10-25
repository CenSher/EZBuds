# 设置界面更新

本次仅调整多型号版本的设置与托盘电量刷新策略。源码修改前备份：`.scratch/settings-before-20261022/`。主入口：根目录 `启动耳机控制中心.cmd`，当前指向 `dist-current/FreeClipControl.Shell.exe`。

## 实现

- 开关使用 WinUI SDK 中原生的 40 DIP 轨道尺寸，显式移除 154 DIP 最小宽度；状态文字置于开关左侧，动作靠卡片右边排列。
- 抽象文案采用原生 Expander，默认收起。标题行保留开关；导入、恢复和文案统计放入展开区域，使用系统展开/收起与箭头动画。
- 设置的四个页面统一从下方 28 DIP 上滑进入，位移动画 250ms、淡入 167ms；快速切换会替换前次动画，遵循 Windows 关闭动画的设置。
- 托盘电量默认开启并保存。关闭立即移除提示中的百分比，跳过后台启动读取；面板和设备页都不可见时停止刷新计时器。在面板或设备页查看数据时仍允许读取。重新开启恢复电量提示与后台刷新。
- 节省内存说明已放入设置项描述；未测量实际内存降幅。

## 官方依据（通过 Microsoft Learn MCP 查阅）

- [应用设置规范](https://learn.microsoft.com/windows/apps/design/app-settings/guidelines-for-app-settings)：卡片右侧动作控件、按需显示子选项。
- [Expander](https://learn.microsoft.com/windows/apps/develop/ui/controls/expander)：默认折叠、自动布局及原生展开/收起。
- [XAML 动画](https://learn.microsoft.com/windows/apps/develop/motion/xaml-animation)：内容进入与页面切换的动画用途。
- 本地 SDK `Microsoft.WindowsAppSDK.WinUI 2.3.9` 的 `generic.xaml`：ToggleSwitch 默认最小宽度 154、轨道宽度 40，Expander 标题内容横向拉伸。

## 验证与边界

- 完整发布通过，主输出具有 `BUILD_INFO.json` 源码 SHA256 记录；构建期间源码未发生变化。
- 28 项文案/设置兼容性检查、250 项模拟蓝牙检查、24 项窗口行为策略检查通过；源代码约定和阴影资产检查通过。
- 模拟读取覆盖：关闭时多次后台调用均不连接、不查询；开启后恢复被动读取；关闭后立即通知托盘去掉百分比；主动面板读取继续工作。
- 实际初步检查确认折叠与展开可操作、开关能保存，运行日志记录 `Tray tooltip updated: FreeClip2`、`Battery refresh timer stopped` 和 `Tray battery disabled; refresh timer=False`。
- 最终宽度调整后的像素布局、导航/折叠动画观感、缩放与深色主题、耳机实物读取仍待用户验收。没有将代码、编译或初步检查等同于全部界面验收。

## 最小手动验收

1. 打开设置 → 个性化，确认开关贴近右侧，抽象文案默认收起；切换开关时不应连带展开。
2. 连续展开/收起抽象文案，检查内容与箭头动画；导入/恢复功能仍在展开区。
3. 在四个设置页间快速往返，确认统一向上进入、无位移累积；关闭系统动画时应直接显示。
4. 关闭悬浮电量后离开设备页、收起面板，确认托盘仅显示耳机名；等待超过 30 秒，确认没有新的后台读取。
5. 重启检查设置保留；打开面板仍可看电量，再开启托盘电量确认恢复。
