# 只读音频输出身份检查

```powershell
dotnet run --project probe/AudioEndpointCheck/AudioEndpointCheck.csproj -- AA:BB:CC:DD:EE:FF
```

使用与正式客户端相同的匹配逻辑，按蓝牙耳机的设备容器身份查找唯一且启用的音频输出。不播放音频、不创建音频流、不改变默认设备，不发送耳机控制命令。需要在普通桌面会话中运行；隔离环境可能看不到蓝牙设备。
