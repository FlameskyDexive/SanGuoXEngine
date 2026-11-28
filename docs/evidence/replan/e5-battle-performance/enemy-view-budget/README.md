# E5 编辑器 Play 敌人展示预算

桃园战斗内核仍完整模拟 234 个敌人，展示层预算从 32 个真实 prefab 调整为 20 个。当前 1280×720 机位实际可见约 12 个敌人，因此没有减少当前取景中的角色；超出预算的敌人继续保留逻辑状态，不创建蒙皮层级和 HUD。

## 验收

- `dotnet test SanGuoXEngine.Tests.csproj --no-restore --verbosity minimal`：51/51 通过。
- D3D12、GameView 1280×720、等待 20 秒后 Play 截图：[editor-gameview.png](editor-gameview.png)
- 原始结果：[runtime-result.json](runtime-result.json)
- 原始统计：[runtime-stats-d3d12.json](runtime-stats-d3d12.json)

本次 `drawCalls=58`、`instancedDrawCalls=3`、`renderablesDrawn=55`、`skinnedPaletteRows=46`、`renderCollectMs=4.056`、`componentCollectMs=3.321`、`skinningUpdateMs=2.187`、`gpuFrameTimeMs=3.294`、`cpuFrameTimeP95Ms=24.645`。相较 32 展示预算的同机位，收集和蒙皮工作下降；P95 仍受编辑器尖峰影响，E7 门禁继续保持未通过。
