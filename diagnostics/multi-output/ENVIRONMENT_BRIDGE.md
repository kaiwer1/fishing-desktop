# P0：同步环境采集到输出策略的桥接

`EnvironmentCaptureBridge`是独立host侧桥接，持有[采集器](../output-environment/README.md)和[OutputPolicy](OUTPUT_POLICY.md)，只公开无参数的`Poll()`/`CaptureAndApply()`及固定诊断状态。本轮未接产品Host、浏览器、游戏、时钟或SDK，未读取/操作真实其他进程；桥接查询能力取决于注入的采集器。当前范围在必要纯测后冻结，物理多屏和罕见兼容不阻塞单屏可玩链路。

## 接口与资格边界

- 构造`EnvironmentCaptureBridge(OutputPolicy policy, OutputEnvironmentCapture capture)`在policy所属线程注册唯一`OutputEnvironmentSource`，初始持有`EnvironmentUnknown`。token保持私有，没有公开清票、递交sample或传targets接口；第二个桥接不能接管该来源。
- `Poll()`同步调用`CaptureAndApply()`。一次policy快照提供同一`TopologyGeneration`和全部输出的冻结矩形，包括尚未绑定view的输出；只有这些矩形可以传给真实`OutputEnvironmentCapture.Capture`。
- 每次合法调用先分配单调`LastPollSerial`，阻塞/失败尝试也不复用，拓扑换代不重置桥接serial；policy自己的每代采样水位仍由policy维护。线程错误/重入在分配和raw查询前拒绝，checked溢出是调用失败，不能回绕继续采集。
- 无拓扑/空输出返回`NoTopology`，`TopologyChanging`返回`TopologyBlocked`；均不执行raw查询，环境来源保持Unknown。正代数的负结果通过同一批量接口确认，代数0由注册时Unknown保障。
- Capture返回后重新读取policy快照，代数/Revision变化或出现`TopologyChanging`都丢弃本轮结果，返回`ScopeChanged`，仅向当前代提交Unknown；不能把旧矩形结果贴上新代数。现在没有独立屏障epoch，因此同代屏障升起又解除，乃至其他policy修订，本轮均保守Unknown并需下次重采。
- 正常同步raw查询没有await或消息泵；稳定poll不重建source/拓扑/view。同值覆盖只推进采样serial，不反复改变revision或输入epoch。
- Known的完整覆盖和该来源全局票通过一次`ApplyEnvironmentBatch`提交；不循环调用`Observe`，不再单独清一遍Unknown。环境枚举`1/2`显式映射到policy的`EnvironmentUnknown=16/SessionUnavailable=4`，不进行枚举强转。
- Unknown/采集异常不复用旧正资格，独立global source与legacy票继续保留。环境覆盖独立于页面Fullscreen，Bind/Ready/Retire不能清掉它；恢复只产生资格，不发lease或启用交互，旧输入scope仍失效。

返回结果为`NoTopology/TopologyBlocked/ScopeChanged/Applied/Unchanged/Unavailable/Rejected`。`LastCaptureTopology`是本轮开始快照代数，`LastReason`是固定原因枚举；不发布原始HWND、PID、标题或异常详情。串行化错误或policy提交异常不伪装成成功，调用方须按诊断故障停用而非自行清票。

Policy的矩形仍是声明数据；只有由已验证拓扑调用方创建并维护这些定义，联合该桥接，才能称为对应当轮拓扑的环境观察。本轮纯测没有接入[真实拓扑桥接](TOPOLOGY_BRIDGE.md)，不证明声明矩形就是物理屏幕。Win32采样本身的ABA、句柄复用、采集后变化等限制也仍存在，不是实际遮挡或独占全屏证明。

## 纯测与当前结果

```powershell
./diagnostics/multi-output/test-environment-bridge.ps1 -OutputDirectory <已有仓库外目录>
```

runner沿用私有路径校验，在仓库外创建唯一目录，编译x64/.NET Framework、warnings-as-errors并记录输出/summary；唯一入口为专用纯测exe。测试用`INativeEnvironmentQueries`合成回答调用真实采集类，不实例化Win32查询器、不创建窗口、不读取真实前台或其他进程。

2026-10-27：首轮33项必要纯检查全部通过，warnings-as-errors编译成功，专用exe退出0。覆盖单批多输出/冻结矩形、稳定采样及单次epoch变化、独立票语义映射与恢复、未绑定/Ready/Retire保留覆盖、无拓扑/空输出/容量与重叠拒绝、捕获中换代/同key改矩形/屏障脉冲/其他修订、线程与重入、查询异常、serial连续性与溢出、旧输入scope及无外部sample/targets/token入口。完整记录仅保留在仓库外唯一目录，合成查询通过不等于真实环境联合验收。

本轮不运行native、不接物理显示/全屏fixture、不做产品Host联合或游戏测试；不宣称物理多屏、混合DPI、热插拔、真实会话转换已验收。不扩展新采集或平台兼容工作，后续主线回到既定单屏可玩链路。
