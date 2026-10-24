# P0：单一 owner 与两个独立窗口

本诊断把一个只读页面和一个交互页面放在两个普通 WinForms 窗口中。共享状态仍是独立计数器，不加载游戏、不接入 WorkerW、Explorer 或 Lively，不读取原存档。

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
| `view.html` | 只呈现快照的页面和计数器提交按钮；`sendRaw` 仅用于重放过期诊断消息 |
| `DualWindowOwnerTests.cs` | 无浏览器的生命周期、写锁和旧身份回归测试 |

构建链接现有 `state-owner/DurableCounterStore.cs`、`state-owner/ViewAuthority.cs` 和 `input-window/HostInstanceGuard.cs`，复制现有 `lease-session.mjs`，不修改这些实现。与输入候选宿主共用当前 Windows 会话的单实例门禁，发现另一个候选实例时退出码为 2；不要为运行测试关闭不属于本次测试的进程。

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

## 可见模式手动使用

在明确要测试普通窗口时，手动运行 `$taskBuild.executable`，不传 `--self-test`。只读窗口和交互窗口分别显示；点击交互窗口的 `Enable interaction` 后才能提交 +1。切到其他窗口会撤销授权，回来后需再次显式启用。窗口顶部及托盘可重开已关闭的窗口；标题栏关闭按钮只关闭该窗口。

退出整个诊断请使用 `Exit both + owner` 或托盘菜单 `Exit both windows and owner`。两个窗口都关闭时 owner 仍持有日志锁和单实例门禁，这属于本诊断要验证的行为；此时从托盘退出即可。计数日志、错误详情、profile、编译产物和测试报告均留在私有输出目录。

## 验证边界

这是两个普通应用窗口的 owner 生命周期验证，不能证明原生鼠标左/右/中键、滚轮、按住后切窗、桌面穿透、图标/右键菜单、多显示器、DPI 或宿主暂停策略。不可见模式中的显式授权仅供协议测试，不代表真实焦点或用户输入授权；页面重放旧 token 也不等于已制造真实进程队列中的所有迟到时序。

当前没有运行浏览器崩溃注入，故障分支的实现不能作为崩溃恢复实测结果。没有测试系统断电、磁盘故障、长期资源占用或正式游戏业务。不要据此宣称桌面壁纸、原生输入、游戏存档或完整钓鱼流程已经完成。
