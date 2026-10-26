# P0：两个逻辑输出与一个控制页

2026-10-26 18:27实测：10项通过，三并发WebView、六次文档加载、最终2/10；正常退出/清理、无强制终止、专用残留0。[Owner接口与范围](../dual-window/MULTI_OUTPUT_OWNER.md)。

本目录是独立隐藏WebView2协议探针，复用同一DualWindowOwner、ViewAuthority和持久计数器。它不会挂接桌面、改变显示器/DPI或读取游戏进度，不加载三维场景或暂停中的游戏UI。

```powershell
./diagnostics/multi-output/build.ps1 -SdkDirectory <SDK目录> -OutputDirectory <已有仓库外目录>
./diagnostics/multi-output/test-webviews.ps1 -SdkDirectory <SDK目录> -OutputDirectory <已有仓库外目录>
```

构建创建唯一私有目录，复制SDK程序集/许可和专用极简页面。测试启动两个只读逻辑输出left/right和一个控制页，全部透明、NoActivate、不显示任务栏。只通过C#明确测试授权与真实WebView消息提交计数，不注入鼠标或键盘；这不是新的人机输入验收。

已执行检查包括：两个输出同owner快照、只读拿到控制lease也被拒绝、提交+7广播、单输出重开及另一输出重载旧文档拒绝、回执去重、全局暂停/恢复不自动授权、最终2/10广播，以及全部视图关闭时owner仍持锁。源/当前pane/文档/导航身份均验证，权限/下载/弹窗拒绝，浏览器故障保守退出，测试有55秒内部和60秒外部期限。正常清理失败或强制终止不能记通过；未知profile残留只报告，不按名称杀其他进程。

真实执行结果由开发日志记录，本文件不把编译当成运行通过。它证明的范围最多是一个进程内多个独立页面共享owner与生命周期协议。

现有DualWindowHost仍用单只读页、PrimaryScreen和bool窗口表；产品的输出注册/选择、逐输出暂停/渲染监测、桌面位置、混合DPI与物理多屏尚未接通。逻辑left/right不是显示器仿真，也不是两台真实显示器验收。专用页面没有完整输入握手/实际鼠标交互、三维绘制或正式游戏存档。
