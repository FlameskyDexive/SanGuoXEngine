# B7 编辑器 Play 性能验收

日期：2026-11-27

## 本节点实现

- 战斗相机默认挂载 `UniversalAdditionalCameraData`。
- 编辑器 Play 默认关闭动态阴影提交，保留后处理和静态/烘焙场景数据，作为编辑器优先的性能预算。
- PC 包和手机包继续留到编辑器验收完成后处理。

## D3D12

![D3D12 battle](battle-d3d12-shadow-budget.png)

- `drawCalls=1420`
- `instancedDrawCalls=37`
- `triangles=1,630,328`
- `shadowDrawCalls=2,788`
- `shadowTriangles=1,039,150`
- `frameTimeMs=256.47`
- `gpuFrameTimeMs=217.01`
- `cpuFrameTimeP95Ms=256.47`
- `backendName=Direct3D 12`

## Vulkan 回归

![Vulkan battle](battle-vulkan-shadow-budget.png)

- `drawCalls=1420`
- `shadowDrawCalls=2,788`
- `frameTimeMs=313.17`
- `gpuFrameTimeMs=349.06`
- `backendName=Vulkan (Intel(R) Arc(TM) 140T GPU (32GB))`

## Gate 结果

动态阴影预算已生效，但 D3D12 和 Vulkan 都仍未达到 60 FPS、p95 ≤ 16.6 ms。B7 保持进行中，后续需要继续处理场景几何提交、编辑器后端同步和资产 LOD，不能宣称编辑器终验通过。
