# 两轮设置修改：待用户界面验收

发布：`releases/settings-complete-20261022/FreeClipControl.Shell.exe`。根目录 `启动耳机控制中心.cmd` 已指向此目录。请从托盘退出旧版后再启动，普通启动只驻留托盘。

## 耳机卡片与删除

移除上方重复的耳机卡片，保留下方大卡片的图标、名称、连接状态、地址和型号说明。默认收起，点击整行可展开已配置列表；展开区提供添加、切换、修改和删除。

删除仅移除软件配置，保留 Windows 配对。删除非当前耳机不影响当前数据；删除当前耳机会清空旧电量与控制状态，等待手动选择其他耳机。删除地址会持久记录，重启或自动识别不会将其重新加入；用户仍可显式添加。保存失败则保留原配置和当前状态。

## 全局折叠菜单与开关

当前设置页的两个折叠菜单（耳机选择、抽象文案）使用共享标题样式。悬停与按压反馈覆盖整条标题区域，箭头仅为不接收点击的装饰指示，不再绘制独立按钮底色。标题继续由原生 Expander 的单个 ToggleButton 响应点击与键盘操作；保留原生展开/收起动画、内容裁剪及辅助功能语义。

抽象文案和托盘电量开关取消了强制的 40 DIP 控件宽度。40 DIP 是滑轨宽度，原生控件还包含内容间距；现在由原生模板计算完整所需宽度，外部“开/关”标签仍在左侧，整组靠右。

此前的导航上滑动画、默认折叠、托盘电量开关以及后台刷新停用逻辑均保留。同工作区的文案悬浮提示更新也已合并。

## MCP 官方资料

本次重新通过 Microsoft Learn MCP 查阅：

- [应用设置规范](https://learn.microsoft.com/windows/apps/design/app-settings/guidelines-for-app-settings)：卡片右侧动作控件及按需展开的子选项。
- [SettingsExpander](https://learn.microsoft.com/dotnet/communitytoolkit/windows/settingscontrols/settingsexpander)：标题中的动作控件与展开子项的组织方式。
- [Toggle switches](https://learn.microsoft.com/windows/apps/develop/ui/controls/toggles)：原生开关与开关文案。
- [OnApplyTemplate](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement.onapplytemplate)：在保留原生控件基础上更换标题模板。

另对照已安装 WinUI SDK 的 `generic.xaml`，确认开关轨道和内容间距、Expander 模板部件、整行 ToggleButton 与默认独立箭头高亮的来源。

## 验证及边界

独立源码快照完整编译、资源与窗口策略检查通过；发布目录 `BUILD_INFO.json` 记录源码 SHA256，交付前与当前源码一致。263 项模拟蓝牙/配置回归检查通过，包含删除、重新添加、切换、重启与保存失败恢复。开关偏好与文案回归检查通过。

源代码结构检查确认两个折叠组均使用共享标题、没有单独箭头按钮，两个开关均未限制固定宽度。它们是代码检查，不等同于实际渲染或交互验收。本次未启动预览、未使用 Computer Use、未自动操作耳机，也未改动真实耳机配置。

## 手动验收

1. 蓝牙和设备页顶部只有一张大耳机卡片；鼠标分别移到标题左侧、空白和右侧箭头，整条标题应有相同的悬停反馈，点击均可展开/收起。
2. 展开后切换、修改和删除配置；删除当前耳机后旧数据应清空，删除最后一副后可重新添加，重启不应自动加回已删除设备。
3. 个性化页两个开关在开、关状态均完整显示，尤其检查开启时右侧滑块。分别在常用缩放比例、浅色与深色主题下确认。
4. 抽象文案折叠组也应整行高亮；操作其开关不应同时展开或收起。
5. 检查折叠和页面导航动画，以及关闭托盘电量后的名称提示与后台刷新行为。
