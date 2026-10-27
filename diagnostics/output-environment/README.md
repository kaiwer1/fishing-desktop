# P0：同一次前台/会话观察对应多个输出

这是独立的只读环境采集器，不接入现有 Host、NativeEnvironment、OutputPolicy、owner 门禁、时钟或浏览器。2026-10-27（北京时间补记）：66 项合成查询纯测试复验通过，原生探针编译、脚本解析通过。

主代理完成本机当前配置的只读复验：两次环境结果 Known、Unknown 0，单个逻辑输出的 Fullscreen 均为 false；调用者拓扑代数 7，拓扑捕获序号 1/2 且内容稳定。临时拓扑 HWND 创建/释放 4/4、剩余 0，正常退出 0，无强制终止。这证明本轮单屏非覆盖观察合同完成，不代表真实全屏、物理多屏、混合 DPI、会话切换或产品门禁已经验收。

## API 与数据边界

命名空间 `FishingDesktop.Diagnostics.OutputEnvironment`：

- `OutputEnvironmentCapture(INativeEnvironmentQueries query, int ownProcessId, int currentSessionId, Func<bool> perMonitorV2, int maximumOutputs=16)` 接受可注入的原始查询边界。
- `Capture(long topologyGeneration, IEnumerable<OutputEnvironmentTarget> declared)` 接受调用者同一份已知拓扑的整组声明矩形。`OutputEnvironmentTarget(key,x,y,width,height)` 本身不证明物理屏幕存在；采集器验证整组声明，不自行枚举或切换拓扑。
- 返回不可变 `OutputEnvironmentSample`，包含调用者的 `TopologyGeneration`、`Known`、固定枚举 `Reason`、`GlobalBlocks` 及只读 `Outputs`。每行仅有逻辑 `Key` 与 `Fullscreen`，没有 HWND、PID、外窗矩形或标题。没有把捕获序号当作拓扑代数，也不自行增加代数。
- `OutputEnvironmentGlobalBlock` 是独立枚举，仅有 `None`、`EnvironmentUnknown`、`SessionUnavailable`。不可按整数强转 RunGate 或 OutputPolicy 的枚举；本模块不写入二者。Known 时全局位为空，即使某些输出的 Fullscreen 为 true；Unknown 必有全局 EnvironmentUnknown 且输出行为空。
- 会话或输入桌面未确认/变化时还设置 SessionUnavailable。其余查询失败只设置 EnvironmentUnknown，也足以阻塞整个环境，不用局部 false 冒充可用输出。没有旧正样本缓存。
- `CreateKnown`/`CreateUnavailable` 工厂供下游纯边界测试构造声明；合成 Known 不构成实机证据。非正 topologyGeneration 是调用错误，查询前抛出异常；畸形拓扑整批返回 Unknown。

创建线程独占采集；错误线程返回 Unknown 而不查询 OS。默认上限 16，可配置 1–64；空集、非法/重复别名、零/负尺寸、端点溢出、重叠矩形、容量超限或枚举中断均使整批不可用。负坐标合法。调用者必须在应用结果前核对仍为该 topologyGeneration，并独立失效旧 view/Ready/输入作用域；本模块不实施这些产品策略。

`CreateForCurrentProcess()` 与现 `NativeEnvironment.cs` 同编译，直接复用其 internal `Win32EnvironmentQueries`，没有复制一套 Win32 查询或改变原类 API。当前进程信息在 using 内读取；PMv2 校验复用 `Win32TopologyQuery.IsPerMonitorV2`，只读当前线程 context，不设置 awareness，也不创建该查询器的测量 HWND。

## 一批观察如何计算

1. 确认坐标上下文为 PMv2，再读一次当前会话、Default 输入桌面、前台 HWND/存在/visible/iconic/PID、shell HWND/存在/PID。空前台、未知 shell、零 PID 等均使环境 Unknown。
2. 前台 PID 等于自身或 shell PID 时忽略覆盖，不读其几何。这沿用现 NativeEnvironment 的进程级忽略规则，仍须完成结束复核。
3. 其他前台只读取两次同一 HWND 的窗口矩形并要求相等、正尺寸且不溢出。两次几何之后重新核对前台 HWND/PID/存在/visible/iconic 与 shell HWND/PID/存在，再复核会话、Default 桌面及 PMv2。第二次几何内部发生的前台或 shell 变化也会被末端身份检查拒绝。
4. 所有观察一致后，对每个声明输出调用现 `NativeEnvironment.Covers`，复用其一个物理像素容差。输出数量增加不增加前台、会话、shell 或几何采样次数；不通过 N 次 NativeEnvironment.Capture 合并局部结果，也不反复设置/清除一个共享 Fullscreen 位。

