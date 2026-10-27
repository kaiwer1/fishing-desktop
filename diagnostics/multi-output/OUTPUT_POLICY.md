# P0：多输出可用性与全局暂停纯模型

2026-10-26。`OutputPolicy`是独立owner线程模型，已在本目录隐藏WebView探针中做适配验证，尚未接入实际DualWindowHost。它不枚举屏幕、不读取EDID、不挂接桌面、不推进游戏时钟，也不发输入lease。

## 模型与API

- `OutputDefinition(key,x,y,width,height,dpiX,dpiY)`保存host声明的几何和DPI，检查非零尺寸、正DPI和整数端点范围；负坐标合法。数据不是实机观测，模型不证明这些矩形对应真实显示器。输出key沿用1至64个ASCII字符，首字符字母/数字，后续另允许`-_.`，统一小写。
- `ReplaceTopology(generation,definitions)`一次验证并替换整组输出。重复key、空定义、容量超限或枚举中断都不改变旧状态；旧/相同generation返回false。新拓扑使所有视图未绑定，旧拓扑回调无效。默认容量16是可配置诊断上限，不是发行版硬件承诺。
- `BindView(key,topology,newViewGeneration)`要求当前拓扑及高于该槽高水位的正view代数，清除该输出先前ready/渲染观察。`Observe`只接受当前仍绑定的view，原子更新ready、landscape、fullscreen、rendererAvailable；旧回调返回false。
- `RetireView`保留view高水位并标记未绑定，防止重开前的迟到ready重新激活旧页。重复退役或旧代数不能退役替换页。
- `SetGlobal(reason,blocked)`只改 legacy 票中的一个全局位；`ReplaceGlobal(mask,blocked)`原子替换 legacy 票中的一组相关观察。默认 Startup 也属于 legacy 票；没有注册来源时保持原行为。二者不能清除注册来源的票。
- `SelectControlTarget(key/null)`最多选择一个当前逻辑目标。`CanActivate(key,topology,view,inputEpoch)`仅检查当前资格，没有授权副作用，仍须实际宿主的前台/文档/握手与lease逻辑。
- `Snapshot()`返回只读集合及不可变行，包含revision、拓扑代数、inputEpoch、target、全局位、每输出本地阻塞/绘制资格、共享CanRun资格。

所有读写限定创建模型的线程，旧回调、无效参数和拒绝请求不部分改变状态。有效重复观察不持续增加版本。

## 多来源全局阻塞票

`OutputPolicy(int maximumOutputs=16, int maximumGlobalSources=8)`增加有界的 host 来源容量；来源上限可配置为 1–64。来源 token 仅由宿主持有，不是页面标识或输入 lease。

- `RegisterGlobalSource(mask, initialBlocked=None)`注册一个不透明 `OutputGlobalSource`。只读 `OwnedMask`在注册时固定，初始票必须在此 mask 内；等待首次可信观察的控制器应显式传入相应 Unknown 初始票，注册本身不会猜测来源状态。
- `ReplaceGlobalForSource(token, blocked)`原子替换该来源的整张票，只接受自身 policy 注册的同一个 token，且 blocked 必须在其固定 OwnedMask 内。null、未注册/外 policy token、非法位及错误线程在改变状态前拒绝。
- 有效全局状态始终为 `legacy票 OR 所有来源票`。允许多个来源投同一位；轮询恢复只能清自己的票，不能清通知锁存、Manual 或其他来源的同位阻塞。
- **隐藏票也保存。** 例如 A 已投 EnvironmentUnknown，B 再投同位时聚合不变，但必须记住 B；A 恢复后仍由 B 阻塞。legacy 票被来源遮住时，其增加或清除同样保存。
- 两个 Replace 方法及 SetGlobal 的 bool 返回值表示**聚合是否变化**，不是“隐藏票是否写入”。聚合不变可返回 false，仍已保存新票，且不增加 revision/inputEpoch。聚合变化沿用原规则：新增阻塞失效输入作用域；最终恢复资格仍须新 epoch，不会授予或恢复实际 lease。
- 没有注销、自动 Dispose 或来源替换接口。清空票后 token 仍占容量，直到该 policy 生命周期结束；宿主应按固定职责注册并保留 token，不能每次采样重建来源。

