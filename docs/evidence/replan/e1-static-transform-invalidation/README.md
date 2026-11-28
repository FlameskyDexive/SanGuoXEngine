# E1 static transform invalidation

本节点修复静态合批 registry 的失效契约。`Transform.Position/Rotation/Scale`、原子变换和内部旋转修改现在在对象为 `IsStatic` 时递增 `Scene.StructureVersion`，触发下一帧 registry 重建；动态对象仍只走自己的 Transform 版本缓存，不会因每帧移动而重建整场静态批次。

## 验证

- `dotnet test XEngine.Runtime.Test/XEngine.Runtime.Test.csproj -c Debug --no-restore --filter "FullyQualifiedName~StaticBatchingTests"`：10/10 通过。
- `dotnet build XEngine.Editor/XEngine.Editor.csproj -c Debug --no-restore`：通过，0 error。
- D3D12 编辑器 Play，`TaoYuanBattle` / GameView：先采样 `stats_before`，运行时移动 `i_zx_menbu01 (3)` 的静态 MeshRenderer 0.75 世界单位，再采样 `stats_after`。
- 原始结果：`runtime-result-d3d12.json`。移动前后 `staticBatchGroups=2`、`staticBatchInstances=6`，并且移动操作返回 `structure=6168`，证明静态 registry 的 scene 版本已失效并重新收集；D3D12 GPU 约 2.40→2.36 ms，稳态 CPU P95 17.97→18.35 ms，未出现绘制数量异常。
- 截图：`editor-gameview-before.png`、`editor-gameview-after.png`。该物件位于当前 Battle Camera 视锥外，画面保持一致属于预期；退出 Play 后场景恢复。

## 限制

该节点只修复静态 Transform 变更后的批次生命周期；跨 Mesh CPU 合并仍保持关闭，只有契约兼容的同 Mesh GPU Instancing 才会合批。E7 与角色/场景保真门禁仍未通过。
