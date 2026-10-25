# 自定义耳机型号配置

在设置 → 耳机配置 → 耳机型号库中导入 UTF-8 JSON 文件。发布目录的 `Examples/headset-profile.example.json` 是基于现有 FreeClip 2 协议的可导入示例，导入后会新增一个型号，不会覆盖原 FreeClip 2。

```json
{
  "formatVersion": 1,
  "profiles": [
    {
      "id": "custom:freeclip2-example",
      "name": "HUAWEI FreeClip 2（自定义示例）",
      "channel": 1,
      "splitBattery": true,
      "deviceManagement": true,
      "connectionLock": false,
      "connectDelayMilliseconds": 0,
      "keywords": ["FreeClip 2 示例"]
    }
  ]
}
```

| 字段 | 含义 |
|---|---|
| `id` | 稳定且唯一的配置标识，建议以 `custom:` 开头；不能覆盖内置型号的 ID |
| `name` | 型号下拉列表显示的名称 |
| `channel` | 已确认的华为 SPP / RFCOMM 通道，1–30 |
| `splitBattery` | `true` 显示左 / 右 / 盒电量；`false` 显示整体电量 |
| `deviceManagement` | 是否使用已实现的华为双连接设备列表及连接 / 断开命令 |
| `connectionLock` | 导入配置只允许为 `false`；FreeClip 2 的锁定命令不自动开放给其他配置 |
| `connectDelayMilliseconds` | 建立控制连接前的等待时间，0–10000 毫秒 |
| `keywords` | 用于蓝牙名称自动匹配的关键词数组；同时会匹配型号名称。忽略大小写和空白，优先匹配最长关键词 |

文件最多 1 MB，每批最多 300 项，关键词每项最多 30 个。相同自定义 ID 再次导入时更新该配置；其他自定义配置与设备绑定保留。
只有当前华为 SPP 协议能表达的型号参数可以通过 JSON 配置；其他品牌或新的协议、命令、字段解析需要更新软件，不能仅通过改名获得支持。

## 从 OpenFreebuds 更新

点击“从 OpenFreebuds 更新”会读取其 GitHub 主分支的一个固定版本，核对型号映射、各型号 SPP 通道、电量和双连接处理信息。下载的 Python 文件只作为文本解析，不执行；不获取固件、不发送耳机设置命令。

支持解析的型号会加入或更新型号库。遇到新的驱动结构会跳过并提示需要更新软件；网络或解析失败会保留旧库。自定义配置独立保留。更新成功后，下拉框立即刷新；当前正在使用的连接会在重新选择该型号或重启程序后使用新配置。

型号库保存在 `%LOCALAPPDATA%/HuaweiHeadsetControl/model-catalog.json`，保存前保留 `.previous` 文件。设备和型号的绑定保存在同目录的 `headsets.json`；两者随 Windows 用户保存，不在不同电脑之间自动同步。
