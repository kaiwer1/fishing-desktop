# P0：只读输出矩形与有效 DPI

本目录是独立采集边界，不接入现有 Host、OutputPolicy、桌面挂接或游戏界面。2026-10-26：44 项合成查询纯测试通过，原生探针编译成功。

2026-10-26 20:22（北京时间），主代理执行当前设备原生探针：两次 Known，均为一块逻辑输出 `output-1`、`(0,0) 1920×1080`、有效 DPI 96；临时 HWND 创建/释放 4/4、剩余 0，进程身份验证通过，正常退出 0，无强制终止。仅证明本机当前单屏配置；真实物理多屏、混合 DPI、显示变更及产品挂接仍未验收。

20:40 使用最新 runner 复验通过：仍为两次 Known、同一单屏尺寸/DPI，创建/释放 4/4、剩余 0；`normalProcessExit=true`、`processExitedWithinDeadline=true`，无强制终止。采集结果到策略的独立适配见 [TopologyPolicyBridge](../multi-output/TOPOLOGY_BRIDGE.md)：17 项纯测试通过，逻辑页面联合截至该记录尚未运行。

## API 与数据约定

后续[实际拓扑→隐藏页面](../multi-output/CAPTURED_TOPOLOGY.md)12项另已通过；这是当前单屏的协议联合，仍不包含物理多屏/混合DPI或实际输出定位。

命名空间 `FishingDesktop.Diagnostics.OutputTopology`：

- `TopologyCapture(ITopologyQuery query, int maximumOutputs=16).Capture()` 返回不可变 `TopologySample`。
- Sample 包含 `CaptureSerial`、`Known`、固定枚举 `Reason` 和只读 `Outputs`。未知样本始终没有输出行，绝不返回旧的正样本或默认 96 DPI。
- `TopologyOutput` 包含 `Key`、`X/Y/Width/Height`、`DpiX/DpiY`、`Primary`。矩形是 PMv2 线程得到的虚拟桌面屏幕坐标，可为负。`GetDpiForWindow` 返回单个有效窗口 DPI，因此本适配器将相同值写入 X/Y；这不是面板物理 DPI，也不读取 EDID。
- `Key` 为当前样本按几何排序得到的 `output-1` 等逻辑别名，不是持久硬件标识，屏幕重排后不能据此认定是同一块物理显示器。原始 HMONITOR 只在内存中参与一次捕获的前后比较，不进入样本或报告；适配器连设备名称也不查询。
- `CaptureSerial` 只是观测顺序，相同内容的新观测仍有新序号。后续控制器需独立决定何时替换拓扑、失效旧 view/Ready/授权；不能把每次采样直接视作拓扑变化。本模块没有生成拓扑代数或恢复产品输入的功能。
- `TopologySample.CreateKnown(serial, rows)` 与 `CreateUnavailable(serial, reason)` 提供下游纯边界测试构造入口。工厂只验证声明的值，调用它不构成已测物理设备的证据。

默认上限 16 块，可配置 1–64。拒绝空、无/多主屏、零句柄、重复句柄、零/负尺寸、坐标加法溢出、重复或重叠矩形。克隆/镜像或伪监视器若造成歧义也保守不可用，不猜选一块屏。纯模块与原生查询均限制 owner thread。

## 一次捕获的证据范围

1. 以 `AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), PMv2)` 确认当前线程；不调用任何 awareness setter。
2. `EnumDisplayMonitors(NULL,NULL,...)` 加 `GetMonitorInfoW(MONITORINFO)` 重新枚举 A。没有使用可能缓存的 `Screen.AllScreens` 或 PrimaryScreen，也不读取内容/DC 像素。
3. 对每个 A 项同步创建一个自有的 1×1 `STATIC`、`WS_POPUP`、`WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW` HWND，位于该矩形内部。创建参数不含 `WS_VISIBLE`，不调用 Show/Activate；验证 PID、创建线程、隐藏/样式、窗口 DPI context、窗口矩形和 `MonitorFromWindow(..., MONITOR_DEFAULTTONULL)`。
4. 在同一 HWND 上读两次 `GetDpiForWindow`，正值相等才可用；查询后重验所有窗口条件及当前 monitor metadata。`finally` 只销毁这次创建的 HWND，并确认句柄已经消失。失败即整次不可用；清理不能确认时查询实例进入拒绝状态，不继续累积新 HWND。
5. 重新枚举 B，必须与 A 的句柄/矩形/主屏状态完全一致（忽略枚举顺序）。再用全新的隐藏 HWND 重做第二轮 DPI。
6. 重新枚举 C 并再次确认线程 PMv2；A/B/C 一致、两轮 DPI 一致、每个 HWND 均清理确认后，才输出 Known。任何失败、0 DPI、句柄/监视器错配或中途变化均丢弃全部行。

