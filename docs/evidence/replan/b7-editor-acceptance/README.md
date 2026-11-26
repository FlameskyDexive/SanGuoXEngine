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
