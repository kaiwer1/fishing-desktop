# P0：两个逻辑输出与一个控制页

2026-10-26 19:07：OutputPolicy适配15项实际通过，三并发WebView/六次文档、host view代数5、最终2/10，正常退出、零残留。初轮页面确认端误认嵌套列表容器导致超时Exit4，正常清理；改用IList接收真实ArrayList后复验通过，不修改策略判定来迁就结果。真实拓扑替换、时钟、SDK绘制/挂起和物理全屏仍未验收。

2026-10-26 18:27实测：10项通过，三并发WebView、六次文档加载、最终2/10；正常退出/清理、无强制终止、专用残留0。[Owner接口与范围](../dual-window/MULTI_OUTPUT_OWNER.md)。

后续接入[OutputPolicy](OUTPUT_POLICY.md)的适配版本已编译，新增15项定义等待单独实测记录；不将上方10项历史结果当作新集成已通过。

本目录是独立隐藏WebView2协议探针，复用同一DualWindowOwner、ViewAuthority和持久计数器。它不会挂接桌面、改变显示器/DPI或读取游戏进度，不加载三维场景或暂停中的游戏UI。

```powershell
./diagnostics/multi-output/build.ps1 -SdkDirectory <SDK目录> -OutputDirectory <已有仓库外目录>
./diagnostics/multi-output/test-webviews.ps1 -SdkDirectory <SDK目录> -OutputDirectory <已有仓库外目录>
```

构建创建唯一私有目录，复制SDK程序集/许可和专用极简页面。测试启动两个只读逻辑输出left/right和一个控制页，全部透明、NoActivate、不显示任务栏。只通过C#明确测试授权与真实WebView消息提交计数，不注入鼠标或键盘；这不是新的人机输入验收。

已执行检查包括：两个输出同owner快照、只读拿到控制lease也被拒绝、提交+7广播、单输出重开及另一输出重载旧文档拒绝、回执去重、全局暂停/恢复不自动授权、最终2/10广播，以及全部视图关闭时owner仍持锁。源/当前pane/文档/导航身份均验证，权限/下载/弹窗拒绝，浏览器故障保守退出，测试有55秒内部和60秒外部期限。正常清理失败或强制终止不能记通过；未知profile残留只报告，不按名称杀其他进程。

真实执行结果由开发日志记录，本文件不把编译当成运行通过。它证明的范围最多是一个进程内多个独立页面共享owner与生命周期协议。

## OutputPolicy适配与新增检查

控制器声明left/right两块虚拟矩形及DPI，不改变窗体的实际输出或系统DPI。每次只读NavigationStarting分配host跨窗口单调view代数并BindView，Ready后Observe，关闭时RetireView；窗体自己的导航计数不当作跨重开的policy token。旧异步导航完成只在当前pane/导航匹配时处理，旧policy回调不能复活退役页或关闭替换页。

适配器按语义逐项映射七个OutputGlobalBlock到RunGate暂停位，不将两个不同枚举强转。无任何本地可画输出时增加RendererUnavailable；本地恢复只能清该聚合位，不能清Manual。InputEpoch变化独立撤销控制lease，因此目标重载/切换在另一个健康输出仍保留owner运行资格时也必须重新授权。

GrantForTest明确核对当前控制页、目标输出、拓扑/view/inputEpoch及CanActivate后才调用owner.Grant。这是native协议测试入口，没有实现前台/页面取消epoch/完整输入握手，不是产品启用交互按钮。页面收到当前policy快照并返回状态，检查实际WebView协议而非只读C#模型快照。

新增15项包括原只读/广播/去重/日志锁合同，以及：非target输出重开保留健康target的原控制；target重载与目标切换拒绝旧授权/旧token；七个全局位逐项映射；单输出全屏观察时另一输出仍可运行但不可给被遮挡target授权；双输出全屏观察阻止owner；恢复一屏不清全局Manual；Manual及target恢复均不自动授权；最终2/10与全部视图关闭后的状态保留。

全屏、可画、DPI及全局状态都是测试声明，未调用OS全屏观察。合成SceneFault位的设置/清除只验证枚举映射，不代表生产故障退出后自动恢复。没有按输出执行SDK挂起、绘制或watchdog，policy的CanDraw只表示资格。

现有DualWindowHost仍用单只读页、PrimaryScreen和bool窗口表；产品的输出注册/选择、逐输出暂停/渲染监测、桌面位置、混合DPI与物理多屏尚未接通。逻辑left/right不是显示器仿真，也不是两台真实显示器验收。专用页面没有完整输入握手/实际鼠标交互、三维绘制或正式游戏存档。
