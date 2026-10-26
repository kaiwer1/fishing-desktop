# 原生环境查询的确定性检查

2026-10-26 15:10，北京时间。P0代码边界验证，不改变系统设置、不启动全屏窗口。

NativeEnvironment.Capture原先直接查询WTS会话、输入桌面、前台/Shell窗口、PID和矩形。现在抽出INativeEnvironmentQueries原始查询接口；默认构造器仍用同文件Win32适配器，Capture签名、查询顺序、未知判定、线程约束及异常传播保持兼容。WTS缓冲区与输入桌面句柄仍在适配器finally释放。

测试替身只供应原始布尔值、窗口标识、PID和矩形，所有判断使用真实Capture，没有在替身复制全屏算法。

```powershell
./diagnostics/host-policy/test-native-queries.ps1 -OutputDirectory <已有仓库外目录>
```

25项纯检查通过：会话/输入桌面不可用，空/销毁/隐藏/最小化前台，前后PID失败或变化、HWND中途变化，缺Shell/其PID，几何查询失败/非法/溢出，自身/Shell忽略仍需身份稳定，负坐标覆盖，查询异常，每次重新采样，非法参数与跨线程拒绝。未知样本的暂停原因也检查，不复用上次可运行结论。

原NativeEnvironmentTests消费者编译兼容通过，未运行其约50秒前台fixture。正常三维30项和短循环31项另有本轮结果；不能代替真实全屏、锁屏、休眠、多屏、热插拔或Explorer重启。适配器释放是代码结构保留，不称本轮实机制造了全部原生失败。
