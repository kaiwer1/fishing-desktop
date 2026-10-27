# P0：唯一环境来源与原子覆盖批次

2026-10-27。这里实现纯模型，不读取 OS、不加载浏览器、不发输入 lease。专用 60 项纯测试、原 OutputPolicy 50 项和 TopologyPolicyBridge 17 项回归均通过。主代理另跑原捕获拓扑/隐藏页12项兼容检查，正常退出0、无强制清理、专用残留0；旧探针没有注册新环境来源，不作为新来源的原生集成验收。声明的双屏覆盖和布尔值不构成物理几何、全屏或会话通知验收。

## 环境来源与全屏两侧

`RegisterEnvironmentSource()`返回唯一不透明的 `OutputEnvironmentSource`，原子加入 EnvironmentUnknown。每个 policy 最多一个环境来源，不占通用来源池；没有注销/Dispose。其类型与 `OutputGlobalSource`不同，不能借通用 ReplaceGlobalForSource 清掉保留环境票，legacy 清位也不能清掉它。不注册此来源时保持原有 view-only 行为。

每输出分别保存 `ViewFullscreen` 和 `EnvironmentFullscreen`，已有 `Fullscreen`是二者 OR。`Observe(...fullscreen...)`只修改 view 侧；环境批次只修改 environment 侧。任何一侧 false 都不能清掉另一侧 true。BindView/RetireView 保留当前环境覆盖，仍按旧规则重置/退役 view 的 Ready 等状态；替换页面不能使被环境覆盖的输出重新开放。

全局聚合为 `legacy票 OR 普通来源票 OR 保留环境票`。本批次不能清掉其他来源的同位 EnvironmentUnknown/SessionUnavailable、原生 power 票或 Manual。Snapshot 增加 `EnvironmentRegistered`、`EnvironmentKnown`和 `EnvironmentCaptureSerial`；EnvironmentKnown 只描述这个保留来源，不保证其他来源或输出可用。

## 批次 API

```csharp
EnvironmentBatchResult ApplyEnvironmentBatch(
    OutputEnvironmentSource source, long expectedTopology, long captureSerial,
    bool known, IEnumerable<OutputEnvironmentCoverage> completeCoverage,
    OutputGlobalBlock envBlocks = OutputGlobalBlock.None);
```

`OutputEnvironmentCoverage(key, fullscreen)`仅是 host 声明。Known 要求当前非空拓扑的完整 key 集，包含未绑定输出；按 OutputKeys 规范化后检查唯一性、缺失和多余项，顺序不重要。输入不含页面、文档、lease、HWND、PID 或物理坐标。

envBlocks 使用 **OutputGlobalBlock**，只允许 EnvironmentUnknown/SessionUnavailable 两位。Known 必须传 None；Unknown 自动加入 EnvironmentUnknown，可附 SessionUnavailable。后续采集桥接必须按语义映射独立的采集枚举，不能强转整数。

| 返回值 | 行为 |
| --- | --- |
| WrongTopology | expectedTopology 非正或不等于当前代数；不枚举、不改票或当前 serial，未来代数也拒绝。 |
| Stale | 当前代 captureSerial 不高于水位，包括 0/负数；不枚举、不更改资格。 |
| Applied | 合法 Known 一次构造全部环境覆盖与自身票，再一次 Commit；没有逐屏开放的中间提交。 |
| Unchanged | 合法新 Known 未改变局部环境覆盖或有效全局位；仍保存自身票与新 serial，不增加 revision/epoch。 |
| Unavailable | 新 Unknown 保留原局部覆盖，更新自身 Unknown/Session 票和 serial；相同资格不反复换版。 |
| Rejected | 当前代新批次的 Known 不完整/畸形、枚举失败或 envBlocks 矛盾/越界；保留覆盖及原有自身 Session 位，加入 Unknown，消费该新 serial，禁止旧正样本恢复。 |

null、未注册/foreign token 和错误线程属于调用错误，在更改状态前抛出异常。Unknown 不枚举无效覆盖载荷。Known 枚举最多读取当前输出数加一项，也受配置容量限制；null、重复、非法/未知 key、缺项、超项、MoveNext/Dispose 异常均整批拒绝。该次数上限不能中断单次自行阻塞的 host MoveNext，宿主仍须提供无阻塞声明集合。

同一批次最多一次 revision 和一次 inputEpoch 变化。只有非目标输出覆盖变化时不撤销健康目标；目标新增阻塞或资格恢复沿用原 epoch 规则。新 serial 的重复内容只更新观测水位，不能视作自动恢复输入授权。

## 拓扑与事务边界

成功 ReplaceTopology 在同一次 Commit 中替换全部输出并将已注册环境票设置为 Unknown，同时把该代 serial 水位置 0。新行不复用旧几何的环境覆盖；所有 view 未绑定。即使新 view 全部 Ready，仍须当前代完整 Known 后才能解除环境门槛；旧/未来代样本都不能污染当前水位。新代可从 serial 1 开始，即使上一代水位已到 Int64.MaxValue。

ReplaceTopology 枚举结束后复核代数；MoveNext/Dispose 重入提交相同或更高代数后，外层返回 false，不倒退拓扑或覆盖内层环境票/serial。环境批次也在枚举结束后再次检查 topology/serial，不能用旧外层 Known 覆盖重入的新 Unknown。这是特定观测的护栏，不是允许任意宿主嵌套调用后全部回滚的事务框架。

票、serial 和注册 token 只在可能溢出的 Commit 成功后保存。revision/inputEpoch 溢出不会部分写入覆盖、票或水位，也不会占用未成功注册的唯一环境来源。聚合不变时允许保存隐藏票和新 serial，不强行增加已饱和版本。溢出测试仅在专用纯测试内反射注入边界值，没有加入生产测试后门。

这个 API 仅核对声明的 key/代数/顺序。真正的采集入口仍须冻结 host 捕获的矩形、在采集后核对拓扑身份并映射环境结果；不能从本批次的 Known 推断物理几何已经核实。NativeSignalState 的锁存/恢复观察、SDK 绘制/挂起、OS 全屏、owner 门禁和页面握手均未由本阶段接入。

## 纯验证

```powershell
./diagnostics/multi-output/test-environment-policy.ps1 -OutputDirectory <已有仓库外目录>
./diagnostics/multi-output/test-policy.ps1 -OutputDirectory <已有仓库外目录>
./diagnostics/multi-output/test-topology-bridge.ps1 -OutputDirectory <已有仓库外目录>
```

60 项专用检查覆盖两屏一次交换、目标各一次/非目标不变、全屏两侧互不清除、Bind/Retire 与未绑定完整集、Unknown/迟到/同值 serial、拓扑到 Ready 空档、同位普通来源、非法数据/有限迭代/异常、MoveNext/显式 Dispose 重入、不可变快照及版本溢出。所有编译产物和完整结果写入通过 private-path 校验的仓库外唯一目录，不进入公开源码。
