# P0：单一 owner 与两个独立窗口

本诊断默认把一个只读页面和一个交互页面放在两个普通 WinForms 窗口中。可选桌面自测会把只读窗口隐藏挂接到已有 WorkerW；共享状态仍是独立计数器，不加载游戏或原存档。

`DualWindowContext : ApplicationContext` 持有 `DualWindowOwner`，后者持有现有的 `DurableCounterStore` 和 `ViewAuthority`。没有指定 `MainForm`；关闭任一窗口只撤销并移除对应视图，不释放 owner 或日志锁。两个窗口都关闭时应用仍运行，可通过托盘重新打开；只有显式退出整个诊断或故障退出才释放存储。窗口重建保留同一个 `OwnerId`、已提交序号和去重回执；整个应用显式重启后会产生新的 `OwnerId`，日志由原存储实现重放。

每个新窗口有唯一的视图 ticket，每个新页面有独立 `documentId`。重新加载或关闭先使旧 ticket/页面授权失效，再处理异步页面返回。旧窗口事件不能撤销新窗口、重新绑定新文档或提交修改。页面复用 `state-owner/lease-session.mjs`，默认无授权；必须显式启用当前交互窗口。原生窗体失焦和页面 blur/hidden/pagehide/Escape 都撤销交互授权，取消前排队的授权不能自动恢复控制。只读窗口永远不能提交修改。

启用按钮先发出唯一请求，让当前页面回传取消 epoch；宿主同时核对请求、ticket、文档、导航代数及前台窗口后才授权。页面取消携带它已看到的激活请求 ID，避免按钮点击之前排队的 blur 误取消之后的显式请求，同时防止取消后迟到的握手回执重新启用。重载时，旧导航异步失败只在导航 ID 和代数仍匹配时才触发故障退出。

浏览器进程故障或 WebView `ProcessFailed` 会让整个应用保守退出，不自动重建浏览器。两窗正常关闭引发的正常浏览器退出不结束 owner。应用退出先撤销视图，再延后到当前浏览器回调返回后销毁窗口和存储。

## 文件与依赖

| 文件 | 职责 |
| --- | --- |
| `DualWindowOwner.cs` | 不依赖窗体或浏览器的 owner、视图 ticket 和授权生命周期 |
| `ActivationHandshake.cs` | 一次性显式授权请求、取消关联及当前导航检查 |
| `DualWindowHost.cs` | ApplicationContext、两个独立窗口、托盘恢复入口、WebView 消息和关闭处理 |
| `DualWindowProbe.cs` | `--self-test` 的真实页面消息协议验证 |
| `DualWindowDesktop.cs` / `desktop.manifest` | 可选隐藏桌面挂接、UI 线程定时核对、撤回及本进程 PerMonitorV2 声明 |
| `DualWindowVisual.cs` / `visual-probe.mjs` | 显式可见自测、单个可取消动画排程与仅本 WebView 的像素检查 |
| `DualWindowPolicy.cs` / `../host-policy/RunGate.cs` | 多原因暂停、单调活动时钟、owner 命令门禁及受控真实消息测试 |
| `DualWindowNativePolicy.cs` | 200 ms 原生采样接入 owner、独立自有全屏窗口及联合消息检查 |
| `DualWindowRenderer.cs` / `../host-policy/RendererSuspender.cs` | 只读页选择性挂起、最新意图协调、暂停重载及释放重建 |
| `FrozenFrameSurface.cs` / `../host-policy/BoundedFrameStream.cs` | 仅本 WebView 的单张暂停静帧、字节/像素预算和显式释放 |
| `view.html` | 只呈现快照的页面和计数器提交按钮；`sendRaw` 仅用于重放过期诊断消息 |
| `DualWindowOwnerTests.cs` | 无浏览器的生命周期、写锁和旧身份回归测试 |

构建链接现有 `state-owner/DurableCounterStore.cs`、`state-owner/ViewAuthority.cs`、`input-window/HostInstanceGuard.cs` 及 `desktop-layer` 的两个组件源文件，复制现有 `lease-session.mjs`，不修改这些实现。与输入候选宿主共用当前 Windows 会话的单实例门禁，发现另一个候选实例时退出码为 2；不要为运行测试关闭不属于本次测试的进程。

