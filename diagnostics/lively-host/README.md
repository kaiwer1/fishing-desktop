# Lively 独立宿主诊断

用于分离页面脚本、Canvas/WebGL、宿主属性回调和暂停截图问题，不属于游戏界面。页面和 Node 收集器无需第三方包；Windows 性能采样依赖系统计数器，呈现采集另需指定版本的 PresentMon。不读取存档、账户、浏览器资料或其他应用画面。

2026-10-26 17:09：呈现采集支持[确切独立Owner](OWNER_PRESENTS.md)，25项范围/20项离线检查通过；[首次15秒结果](OWNER_PRESENTS_RESULTS.md)有两个交换链且跨策略版本，内容FPS不可用，不合并为游戏帧率。原Lively接口和旧记录验证保留。

## 运行

在仓库根目录运行 `node diagnostics/lively-host/server.mjs`，收集器仅绑定 `127.0.0.1:43817`。可选环境变量 `PROBE_RECORD` 指向仓库外的 JSONL 记录文件。`/status` 返回当前会话最近 600 条事件，重启会清空内存记录；Ctrl+C 关闭。不要将记录文件写进公开仓库。

把本目录的 `index.html`、`LivelyInfo.json`、`LivelyProperties.json` 复制到 Lively 壁纸库中的一个**新建专用目录**，再通过 Lively 导入/加载。2.2.1.0 的 CLI 不接受任意外部项目目录作为库项目；不要覆盖已有壁纸。加载前保存布局与设置，存在用户壁纸时须恢复原布局。

页面先读取自己绘制的 Canvas/WebGL 像素，再按秒报告计数和回调。页面记录鼠标按钮编号以及是否收到键盘事件，不记录键盘内容；只有真实桌面输入才能作为输入验收，脚本派发事件不算。

通过官方 CLI 分别测试属性 `probe=7`、`app --play false` 与 `app --play true`。最后一个命令恢复**自动策略**，前台/全屏等规则仍可能让壁纸暂停。截图命令仅请求本壁纸的画面；CLI 子进程退出只代表请求已发出，需等文件及宿主成功/失败日志，不能立即把文件不存在判成失败。

暂停时截图可能约 5 秒超时，恢复后才落盘。使用不同文件名标记每次请求，并核对请求、回调和文件时间，避免把迟到结果当成暂停时截图成功。不要为截图永久关闭用户的暂停规则。

## 对旧样机做有限观察

`baseline-observer.js` 只在**独立导出的构建副本**使用：复制进其目录，在 HTML 的 `<head>` 开头添加 `<script src="./baseline-observer.js"></script>`，早于正式模块加载。给该测试副本一个独立的库目录与标题。

观察器计数 WebGL draw 调用，采集环境时钟/阶段/错误提示是否可见，并包装已有暂停回调后调用原函数。找到已有 `livelyWallpaperPlaybackChanged` 后才发送启动事件及每秒心跳。它不模拟输入、不读写游戏存档，不代表原封不动的发行包。先对干净基线取得壁纸截图，再对观察副本做暂停对照；不要替换发布构建。

主要字段如下；计数随页面重新加载而归零，`run` 随之更换：

| 字段 | 含义与限制 |
| --- | --- |
| `drawCalls` | 包装的 WebGL/WebGL2 `drawArrays`、`drawElements` 及其实例化方法的累计调用次数。每次绘制通常有多次调用，不等于帧数，也不保证调用产生有效像素。 |
| `renderedCallbacks` | 执行期间出现上述 draw 调用的 RAF 回调累计数；没有 draw 的 RAF 不计入。draw 后抛错的回调仍会计数，原错误继续抛出。 |
| `paused` | 最近一次宿主暂停回调的 `IsPaused`；首次回调前为 `null`。它是观察到的宿主状态，不能用操作者标签代替。 |
| `callbackCpuMsP95` | 最近最多 120 次含 draw 的 RAF 回调耗时的 P95，单位毫秒。是 JavaScript 回调的墙钟耗时，不是进程 CPU 占用或 GPU 执行时间。 |
| `callbackTimestampAgeMsP95` | 相同滚动窗口内，回调开始时 `performance.now()` 减 RAF 时间戳的 P95。反映时间戳到回调执行的间隔，不是画面呈现延迟或输入延迟。 |
| `requestedSettings` | 从界面读取的 `quality`、`scale`、`fps` 请求值；不能证明对应画质、分辨率或帧率已经实际生效。 |
| `width` / `height`、`drawingBuffer` | 当前页面视口尺寸，以及最近一次 draw 所用上下文的绘图缓冲区尺寸。后者在还没有 draw 时为 `null`，暂停或调整窗口后可能仍是上次绘制的尺寸。 |
| `weather`、`phase`、`fallbackVisible` | 对应界面文字与错误提示可见状态。错误事件另记录截断后的错误文字。 |

