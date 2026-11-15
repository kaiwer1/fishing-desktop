# 从 Steam 安装文件对照岸钓体验

2026-11-15。本轮对象为 Windows Steam 版《俄罗斯钓鱼4》的安装目录。下表把读取到的事实和本项目的设计决定分开列出。

| 检查对象 | 本机读到的内容 | 对钓鱼桌面的用处 |
| --- | --- | --- |
| Craft Anything 0.6.1 的 Unity 文件检查 | `globalgamemanagers`、`level0`、`resources.assets` 都报告 Unity 2022.3.62f3、Windows 64 位。最后一个文件列出 46,045 个对象，包含 `panel_shop`、`panel_shop_details`、`panel_market` 及 `table_item_inventory`。 | 能交叉确认商店、详情、鱼市和物品列表在客户端有各自资源。 |
| `rf4_x64_Data/app.info` | 开发者为 Russian Fishing LLC，产品为 Russian Fishing 4。目录中包含 UnityPlayer、GameAssembly、IL2CPP 数据及 Unity 序列化资源。 | 可按 Unity 的对象、脚本关联和资源包分别核对。仅从这些文件名无法还原操作时序。 |
| `globalgamemanagers.assets`、`resources.assets` | 实际对象 `panel_belongings`、`panel_shop`、`panel_market` 分别关联背包、商店和鱼市的独立面板组件；另有技能面板与鱼、竿统计组件。 | 把原来共用一长页的营地改为独立分区。保留用户要求的菜单售鱼，直接从鱼护出售。 |
| `Data/fishes`、`rods`、`reels`、`bobbers`、`lures` | 五个文件起始标记均为 UnityFS，按鱼、竿、轮、漂、拟饵分包。鱼包在此安装中为 6,459,574,672 字节。 | 游戏中各类物件有独立资源。这个字节数不能用于推断可钓鱼种数、出现概率或动画质量。 |

核对面板关联时从同一安装只读打开两个 Unity 资源文件，读取对象名和脚本引用。钓鱼桌面的界面、渲染、输入判断和渔获结算使用项目自己的源码。运行包中的钓场缩略图来自钓鱼桌面自己的钓场。

复查使用官方便携版的 `core-target-open` 与 `game-unity-list`，在安装目录运行 `game-unity-code-names-show` 返回 `not_found`。此次读到的是客户端对象和脚本关联，尚未查明 RF4 的服务器鱼群概率或原版挥竿时序。[Craft Anything 0.6.1 官方发行](https://github.com/storytold/craft-anything/releases/tag/v0.6.1)
