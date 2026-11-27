# E1 静态 registry 单例接管与回退

日期：2026-11-28

## 交付内容

- `GameObject.IsStatic` 且通过 Mesh、材质、Shader 状态校验的 `MeshRenderer` 全部进入场景级 `StaticBatchRegistry`。
- 同 Mesh、材质状态、PropertyState、子网格、Layer、RenderingLayer 和 64m 空间块的多实例组保持 `InstancedMeshRenderable`；只有这些兼容组才使用 `DrawIndexedInstanced`。
- 单例组使用透传源 renderable 的静态 proxy，避免每帧进入 `MeshRenderer.TryCollectStatic`，但不强行切换到 GPU Instancing shader 变体。
- registry 不完整（资源流式加载、过滤或状态不满足）时保留原静态收集回退。

## 离线验证

- `dotnet test XEngine.Runtime.Test/XEngine.Runtime.Test.csproj -c Debug --no-restore --filter FullyQualifiedName~StaticBatchingTests`
- 7/7 通过；覆盖同 Mesh instancing、不可读 Mesh 回退、静态缓存、动态批处理和单例 proxy 不重复收集。
- `dotnet build XEngine.Editor/XEngine.Editor.csproj -c Debug --no-restore`
- 通过，0 error。

## 编辑器 Play 验证

环境：D3D12、TaoYuanBattle、Battle Camera、编辑器 Play，等待约 10 秒后采样；原始结果见 `runtime-result-check.json`。

- `static=2374`、`staticMesh=2167`、`registry complete=true`、`batched=2071`
- `renderablesCollected=2905`、`renderablesCulled=2876`、`renderablesDrawn=29`
- `drawCalls=31`、`instancedDrawCalls=0`（当前机位没有形成可升级的同状态多实例颜色组；多实例路径由已有 E2/动态批处理测试覆盖）
- `cpuFrameTimeP50Ms=48.80`、`cpuFrameTimeP95Ms=56.86`、`gpuFrameTimeMs=35.37`
- 画面截图：`battle-gameview-d3d12-960x540.png`、`editor-gameview-d3d12.png`

本节点证明静态组件收集不再逐个重建缓存，同时没有以单例强制实例化引入白化/材质变体回归。E7 的 60 FPS gate 仍未通过；下一节点继续处理 renderables 收集、编辑器主循环和真正可见的同状态实例组。