两个 P95 都只在含 draw 的回调后更新，暂停时可能保持原值；它们的 120 次窗口可能包含当前采样时段之前的数据。WebGL draw 调用数、含 draw 的 RAF 回调数都不能直接解释为实际呈现帧率。短时间处于“准备抛竿”状态也不等于已验证蓄力、咬钩、遛鱼等状态的恢复。

## 清理

关闭本次测试壁纸，恢复测试前布局/设置，关闭由测试启动的宿主和收集器，检查没有测试播放器残留。本项目测试安装的开机启动保持关闭；已有用户设置应按备份恢复。本次实测与未验证项见 [宿主基线](../../docs/WALLPAPER_HOST_BASELINE.md)。

## 原生前台测试程序

`ForegroundProbe.cs` 是独立的可见窗口，只改变自己的窗口大小和边框，不自动操作其他应用。按钮或键盘焦点上的 Enter 进入无边框全屏，Esc 恢复 960×600 的普通窗口。通过同一本机收集器报告自己的窗口状态，便于与壁纸暂停回调按时间对照；这是普通无边框全屏，不模拟独占 Direct3D 游戏。

在 Windows 中使用已有 .NET Framework 编译器构建：

```powershell
./diagnostics/lively-host/build-foreground.ps1 -OutputPath 'C:\existing-private-test-folder\ForegroundProbe.exe'
```

输出目录必须已存在，并放在仓库外。编译后启动该程序，再用其按钮/键盘操作；不把编译成功或可访问性文本本身当成自动暂停通过，需要窗口状态、回调及暂停期间计数一起证明。关闭测试程序后确认无进程残留。程序不安装任何依赖，不修改系统全屏、显示或输入设置。

## 短时宿主进程采样

```powershell
./diagnostics/lively-host/measure-processes.ps1 -LivelyExecutable 'C:\path\Lively.exe' -OutputPath 'C:\existing-private-test-folder\load.json' -Phase paused -Seconds 10
```

只匹配指定路径的一个宿主及其子进程，不枚举输出其他应用的资料。`Phase` 是操作者标签，必须用同时段的页面记录确认实际状态。CPU 使用前后均存在的进程及各自采样间隔计算；机器归一化值除以逻辑处理器数量。私有提交内存和工作集分别统计，合计工作集可能重复计算共享页。短时 CPU/内存数值不是 GPU/显存、实际呈现帧率、功耗或长期内存验收，暂停后也不能预设一定明显省 CPU。

## CPU、内存与 GPU 计数器采样

`measure-gpu.ps1` 只读采样已运行的指定宿主，不启动壁纸、不改变窗口或暂停策略。`LivelyExecutable` 必须是准确的 `Lively.exe` 路径，并且当前只能匹配一个实例。`OutputDirectory` 必须已存在且在仓库外，路径及祖先不能是 junction 等重解析点。

```powershell
./diagnostics/lively-host/measure-gpu.ps1 `
  -LivelyExecutable 'C:\path\Lively.exe' `
  -OutputDirectory 'C:\existing-private-test-folder\perf-session-example' `
  -Seconds 30 -IntervalSeconds 1 -Phase unlabelled
