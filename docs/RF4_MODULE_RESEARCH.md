# RF4 客户端模块调查

调查日期：2026-10-19，北京时间。对应主模块 SHA-256：`8a6d9e6c1375d03cbd215881bb2f1e92be3b4a8a34a990eb7c9f4a7379344971`。

目标已扩大为参考整套 RF4 钓鱼体验。按照用户最新要求，当前界面和钓鱼流程开发暂停，先做客户端静态分析，再编制开发计划。本文记录分析证据，不表示已经恢复 RF4 的完整源码或服务器。

## 工具与方法

- REA 6.7.0，通过本地 CLI 和独立 stdio MCP 会话使用；未改 Codex 全局配置。
- Ghidra 12.1.4 和 Eclipse Temurin JDK 21.0.12.1+1 已完整下载、通过 SHA-256 校验并解压运行。
- Ghidra 包：569,649,598 字节，SHA-256 `ddac49f903da9d5bac833e5cc79395098b9c33cfd3279be5f31bd00387d2d4db`。
- JDK 包：205,073,461 字节，SHA-256 `f9d6e191ab098c0d416e7d588a24420a8621cd2f4720dab2459b8b7b2d2d8b4e`。
- UnityPy 1.25.4：通过官方 PyPI 安装到独立研究工具目录，只读解析指定的 Unity 序列化文件。未导出贴图、模型、动画内容或音频。
- Il2CppDumper 6.7.46：从官方发行页取得 .NET 6 工具及同版本源码，用其格式定义交叉核对。由于客户端元数据头并非标准布局，本轮没有宣称其自动恢复成功；下面的表与地址映射来自独立的本地只读解析器。
- 原生 DLL 只作为静态输入；未执行 DLL、附加游戏进程、读取游戏内存、注入、修改游戏文件或请求游戏服务。

原始工具结果、脚本类型清单及组件记录留在本机研究目录，不进入公开仓库。公开内容为独立整理的结构结论。

## 可复查的结构事实

| 检查对象 | 观察结果 | 覆盖范围 |
| --- | --- | --- |
| `GameAssembly.dll` | 90,041,160 字节，x86-64 PE，6 个节，387 个导出名称，无 CLI 数据目录 | 不是可直接读取 CIL 方法的普通 .NET 程序集 |
| PE 代码区 | 独立 `il2cpp` 节约 49.8 MB；导出有 IL2CPP 运行时接口 | 原生编译结构；并非已经获得原始 C# 方法 |
| 脚本程序集索引 | 122 条名称记录 | 包含 Unity、第三方库及游戏程序集；名称存在不代表所有功能均在运行 |
| `globalgamemanagers.assets` | 6,059 条 MonoScript 记录；其中 4,206 条指向游戏程序集；3,221 条记录的命名空间以 `RF4.` 开头 | 记录数，不是独立方法数或已恢复函数数；可有同名类型记录 |
| 构建设置 | 29 条场景入口 | 含启动、登录、加载、住宅、教程及水域，不应称作 29 张可钓鱼地图 |
| 数据包目录 | 65 个文件均带 UnityFS 头 | 鱼、饵、浮漂、钩、线、前导线、沉子、饵笼、拟饵、竿、轮、支架等资源分类 |
| `resources.assets` | 46,045 个序列化对象；含 8,096 个 GameObject、12,452 个 MonoBehaviour | 只针对这个文件，不能当作整款游戏对象总数 |
| 组件引用 | 从上述文件的通用 MonoBehaviour 头解析出 12,447 条脚本关联，涉及 800 种脚本类；5 条未解析成功 | 自定义字段未解码；不代表取得组件方法或全部配置 |
| 文本资源 | 31 条 TextAsset，存在多语言、天气、场景和声音编排配置名称 | 只记录格式、键名和计数，未发布原游戏文本或数值表 |
| 解码后的元数据子集 | 已校验 19,025 条类型、131,529 条方法及 91,144 条字段记录 | 名称索引、UTF-8 终止符、Token 类别、表边界和方法归属逐条检查通过；尚未恢复全部表、完整签名和原始业务语义 |
| 主游戏程序集映射 | 首个元数据镜像 `Assembly-CSharp.dll`：7,802 条类型、57,287 条方法记录 | 方法数量与原生 CodeGenModule 数量相同，按 Token 行号取指针并校验映射范围 |
| 非空方法指针 | 54,084 条记录有地址，涉及 42,161 个不同地址；3,203 条记录为空 | 允许泛型/抽象/共享实现等情况；不能称作 54,084 个独立或已理解的函数 |

