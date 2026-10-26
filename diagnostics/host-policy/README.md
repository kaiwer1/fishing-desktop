# P0 宿主暂停策略与原生环境采样

本目录是独立诊断代码，不是游戏模拟或正式存档。`RunGate` 由唯一 owner 的线程维护；网页不能更改宿主暂停原因。`ActiveClock` 只用于活动时长验证，尚未接入抛竿、鱼群或物理。

## 策略

- 启动、手动暂停、无视图、渲染器不可用、未知环境、全屏遮挡、会话不可用、系统挂起、显示配置变化和竖向输出分别占一个原因位。多个原因可以共存，清除一个不会清除其他原因。
- 添加暂停原因撤销控制，恢复只恢复活动资格，不恢复旧 lease。启用握手同时绑定文档、导航代次、页面取消 epoch 和宿主策略 revision；暂停前排队的回执不能在恢复后生效。
- 失焦取消输入可以独立于模拟暂停；只读壁纸仍可活动。相关环境原因可以一次原子替换，避免从全屏切到不可用会话时短暂变成可运行。
- 活动时钟使用单调时间，暂停两端均重建基准；暂停时间和首次恢复间隔不补算，活动卡顿的单次推进最多 250 ms，额外丢弃时长单独记录。该上限是诊断规则，不是已确定的游戏物理步长。
- 唯一 owner 在暂停时拒绝修改及回执重放（`host-paused`），但继续持有日志锁与状态。恢复后旧 lease 仍被拒绝，明确新授权后可查询原回执而不重复提交。

```powershell
./diagnostics/host-policy/test-policy.ps1 -OutputDirectory <已存在的仓库外目录>
./diagnostics/dual-window/test-owner.ps1 -OutputDirectory <已存在的仓库外目录>
./diagnostics/dual-window/test-webviews.ps1 -SdkDirectory <WebView2 SDK目录> -OutputDirectory <已存在的仓库外目录> -HostPolicy
```

输出使用唯一私有子目录，路径检查拒绝仓库内部及重解析点祖先。编译依赖 Windows x64 的 .NET Framework 编译器；真实页面检查还需要现有 WebView2 Runtime/SDK。

2026-10-26 北京时间实测：策略/时钟 16 项、owner/授权生命周期 14 项通过；真实双 WebView 的受控暂停模式 15 项通过，最终 value 10 / sequence 2，暂停时钟保持不变，恢复不自动授权，排队旧启用被拒绝，零专用进程残留，无强制清理。普通模式 11 项、可见桌面模式 16 项回归通过。受控模式直接在 C# 设置原因，不能称为实际系统锁屏或全屏检测。

## 原生只读采样

`NativeEnvironment.Capture(target)` 只查询当前会话、输入桌面、前台窗口可见/最小化状态及边界：

- 当前 WTS 连接状态必须为 Active；输入桌面要求可只读打开且名为 Default。查询失败、非默认输入桌面或会话不可用均阻止活动；这不是完整锁屏状态还原。
- 无稳定前台窗口、前后窗口/PID 不一致、无法查询几何等情况记为环境未知。只有同一稳定样本才能判定覆盖。
- 前台为自身进程或 Shell 进程时不按全屏暂停；其他前台窗口边界覆盖目标输出矩形时暂停，容许 1 个物理像素误差。其他屏幕上的全屏不会因为名称或进程匹配而暂停本目标。
- 使用进程 PerMonitorV2 清单，避免将不同 DPI 虚拟坐标直接比较。使用 `GetWindowRect` 包含不可见边框，因此这是保守的屏幕覆盖规则，可能把某些铺满屏幕的普通窗口当作全屏；不是独占游戏检测。
- 不读标题、命令行、截图或外部内容，不记录 PID/HWND；不使用钩子、不发送输入、不切换桌面，不修改或关闭外部窗口。所有打开的桌面/WTS 缓冲区均释放。

```powershell
# 会短暂显示自有测试窗：普通尺寸→全屏→普通尺寸，约50秒自动关闭。
./diagnostics/host-policy/test-native.ps1 -OutputDirectory <已存在的仓库外目录>
```

原生测试包含几何边界及一个独立进程的自有窗口；测试程序只调整自己的窗口。样本必须确认该测试进程处于前台，不满足数量/连续性则标为失败或不可判定，不强行恢复其他应用的焦点。超时仅请求/清理自己启动且身份匹配的进程。

2026-10-26 08:00 实测通过 3 项：几何边界、真实普通/全屏/恢复采样、全屏期间时钟冻结及恢复。最终样本为普通 17、全屏 349、恢复 92；全屏窗口为自有 1920×1080 窗口。早期短阶段因前台样本不足未判通过；延长阶段后，通过 Computer Use 对已返回的自有测试窗口明确激活，取得连续有效样本。没有模拟页面输入或修改其他应用。

08:05 后续已接入[双窗口原生联合模式](../dual-window/README.md)：`test-webviews.ps1 -NativePolicy` 的 18 项检查通过，200 ms 采样实际触发 owner 暂停与真实页面命令拒绝，346 个确认全屏样本期间活动时钟不变，恢复不还原旧授权。最终计数/序号 10/2，无强制清理和专用残留。此模式仍使用不可见普通视图，未与可见桌面渲染同时运行，尚未验证浏览器挂起降载。

锁屏、实际休眠/唤醒、断开会话、独占全屏、HDR、不同缩放组合和实际多屏切换未验证。几何的负坐标/其他屏案例是固定输入，不能作为多显示器实测。

## 官方依据

- [OpenInputDesktop](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-openinputdesktop)：返回接收输入的桌面，必须 CloseDesktop；断开会话时可返回将来重连时使用的桌面，因此同时检查 WTS 连接状态。
- [GetUserObjectInformationW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getuserobjectinformationw)：这里只读取对象名称，未查询 SID 等信息。
- [GetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow)：激活切换时可能为 NULL，不能直接视作可运行。
- [GetWindowRect](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect)：坐标会受 DPI 虚拟化影响，且可能包含不可见边框。
