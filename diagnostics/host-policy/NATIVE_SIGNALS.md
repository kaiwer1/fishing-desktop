# 会话与电源通知

这是 P0 诊断宿主的通知接入，仍不加载游戏。`NativeSignalWindow` 创建从不显示/激活的独立顶层 HWND，注册本会话的 WTS 通知；不挂到 Explorer，不依附任一 WebView，不使用全局输入钩子。关闭时先解注册再销毁自己的 HWND，失败保留错误。

窗口将 `WM_WTSSESSION_CHANGE` 和三种电源广播转交唯一 owner 的线程。该入口只改变暂停原因，从不授予输入；通知不是安全边界，当前仅防止生命周期和异步恢复错误。

| 收到的通知 | 暂停状态处理 |
| --- | --- |
| 当前会话锁定 | 单独锁存锁定原因，后续看似可交互的轮询不能清除 |
| 控制台/远程断开 | 锁存连接断开原因；连接恢复只清除该原因 |
| 注销/登录 | 对应设置/清除注销原因，不清除锁定或断开 |
| 解锁 | 只清除锁定，要求一次新的环境观察 |
| 桌面就绪/远程控制变化 | 要求新观察，不清除其他原因 |
| 其他会话 | 忽略，不撤销本会话授权 |
| `PBT_APMSUSPEND` | 锁存电源挂起，立即停止 owner 活动；轮询不能清除 |
| `PBT_APMRESUMEAUTOMATIC` / `PBT_APMRESUMESUSPEND` | 清除电源挂起，先进入环境未知，再同步查询当前会话、输入桌面和全屏状态 |

相关原因用同一个 mask 原子替换。手动暂停、无视图等不在该 mask 内，不会被恢复通知清除。锁定、连接、注销分别锁存，避免“重连清掉锁屏”或“解锁清掉断开”。最终恢复不恢复旧 lease，活动时钟在两端重建基准，不补算休眠时长。

```powershell
./diagnostics/host-policy/test-signals.ps1 -OutputDirectory <已有仓库外目录>
./diagnostics/host-policy/test-notification-window.ps1 -OutputDirectory <已有仓库外目录>
```

前者使用固定信号和时钟，不改变系统会话或电源。后者在本机真实创建隐藏 HWND、注册/解注册 WTS，直接调用自有解码入口检查路由和释放；没有广播系统消息，也没有锁屏、休眠、断开远程桌面。两个脚本均明确写 `actualLockOrPowerTransition=false`。

200 ms 原生轮询保留；通知负责补足轮询间的暂停边界，轮询负责恢复前的新观察。暂未加入显示配置消息处理，桌面层仍靠原定时校验回退。启动时错过既往通知、系统通知缺失、真正的锁屏/休眠/远程断开和显示器变化仍需实机验收。

2026-10-26 08:58 北京时间，通知接入版210.96秒循环通过29项联合检查，103次心跳、一次只读视图重建；五次挂接/撤回、20次自有显示/隐藏前台核对，零专用残留，无强制清理。受控调用经过当前 owner、原生轮询、真实 WebView 命令通道和可见桌面静帧/挂起路径，验证锁存不会被轮询提前清除、其他会话无效、恢复保留手动原因与去重。它不是系统广播传递或实际锁屏/休眠的证明。普通窗口11项回归通过；测试 Lively 正常退出并恢复原设置。

依据：[WM_POWERBROADCAST](https://learn.microsoft.com/en-us/windows/win32/power/wm-powerbroadcast)、[WM_WTSSESSION_CHANGE](https://learn.microsoft.com/en-us/windows/win32/termserv/wm-wtssession-change)、[WTSRegisterSessionNotification](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification)。自动恢复广播和用户恢复广播可能先后到达，每次都要求新观察；注册后必须在 HWND 销毁前成对解注册。
