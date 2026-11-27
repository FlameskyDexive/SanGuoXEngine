# E2 GPU Instancing / 动态合批可观测性节点

日期：2026-11-28

## 本节点交付

- `runtime_stats` 现在返回 `StaticBatchGroups`、`StaticBatchInstances`、`DynamicBatchGroups`、`DynamicBatchInstances`、`StaticCollectionCacheHits` 和 `StaticCollectionCacheBuilds`。
- 这些字段与 `RenderStats.Last` 同源，便于把编辑器 Play 截图、批次计数和帧时间放在同一份原始 JSON 中核对。

## 验证

- `dotnet build XEngine.Editor/XEngine.Editor.csproj -c Debug --no-restore`：通过，0 error。
- `dotnet test XEngine.Runtime.Test/XEngine.Runtime.Test.csproj -c Debug --no-restore --filter "FullyQualifiedName~StaticBatchingTests|FullyQualifiedName~LODGroupTests"`：28/28 通过。
- D3D12 TaoYuanBattle Play 原始结果：`runtime-result.json`。
- 当前机位：`drawCalls=31`、`instancedDrawCalls=0`、`staticBatchGroups=0`、`dynamicBatchGroups=0`、`renderablesCollected=2653`、`cpuFrameTimeP95Ms=57.29`、`gpuFrameTimeMs=35.33`。
- 截图：`battle-gameview-d3d12-960x540.png`、`editor-gameview-d3d12.png`。

## 编辑器批处理探针复验

通过 `SANGUO_BATCH_PROBE=1` 临时生成 64 个 `IsStatic` 方块和 64 个动态方块，
两组都使用同一可读 Mesh；静态组由 scene registry 接管，动态组由颜色 pass 的运行时
兼容分组升级为 instanced draw。探针只存在于验收进程，不改变默认桃园场景。

- 原始结果：`batch-probe-d3d12.json`。
- GameView 截图：`batch-probe-gameview-d3d12.png`。
- D3D12 stats：`drawCalls=35`、`instancedDrawCalls=4`、`staticBatchGroups=6`、
  `staticBatchInstances=112`、`dynamicBatchGroups=1`、`dynamicBatchInstances=63`、
  `renderablesDrawn=95`、`cpuFrameTimeP95Ms=24.81`、`gpuFrameTimeMs=2.30`。
- 该探针证明静态 registry 和动态运行时合批都实际进入编辑器 Play 的绘制命令；
  24/63 的可见数量受 Battle Camera 视锥和空间块裁剪影响，不能按探针总数推算吞吐。

## 状态

批处理探针的功能验收通过；默认桃园机位仍保留原始 0-instanced 基线，不能用探针结果替代真实场景的材质/Lightmap/阴影保真验收。

补充采样：registry 在同一战斗机位收集到 `64` 个多实例 proxy、共 `231` 个实例，说明静态分组已经形成；但这些组的 AABB 全部在当前 Battle Camera 视锥外，颜色 pass 没有提交它们，因此 `instancedDrawCalls=0`。当前机位可见的 4 个静态场景对象仍属于不同状态，不能为了制造 draw call 忽略 Lightmap/材质契约。

## 收集耗时观测

引擎节点增加 `RenderCollectMs` 与 `StaticRegistryMs`。D3D12 原始结果见 `runtime-result-timing.json`：`renderCollectMs=36.72`、`staticRegistryMs=0.019`，证明后续 CPU 优化重点应放在动态组件收集，而不是静态 registry 重建。
