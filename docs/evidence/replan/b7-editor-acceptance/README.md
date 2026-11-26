# B7 编辑器 Play 性能验收

日期：2026-11-27

## 本节点实现

- 战斗相机默认挂载 `UniversalAdditionalCameraData`。
- 编辑器 Play 默认关闭动态阴影提交，保留后处理和静态/烘焙场景数据，作为编辑器优先的性能预算。
- PC 包和手机包继续留到编辑器验收完成后处理。

## D3D12

![D3D12 battle](battle-d3d12-shadow-budget.png)

- `drawCalls=36`
- `instancedDrawCalls=1`
- `triangles=100,892`
- `shadowDrawCalls=0`
- `shadowTriangles=0`
- `frameTimeMs=51.63`
- `gpuFrameTimeMs=35.49`
- `cpuFrameTimeP95Ms=62.11`
- `backendName=Direct3D 12`

## Vulkan 回归

![Vulkan battle](battle-vulkan-shadow-budget.png)

- `drawCalls=1420`
- `shadowDrawCalls=2,788`
- `frameTimeMs=313.17`
- `gpuFrameTimeMs=349.06`
- `backendName=Vulkan (Intel(R) Arc(TM) 140T GPU (32GB))`

## Gate 结果

战斗 Play 现在只保留 Battle Camera，原始 Scene Camera 仍可在编辑模式使用。重复的整场景渲染已移除，这是目前测得的最大编辑器 Play 性能改善。D3D12 约 19 FPS、p95 62.11 ms，距离 60 FPS gate 仍有差距，B7 继续进行中。

## B7c 敌人视图预算复测

- 逻辑层仍完整运行 234 个敌人，编辑器 Play 只为最靠近战斗入口的 32 个敌人创建真实模型视图。
- 敌人 Prefab 在战斗启动时只解析一次，避免每个敌人重复建立资源引用。
- GameView 固定到 `HD (1280x720)`，D3D12 复测保持 1 台 Battle Camera。
- `drawCalls=36`
- `instancedDrawCalls=1`
- `triangles=100,754`
- `renderablesCollected=2,806`
- `renderablesCulled=2,775`
- `renderablesDrawn=31`
- `frameTimeMs=46.27`
- `gpuFrameTimeMs=38.70`
- `cpuFrameTimeP95Ms=54.50`
- `shadowDrawCalls=0`
- `backendName=Direct3D 12`

![B7c 1280x720 GameView](battle-d3d12-shadow-budget.png)

该子节点降低了敌人场景视图的对象和 Presenter 数量，未把逻辑敌人数量裁剪掉。60 FPS gate 仍未通过，B7 继续进行动态分辨率、编辑器 UI 和场景收集路径的专项优化。
