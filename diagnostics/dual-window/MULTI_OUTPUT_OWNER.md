# 单一owner的多只读输出基础

2026-10-26 18:27，北京时间。P0数据与协议首包，不是完整物理多屏壁纸。

19:07补充：[逐输出策略](../multi-output/OUTPUT_POLICY.md)已在独立隐藏探针接入，15项实测通过，局部遮挡/全局暂停/目标epoch经真实消息确认，零残留；不是实际DualWindowHost改成多屏。

DualWindowOwner新增host-only OpenOutput(outputId)，每个只读ticket记录OutputKey。ID为1至64个ASCII字符，首字符字母/数字，后续允许字母/数字、连字符、下划线和点，统一小写；这是逻辑ID，不是文件路径或显示器硬件标识。默认default与原Open(false)互为别名，默认observer单例和唯一控制页保持兼容。页面消息不能创建输出或凭ID取得控制权。

不同输出共用同一owner、日志写锁、确认序号和去重回执，只读页始终不可授权。单输出关闭/重建只退役其ticket/文档；旧ticket、旧文档或另一个owner的票据不能删除/影响替换项。关闭所有视图仍不释放owner，只有明确宿主退出释放日志。

```powershell
./diagnostics/dual-window/test-multi-output-owner.ps1 -OutputDirectory <已有仓库外目录>
./diagnostics/dual-window/test-owner.ps1 -OutputDirectory <已有仓库外目录>
./diagnostics/multi-output/test-webviews.ps1 -SdkDirectory <SDK目录> -OutputDirectory <已有仓库外目录>
```

新增16项纯检查、原14项检查通过。独立隐藏真WebView探针10项通过：两个逻辑只读输出加一个控制页同时存在，六次文档加载；真实页面消息完成只读拒绝、+7广播、单页重开/重载旧文档拒绝、回执去重、全局暂停门拒绝旧命令、恢复明确新授权及最终序号2/值10广播。所有视图关闭时仍持日志锁，退出0、无强制清理、专用残留0。专用页面由C#明确测试授权，没有完整产品输入握手或真实鼠标验收，也没有模拟时钟/游戏保存。

旧单输出三维30项另通过，六次场景加载、运行时监测正常、退出零残留。新增输出API没有把原控制器改成多屏：DualWindowHost仍为bool窗口表，桌面挂接/环境采样/显示恢复仍取PrimaryScreen，看门狗仍为单只读绑定。

拓扑/视图版本、局部与全局资格聚合及目标epoch已形成纯模型并完成隐藏适配；下一步是真实拓扑/矩形/DPI、逐输出SDK/监测和桌面控制器。某屏全屏不能由另一屏清同一个全局Fullscreen位；多视图也不能各自推进一份游戏状态。底层桌面层已有目标矩形与多lease接口，但本轮未做多窗挂接。

逻辑left/right不是显示器仿真。真实位置、混合DPI、每屏全屏、热插拔、Shell重建与多屏资源仍待实机；这些未完成项不能仅改表格为通过。
