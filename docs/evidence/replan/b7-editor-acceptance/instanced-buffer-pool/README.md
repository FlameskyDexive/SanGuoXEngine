# B7g GPU instance buffer pool

- 目标：静态 MeshRenderer 和动态合批在每帧构造实例数据时使用 `ArrayPool<InstanceData>`，避免为每个批次分配新数组；命令缓冲在编码时复制 payload，编码完成后归还池内数组。
- 兼容性：`InstancedMeshRenderable.InstanceCount` 区分有效元素数和池数组容量，绘制、winding 分组、AABB 计算均只读取有效范围；材质、子网格、RenderingLayer、透明排序和静态/动态批次分类保持原有分组规则。
- 引擎测试：`dotnet test XEngine.Runtime.Test/XEngine.Runtime.Test.csproj -c Debug --no-restore --filter FullyQualifiedName~StaticBatchingTests`，4/4 通过。
- 引擎构建：`dotnet build XEngine.Editor/XEngine.Editor.csproj -c Debug --no-restore`，0 error。
- 编辑器 D3D12 截图：`battle-gameview-d3d12-1280x720.png`，GameView 1280×720，四英雄和 HUD 可见。
- 运行统计：`drawCalls=29`、`instancedDrawCalls=1`、`renderablesCollected=2809`、`renderablesDrawn=49`、`cpuFrameTimeP95Ms=54.41`、`gpuFrameTimeMs=33.84`。本节点降低了实例数组分配，但 60 FPS gate 仍未通过，后续继续处理静态分块、编辑器 CPU 热点和 RTV 生命周期。
