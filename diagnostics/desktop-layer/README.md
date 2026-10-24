# 桌面层挂接诊断组件（P0，未验收宿主）

本目录提供可复用的 C# 组件和隔离测试，不提供自动运行的桌面壁纸。当前没有运行真实 WorkerW 挂接，也没有证明桌面图标、右键菜单、鼠标穿透、多显示器或 Explorer 重启恢复可用。它不改变暂停中的 UI / 游戏流程。

## 已实现边界

- `DesktopLayerController.Discover(rect)` 只接受当前 Shell 所属、路径为 Windows 目录下 `explorer.exe` 的稳定进程身份（PID、启动时间、路径），及其已经存在的传统顶层结构：唯一直接 `SHELLDLL_DefView` 宿主之后的第一个顶层 `WorkerW`。目标矩形必须在虚拟桌面、图标层和 WorkerW 内。
- 新版抬升桌面层级、多个图标宿主、尺寸不符、DPI 上下文不兼容、其他程序占用的 WorkerW、找不到已有 WorkerW，都返回明确 `unavailable` 原因。首版只接受传统结构，不自动修复或创建桌面层。自有 UI 窗口以下的 WebView 子进程窗口不算第三方占用。
- `Attach(candidate, hwnd)` 仅接受本进程、创建控制器的 UI 线程所属、初始隐藏且无真实父窗口的自有窗口。显式设置 `WS_CHILD`、清除 `WS_POPUP`、增加 `WS_EX_NOACTIVATE`，重复核对 Shell / 父层身份，以不激活、不改变 Z 序的标志定位。成功后仍保持隐藏。
- 失败先隐藏自己的窗口；确认隐藏后才撤回父关系、样式和尺寸。若隐藏或恢复失败，尝试销毁自己的窗口。`Reason` 保留原始失败，`Issues` 保留步骤和原生错误码，`Cleanup` 如实记录隐藏恢复、关闭或清理失败；不会把失败当成成功。
- `ValidateAndRecover(lease)` 供宿主 UI 线程的定时检查、显示配置变化和 Shell 变化通知调用。发现父层消失、身份 / 几何 / 窗口状态改变时使租约失效，并隐藏回退或关闭自有窗口。它不创建新桌面层、不重启 Explorer，也不自己重建视图。`Validate()` 仅检查；宿主日常检查应使用 `ValidateAndRecover()`。
- `Detach(lease)` 先隐藏再恢复。组件没有显示或激活窗口 API，没有全局 DPI 修改、`0x052C` 消息、Explorer 图层创建 / 隐藏 / 销毁或外部窗口标题读取。

`Win32DesktopApi` 的每个窗口修改入口都会再次核对本进程和当前 UI 线程。跨进程查询只保留 Shell 的类名、身份、父关系及矩形；其他进程的后代仅用于占用判定，不记录标题或画面。`GetAncestor(GA_PARENT)` 返回系统桌面 HWND 时统一归一为 `0`，避免把真正顶层窗误认为子窗；真实子层关系保留。

## 接口与集成顺序

在既有 C# owner 的 UI 线程创建 `Win32DesktopApi` 和 `DesktopLayerController`。调用 `Discover`，准备隐藏、无边框的自有观察窗，再调用 `Attach`。保存返回的 lease；只有宿主在成功并再次验证后才能决定以无激活方式显示。宿主关闭或失效时调用 `Detach`，并处理 `Cleanup` 失败；组件不会接管状态 owner 生命周期。

不要将只读状态权限当成鼠标穿透保证。实际宿主仍需验证桌面图标单 / 双击、右键菜单、前后台切换、交互窗焦点、全屏暂停、显示器变化、Explorer 重启、单实例与退出清理。跨进程窗口检查与修改之间仍存在不可原子消除的系统竞态；重复身份检查和失效清理降低风险，不能证明零竞态。

## 编译与固定测试

输出目录必须已经存在，位于源码仓库之外，且其所有祖先不能是 junction / symlink 等 reparse point。脚本创建专属子目录，输出 `DesktopLayer.dll`、测试可执行文件及私有报告。

```powershell
./diagnostics/desktop-layer/test-desktop-layer.ps1 -OutputDirectory <仓库外目录>
```

默认仅执行 fake Win32 固定测试，不创建窗口、不实例化原生适配器、不读取真实桌面。覆盖已有层选择、路径 / 身份变化、尺寸 / 祖先错误、DPI 不兼容、NULL 返回且错误码 0、无激活标志、隐藏回退、关闭失败诊断和父层失效。

可选的实际适配器边界测试只创建本测试进程自己的隐藏 WinForms 窗口，核对父关系归一、隐藏定位、销毁与前台窗口未改变；不调用 `CaptureDesktop` 或 `SetParent`，不查询 / 修改 Explorer。

```powershell
./diagnostics/desktop-layer/test-desktop-layer.ps1 -OutputDirectory <仓库外目录> -NativeHiddenWindowBoundary
```

控制器按 HWND 保存当前 lease 引用；旧 lease 的迟到撤销 / 恢复不能清理同 HWND 上的新挂接。清理失败的当前 lease 保留，允许再次尝试清理。

2026-10-25 北京时间本机验证：固定测试 34 项通过；可选自有隐藏窗口边界测试 1 项通过。原生边界记录 raw parent 是否为系统桌面、归一父为 0、目标矩形、隐藏状态、定位 / 销毁结果和前台未改变，不输出第三方窗口信息。DLL 与测试程序使用本机 .NET Framework x64 C# 编译器编译。真实桌面挂接仍未执行。

## 官方依据

- [SetParent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)：不会自动调整 `WS_CHILD` / `WS_POPUP`；返回值是原父窗口，NULL 需结合错误码判断；不同 DPI awareness 的跨进程挂接有重置或异常风险，因此本组件在上下文不兼容时拒绝，且从不修改全局 DPI。
- [GetAncestor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getancestor)：`GA_PARENT` 只查真实父关系，不混入 owner 关系。

WorkerW 层级属于本项目保守探测规则，不是 Windows 官方承诺的壁纸扩展接口。以上 API 文档不能证明桌面图标输入已通过。