Windows 没有为上述调用提供原子事务。本模块证明的是几次有界观察一致，不能发现两次查询之间发生又恢复的瞬时变化，也不保证返回后拓扑仍未变化。它不是完整显示事件监听器；产品接入仍需对显示/缩放变化保守阻塞、重新采集及重新绑定。这里不创建显示事件循环，也不操作外部窗口、Explorer、分辨率、系统缩放、注册表、驱动、输入或画面。

## 构建与验证

```powershell
./diagnostics/output-topology/test-pure.ps1 -OutputDirectory <已有仓库外目录>
./diagnostics/output-topology/build.ps1 -OutputDirectory <已有仓库外目录>
./diagnostics/output-topology/test-native.ps1 -OutputDirectory <已有仓库外目录>
```

所有程序、日志和 JSON 写入通过 `../dual-window/private-path.ps1` 验证的仓库外唯一目录，不覆盖其他轮次。纯测不编译或调用 Win32 适配器，44 项覆盖非法/重复/空/失败、负坐标、三轮枚举变化、DPI 单独变化、错误线程/awareness、窗口归属/隐藏/NoActivate/monitor/清理不明和旧正样本不复用。其不同 DPI 和多块矩形都是合成输入。

原生 runner 只启动专用 exe，`Start-Process -WindowStyle Hidden` 后验证该进程的完整路径和启动时刻。探针先保留 400ms 启动身份验证间隔，内部总期限 10 秒，外部等待最多 15 秒。超时仅可终止仍匹配路径/启动时刻的自有进程，最多再等 3 秒；不按名称寻找、接管或杀死其他进程。正常退出、内部报告通过、所有临时 HWND 清理确认且没有强制终止才算通过；未知/超时/清理失败不会记作通过。没有修改系统或用户设置需要恢复。

原生探针只在当前设备执行两次新捕获，私有报告包含逻辑别名、尺寸/位置、有效 DPI、固定原因和临时 HWND 计数。即使当前仅一块屏测得 Known，也不能宣称真实物理多屏、混合 DPI、热插拔、缩放切换、产品窗口定位或桌面体验已经验收。也不加载 WebView、三维场景、输入或正式存档。

## Microsoft API 依据

- [GetDpiForWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow)：结果取决于窗口自身 awareness，per-monitor 窗口取所在屏幕 DPI，无效 HWND 返回 0。因此必须验证窗口 awareness 和 monitor；本实现不使用 GetDpiForMonitor。
- [GetThreadDpiAwarenessContext](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getthreaddpiawarenesscontext) 与 [GetWindowDpiAwarenessContext](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowdpiawarenesscontext)：线程与指定窗口的 context 要分别确认。
- [AreDpiAwarenessContextsEqual](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-aredpiawarenesscontextsequal)：context 不能用直接位比较；通过该 API 与 PMv2 比较。
- [EnumDisplayMonitors](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaymonitors) 与 [GetMonitorInfoW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getmonitorinfow)：查询 HMONITOR 和矩形，MONITORINFO 已足够，不需要设备名。
- [CreateWindowExW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createwindowexw)、[Extended Window Styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)、[MonitorFromWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfromwindow) 与 [DestroyWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-destroywindow)：创建位置、NoActivate、相交屏幕选择及同线程销毁语义。
- [Process default DPI awareness](https://learn.microsoft.com/en-us/windows/win32/hidpi/setting-the-default-dpi-awareness-for-a-process)：优先 manifest 声明，本探针只声明 PMv2 并运行时确认，不改变 awareness 以制造成功结果。
