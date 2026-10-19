# 工具与运行方式

## 当前工具链

| 工具 | 版本 | 用途 | 状态 |
| --- | --- | --- | --- |
| Node.js | 24.15.0 | 本地开发、构建及规则测试 | 使用现有安装 |
| npm | 11.12.1 | 获取官方注册表依赖 | 使用现有安装 |
| Three.js | 0.186.1 | 浏览器 WebGL 三维画面 | 已安装，版本锁定 |
| Vite | 8.3.4 | 开发预览与静态资源构建 | 已安装，版本锁定 |
| Git / GitHub CLI | 2.53.0 / 2.102.0 | 源码版本与公开仓库同步 | 使用现有安装，登录已修复 |
| Lively Wallpaper | 2.2.1.0 | 将网页作为 Windows 动态壁纸 | 已安装并启动宿主/WebView2；有桌面层与导航日志，截图超时，尚未通过完整验收 |
| REA | 6.7.0 | 文件分类与原生分析会话 | 已在仓库外的研究工具目录安装运行 |
| Ghidra / Eclipse Temurin JDK | 12.1.4 / 21.0.12.1+1 | 本机 DLL 定点反汇编与反编译 | 官方 ZIP 已完整校验并运行；全库自动分析未完成，定点结果与限制见模块调查 |
| UnityPy | 1.25.4 | Unity 序列化对象与脚本类型关联 | 独立研究目录安装，只读使用，不导出美术素材 |
| Il2CppDumper | 6.7.46 | IL2CPP 格式定义交叉核对 | 官方发行文件及源码已取得；未声称自动恢复客户端成功 |

依赖通过 npm 官方注册表取得，并记录在 `package-lock.json`。构建输出不依赖远程 CDN。当前场景没有外部图像、模型或音频资源。

## Lively 安装包来源

- 官方发布页：https://github.com/lively-community/lively/releases/tag/v2.2.1.0
- 官方文件名：`lively_setup_x86_full_v2210.exe`
- SHA-256：`98f4e96bb8e2c416384eeaf48016eadaea9dce8263b8d212052775ebcf2d7e34`
- 下载后已核对哈希相符。安装包与下载缓存保留在源码仓库之外。

## 本地与构建

```sh
npm ci
npm test
npm run dev
npm run build
```

开发预览默认仅绑定本机回环地址，不自动向局域网或公网开放。`dist/` 是可部署的网页资源，不是完整正式游戏。

Windows PowerShell 中运行 `./scripts/package-wallpaper.ps1` 可构建 `artifacts/fishing-desktop-0.1.0-lively.zip`。ZIP 是待宿主验证的候选包；不要把成功压缩或浏览器预览等同于实际壁纸兼容性验证。

## 为什么逆向后的游戏可以在网页运行

常见路线包括：

1. 分析行为与文件格式后，用 JavaScript 和 WebGL 重新实现。
2. 从已有源码、开源引擎或恢复的可编译代码出发，用 Emscripten 把 C/C++ 编译成 WebAssembly，再适配图形、音频、文件系统及输入。
3. 在浏览器内运行编译成 WebAssembly 的模拟器，由模拟器运行原平台程序。
4. 游戏在远端运行，网页只传送画面与输入，即云端串流。

仅凭“能在网页玩”无法判断它采用哪一种路线。反编译器通常输出伪代码，仍需要恢复数据结构、依赖与行为并做验证，不能直接保证得到可移植源码。本项目独立实现自己的逻辑与美术。

## 官方参考

- Three.js：https://threejs.org/docs/
- Vite：https://vite.dev/guide/
- Emscripten：https://emscripten.org/docs/introducing_emscripten/about_emscripten.html
- Lively 网页交互：https://github.com/lively-community/lively/wiki/Web-Guide-IV-:-Interaction
- Lively 暂停事件：https://github.com/lively-community/lively/wiki/Web-Guide-V-:-System-Data
