# 独立桌面启动准备候选

2026-10-26 11:25，北京时间。此候选为P0补充，未接入双窗口宿主默认启动，也未完成首次登录实测。现有 `desktop-layer` 挂接组件不变，仍只接受已存在、核对通过的传统 WorkerW。

## 行为

`DesktopStartupCoordinator.Prepare(target, allowMissingLayerRequest)` 首先调用原发现组件：已有可用层直接返回，任何占用、DPI、几何、身份、抬升桌面或其他错误都不触发准备。默认不允许请求。

只有明确允许、发现结果恰为缺少 WorkerW、稳定且唯一的传统图标层满足条件时，才生成绑定 Shell进程/启动时间/路径、窗口、图标层、DPI和目标范围的意图。再次发现若已有层则复用；否则核对意图仍相同，每个协调器至多调用一次准备接口。成功返回仍必须重新发现有效层，不能把消息发送成功当作桌面准备完成。

原生适配器在创建线程执行，先查询当前交互会话/环境，再核对实际Shell快照。固定发送到核对过的Progman，使用 `0x052C / wParam=0xD / lParam=1`、1000毫秒和 `SMTO_BLOCK | SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT`；不使用广播或取消超时选项。该私有协议参数来自[Lively v2.2.1.0 的实际实现](https://github.com/lively-community/lively/blob/v2.2.1.0/src/Lively/Lively/Core/WinDesktopCore.cs)，不是Windows承诺的壁纸扩展接口；抬升桌面仍拒绝。这里是独立的保守实现，没有复制Lively源码。

请求失败、超时、Shell切换或出现不合格层时返回失败，不重试、不重启Explorer、不关闭/隐藏/销毁其窗口。Explorer自行创建的层归Shell所有；请求超时可能仍在稍后生效，不能声称已撤销或回滚Shell内部动作。候选不挂接窗口，也不改变全局DPI。实际挂接继续使用原组件及其重复验证/撤回规则。

## 运行

```powershell
# 纯固定检查，不实例化原生适配器，不发送Shell消息。
./diagnostics/desktop-startup/test-startup.ps1 -OutputDirectory <已有仓库外目录>
# 只查询，默认不准备、不创建窗口。
./diagnostics/desktop-startup/probe.ps1 -OutputDirectory <已有仓库外目录>
# 仅显式选择时允许缺少层的准备；已有可用层仍直接复用。
./diagnostics/desktop-startup/probe.ps1 -OutputDirectory <已有仓库外目录> -PrepareMissingLayer
```

原生探针声明PerMonitorV2，使用当前会话的诊断单实例门禁，外层10秒期限，仅清理身份仍匹配的自有探针。输出只含布尔、固定原因、计数与屏幕尺寸，不写第三方窗口身份、标题或画面。构建/报告在仓库外。

## 实际结果与缺口

- 24项纯检查通过：已有层复用、并发出现、缺层默认拒绝、有效准备后重新发现、占用/抬升/DPI/图标歧义/身份/几何变化、失败/异常只尝试一次、调用线程及请求统计边界等。假接口不是实际Shell准备成功的证据。
- 本机1920×1080两次原生探针，分别默认和允许准备；均复用现有层，准备接口未调用、实际Shell消息0，前台未变化，无窗口创建或挂接，自有进程正常退出。
- 实际缺层时发送消息、首次登录、Explorer重启、不同Windows桌面结构、多屏/DPI及准备后可见挂接尚未实测。当前不能宣称已解决无现成层启动，或已移除Lively产品依赖。

Windows API边界参考：[SendMessageTimeoutW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw)的返回/超时与标志、[SetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)的样式和跨DPI要求。它们不保证私有Shell协议或图标输入；本阶段不重新执行人工输入测试，不恢复暂停中的游戏UI/玩法。