这里只观察**当前前台窗口**的覆盖矩形，没有枚举其他后台窗口、读取内容/标题/命令行、截图、注入输入、变更焦点、挂接桌面或修改显示设置。Fullscreen 字段表示现 Covers 几何规则下的覆盖资格，不是独占全屏模式、实际遮挡证明或渲染器绘制状态。GetWindowRect 可能包含不可见缩放边框；没有改用 DWM 边界或新造识别规则。

Windows 不提供上述调用的原子事务。重复元数据只能拒绝被观察到的不一致，不能排除查询间的 A→B→A、同进程内句柄复用或采集返回后的变化。IsWindow/PID 复核不提供对外部窗口的生命周期锁。产品仍需显示/会话事件、重新采样与代数检查；持续监听、真正逐屏全屏与物理多屏体验尚未由此实现。

## 构建、纯测与可选原生观察

```powershell
./diagnostics/output-environment/test-pure.ps1 -OutputDirectory <已有仓库外目录>
./diagnostics/output-environment/build.ps1 -OutputDirectory <已有仓库外目录>
./diagnostics/output-environment/test-native.ps1 -OutputDirectory <已有仓库外目录>
```

所有产物经 `../dual-window/private-path.ps1` 验证后写入仓库外唯一目录。66 项纯测只使用合成矩形和原始查询回答，覆盖：一次批量采样与局部/跨屏覆盖、负坐标和边界、畸形/重复/空/无限输入、会话/桌面/awareness 不可用、前台与 shell 身份/可见/几何变化、二次几何内部变化、异常、旧正样本不复用、调用者代数、不可变快照及错误线程。合成双屏和跨屏窗口不是实机多屏测试。

原生探针没有 fixture、页面、授权或主动前台切换。它先用既有 TopologyCapture 确认当前矩形，以**调用者固定诊断代数 7**做两次新的整批环境观察，再重新捕获拓扑并要求内容稳定。拓扑捕获序号为 1/2，未被借作环境结果的拓扑代数。所有临时 HWND 仅来自既有只读拓扑查询，并检查创建/释放计数。

原生 `passed/readOnlyObservationContract` 只表示有界只读合同完成：拓扑前后已知且稳定、两次环境结果各自遵守 Known/Unknown 数据边界、临时拓扑 HWND 清理确认。**环境 Unknown 可以成为这项合同的有效结果**，报告另列 known/unknown 数量、固定原因和 `environmentAvailabilityConfirmed`，不会将 Unknown 当作当前环境已知。`nativeFullscreenAcceptance`、`physicalMultiMonitorAcceptance`、`mixedDpiAcceptance` 和 `actualSessionTransitionTested` 始终 false；偶然观察到 Fullscreen 也不替代可信 fixture/实机验收。

runner 仅启动专用 exe，使用隐藏进程窗口、完整路径/启动时刻身份核对、400ms 身份检查间隔、10 秒内部期限和 15 秒外部等待；超时只可终止仍精确匹配的自有进程，再等最多 3 秒。强制终止或清理不明不可通过；不按名称寻找或终止其他进程。复用 raw adapter 已有 finally 释放 WTS/desktop 查询资源，本接口未暴露逐次 CloseDesktop 成功遥测，因此报告只明确确认拓扑临时 HWND 和进程正常退出，不宣称新增了所有原生句柄的独立审计。

## Microsoft API 语义依据

- [GetWindowRect](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrect)：屏幕坐标矩形、DPI 虚拟化及不可见边框，因此在采集两端确认 PMv2 并保留覆盖启发式的限制。
- [GetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow) 与 [GetShellWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getshellwindow)：前台和 shell 可为 NULL，不能补用缓存窗口。
- [IsWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iswindow)：外窗可随时销毁且句柄会复用；这里只用作重复只读元数据的否决条件，不将它当作窗口所有权或持续有效性证明。