```

`Seconds` 范围为 5–300，默认 30；`IntervalSeconds` 范围为 1–60，默认 1，且不得大于 `Seconds`。间隔是最小目标值，计数器查询耗时可能延长间隔及总时长。`Phase` 可取 `active`、`paused`、`unlabelled`，默认最后一项，仅作人工注释。每次创建独立 `gpu-run-<随机值>/`，保存 `samples.jsonl` 与 `summary.json`，并把同一摘要打印到标准输出。

采样范围由根进程及可证明属于它的后代构成，使用 PID 与启动时间识别进程；GPU 查询前后再次核对身份。DWM 与其他应用不计入。CPU 只计算两次观测均存在的进程，首个样本没有 CPU 增量，新建或退出进程可能漏计；`cpuMatchedProcessCount` 用于说明覆盖。CPU 机器归一化百分比除以逻辑处理器数量，`privateBytes` 为私有提交内存，合计工作集可能重复计算共享页。

GPU 使用 Windows 的 `GPUEngine` 与 `GPUProcessMemory` 格式化性能计数器。首次类信息发现只是参考，实例在每次采样重新读取；某次失败保留不可用，后续查询可以恢复。报告必须同时看以下三层覆盖，不能只看一个占用数值：

- `gpuEngineProviderAvailable` / `gpuMemoryProviderAvailable`：此次 provider 查询是否可用；可用不代表所有目标进程都有实例或数值。
- `gpuStableProcessCount`、`gpuEngineProcessCount` / `gpuMemoryProcessCount`：查询前后身份稳定的进程数，以及匹配到计数器实例的进程数。
- `gpuEngineNumericProcessCount`、`gpuDedicatedNumericProcessCount`、`gpuSharedNumericProcessCount`：该指标所有匹配实例都有有效非负数值的进程数；`gpuMemoryNumericProcessCount` 要求 dedicated 与 shared 均满足。没有实例不算完整覆盖。原始样本还提供逐进程的实例数和数值覆盖。

缺失实例或字段保留 `null`，不能写成 0；部分覆盖也不意味着未覆盖进程一定在用 GPU。`maxSingleEngineReportedPercent` 是同一引擎内汇总目标进程后最忙引擎的报告值；`sumAcrossEnginesReportedPercent` 跨引擎求和可能超过 100，不能当成整卡利用率。dedicated/shared 值以字节计，汇总仅覆盖可用实例，同一共享分配可能被多个进程重复统计，不能当成唯一物理显存占用。

格式化计数器存在刷新间隔与整数精度，快速轮询不会创造额外精度。GPU provider 只有 PID，前后身份检查仍不能完全排除缓存归属问题。摘要的 `mean` 是可用样本的算术平均，`peak` 是其中最大值，二者都不是按持续时间加权的结果。本工具不测帧率、功耗、温度或长期稳定性。

## 离线关联心跳与性能样本

给一次观察建立**全新的仓库外会话目录**。以下收集器命令在单独终端运行，父目录须已存在；使用本次创建的完整会话路径供其他终端引用：

```powershell
$taskSession = Join-Path 'C:\existing-private-test-folder' ('perf-session-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskSession | Out-Null
$env:PROBE_RECORD = Join-Path $taskSession 'events.jsonl'
node diagnostics/lively-host/server.mjs
```

在另一个终端指定同一会话目录，保存采样脚本的标准输出为顶层 `*-summary.json`。每次采样使用不同文件名，且观察器应在采样前已开始连续发送心跳：

```powershell
$taskSession = 'C:\existing-private-test-folder\perf-session-实际会话值'
./diagnostics/lively-host/measure-gpu.ps1 `
  -LivelyExecutable 'C:\path\Lively.exe' -OutputDirectory $taskSession `
  -Seconds 30 -IntervalSeconds 1 -Phase unlabelled |
  Set-Content -LiteralPath (Join-Path $taskSession 'sample-01-summary.json') -Encoding utf8

node diagnostics/lively-host/summarize-perf.mjs $taskSession |
  Set-Content -LiteralPath (Join-Path $taskSession 'correlated-report.json') -Encoding utf8
```

`summarize-perf.mjs` 只接收一个会话目录参数，只读其中的 `events.jsonl`、顶层 `*-summary.json` 及各摘要的 `rawSamplesFile`，只向标准输出打印整理 JSON。所有输入实际路径都必须留在同一会话内且在仓库外；拒绝 `..` 穿越、符号链接逃逸、无效输入及超过 64 MiB 的单个文件。不要把整理报告命名为 `*-summary.json`，也不要把 PresentMon 的摘要放入该输入集合。

报告按每个摘要的 `startedAtUtc` 至 `startedAtUtc + elapsedSeconds` 划定窗口，以收集器事件接收时间关联同一观察器 `run`。它输出心跳数、draw/RAF 计数增量、`paused` 的 true/false/null 数量、天气首尾值、两个滚动 P95 的范围、请求设置、视口/绘图缓冲区尺寸、错误数、provider 与进程覆盖，以及原摘要的 CPU/内存/GPU/显存均值和峰值；不输出个人路径、PID、run 标识或原始错误文字。

`applicationSubmissionCallbacksPerSecond` 用首末心跳间隔计算含 draw 的 RAF 回调数每秒，不能标为呈现 FPS。心跳不足 2 条、窗口内多个 run、无效或非单调计数、心跳间或窗口边缘超过 2.5 秒的观测空缺均标为 `indeterminate`；原始样本数量、时间范围或顺序不一致也会使结果不可判定。`Phase` 始终保留为未经验证的标签，暂停结论只来自心跳，混合状态会明确标出。可判定表示这些关联检查通过，不表示性能达标或状态恢复已验收。

## Chromium 呈现采集

`measure-presents.ps1` 使用已校验签名及固定 SHA-256 的 **PresentMon 2.6.0 x64**，不接受随意替换的版本。提供准确的工具文件路径与已运行的 `Lively.exe` 路径：

```powershell
./diagnostics/lively-host/measure-presents.ps1 `
  -LivelyExecutable 'C:\path\Lively.exe' `
  -PresentMonExecutable 'C:\path\PresentMon-2.6.0-x64.exe' `
  -OutputDirectory 'C:\existing-private-test-folder\perf-session-example' `
  -Seconds 30
```

`Seconds` 范围为 5–60，默认 30。输出目录必须已存在、位于仓库外，且路径祖先不能是重解析点。脚本只接受一个准确匹配的 Lively 根、其下一个 `Plugins\WebView2\Lively.Player.WebView2.exe` 播放器，以及属于该树的唯一 `msedgewebview2.exe --type=gpu-process`。没有匹配或存在歧义就拒绝采集，不扩大到全部进程；启动前再核对 PID 与启动时间。

PresentMon 在隐藏窗口中采集该 GPU 进程，使用唯一 `FishingDesktop-<随机值>` ETW 会话、定时退出、目标退出终止、v2 指标及显示元数据；关闭输入与 GPU 工作追踪。不提权、不设置全局热键、不改变系统服务或壁纸输入。超时只停止本次启动且身份匹配的辅助进程，并仅针对自己的唯一 ETW 会话尝试清理，再查询确认会话已不存在；不会停止其他会话。

每次在新的 `present-run-<随机值>/` 保存 `metadata.json`、`presents.csv`、工具标准输出/错误日志、必要时的清理日志及 `summary.json`。工具退出码为 0 仍须有有效且归属正确的 CSV；空 CSV、失败、目标变更或清理未确认会报告 `unavailable`，不能记为 0 FPS。`captured` 只表示获得通过基本检查的呈现记录。脚本始终输出 `fps: null`，不计算帧率。

必须区分三个层次：

| 观测层次 | 能说明什么 |
| --- | --- |
| 应用 WebGL draw 与含 draw 的 RAF 回调 | 应用何时调用绘制、提交回调是否停止；一个回调可有多次 draw，同一刷新周期也可有多个回调。 |
| Chromium GPU 进程的交换链呈现 | PresentMon 记录系统呈现链路中的事件；同一 GPU 进程可包含多个交换链，必须先核对交换链与显示归属。 |
| 实际钓鱼内容更新帧率 | 还需证明呈现的内容发生了更新，并关联应用提交、交换链与显示事件。合成器可能反复呈现已有内容，CSV 行数或呈现事件率不能直接当成新内容帧率。 |

页面 draw 不再增加，可以证明所观察的方法没有继续被调用，不能据此推断系统合成器也停止呈现。PresentMon 有记录，也不能据此证明游戏逻辑仍在推进。这里仅说明采集与解释方法，不代表某一轮最终性能验收结果。

### 离线整理呈现记录

```powershell
$taskCapture = 'C:\project-parent\tools\temp\perf-session-实际会话值\present-run-实际捕获值'
node diagnostics/lively-host/summarize-presents.mjs $taskCapture
```

输入必须是完整路径，位于本仓库旁的 `tools/temp` 或系统临时目录内，且不在仓库中。拒绝 `..`、链接/重解析路径、跨目录文件引用。读取 `summary.json`、`metadata.json`、`presents.csv`，核对已记录的工具身份、目标和时间顺序、参数、清理状态及字节/行数；这是离线一致性检查，不是重新认证实时进程。输出不含 PID、交换链地址、路径或会话名。

按交换链、显示 source 和 layer 分组匿名输出，不合并多组速率。v2 指标用 `CPUStartTime + CPUBusy` 推导提交时点，用 `CPUStartTime + DisplayLatency` 推导显示开始时点。每组提供首末跨度、nearest-rank 间隔分位数、边界和缺失记录；不使用包装程序耗时替代帧间隔。NA、未知时间、采集失败或清理未确认不会被补为零；`contentFramesPerSecond` 始终为空，因为这些记录尚未与实际壁纸内容逐帧对应。

## 私有记录与验证

事件、截图、原始性能样本、PresentMon CSV、可执行文件路径、进程身份、显示元数据和设置备份均留在仓库外的专用目录。采样脚本打印的摘要也可能含绝对路径和 PID，应按原始记录保管；整理 JSON 省略这些字段不等于自动授权发布。收集器的 `PROBE_RECORD` 是追加写入，新会话应使用新文件，结束后在该终端清除本次环境变量，避免下次写入旧记录。

观察器与离线整理器可做无浏览器回归验证：

```powershell
node --test diagnostics/lively-host/baseline-observer.test.mjs diagnostics/lively-host/summarize-perf.test.mjs diagnostics/lively-host/summarize-presents.test.mjs
./diagnostics/lively-host/measure-gpu.test.ps1
```

Node 检查共 29 项通过；GPU 脚本的 27 项固定测试只提取函数并替换 CIM/进程接口，不执行真实采样入口。它们验证封装、计数、恢复、进程归属和关联规则，不能证明真实 WebGL、桌面输入、宿主暂停或实际显示帧率。原生脚本的语法校验同样不能代替目标机器的实际采样。