需要 Windows x64、.NET Framework 编译器、WebView2 Runtime，以及解包后的 `Microsoft.Web.WebView2 1.0.3405.78` SDK。纯 owner 测试不需要 WebView2。存储格式、容量上限和恢复边界沿用 [state-owner 说明](../state-owner/README.md)，不是新游戏存档格式。

## 构建与无窗口测试

从仓库根目录执行。所有输出父目录必须已存在、位于仓库外，且不能通过重解析点指向其他位置。脚本每次创建新的私有子目录，不覆盖既有构建、计数日志或浏览器 profile。

```powershell
$taskParent = Split-Path (Get-Location).Path -Parent
$taskOutput = Join-Path $taskParent 'tools/temp'
$taskSdk = Join-Path $taskParent 'tools/webview2-sdk-1.0.3405.78'

& ./diagnostics/dual-window/test-owner.ps1 -OutputDirectory $taskOutput
$taskBuild = & ./diagnostics/dual-window/build.ps1 -SdkDirectory $taskSdk -OutputDirectory $taskOutput
$taskBuild
```

`build.ps1` 只编译，返回 `outputDirectory` 和 `executable`，不启动窗口。构建目录包含 SDK DLL/许可证、页面副本及编译日志。`test-owner.ps1` 只启动自己的无窗体控制台测试，保存 `test-output.txt`、`summary.json` 与独立计数日志。

截至 **2026-10-25 00:01（北京时间）**，实际验证记录如下。原始报告保存在仓库外的私有测试目录。

| 验证层次 | 实际结果 | 覆盖 |
| --- | --- | --- |
| 构建与语法 | 宿主编译通过，PowerShell 与页面 JavaScript 语法通过 | 构建时启用编译器警告视为错误；这项本身不证明运行行为 |
| 纯 C# 生命周期 | 6 项通过，不加载 WebView | 两窗关闭/重建保持 owner 与写锁、旧 ticket 隔离、重载后旧文档/租约失效、只读重建不撤销交互授权、撤销边界、重复角色及跨线程拒绝 |
| 不可见真实双窗口 WebView | 9 项真实消息检查全部通过 | 页面回执、窗口关闭/重建、零窗口期间 owner 存活、重载、旧消息拒绝及请求去重；最终 value 为 10、sequence 为 2 |
| 正常退出清理 | `forcedCleanup=false`，专用残留进程数为 0 | 测试无需强制清理；不据此推断所有故障退出路径均已验证 |

该轮之后修复了原生按钮与页面 blur 的 epoch 竞态，以及旧导航异步异常误退出问题。修复版宿主已重新编译，纯 C# 检查增加至 **10 项并全部通过**；随后重新运行真实 WebView 自测，**11 项全部通过**，最终 value 10 / sequence 2，`forcedCleanup=false`，专用残留进程数为 0。新增检查包含真实页面授权握手/取消回执；过期导航异常是向处理入口进行的受控调用，不是真实浏览器故障注入。可见按钮、真实失焦输入仍未验收。

## 不可见真实 WebView 自测

在单独安排的测试时段运行以下命令；它会启动诊断宿主及其私有 WebView2 进程，不是纯逻辑测试：

```powershell
& ./diagnostics/dual-window/test-webviews.ps1 -SdkDirectory $taskSdk -OutputDirectory $taskOutput
```

脚本先创建新构建目录，再以 `--self-test` 启动。两个原生窗体设置为零透明度、不出现在任务栏，并带有不激活标志；没有可见托盘入口。WebView 保持真实宿主和消息通道，用页面确认回执作为推进条件，不模拟鼠标。

自测检查：

- 两个独立页面初始读取同一 owner、初始无授权，只读/未授权命令被拒绝。
- 提交先取得回执，再确认另一窗口收到快照。
- 关闭并重开交互窗口后保留 owner、日志锁和序号，重放旧文档/租约/撤销消息被拒绝，重复请求不重复提交。
- 关闭并重建只读窗口保留交互授权，后续提交仍发布到新只读页面。
- 两个窗口都关闭、消息循环继续后再打开，保留同一 owner 和去重记录。
- 独立页面重载默认无授权，显式页面取消收到确认后旧租约不能继续提交。
- 授权通过页面 epoch 握手；已取消请求的迟到和重复回执不能复活授权，新的显式请求仍能启用。
- 向异常处理入口注入受控的旧导航异常后，当前真实页面仍可提交去重请求；这不等于已制造所有真实浏览器导航异常时序。

