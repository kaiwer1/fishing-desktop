# RF4 本机文件只读调查

日期：2026-10-19。目标：评估能否通过 REA 理解本机客户端中的钓鱼实现，尤其是鱼群机制。以下是实际文件检查结果，不是已还原的游戏源码或鱼群算法。

## 已执行

- 从官方 npm 包安装 REA `6.7.0`，仅放入工作区工具目录，通过 CLI 调用；没有修改 Codex 全局配置。
- 使用 `inspect-managed-artifact` 实际检查本机 `GameAssembly.dll`，保留本地 Evidence。
- 独立读取 PE 头及 IL2CPP 标志，与 REA 分类互相核对。
- 在游戏数据目录查找标准命名的元数据文件；只读取鱼类、鱼饵和地图资源包的前 96 字节，识别文件格式。
- 对原生模块做受限的名称线索扫描。未读取游戏进程内存、未注入、未更改游戏文件，也未提取模型、贴图或音频。

## 观察到的事实

| 项目 | 结果 | 能说明什么 |
| --- | --- | --- |
| 原生模块 | `GameAssembly.dll`，90,041,160 字节 | 已识别实际分析目标 |
| 文件身份 | SHA-256 `8a6d9e6c1375d03cbd215881bb2f1e92be3b4a8a34a990eb7c9f4a7379344971` | 便于后续确认是否为同一版本 |
| REA 分类 | x86-64 PE、6 个节、无 CLI 数据目录、`not-managed` | 无法直接通过 .NET 元数据/CIL 分析路径读取原始方法 |
| IL2CPP 线索 | 存在 `il2cpp_init` 与 `il2cpp_get_corlib` 字符串；目录有 `il2cpp_data` | 结合 Unity 文件结构，符合 IL2CPP 原生编译部署 |
| 标准元数据路径 | 本次在游戏数据目录未找到文件名包含 `metadata` 的文件 | 常见元数据入口暂未找到；不能据此断言元数据不存在或已加密 |
| 资源文件 | `fishes`、`baits`、`maps` 均具有 `UnityFS` 文件头 | 存在对应资源包；文件名和包格式不能证明内含鱼群判定公式 |
| 名称线索扫描 | 受限的 ASCII 标识符扫描没有匹配到所选鱼群/鱼饵关键词 | 未定位目标入口；不是证明相关逻辑不存在 |

## REA 能做什么，以及本轮没做到什么

REA 的原生分析可以配合 Ghidra 检查汇编、伪代码、调用关系和字符串引用。它的独立 .NET 静态分析路径明确不解码 IL2CPP 元数据，因此仅安装 REA 不等于能直接恢复这款游戏的 C# 项目。

本机 REA 检查报告缺少 Ghidra 配置。本轮查询了官方 Ghidra 12.1.4 与 Eclipse Temurin JDK 21 的发布文件及校验值，下载传输缓慢，尚未取得完整且通过校验的工具包。已结束本次下载，部分文件标记为 `.partial`，未解压或运行。不能把这些工具记为已安装。

因此，本轮完成的是文件分类与分析可行性调查，尚未进行 Ghidra 反汇编、方法还原或鱼群函数定位。没有依据判断鱼群生成、迁移或咬钩概率具体由客户端还是服务器执行。若某项逻辑只在服务端执行，本机安装包本身不足以还原它。

## 后续调查范围

1. 完成 Ghidra/JDK 获取与校验，验证 REA 的 Windows 只读分析环境。
2. 定位与鱼种、落点、咬钩和客户端/服务器职责有关的静态入口，按具体函数证据判断能确认什么。
3. 将可确认的行为与已有公开资料互相核对，未确认的公式和参数继续标记为未知。
4. 项目公开仓库仅保留独立整理的结论与我们自己编写的实现。原游戏文件、原始分析输出和游戏素材留在本地研究区。

## 工具依据

- [REA 官方仓库](https://github.com/morluto/rea)
- [REA 托管代码分析边界](https://github.com/morluto/rea/blob/main/docs/managed-code-analysis.md)
- [REA CLI 与证据格式](https://github.com/morluto/rea/blob/main/docs/cli.md)
- [REA Windows Ghidra 支持](https://github.com/morluto/rea/blob/main/docs/windows-ghidra-p0.md)
- [Ghidra 官方发布](https://github.com/NationalSecurityAgency/ghidra/releases/tag/Ghidra_12.1.4_build)
- [Eclipse Temurin 官方发布](https://github.com/adoptium/temurin21-binaries/releases)
