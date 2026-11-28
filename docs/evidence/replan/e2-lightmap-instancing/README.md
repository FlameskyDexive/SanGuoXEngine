# E2 follow-up：静态烘焙物件的每实例 Lightmap ST 合批

日期：2026-11-28

## 目标

让 `GameObject.IsStatic` 的运行时 GPU Instancing 不再因为每个 Renderer 独有的
`_LightmapScaleOffset` 被拆成单例。批次 key 继续区分 Mesh、材质状态、Lightmap 页、UV
模式、GI 模式、子网格、Layer、RenderingLayer 和阴影状态；Lightmap ST 改由
`InstanceData.CustomData` 按实例上传，Standard shader 在 GPU_INSTANCING 路径按实例采样。

这保持了 Unity 静态批处理的关键语义：同一烘焙页可以共享 draw，实例仍使用各自的烘焙
UV 变换。没有 Lightmap 的对象保持原有零 custom data 路径；单例 proxy 继续使用普通
Renderer 的 uniform ST。

## 真实编辑器验收

- 后端：D3D12，TaoYuanBattle，编辑器 Play，GameView 1280×720。
- 截图：`editor-gameview-1280x720.png`、`editor-play-battle-960x540.png`。
- 原始 MCP 命令：`commands.json`。
- 原始 `runtime_stats`：`runtime-stats-d3d12.json`。
- 关键结果：`instancedDrawCalls=2`、`staticBatchGroups=3`、`staticBatchInstances=6`、
  `gpuFrameTimeMs=2.98`、`cpuFrameTimeP50Ms=16.47`、`cpuFrameTimeP95Ms=19.29`、
  `staticRegistryMs=0.0187`。
- `registry_probe2` 报告 `batched=1818`，证明场景静态 Renderer 由 registry 接管；
  画面保持可见，没有白块或整片光照丢失。

## 测试

- `dotnet build XEngine.Editor/XEngine.Editor.csproj -c Debug --no-restore`：0 error。
- `dotnet test XEngine.Runtime.Test/XEngine.Runtime.Test.csproj -c Debug --no-restore
  --filter "FullyQualifiedName~StaticBatchingTests"`：8/8 通过。
- 新增断言覆盖：两个不同 Lightmap ST 的 PropertyState 仍具有相同批次 key。

## 限制

本节点只扩大同 Mesh/同材质/同 Lightmap 页的实例化范围；跨 Mesh CPU 静态合并、透明
排序、复杂自定义 shader 的 per-instance ST 仍按契约回退，不能用本节点宣称所有场景都已
完成 Unity 全量静态合批。E7 稳态 p95 和长稳态仍未通过，PC/手机包继续冻结。
