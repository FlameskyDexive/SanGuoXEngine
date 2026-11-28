# E4 真实 Monster_YellowArmy prefab

本节点把战斗敌人从 `xiaobingyuanjing` 四边形代理切换为来源于 `F:\jws2\client\Assets\Actor\GPUMonster\Monster_YellowArmy` 的真实 FBX。

## 导入与绑定

- XEngine 导入 FBX 后得到 1 个 `SkinnedMeshRenderer`、17 根骨骼绑定和原生模型子资产。
- 生成原生 `Monster_YellowArmy_Yellow.prefab` 与 `Monster_YellowArmy_Body_Yellow.mat`，材质绑定源黄巾兵身体贴图。
- `SanguoBattleDriver.EnemyViewPrefabGuid` 更新为 `6f864fb6-05e4-58fd-92ba-a3e291b018d6`，旧场景四边形不再用于战斗。

## 编辑器 Play 验证

- D3D12、GameView 1280×720，等待 20 秒，首波敌人进入英雄镜头。
- 截图中可见真实黄巾兵群、吕布 Boss 和四英雄，没有巨大灰色四边形。
- `runtime_stats`：`drawCalls=67`、`instancedDrawCalls=3`、`renderablesDrawn=64`、`skinnedPaletteRows=58`、`gpuFrameTimeMs=3.617`、`cpuFrameTimeP95Ms=18.131`。
- 子模块测试仍为 50/50 通过；脚本编译通过。

## 剩余工作

当前 prefab 已完成几何、材质和骨骼绑定，但还没有绑定黄巾兵专用 Animator Controller/动作剪辑；E4 的 Idle/Run/Attack/Hurt/Die 动作验收、Boss 动作、HUD 伤害数字和 Unity 同机位对照继续保留为后续节点。
