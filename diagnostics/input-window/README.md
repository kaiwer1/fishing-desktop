# 独立输入窗口候选：P0 实验

普通 WinForms + WebView2 窗口，直接接收自己前台窗口内的网页输入。当前只加载本目录诊断页，不运行游戏、不读取游戏存档，不嵌入桌面层，也不与 Lively 同时运行游戏模拟。它是输入路线试验，尚不是壁纸发行版。

## 构建与运行

环境：Windows x64、.NET Framework 编译器、WebView2 Runtime。SDK 固定为 `Microsoft.Web.WebView2 1.0.3405.78`，与本次 Lively 内使用版本一致。从 [NuGet 包端点](https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.3405.78/microsoft.web.webview2.1.0.3405.78.nupkg)下载并解压到仓库外。该次下载 SHA-256 为 `d035807b2aaba871e8c014759626f566e96934e6ce6f0587056ee81d5228c373`；未将 SDK 或编译程序加入 Git。

```powershell
./diagnostics/input-window/build.ps1 -SdkDirectory 'C:\private-tools\webview2-sdk' -OutputDirectory 'C:\existing-private-test-folder'
node --test diagnostics/input-window/input-session.test.mjs
```

输出目录须已存在且放在仓库外。脚本使用 SDK 的 net462/x64 文件，复制必要 DLL、网页和 SDK 许可，生成 `InputProbeHost.exe`。运行后，专用 WebView2 配置、`input-events.jsonl` 与 `input-page.png` 都写在输出目录，不接触用户浏览器资料。

- F8 或 Enable input：明确开启本窗口交互。
- F6 或 Observe / cancel：取消输入会话，进入观赏状态。
- F7 或 Save page image：通过 WebView2 自身接口保存**本诊断页**画面，不截取其他应用。
- 页面内 Esc、失焦、隐藏、宿主失焦或最小化：取消并清空按住状态。返回窗口不会自动启用，须再次主动开启。

这些快捷键只属于该窗口，不是全局热键。网页采用固定本地虚拟域名，只允许导航该来源，弹出窗口不打开。桥接只传模式、版本号和取消原因；事件记录只含计数、鼠标按钮编号和可信标志，不保存键盘内容。

## 2026-10-19 19:07 验证结果

已执行构建并启动。通过 Windows 工具发送 F8 后，页面交互开启；发送普通键后，页面计数从 0 到 1，记录 `trusted:true`。切换到另一个本项目测试窗口后，页面收到 `blur` 和 `host-deactivate`，交互关闭；回到窗口仍为关闭，再发送普通键计数保持 1。已用宿主自身截图接口保存并查看[诊断页画面](../../docs/validation/2026-10-19-input-window.png)。

四项独立状态测试通过：禁用输入忽略、三个按钮各自按住/只松开一次、失焦清理后必须新激活、迟到旧激活不能覆盖新取消。这里的鼠标状态测试是纯状态单测；**没有把它当成原生鼠标按住/松开实测**。

左右键/中键、滚轮、拖动与鼠标按住后切窗口尚未实测；桌面图标、系统右键菜单、全屏覆盖层和多屏尚未接入。唯一模拟与唯一存档写入者仍是接入游戏前的前置条件，目前没有游戏进度可被复制或结算。测试程序和专用 WebView 子进程已清理，不在后台运行。