来源票跨拓扑替换保留；拓扑/view 仍按原规则重新绑定。来源注册/票更新在聚合改变前完成校验，拒绝不会部分更新票或版本。现 TopologyPolicyBridge 的 legacy TopologyChanging 写入保持兼容；具体位由哪个控制器负责仍须在宿主明确约定，来源机制不识别真实 OS 通知，也不提供环境适配器。

## 聚合规则

本地未绑定、未Ready、竖向、被全屏覆盖或渲染器不可用只阻止该输出。一屏全屏而另一屏可画时，仍可保留共享活动资格；没有任何可画输出时CanRun=false。

Startup、Manual、SessionUnavailable、PowerSuspended、EnvironmentUnknown、TopologyChanging与SceneFault是独立全局阻塞，有任一个就禁止所有输出绘制与共享活动资格。清除局部位不清全局，清除一个全局位不清其他位。CanRun不是已经实现的游戏物理/奖励时钟；观赏/菜单等玩法门槛仍须在实际集成中保留，不能因有一个可见输出就强制推进游戏。

`OutputGlobalBlock`是本模型的独立枚举，不能按整数强转现有`RunGate.PauseReason`；集成适配器须按语义映射并测试，Fullscreen只送入对应输出观察。

目标切换/清除、拓扑替换、目标view替换/退役、新阻塞和目标资格变化会使inputEpoch失效。恢复资格也更换epoch，使不可用期间排队的请求不能自动生效；没有调用发lease的方法。其他输出的局部变化不撤销一个仍健康的当前目标。

几何/landscape是host提供的声明/当前观察，模型不从真实OS验证两者。镜像布局、重叠输出、物理DPI、全屏归属与拓扑枚举仍在后续实机/控制器工作中明确。

## 验证与下一步

```powershell
./diagnostics/multi-output/test-policy.ps1 -OutputDirectory <已有仓库外目录>
```

2026-10-27：50项纯检查通过，其中原30项回归覆盖单屏全屏与其他健康输出、全部不可用、全局位部分清除与原子替换、目标切换/移除/恢复、退役空档与代数高水位、旧拓扑/view回调、重复观察、非法/重复/中断配置原子拒绝、快照不可变、跨线程拒绝和激活查询无授权副作用。

新增20项覆盖：同位来源互不清除、来源与 legacy 隐藏增/删票、环境/通知/session/power/Manual 交错恢复、整票原子替换、不变聚合不 churn、未知/foreign token、固定 mask、容量/边界、错误线程、旧 epoch 拒绝、全部七位的来源隔离、跨拓扑保留与来源清空后不自动注销。新来源路径这里只验证纯模型，未接入 EnvironmentBridge、通知接收器或产品 owner。

主代理另复跑原TopologyBridge 17项、隐藏三页15项兼容检查，正常退出0、无强制清理、专用残留0；页面仍使用原legacy调用及合成局部观察，不能当作新来源的原生/全屏集成测试。

19:07补充：隐藏三WebView适配15项通过，六次文档/五个跨窗口view代数，最终2/10、正常退出、零残留。源/文档/当前票据与实际页面policy确认已验证；全局位逐项语义映射、epoch变化撤销控制、目标新授权和局部/全局阻塞隔离经过真实页面消息。拓扑generation固定1、遮挡观察合成；拓扑替换仍只有纯检查。

没有接入每输出SDK挂起/watchdog、真实屏幕矩形或产品输入握手，也没有模拟活动时钟。现有DualWindowHost仍是PrimaryScreen与单只读输出。后续需将真实拓扑和可信观测送入控制器，再独立应用每输出渲染与共享资格；不能只对多个输出反复设置/清除同一个全局Fullscreen位。
