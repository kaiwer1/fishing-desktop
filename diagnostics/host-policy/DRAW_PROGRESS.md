# 循环中的绘制进度与文档一致性

2026-10-26 13:09，北京时间。只看owner允许运行和SDK未挂起，不能证明画面仍在更新。独立循环新增实际绘制进度门槛，不改游戏或原暂停策略。

同一当前文档、同一策略版本的相邻有效活动样本，必须保持draw loop运行且帧计数增加；停止、停滞或回退使诊断失败。策略变化、暂停、在途操作或文档切换先清空比较，不把旧文档帧数与新文档起始0混比；最终至少取得3个有效进度区间，才接受循环覆盖。

异步读取前后核对当前文档和导航代数。记录 `viewConsistent`、`viewNavigationGeneration`、`drawLoopRunning` 与有效进度检查数；采样跨导航、停止循环或在途状态不参与资源阶段对比。资源区间和绘制回调速率同样排除导航边界。旧格式仍可单独整理，不允许同轮混用有/无导航代数的格式。离线另报告 `activeDrawStalls`，不会只用平均回调速率掩盖停滞。

```powershell
./diagnostics/host-policy/test-draw-progress.ps1 -OutputDirectory <已有仓库外目录>
node --test diagnostics/dual-window/summarize-soak.test.mjs
./diagnostics/dual-window/test-webviews.ps1 -SdkDirectory <SDK目录> -OutputDirectory <已有仓库外目录> -ThreeScene -SoakSeconds 210
```

8项守卫检查通过，覆盖实际停止/计数停滞/回退拒绝、暂停/在途/换文档/换版本的比较重置；这些为纯逻辑检查，不声称对实际GPU注入了故障。16项整理检查通过，含跨导航样本/区间、格式及停滞记录。

实际三维210.56秒循环通过31项，103次心跳、55次相邻有效绘制进度核对、活动停画0、暂停时钟异常0；一次重建/七次三维文档加载、六次挂接/撤回后无剩余lease或专用进程，无强制清理。该轮没有重复采资源，原生全屏仍明确跳过。

该门槛检测秒级停画和失效采样，不是逐帧延迟、物理显示呈现、最终写实预算或长期无泄漏证明。历史循环没有运行此新增守卫，仍保留各自版本边界。
