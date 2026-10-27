# P0：实际拓扑采集与隐藏页面协议

这是独立探针的新显式模式，复用原有页面/source/doc/nav/ticket防护、唯一owner及持久计数器。默认`--self-test`的15项逻辑矩形模式保留。

2026-10-26 21:34（北京时间补录）：主代理实机执行12项检查通过。当前设备为1个输出，2个并发WebView、3次文档加载、host view代数2；captureSerial最终4、拓扑代数2、同一owner最终2/10。三次实际Known采样共创建/释放6/6个临时测量HWND、剩余0；正常退出0、无强制清理、清理确认、专用残留0。WebView2 Runtime为155.0.4283.62。同轮默认15项模式也复验通过。

首轮受限执行失败，原生页报告`Private WebView failed`，wrapper的CIM进程查询遭拒，当轮清理无法确认，因此未记通过。之后以正常用户权限补查该轮专用profile，确认进程为0，再执行上述通过的复验。这些观察没有证明失败的唯一根因，不把首轮失败改记为成功。

```powershell
./diagnostics/multi-output/build.ps1 -SdkDirectory <SDK目录> -OutputDirectory <已有仓库外目录> -CapturedTopology
./diagnostics/multi-output/test-webviews.ps1 -SdkDirectory <SDK目录> -OutputDirectory <已有仓库外目录> -CapturedTopology
```

runner选择`--captured-topology-self-test`，构建使用`output-topology/desktop.manifest`的PerMonitorV2声明。运行时仍由Win32查询核对线程/临时窗口awareness；不调用awareness setter或修改系统设置。只启动自己的唯一构建目录与私有profile，沿用55秒内部/60秒外部期限、完整路径/启动时间身份核对及正常清理要求。任何强制终止、临时HWND或专用浏览器进程清理不明都不能通过。

## 数据与边界

`TopologyCapture(Win32TopologyQuery)`实际读取当前设备的输出矩形及有效窗口DPI，经`TopologyPolicyBridge`决定是否换代。当前模式明确上限4个输出，超过上限整次不可用，不截断猜选；每个实际捕获的逻辑别名创建一个只读WebView，另有一个控制WebView。逻辑别名来自本次捕获，不是永久物理设备ID。

协议窗只创建Form/WebView handle，不调用Show，不附桌面；Ready前核对其自有PID/线程、隐藏状态和NoActivate。它们没有按实际输出定位，也不读取外部窗口或画面。横屏资格来自捕获矩形宽高；本轮写入场景要求当前主输出具备横屏资格，否则不会为它强行发测试授权。全屏未遮挡及renderer可用仍是显式合成资格声明，没有OS逐屏全屏/渲染watchdog观察、SDK挂起或绘制策略。

所有不确定采样返回Unknown并阻塞。第二次实际采样与恢复采样必须与首个已确认内容相同，才可完成本模式；如果运行时系统拓扑变化，本轮明确失败，不把不同配置当作稳定采样或同内容恢复通过。此限制不等于产品已能监听、定位或恢复真实显示变更。

第三次采样由`ITopologyQuery`包装器在一次Enumerate中抛出明确合成故障，实际Win32查询未改动，不制造系统热插拔或驱动异常。恢复再调用原Win32查询，不使用缓存正样本。

## 本轮已执行的12项检查

1. 第一次实际Known采样建立拓扑代数1，未绑定页、未自动Ready或授权。
2. 当前每个逻辑输出与单控制页使用独立隐藏NoActivate页面、同一owner；Startup独立保持到明确解除。
3. 每个只读页即使携带当前控制lease，真实页面消息也被拒绝。
4. 原生显式测试授权提交`+7`一次，所有页收到同一owner/1/7。
5. 第二次相同实际内容只增加captureSerial，不换拓扑、文档、view代数、输入epoch或原授权；原请求仍返回去重回执。
6. 第三次明确合成Enumerate故障返回无输出的Unknown，owner暂停、旧控制拒绝；不创建额外临时测量HWND。
7. 第四次真实采集恢复相同内容仍建立拓扑代数2，解绑旧Ready、拒绝旧Observe/激活token，并保留独立Manual位。
8. 显式退役旧只读页后重建隐藏页，旧ticket不能复活；控制页、owner及journal写锁保持，Ready不清Manual。
9. 明确清Manual后旧lease/旧文档仍拒绝；只有重新核对policy并发放新测试grant才能写入。
10. 新授权提交`+3`，全部重建页与控制页收到同一owner/2/10。
11. 两个原请求在恢复后仍返回原回执，不增加第三笔提交。
12. 所有页正常关闭后owner仍持有journal锁和2/10，活动资格关闭；终结清理由runner再核对。

若本机当前有N个输出，期望最大并发WebView为N+1、文档加载2N+1、host view代数2N；三次Known实际采样共创建/释放6N个临时测量HWND，合成失败采样不创建HWND。captureSerial最终4，拓扑代数最终2。

私有`captured-topology-observations.json`记录四次采样的逻辑别名/矩形/DPI、Known/原因、桥接结果及临时HWND计数；没有原始HMONITOR、设备名或外部画面。`captured-topology-summary.json`记录检查及边界，`process-summary.json`记录正常退出/清理。页面确认超时只记录角色与预期/实际policy revision，不输出owner或lease。

本轮只证明当前单屏设备配置下的采集→纯策略→隐藏页面/owner协议；不能称为真实物理多屏、混合DPI、热插拔、缩放改变、产品窗口定位或桌面体验验收。Unknown是包装器合成的枚举失败，恢复是再次真实采集相同内容，不是实际改变系统显示配置。现有DualWindowHost仍为单只读页/PrimaryScreen；没有接通正式多输出控制器、时钟、三维场景、实际输入握手或游戏存档。