`webview-summary.json` 是宿主的页面协议报告；脚本还核对退出码、11 项检查、最终 value 10 / sequence 2 和子进程清理，再写 `summary.json`。脚本按 PID、启动时间和已证明的父子关系跟踪自己启动的进程，初始浏览器子进程还须匹配独立 profile。超时只停止身份仍匹配的自有进程；正常退出后等候子进程清理，必要时只清理已跟踪的自有子进程。遇到无法归属的 profile 残留只报告失败，不扩大清理范围。需要强制清理时不把正常退出生命周期记为通过。

## 可选隐藏桌面 WebView 集成

```powershell
& ./diagnostics/dual-window/test-webviews.ps1 -SdkDirectory $taskSdk -OutputDirectory $taskOutput -DesktopLayer
```

此开关给新构建加入 PerMonitorV2 清单，并启动 `--desktop-self-test`。只读原生窗口在 `EnsureCoreWebView2Async` 前完成挂接，始终不调用 Show，不显示托盘；交互窗沿用原来的透明、不激活协议自测模式。只读页的命令权限仍不可写，与窗口是否隐藏分别验证。

宿主只使用已经存在且通过组件核对的传统桌面层，不发送 `0x052C`、不创建/隐藏 Explorer 图层、不自动启动 Lively。启动时找不到已有层或 DPI 不兼容则明确失败并退出。500 ms UI 线程检查发现已挂接父层或窗口状态失效时，组件先隐藏回退，宿主撤销交互并关闭失效只读视图，保留 owner、存储锁与交互窗；重开必须显式请求，没有自动重试循环。清理失败、浏览器故障等仍让整个诊断保守退出。

`FormClosing` 和直接 `Dispose()` 都在销毁 HWND/WebView 前撤回挂接；窗体重建保留唯一 owner。`desktop-summary.json` 在最终关闭时记录挂接/撤回、验证与失效次数、剩余 lease 及前台核对结果，不能只凭较早生成的页面报告认定正常退出。

2026-10-25 20:43 北京时间实测：默认无准备桌面层时返回 `existing-workerw-unavailable`，无挂接且正常退出。随后确认无 Lively 进程且测试布局为空，临时运行已安装的测试用 Lively 以准备现有层，没有设置壁纸；最终版通过 **13 项真实 WebView 检查**，包括直接 Dispose 和 Close 两种撤回路径，三次挂接/三次撤回、剩余 lease 为 0、前台核对一致；最终 value 10 / sequence 2，无强制清理、专用进程残留为 0。Lively 正常退出，原空布局及设置按原字节恢复。原始报告与环境准备脚本留在仓库外。

同轮原普通模式 **11 项真实 WebView 检查**及 **10 项纯 C# 检查**通过。该结果仅证明隐藏、单屏、已有层上的 WebView 初始化/消息/窗口生命周期；没有显示桌面内容、模拟鼠标或重启 Explorer，也没有证明全屏暂停、多屏和长期资源稳定。

20:46 增加失效隔离：只把本进程隐藏子窗口的实际宽度减小 1 像素，由真实 500 ms 定时器发现 `attached-window-changed` 并回退。失效视图关闭，交互 lease 被撤销，owner 身份、序号及写锁保持；旧 lease 命令经真实消息通道被拒绝，显式重开只读窗口后原回执仍防重。最终 **15 项检查通过**，四次挂接/四次撤回、一次受控视图失效、剩余 lease 为 0，无强制清理或专用进程残留，测试用 Lively 正常退出并恢复原设置。受控几何改变不是 Explorer 重启、系统显示器变化或浏览器崩溃的复现。

## 可见模式手动使用

### 受控宿主暂停自测

```powershell
& ./diagnostics/dual-window/test-webviews.ps1 -SdkDirectory $taskSdk -OutputDirectory $taskOutput -HostPolicy
```

