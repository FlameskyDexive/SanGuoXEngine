# E5 SkinnedMeshRenderer 骨骼上传基线

日期：2026-11-28

- D3D12 Play 同机位截图与原始 stats：`editor-gameview-d3d12.png`、`battle-gameview-d3d12.png`、`runtime-result.json`。
- 当前直接上传基线：`skinnedCollectMs=21.14`、`skinningUpdateMs=20.88`、`boneTextureUploadMs=19.24`、`renderCollectMs=24.62`、`cpuFrameTimeP95Ms=56.09`。
- 懒上传实验曾把 CPU P95 降到约 20 ms，但截图中角色消失只剩血条，已完整回退，未提交。

后续需要共享骨骼调色板或可见性驱动的安全上传契约；当前角色视觉优先于未经验证的性能捷径。
