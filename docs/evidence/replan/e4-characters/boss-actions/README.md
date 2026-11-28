# E4 战斗动作状态映射

`SanguoActorPresenter` 现在把状态机的 `Idle/Run/Attack/Hurt/Dead` 分别映射到真实控制器的 `idle_loop/run_loop/NormalAttack_1/hurt/die`。因此黄巾兵和复用 `Hero_LvBu` prefab 的吕布都会在进入攻击或受击状态时驱动动作，战斗表中的 `n1/attack` 仍通过别名解析到 `NormalAttack_1`。

## 验收

- C#：`dotnet test SanGuoXEngine.Tests.csproj --no-restore --verbosity minimal`，51/51 通过。
- 编辑器：D3D12、GameView 1280×720、`TaoYuanBattle` Play，等待 20 秒。
- 截图：[editor-gameview.png](editor-gameview.png)
- 原始命令结果：[runtime-result.json](runtime-result.json)
- 原始帧统计：[runtime-stats-d3d12.json](runtime-stats-d3d12.json)

采样为 `drawCalls=67`、`instancedDrawCalls=3`、`renderablesDrawn=64`、`skinnedPaletteRows=58`、`gpuFrameTimeMs=3.434`、`cpuFrameTimeP95Ms=23.926`。截图中的吕布和黄巾兵均在真实蒙皮路径中可见；本节点验证的是动作状态映射和编辑器 Play 运行，E7 的 16.6 ms CPU P95 门禁、Boss 专属技能动作和 HUD 伤害数字仍未通过。
