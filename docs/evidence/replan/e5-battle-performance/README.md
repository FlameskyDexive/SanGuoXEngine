# E5 战斗 CPU 与内存：编辑器稳态复测

日期：2026-11-28

## D3D12 预热样本

`post-lightmap-warm-d3d12.json` 使用真实编辑器 `--serve`、TaoYuanBattle Play、D3D12，进入 Play 后等待 25 秒再取样，避免把资源加载和首次批次构建尖峰混入稳态窗口。

- `cpuFrameTimeP50Ms=16.77`
- `cpuFrameTimeP95Ms=17.64`
- `gpuFrameTimeMs=2.25`
- `renderCollectMs=5.13`
- `componentCollectMs=4.65`（新增分项观测，动态组件回调占主要部分）
- `staticRegistryMs=0.020`
- `lodUpdateMs=0.49`
- `skinnedCollectMs=1.57`
- `skinningUpdateMs=1.47`
- `boneTextureUploadMs=0.009`
- `dynamicCollectionCacheHits=940`、`dynamicCollectionCacheBuilds=145`
- `presentFps≈60`

## 状态

骨骼纹理 atlas 已将上传成本压到约 0.01 ms；当前瓶颈集中在动态组件回调、动态视图更新和编辑器 CPU 抖动，静态 registry 仅约 0.027 ms。E7 的 P95 ≤16.6 ms、600 帧零 Gen0 GC、10 分钟稳定性仍未通过，不能进入出包节点。
