# E4 敌人真实 prefab 与单位缩放

本节点修复了“敌人 prefab 已实例化但始终不进镜头/巨大灰色平面遮挡场景”的真实问题：首波敌人与英雄相距约 54 m，而 `FindNearestHero` 原来只查询 40 m，所有敌人一直处于 `Idle`；同时可用的 xiaobingyuanjing FBX 子资产约 51 个世界单位高，直接应用 MonsterModel 的 1.1 缩放会生成巨大四边形。

## 代码变化

- `FindNearestHero` 索敌半径从 40 m 调整到 80 m，首波进入战斗并由 `SanguoActorPresenter` 更新位置。
- 对明显超大（本地最大边界 > 8 m）的真实敌人 MeshRenderer prefab 做单位归一化，目标高度约 2.2 m；正常尺寸未来模型保留 `ModelScale` 原值。
- Boss 继续使用 `Hero_LvBu` prefab，不经过敌人四边形归一化。

## 验证

- `dotnet test SanGuoXEngine.Tests.csproj --no-restore -v q`：50/50 通过。
- 编辑器脚本编译：`mcp_live.py --wait-compile 180` 成功。
- D3D12 编辑器 Play，GameView 1280×720，等待 20 秒后采样。
- 原始 stats：`renderablesDrawn=64`、`dynamicBatchGroups=1`、`dynamicBatchInstances=31`、`staticBatchGroups=2`、`staticBatchInstances=6`、`gpuFrameTimeMs=2.695`、`cpuFrameTimeP95Ms=20.234`。
- 敌人位置已从 702 附近推进到 780 附近，截图中可见 Boss、敌人和四英雄，且不再出现占满画面的灰色平面。

## 未完成

敌人 prefab 的最终贴图/材质、动画动作、Boss 专属动作、HUD 伤害数字和 Unity 同机位逐像素对照仍属于 E4 后续节点；E7 性能门禁仍未通过。