## 模块关联与能确认的边界

以下名称用于说明研究如何定位，不复制原实现。类型名只能证明静态记录存在；界面对象关联进一步证明资源引用了该类型，仍不等于运行时已经观测到相应分支。

| 模块 | 类型或对象关联示例 | 当前能确认什么 | 尚不能确认什么 |
| --- | --- | --- | --- |
| 输入 | `FishingInputLayer`、`RodThrowingInputLayer`、`FishingReelInputLayer` | 输入按操作场景拆分 | 按键时序、蓄力曲线、动作过渡的具体实现 |
| 浮漂与底钓 | `RigBobberClassic`、`RigBottomSimple`、`RigBottomPaternoster`、`RigBottomInline` | 存在不同钓组类型 | 兼容规则、结算公式和全部参数 |
| 鲤钓与其他钓组 | `RigBottomCarpChod`、`RigBottomCarpMethod`、`RigLureComplex`、`RigFly` | 类型分类不止一种钓法 | 各钓法完整流程及参数 |
| 遛鱼 | `FishManeuverController`、`FightPullManeuver`、`FightZigzagManeuver`、`FightExhaustedManeuver` | 有多类搏斗动作/控制器记录 | 状态转移概率、鱼力、体力、断线和脱钩公式 |
| 咬钩 | `BiteReachBaitManeuver`、`BitePullDownManeuver`、`BiteLevelDetector` | 存在接近鱼饵、下拉等不同表现类型 | 咬钩何时生成、客户端与服务端各自职责 |
| 鱼群 | `CellPopulationConverter`、`FishBiteMetaConverter` | 有种群及咬钩数据转换类型线索 | 种群生成、迁移算法、服务端数据结构和真实概率 |
| 竿架/多竿 | `RodPod`、`RodPodSlotInteractive`、`RodRestController` | 有竿架和槽位交互类型 | 同时计时、切竿、取放竿完整逻辑 |
| 渔轮制动 | `ReelBrake`、`ReelSpool`、`RUIReelFrictionIndicator` | 有制动、线杯与泄力界面类型 | 实际阻力、磨损和出线公式 |
| 背包 | `panel_belongings` → `RUIBelongingsExplorerPanel` | 序列化对象的脚本关联已读出 | 分类数据来源及所有筛选/装备方法 |
| 商店与鱼市 | `panel_shop` → `RUIShopExplorerPanel`；`panel_market` → `RUIMarketExplorerPanel` | 两套交易面板存在独立关联 | 原游戏动态价格和库存结算规则 |
| 技能 | `panel_skills_v2` → `RUISkillsExplorerPanelV2` | 技能面板组件关联已读出 | 原版经验曲线、加点和解锁条件 |
| 制作 | `panel_crafting` → `RUICraftingExplorerPanel`；`CraftScriptCatalog` | 有制作面板及配方目录类型 | 配方全集、品质和成功率计算 |
| 地图与探索 | `GamePlaces`、`PaperMap`、`MinimapInputLayer` | 水域、纸图、小地图各有类型记录 | 完整地形、碰撞和行走规则 |
| 船钓 | `BoatController`、`BoatEngineV2`、`TrollingPattern` | 船控、发动机和拖钓类型存在 | 船体物理、租赁规则及完整海钓系统 |
| 成长与社会功能 | 统计、成就、资格、赛事、队伍、交换、住宅面板名称 | 功能范围远超当前试玩 | 对应后端、多人同步和运营规则 |

## 当前缺口

