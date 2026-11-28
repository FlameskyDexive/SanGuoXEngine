# E3 桃园场景保真：光照图编码与后处理 A/B

日期：2026-11-28

## 已修复

桃园导出的 `Lightmap-*_comp_light.exr` 在运行时会落成 Color4b，但它仍然是线性光照图；Standard shader 之前无条件按 RGBM `rgb * alpha * 8` 解码，导致岩石、地面和远景整体发白。引擎现在用 `_LightmapUV` 的符号位同时传递 UV 选择和线性 HDR 标记：`.exr/.hdr` 直接取 RGB，RGBM PNG 保持旧解码。

编辑器战斗相机还增加了 `SANGUO_EDITOR_POST=1` A/B 开关，用于验证 HDR、Tonemap、Bloom 的视觉收益和性能代价；默认编辑器路径仍关闭后处理，避免把 E7 性能预算与后处理成本混在一起。

## 真实编辑器证据

- 构建：`dotnet build XEngine.Editor/XEngine.Editor.csproj -c Debug --no-restore`，0 error。
- 桃园 Play D3D12：`lightmap-linear-d3d12.json`、`lightmap-linear-gameview-d3d12.png`。
- 线性光照图修复后：`cpuFrameTimeP95Ms=19.34`、`gpuFrameTimeMs=2.20`；无后处理、`drawCalls=31`。
- 后处理 A/B：`post-a-b-d3d12.json`、`post-a-b-gameview-d3d12.png`；`imageEffects=2`、`gpuFrameTimeMs=10.09`、`cpuFrameTimeP95Ms=29.19`，暂不作为编辑器默认质量档。

## 当前限制

场景仍与 Unity 参考存在天空色调、曝光、方向光照图/阴影遮罩、SpeedTree 和材质变体差异；E3 继续进行。后处理 A/B 证明视觉路径可用，但当前成本不满足 E7 的稳态帧时间门槛。
