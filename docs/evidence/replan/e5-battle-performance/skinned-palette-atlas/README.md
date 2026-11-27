# E5 Skinned Palette Atlas

日期：2026-11-28

## 交付

默认启用场景级 `SkinnedPaletteAtlas`：不含 BlendShape 的 SkinnedMeshRenderer 共用一张骨骼纹理，27 个 renderer 写入 27 行；每个实例通过现有 GPU-instancing custom data 选择自己的 palette row。带 BlendShape 的 mesh 保留原路径。

这避免了每个角色子网格各自创建/更新骨骼纹理，同时不改变骨骼矩阵、BindPose 或材质采样契约。

## Live 验收

- D3D12：`skinnedPaletteRows=27`、`skinnedPaletteUploads=1`、`boneTextureUploadMs=0.010`、`renderCollectMs=5.92`、`cpuFrameTimeP95Ms=18.21`、`gpuFrameTimeMs=2.21`。
- Vulkan：`skinnedPaletteRows=27`、`skinnedPaletteUploads=1`、`cpuFrameTimeP95Ms=17.62`、`gpuFrameTimeMs=12.80`。
- OpenGL 4.1：`skinnedPaletteRows=27`、`skinnedPaletteUploads=1`、`cpuFrameTimeP95Ms=18.51`、`gpuFrameTimeMs=1.45`。
- 三个后端截图中角色仍可见；D3D12 同机位截图为 `battle-gameview-d3d12.png`。

E7 的 16.6 ms P95 仍未完全满足，但角色骨骼上传从约 19 ms 降到约 0.01 ms，当前瓶颈已转移到编辑器尖峰和其它主循环工作。