此模式与桌面自测分开运行，创建不可见的两个普通窗口。唯一 owner 维护原因位，暂停时拒绝命令、撤销授权并保留日志锁；恢复只重建活动时钟基准。宿主启用请求同时检查运行 revision，防止暂停前的回执在恢复后复活控制。页面只显示宿主快照，不能设置原因。

2026-10-26 实测 15 项真实页面检查通过，其中新增手动暂停、多个受控原因、时钟冻结/恢复及迟到启用。纯 owner 测试增加到 14 项；普通模式 11 项、可见桌面 16 项回归通过，均正常退出且零专用残留。原生环境采样器另行通过自有全屏窗口试验，尚未接入此循环；实际系统信号与真实页面之间的联合验证仍待完成。边界及复现见[暂停策略说明](../host-policy/README.md)。

### 原生全屏与真实页面联合自测

```powershell
& ./diagnostics/dual-window/test-webviews.ps1 -SdkDirectory $taskSdk -OutputDirectory $taskOutput -NativePolicy
```

此开关包含受控暂停检查，并在新构建中编译独立全屏测试程序、加入 PerMonitorV2 清单。控制器每 200 ms 只读采样活动会话、输入桌面和前台边界，原子更新 owner 的环境原因；进入暂停先撤销授权与重建时钟基准，再向页面发送状态。页面不能设置这些原因。测试窗约 50 秒内自动完成普通/全屏/恢复并关闭，可用 Computer Use 明确激活已返回的自有窗口；前台确认样本不足时判失败或不可判定。

2026-10-26 08:05 联合自测 **18 项通过**：346 个确认自有窗口处于前台的全屏样本期间，owner 活动时钟不变，真实页面旧命令返回 `host-paused`，状态及写锁保留；恢复后旧 lease 仍拒绝，显式启用后原请求仍去重。最终 value 10 / sequence 2，自有全屏窗正常退出，无强制清理，专用浏览器及全屏测试进程残留为 0。前台激活使用原生工具，不依赖失败的截图或鼠标坐标。

该模式运行在不可见的两个普通 WebView 窗口中，未同时显示桌面子层；没有测试渲染器挂起、锁屏、实际电源挂起、独占游戏或多屏。原生全屏检测与唯一 owner/真实消息通道已经联合，尚不能称为全部桌面输入与低占用通过。原始报告和 profile 留仓库外。

### 选择性 WebView 挂起自测

```powershell
& ./diagnostics/dual-window/test-webviews.ps1 -SdkDirectory $taskSdk -OutputDirectory $taskOutput -RendererPolicy
```

与原生前台/桌面模式分开运行。先完成受控暂停检查，再为只读页启用异步渲染协调；暂停停止绘制、隐藏控件并请求 SDK 挂起，交互页继续响应但 owner 拒绝修改。挂起期间不再发送只读快照，恢复时补发最新呈现状态，不自动恢复输入。

2026-10-26 08:15，**19 项真实检查通过**，包含暂停重载后再次挂起、恢复绘制、释放挂起视图及重建保持去重。协调器固定检查 14 项、页面启停 5 项通过；普通 11 项和可见桌面 16 项回归通过。全部正常退出、专用残留为 0。固定异常时序不等于实际制造了 SDK 的所有迟到故障；此模式也未测 CPU/内存收益，隐藏控件所需的产品冻结背景仍待接入，见[挂起说明](../host-policy/README.md)。

### 可见桌面渲染自测

08:24 已加入单张冻结背景：使用本 WebView 的 PNG，不采集其他窗口；48 MiB 流上限、8,388,608 像素解码上限，最多一个在途捕获，超时1.5秒后继续暂停。只在代次与 Core 仍匹配时替换缓存；恢复隐藏静帧，关闭释放。预算测试6项、带缓存的真实渲染19项通过。可见桌面/原生全屏联合、峰值内存和最终桌面合成仍待验收；捕获失败只能退回已有缓存或宿主背景。

```powershell
& ./diagnostics/dual-window/test-webviews.ps1 -SdkDirectory $taskSdk -OutputDirectory $taskOutput -VisibleDesktop
node --test diagnostics/dual-window/visual-probe.test.mjs
```

