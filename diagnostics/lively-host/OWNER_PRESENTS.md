# 独立owner的限定呈现采集

2026-10-26。`measure-presents.ps1`新增Owner参数集，作用域、离线检查与采集实测分别记录。结果边界见[首次采集](OWNER_PRESENTS_RESULTS.md)。

```powershell
./diagnostics/lively-host/measure-presents.ps1 `
  -OwnerExecutable '<确切的私有构建目录>\DualWindowHost.exe' `
  -PresentMonExecutable '<工具目录>\PresentMon-2.6.0-x64.exe' `
  -OutputDirectory '<已存在的仓库外目录>' -Seconds 30
```

OwnerExecutable与原LivelyExecutable互斥。原Lively模式仍要求确切Lively根进程、其指定WebView2播放器及唯一GPU后代；不会自动改成按`msedgewebview2.exe`名称采集。

## Owner作用域

- 查找唯一、准确路径的DualWindowHost根进程，逐个读取PID、启动时间和路径验证身份；发现子进程时重查父身份，完成后再重查选中链的所有祖先。拒绝复用PID、路径变化、比父进程更早的子进程及无法证明的WebView后代。
- Owner浏览器必须使用其旁边`private-profile`目录或其中固定的`EBWebView`运行时子目录；这两个位置拒绝重解析点，不接受任意其他profile、路径前缀相似、重复参数、缺少profile或模糊GPU目标。要求根→浏览器→GPU直接父链，两个WebView路径一致。
- Windows风格参数分词后才识别type/profile开关，其他参数内的引用文本不能伪造一个profile开关。选择器返回的元数据不包含原始命令行。
- 启动辅助程序前再次核对根/浏览器/GPU，并在采集期间保留准确GPU进程句柄；结束后仍要求身份一致。工具输出CSV每行必须属于这个GPU PID，目标退出、改变、无记录或工具异常均记unavailable，不填0FPS。

工具仍锁定已校验SHA-256与有效签名的PresentMon 2.6.0 x64，没有下载安装、提权、全局热键或服务操作。仅生成随机唯一ETW会话，确认原先不存在后启动；超时只关闭自己启动且路径/启动时间匹配的辅助进程，清理只针对自己的会话，随后查询确认不存在。无法确认清理或目标身份时不得报告成功。

辅助程序启动或等待失败时，私有输出另有固定阶段、异常类型/HResult及本工具预期/实际路径诊断，不记录命令行或异常消息，不进入脱敏汇总。首次启动失败尚未复现，不据此认定为Path原因，也不放宽身份校验。

## 纯检查与限制

```powershell
./diagnostics/lively-host/test-present-target.ps1
node --test diagnostics/lively-host/summarize-presents.test.mjs
node diagnostics/lively-host/summarize-presents.mjs '<已有私有捕获的绝对目录>'
```

25项纯作用域检查通过，覆盖Owner/Lively正例、外部或缺失profile、EBWebView子目录、嵌入引用伪造、重复参数、多个根/GPU、间接或绕过浏览器父链、不同WebView路径、父PID复用/路径变化、最终GPU消失、未知后代及负启动时序。测试只供应原始身份，决定使用真实选择器；Lively的gpuFlag/playerAncestor由已验证父链推导并保留，避免破坏原离线工具。语法与diff检查通过。

离线汇总20项检查通过，保留旧Lively14项并新增Owner作用域、三处Kind/profile一致性、直接父链/路径/启动先后、显式或首版角色字段和脱敏检查。旧Lively无Kind记录按原合同验证；新Lively/Owner要求summary.hostKind、metadata.hostKind与target.scopeKind一致，三处profile断言也一致。Owner不能只凭文件名推断。

首版Owner元数据没有独立gpuFlag，但记录了明确Owner选择器角色、三处profile断言和直接三节点身份链；严格通过后标为`initial-owner-selector-role`，不是从历史记录重新恢复原始命令行。新采集显式保留已验证gpuFlag，标为`explicit-selector-flag`；存在但为false的标志仍拒绝。所有断言只是离线核对，不是独立认证或实时进程复查。

一份15秒Owner原始记录离线通过：446行、两个交换链组；主组445条均有显示记录，推导提交/显示变化约29.898/29.899次每秒，显示间隔P95约33.3515ms、最大200.0697ms；另一组一条未显示、率不可用。采集跨策略版本，不作为稳定活动区间。没有改写原始记录，没有合并两个组，内容FPS保持null，不代表完整游戏性能验收。

同一个GPU进程可能包含只读页、控制页和多个交换链。准确PID不等于准确湖面窗口；必须再核对交换链与显示归属、活动/暂停阶段和有效区间，才能解释提交/显示变化率。它不证明Three.js内容FPS、最终写实性能、原生全屏、多屏或真实驱动故障。原始CSV、进程身份、工具路径与会话信息始终留在仓库外，不自动公开。
