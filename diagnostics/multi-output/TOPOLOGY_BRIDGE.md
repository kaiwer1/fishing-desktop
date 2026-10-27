# P0：只读拓扑到 OutputPolicy 的桥接

`TopologyPolicyBridge` 是 host 线程内的纯适配器：接收 [TopologySample](../output-topology/README.md)，向 [OutputPolicy](OUTPUT_POLICY.md) 声明输出。它不调用原生枚举、创建窗口、发放 lease、推进时钟或操作 SDK。

构造要求未配置的 policy，立即设置 `TopologyChanging`；`Startup` 等其他全局位由各自子系统负责，桥接只能设置/清除自己的拓扑阻塞位。桥接独占拓扑写入，不能与其他 writer 共同使用。

## 应用约定

`Apply(sample)` 的返回值为 `TopologyApplyResult`；`LastCaptureSerial`、`TopologyGeneration`、`HasVerifiedTopology` 分别表示观测水位、已提交拓扑代数及当前验证资格。

| 情况 | 结果与影响 |
| --- | --- |
| 首次 Known，或已验证内容发生变化 | `Replaced`；代数加一，整体替换并解绑全部 view，再清除拓扑阻塞。 |
| 连续 Known 内容相同 | `Unchanged`；只提高采样水位，不改代数、view、Ready 或 InputEpoch。枚举顺序不构成变化。 |
| 新的 Unknown | `Unavailable`；保存旧配置但全局阻塞，撤销已验证资格，旧 Ready 不可运行。重复 Unknown 不反复增加代数或 epoch。 |
| Unknown/拒绝后恢复 Known | 即便尺寸完全相同也 `Replaced`；必须新绑定 view、提交新观察，旧代数回调与旧输入作用域均失效。 |
| 候选超过 policy 容量 | `Rejected`；不部分写入，不增加代数，保留旧配置并阻塞；后续合法恢复仍需整体新绑定。 |
| 迟到/相同采样序号 | `Stale`；不改变当前观测水位或资格；不会用旧 Known 清除较新的 Unknown。 |
| 发现其他拓扑 writer | 锁定 `Diverged` 并阻塞，不能自动接管外部代数；先查漂移再查 stale，因此旧样本也不能绕过漂移检查。 |

内容比较包括逻辑 Key、位置/尺寸、两轴有效 DPI 和主屏标记。`CaptureSerial` 只用于拒绝迟到观察，不能直接当作拓扑代数。移除当前控制目标时不自动跳转到另一屏。所有入口限制同一 owner thread；恢复不会清除 Manual、SessionUnavailable、SceneFault 等其他阻塞，也不自动启用产品交互。

## 已有证据与范围

2026-10-26：桥接 17 项纯测试通过，包括稳定采样不 churn、Unknown/恢复重新绑定、容量拒绝保持原子性，以及外部 writer 遇 stale 仍被检测。测试使用声明的多块矩形/DPI，不读取物理显示器。

[只读采集模块](../output-topology/README.md)另有 44 项合成查询纯测试。20:40（北京时间）原生探针在当前设备复验：两次 Known，单块 `output-1` 为 `(0,0) 1920×1080`、有效 DPI 96；临时 HWND 创建/释放 4/4、剩余 0，正常退出且无强制终止。这是采集器的独立证据，不能当作桥接与页面联合通过。

后续[实际拓扑与隐藏页面联合](CAPTURED_TOPOLOGY.md)12项已通过：当前单屏恢复换代、旧view/授权拒绝和计数去重取得证据。现有 Host 的输出选择、逐输出绘制/挂起、时钟与 SDK 接入仍未完成；几何排序别名不是持久硬件 ID，不能据此宣称物理多屏、混合 DPI、热插拔或桌面挂接通过。

```powershell
./diagnostics/multi-output/test-topology-bridge.ps1 -OutputDirectory <已有仓库外目录>
```

构建产物与完整记录保持在验证过的仓库外唯一目录，公开文档只保留测试结论、逻辑别名与尺寸/DPI，不发布私有绝对路径或原始记录。
