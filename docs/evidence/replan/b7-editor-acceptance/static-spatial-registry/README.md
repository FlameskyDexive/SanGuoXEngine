# B7h 场景级静态空间分块实例组

- `GameObject.IsStatic` 对象在编辑器 Play 的 GameView 中进入场景级 `StaticBatchRegistry`；按 Mesh、材质状态、属性状态、子网格、Layer、RenderingLayer 和 64m 空间块分组。
- LODGroup 先按当前相机选择可见级别，静态 registry 只接管 `EnabledInHierarchy` 的当前级别；LOD 级别变化会使 Scene 结构版本递增并触发重建。SceneView 有隐藏过滤时 registry 对被过滤对象回退到原始 Renderer 路径。
- 静态批次保存 chunk AABB、实例变换、光照/材质属性和静态阴影标志，普通帧直接以 instanced renderable 参加相机剔除；静态源组件从静态收集通道跳过，避免重复提交。
- 单测：`dotnet test XEngine.Runtime.Test/XEngine.Runtime.Test.csproj -c Debug --no-restore --filter FullyQualifiedName~StaticBatchingTests`，4/4 通过。
- 编辑器构建：`dotnet build XEngine.Editor/XEngine.Editor.csproj -c Debug --no-restore`，0 error。
- D3D12 编辑器截图：`battle-gameview-d3d12-1280x720.png`，四英雄、HUD 和静态场景可见。
- 运行统计：`renderablesCollected=2712`（此前 2809），`registry batches=58`，`drawCalls=29`，`instancedDrawCalls=1`，`triangles=104864`，`gpuFrameTimeMs=31.27`，`cpuFrameTimeP95Ms=57.70`。对象收集开销已下降，但 B7 60 FPS gate 仍未通过，后续继续处理 CPU 峰值、RTV 生命周期和角色蒙皮实例。
