# E5 动态 MeshRenderer proxy 缓存

日期：2026-11-28

## 交付内容

未勾选 `IsStatic` 的 MeshRenderer 仍可能在整个 Play 生命周期内保持不动。现在按 Transform.Version、Mesh、材质 AssetID/状态哈希、Lightmap 参数和纹理覆盖版本缓存其 renderable proxy；Transform 或材质/覆盖状态变化时回到原始构建路径。动画/移动对象不会命中缓存。

## 验证

- `dotnet build XEngine.Editor/XEngine.Editor.csproj -c Debug --no-restore`：通过，0 error。
- `dotnet test XEngine.Runtime.Test/XEngine.Runtime.Test.csproj -c Debug --no-restore --filter "FullyQualifiedName~StaticBatchingTests|FullyQualifiedName~LODGroupTests"`：28/28 通过。
- D3D12 TaoYuanBattle Play，预热 10 秒；原始双采样见 `runtime-result-warm.json`。
- warm sample 1：`renderCollectMs=27.63`、`staticRegistryMs=0.014`、`renderablesCollected=2653`、`drawCalls=31`、`gpuFrameTimeMs=36.23`、`cpuFrameTimeP95Ms=56.47`。
- warm sample 2：`renderCollectMs=38.27`，记录了编辑器尖峰，不能把单次低值当成 E7 通过证据。
- 截图：`battle-gameview-d3d12-warm.png`、`editor-gameview-d3d12-warm.png`。

本节点证明动态收集路径有可复用缓存并降低了稳定样本的收集时间；E7 仍未通过，600 帧零 GC 和 16.6 ms P95 尚未满足。
