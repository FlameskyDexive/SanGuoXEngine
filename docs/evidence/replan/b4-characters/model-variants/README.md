# B4c MonsterID→ModelID 视觉变体绑定

- `SanguoFieldActor.ModelID` 由 `SanguoMonster.ModelID` 在 `SanguoBattleStage.SpawnNextGroup` 固化，场景 presenter 继续携带该 ID。
- `SanguoBattleDriver` 按 ModelID 保存模型数据、缩放和伤害数字高度；可用的 xiaobingyuanjing 真实蒙皮网格按 ModelID 复用，并缓存每个模型的材质变体（黄军、精英法师、浅红、棕色、骑兵色）。相同 ModelID 共享材质对象，避免每个敌人生成独立材质破坏 GPU skinning/instancing。
- 当前源包没有 YellowArmy、EliteMageArmy、HorseShoulder 的独立 FBX/Prefab，ModelData.Path 缺失资源因此保留 xiaobingyuanjing 几何回退；独立原始模型接入仍是后续 B4 节点。
- C# 测试：`dotnet test SanGuoXEngine.Tests.csproj -c Debug --no-restore`，46/46 通过；Python：`py -3 -m pytest -q`，108/108 通过。
- D3D12 编辑器截图：`battle-model-variants-d3d12-1280x720.png`。
- 运行统计：`drawCalls=30`、`instancedDrawCalls=1`、`renderablesCollected=2712`、`gpuFrameTimeMs=35.70`、`cpuFrameTimeP95Ms=56.97`。B7 60 FPS gate 仍未通过。
