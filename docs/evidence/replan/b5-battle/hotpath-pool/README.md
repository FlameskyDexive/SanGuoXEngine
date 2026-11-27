# B5d 战斗热路径对象池

- 怪物近战共享一个不可变 `SanguoMissile` 描述，消除每次攻击的临时对象；技能飞行物的命中 `HashSet<int>` 从显式池租用并在飞行物结束时清空归还。
- 修复敌人没有目标时 `MonsterBrains` 提前退出整个循环的问题，改为继续处理其余敌人。
- C# 测试：`dotnet test SanGuoXEngine.Tests.csproj -c Debug --no-restore`，46/46 通过；Python：`py -3 -m pytest -q`，108/108 通过。
- D3D12 编辑器截图：`battle-gameview-d3d12-1280x720.png`。
- 运行统计：`drawCalls=30`、`renderablesCollected=2712`、`gpuFrameTimeMs=32.26`、`cpuFrameTimeP95Ms=50.26`。60 FPS gate 仍未通过。
