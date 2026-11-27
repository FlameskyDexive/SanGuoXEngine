# B5c 怪物攻击数值接入

日期：2026-11-27

`SanguoBattleStage.AttackOf` 读取 `SanguoMonsterValues.asset` 的 `GroupID` 和 `Atk`。战斗驱动器的近战命中使用该查询，缺失或非正数值回退到 20。此前驱动器始终使用常量 20，导入的怪物数值没有进入运行时。

验证结果：

- `dotnet build SanGuoXEngine.Game.csproj -c Debug --no-restore` 通过
- `dotnet test SanGuoXEngine.Tests.csproj -c Debug --no-restore` 46/46 通过
- `pytest Tools -q` 108/108 通过
- D3D12 编辑器 Play，1280×720 GameView，战斗帧截图有效
- `drawCalls=27`、`instancedDrawCalls=1`、`triangles=100,786`

![Monster attack data battle frame](battle-d3d12-1280x720.png)
