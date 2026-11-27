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

## 状态

可观测性已完成，但 E2 的功能验收未通过：桃园当前机位没有形成可见的同状态实例组，不能把 0 个 instanced draw 宣称为 GPU Instancing 完成。下一步必须接入可重复环境物件/重复 VFX 或相同敌人模型，保持材质、Lightmap、阴影和透明裁剪正确后再重新采样。
