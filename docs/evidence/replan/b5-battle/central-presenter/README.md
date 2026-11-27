# B5e 集中式角色视图更新

- 可见英雄/敌人 presenter 不再各自执行 LateUpdate；`SanguoBattleDriver.LateUpdate` 按稳定列表统一写入 Transform 和动画状态，presenter 保留独立回退模式供非战斗场景使用。
- C# 测试：`dotnet test SanGuoXEngine.Tests.csproj -c Debug --no-restore`，46/46 通过；D3D12 编辑器截图已留档。
- 运行统计：`renderablesCollected=2712`、`drawCalls=30`、`gpuFrameTimeMs=28.03`、`cpuFrameTimeP95Ms=51.93`。该节点改善回调结构，单次编辑器 run 仍受宿主峰值影响，60 FPS gate 未宣称通过。
