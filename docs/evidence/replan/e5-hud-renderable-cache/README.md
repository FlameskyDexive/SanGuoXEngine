# E5 follow-up：HUD renderable proxy 缓存

日期：2026-11-28

`HudManager` 仍然每帧更新合并网格和属性，但按材质槽复用 `MeshRenderable`，不再在
`OnRenderCollect` 中反复构造相同的 renderable proxy。这样保留了角色移动、血条变化、
伤害数字和相机视锥剔除的画面语义，只削减收集期的短命对象。

## 编辑器 Play 验收

- D3D12，TaoYuanBattle，GameView 1280×720；截图和原始命令见本目录。
- 本次原始 stats：`componentCollectMs=4.4029`、`renderCollectMs=4.9761`、
  `gpuFrameTimeMs=2.5781`、`cpuFrameTimeP50Ms=16.5078`、`cpuFrameTimeP95Ms=22.5011`。
- `staticBatchGroups=3`、`staticBatchInstances=6` 与上一节点一致；场景和 HUD 仍可见。

该节点证明缓存没有破坏编辑器 Play 画面，但单次 P95 仍受编辑器抖动影响，E7 尚未
通过；需要后续重复稳态样本和更细的 HUD/动态组件剖析，不能把本次结果宣称为 60 FPS
验收。
