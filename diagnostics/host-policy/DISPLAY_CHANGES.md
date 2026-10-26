# P0 显示变化回退

当前实现是保守诊断路径，尚不是面向玩家的自动适配体验，也没有改变本机分辨率、缩放或显示器布局。

独立通知窗口处理 `WM_DISPLAYCHANGE` / `WM_DPICHANGED`，只读桌面窗自身也观察 DPI 通知。交互窗在其他显示器间移动造成的单窗 DPI 变化，不直接视为整个壁纸目标改变。消息中的宽高或建议矩形不作为已验证的目标几何；隐藏通知窗口不读取 DPI 消息的矩形指针。

## 顺序

1. owner 立即增加显示代次并设置 `DisplayChanging`，撤销旧授权、停止活动时钟并请求渲染暂停。
2. 将旧只读视图的关闭排到 owner 的下一次消息处理，避免在原生回调内直接释放 WebView。闭包只引用旧视图和当时的显示代次；重复通知使旧闭包失效，不会关掉新窗口。
3. 释放该视图的渲染协调器、静帧和桌面 lease，保留 owner、交互窗、写锁与提交状态。没有自动重试循环；本诊断必须显式重开只读视图。
4. 新视图记录当前显示代次；页面就绪后，重新采样当前主屏、输入桌面/会话与前台覆盖。桌面页还须匹配当前视口尺寸，并通过父关系、Shell/WorkerW 身份、几何与 DPI 核对。
5. 只允许当前代次完成 `DisplayChanging`；旧页面、旧重建结果、缺少主屏或竖向目标不能清除它。其他手动/会话/电源原因继续保留，恢复活动不恢复旧 lease。

新视图核对途中发现桌面 lease 失效，则沿已有回退关闭该视图，保留 owner 和显示屏障；清理失败仍保守退出。系统几何查询和重建不是原子操作，不能证明消除了所有系统竞态。

原生采样将无主屏或竖向主屏计为阻止活动；目标恢复横向后仍须满足已有层与当前页面代次。这个检查没有提供竖屏游戏布局。

## 验证

```powershell
./diagnostics/host-policy/test-display.ps1 -OutputDirectory <已有仓库外目录>
./diagnostics/host-policy/test-notification-window.ps1 -OutputDirectory <已有仓库外目录>
./diagnostics/dual-window/test-webviews.ps1 -SdkDirectory <SDK目录> -OutputDirectory <已有仓库外目录> -WallpaperPolicy
# 单独验证显示/受控通知；明确不取得本轮原生全屏证据。
./diagnostics/dual-window/test-webviews.ps1 -SdkDirectory <SDK目录> -OutputDirectory <已有仓库外目录> -DisplayPolicy
```

前两者覆盖8项屏障/时钟/授权/横屏固定检查、5项实际隐藏 HWND 注册/解注册及受控消息解码。完整联合模式预计31项（含循环时32项），其中显示阶段直接调用 owner 回调：旧视图退休、重复代次、旧完成被拒绝、新视图核对、保留其他暂停、接收器与 owner 保持及去重。

这些控制不修改系统分辨率/缩放，也不制造显示器热插拔或真实 DPI 消息；相应报告为 `actualDisplayOrDpiChange=false`。实际多屏、DPI组合、热插拔、全屏切换分辨率、Explorer重建以及自动恢复体验仍需验证。原生全屏采样缺少有效前台样本时整轮不判通过；失败也保留阶段计数，不能以实现存在取代样本。

2026-10-26 09:12 北京时间，独立显示模式26项通过，五次挂接/撤回、11次自有呈现前台核对，正常清理且专用残留0；页面回调结束后再核对/释放的顺序回归通过。旧代次不清除新屏障，重开保留手动暂停，恢复需新授权；通知接收器和 owner 保持。随后不启动 Lively，直接复用仍存在的桌面层也通过26项；不能据此推断全新登录/Explorer重启后的层可用。

本轮两次完整31项模式均在原生全屏阶段样本不足而未通过，第二次为普通17、全屏0、恢复90，未运行后面的显示阶段。独立显示模式报告 `nativeForegroundTestSkipped=true`、`nativePolicy=null`，且 `nativeFullscreenCombined=false`。此前版本的全屏通过记录仍是历史证据，不覆盖本轮缺口。普通11项、隐藏桌面15项和第二实例失败收尾回归通过。

依据：[WM_DISPLAYCHANGE](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-displaychange)、[WM_DPICHANGED](https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged)。这些消息说明分辨率/DPI发生变化，不证明新的 WorkerW、窗口父关系和视口已经稳定。