标准命名的 IL2CPP 元数据入口仍未找到。另对安装目录 209 个文件各检查前 8 字节，没有找到从文件头开始的标准元数据魔数；也在整个主 DLL 中搜索了该魔数，没有命中。这不排除嵌入、编码、压缩、定制装载或其他位置，不能直接断言已加密。

主 DLL 最后一个 PE 节之后有 17,082,696 字节附加数据，安全证书目录位于文件末端附近，大小 14,776 字节。沿实际初始化调用，定位到一个读文件、寻找块标记、处理数据并返回缓冲区的函数；将其伪代码与指令参数交叉核对后，在本地文件副本上重建该变换，得到 17,067,640 字节数据。

该数据未通过标准魔数/版本检查，不能直接作为标准 `global-metadata.dat` 交给普通解析器。进一步识别到符合 v31 记录形状的类型、方法、字段和字符串区，逐条检查了上表所列边界、Token 和归属；随后通过原生模块名称、方法计数、Token 与可执行区范围交叉校验，完成主程序集的方法指针映射。没有对未知头字段强填值，也没有把“存在可读字符串”单独作为恢复成功的证据。

不少类型、字段和方法的名称已混淆。资源中的原始脚本名称与当前元数据名称也不能仅凭文本直接一一对应。当前确认的是上述子集和主程序集指针映射，不是整个 C# 工程、完整签名、全部程序集原生地址或所有行为。整个客户端与服务器的职责边界、数值结算、经济系统和多人协议都还没有被还原。

## 原生函数分析记录

REA 的默认全库自动分析在非返回函数修复启发式中耗时过长。记录线程和日志后，按官方配置跳过 `Non-Returning Functions - Discovered`、使用 4 GB JVM 堆上限重试；第二次进入跳转分析/反编译阶段，但也未完成全库扫描。两次均已停止，不能称作全库反编译完成。

随后通过 Ghidra 官方 headless 入口以 `-noanalysis -readOnly` 运行自编的定点调查脚本，取得实际伪代码与指令。先检查 `il2cpp_init`（`0x180337bd0`）、`il2cpp_get_corlib`（`0x180338140`），再沿静态调用定位初始化与数据装载路径。每个选点的反汇编限制在 8,192 字节、单次最多 8 个选点。修正了导入时 1 字节占位函数边界；也修正了将同一函数内分支误选为独立函数的问题，因此不以此前选点数量作为已恢复函数数。

部分伪代码包含类型传播或重叠全局符号警告，大初始化选点触及了范围上限。原始方法类型和边界未完整导入，间接调用可能缺失；这些伪代码只能作为局部静态证据，不能当作可直接编译或语义完全正确的源码。

在方法指针映射后，进一步对游戏程序集的渔轮 `Update`（`0x180a7b430`）、竿架 `Update`（`0x1809c15b0`）与普通底钓钓组 `Start`（`0x180a87bf0`）做业务选点检查。三个选点都返回了反编译结果，分别读取到 24、291、191 条选定函数体内的指令。渔轮结果包含未恢复跳转表和移除不可达块警告，其他结果仍有重叠全局符号与间接调用限制，不能只根据 `decompileCompleted` 就判断内容完整准确。

这些结果验证了从类型/方法记录走到实际业务地址的路径。对业务语义的解读还需导入类型、恢复字段偏移与调用目标，并逐项和游戏行为核对；本轮没有宣布渔轮物理、多竿机制、底钓规则或鱼群公式已经复原。

## 工具依据

- [REA 官方项目](https://github.com/morluto/rea)
- [REA Windows Ghidra 支持](https://github.com/morluto/rea/blob/main/docs/windows-ghidra-p0.md)
- [Ghidra 12.1.4 官方发布](https://github.com/NationalSecurityAgency/ghidra/releases/tag/Ghidra_12.1.4_build)
- [Eclipse Temurin 官方发布](https://github.com/adoptium/temurin21-binaries/releases)
- [UnityPy 官方说明](https://github.com/K0lb3/UnityPy)
- [Il2CppDumper 官方项目与格式定义](https://github.com/Perfare/Il2CppDumper/tree/v6.7.46)