该独立开关包含 `-DesktopLayer`，启动 `--desktop-visible-self-test`。仅在明确传入时，将第一个只读桌面子窗口通过 WinForms `ShowWithoutActivation` 和 `WS_EX_NOACTIVATE` 短暂显示；前后均核对父关系、尺寸及前台窗口，没有 Activate/BringToFront。只读视图不再包含普通窗口的调试按钮栏，视口匹配主屏。检查结束停止动画、隐藏，再继续原有重建及受控失效检查。

2026-10-26 07:47 北京时间实测：**16 项真实 WebView 检查通过**；1920×1080 的本 WebView PNG 包含预期的两块颜色标记，至少 3 个 RAF 绘制后停止计数稳定；四次挂接/撤回、一次受控失效、零剩余 lease，前台核对一致，无强制清理或专用残留进程。普通模式 11 项、纯 C# 10 项回归通过。另 3 项绘制生命周期测试验证重复开启只有一个排程、停止后的旧回调不能干扰重启、反复启停只分配一个画布。测试用 Lively 正常退出并恢复原设置。

图像由 `CoreWebView2.CapturePreviewAsync` 生成，**仅是本 WebView 内容**，不是包含图标/任务栏的桌面合成截图。可见窗口归属、页面实际绘制和未抢焦点已有证据；最终合成、遮挡、图标输入及鼠标穿透仍未验收。测试图像、profile 和报告留仓库外，没有捕获其他应用画面，也没有把此图用于坐标输入。

API 依据：[Form.ShowWithoutActivation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.form.showwithoutactivation)、[CapturePreviewAsync](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.capturepreviewasync)。这些 API 本身不证明桌面图标输入可用。

### 可见桌面、原生暂停与挂起联合

```powershell
& ./diagnostics/dual-window/test-webviews.ps1 -SdkDirectory $taskSdk -OutputDirectory $taskOutput -WallpaperPolicy
```

此显式开关准备 NativePolicy 构建并运行 `--wallpaper-policy-self-test`，需要已有且匹配的传统 WorkerW。先完成可见渲染、视图重建/失效及受控暂停检查，再显示当前只读桌面页、启用其静帧/挂起协调器，并运行自有全屏窗口。全屏采样暂停 owner，同时保留本 WebView 的静帧并挂起只读页；恢复时显示实时页，输入仍需新授权。

2026-10-26 08:29，**25 项联合检查通过**：341 个连续确认全屏样本，静帧尺寸与1920×1080视口一致，一次有效捕获，SDK 挂起状态在全屏期间保持；恢复绘制、命令拒绝/新授权及请求去重通过。四次挂接/四次撤回，一次受控视图失效，零剩余 lease；无强制清理，专用浏览器/全屏窗均正常退出。空闲测试用 Lively 只准备已有层，测试后正常关闭并按原字节恢复空布局和设置。

全屏测试本身有意改变前台，因此不把整个运行期间的前台不变作为条件；改为围绕本项目显示/隐藏控件的操作核对，实际6次核对通过。报告保留 `foregroundScope=own-presentation-actions`，不能解释成没有任何前台切换。原有单独可见模式仍采用全程前台核对。

此前一轮被用户 Esc 中止并请求自有测试窗退出，未算通过；用户明确恢复后重新执行的本轮由测试程序自行完成，无需原生工具激活。原生工具仍返回上次中止提示，没有继续调用它或注入输入。25项不包括真实鼠标、桌面图标、锁屏、实际休眠、独占游戏、多屏或资源收益；当前画面仍是诊断标记，不是写实钓鱼场景。

### 有时限的桌面循环

显式传入 `-SoakSeconds 900` 会先运行完整联合模式，再按30秒暂停/恢复、180秒重建只读视图的顺序循环15分钟。可选范围120至7200秒；视图可用性接入版最终要求34项检查及按实际重建次数计算的挂接/撤回一致（08:53历史版本26项，08:58通知版29项）。资源读取和阶段汇总见[循环测试说明](../../docs/P0_DESKTOP_SOAK.md)，纯汇总检查可运行 `node --test diagnostics/dual-window/summarize-soak.test.mjs`。心跳和进程原始数据留在仓库外。

原生模式同时注册[本会话/电源通知](../host-policy/NATIVE_SIGNALS.md)，在原有检查后直接调用宿主回调进行受控锁定、挂起与恢复检查。加入显示与视图可用性阶段后，普通原生模式预期26项、完整可见联合33项；含循环34项。受控信号不改变真实系统会话/电源，报告明确保留 `actualLockOrPowerTransition=false`。通知接收窗口由 owner 持有，在只读视图重建时持续存在，退出时先解注册再销毁。心跳从异步绘制读取返回后复核策略代次，跨代次标记未分类；手动原因应用次数字段改为 `manualPolicyApplications`，避免把初次设置也误称一次状态转换。

### 独立显示回退模式

```powershell
./diagnostics/dual-window/test-webviews.ps1 -SdkDirectory <SDK目录> -OutputDirectory <已有仓库外目录> -DisplayPolicy
./diagnostics/dual-window/test-failure-report.ps1 -SdkDirectory <SDK目录> -OutputDirectory <已有仓库外目录>
```

独立显示模式需要已有桌面层，包含可见画布、受控会话/电源、[显示代次回退](../host-policy/DISPLAY_CHANGES.md)及[视图可用性](../host-policy/VIEW_AVAILABILITY.md)，当前共28项；明确不运行原生全屏窗，也不与完整/长循环开关混用。报告必须为 `nativeForegroundTestSkipped=true`、`nativePolicy=null`、`nativeFullscreenCombined=false`，不能拿该模式补写全屏通过。

2026-10-26 09:12，26项通过，五次挂接/撤回、11次自有呈现操作前台核对，当前 owner / sequence 2 / value 10 保留，零强制清理和专用残留。实际分辨率、缩放、多屏均未改变。先前两轮完整模式因原生全屏有效样本不足失败；第二轮记录普通17、全屏0、恢复90，本次独立结果不覆盖它们。普通11项回归通过；Lively正常退出后已有层仍可用，另一次直接复用也通过26项，隐藏模式15项通过。全新登录无已有层、Explorer重启恢复仍未知。

测试失败现在另写私有 `failure-summary.json`，保留模式、退出码、是否强制清理以及清理是否确认。失败报告集成检查用有30秒上限的自有守卫进程占用同一实例门禁，第二宿主退出2、未创建日志/profile、正常清理；守卫正常退出。未通过的运行不会生成整轮成功的 `summary.json`；较早的 `webview-summary.json` 仅表示页面检查，不能替代最终清理结果。不会扩大清理范围。

### 普通窗口

09:18 可用视图策略接入：零窗口或全部页面未就绪时停止诊断活动，至少一个当前页面就绪后才清除对应原因，保留其他暂停和 owner/写锁。真实策略17项、渲染21项、独立显示28项及普通11项通过，无强制清理或专用残留；该规则不代表所有加载中的 WebView 都被挂起。完整模式仍待全屏样本，详见[可用性边界](../host-policy/VIEW_AVAILABILITY.md)。

在明确要测试普通窗口时，手动运行 `$taskBuild.executable`，不传 `--self-test`。只读窗口和交互窗口分别显示；点击交互窗口的 `Enable interaction` 后才能提交 +1。切到其他窗口会撤销授权，回来后需再次显式启用。窗口顶部及托盘可重开已关闭的窗口；标题栏关闭按钮只关闭该窗口。

退出整个诊断请使用 `Exit both + owner` 或托盘菜单 `Exit both windows and owner`。两个窗口都关闭时 owner 仍持有日志锁和单实例门禁，这属于本诊断要验证的行为；此时从托盘退出即可。计数日志、错误详情、profile、编译产物和测试报告均留在私有输出目录。

## 验证边界

默认模式验证两个普通应用窗口的 owner 生命周期；可选模式额外验证已有桌面层上的单屏隐藏 WebView 和本机 DPI 上下文匹配。两者均不能证明原生鼠标左/右/中键、滚轮、按住后切窗、桌面穿透、图标/右键菜单、多显示器、不同缩放组合或宿主暂停策略。不可见模式中的显式授权仅供协议测试，不代表真实焦点或用户输入授权；页面重放旧 token 也不等于已制造真实进程队列中的所有迟到时序。

当前没有运行浏览器崩溃注入，故障分支的实现不能作为崩溃恢复实测结果。没有测试系统断电、磁盘故障、长期资源占用或正式游戏业务。不要据此宣称桌面壁纸、原生输入、游戏存档或完整钓鱼流程已经完成。
