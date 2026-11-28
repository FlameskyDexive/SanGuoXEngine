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

## 天空源修正（2026-11-28）

Unity 的桃园场景并不使用反射探针 `Tex_hdr_the_sky_dusk.hdr` 作为天空盒。`Export/probe/scene.json`
显示 `Root/sky` 使用 `Assets/GameLevels/Levels/Sky/Mat_zhuoxian_yuannei_sky.mat`，其 `_Tex`
指向 `Tex_taoyuan_sky_Cube.tga`。该 2048×1024 TGA 是可直接采样的全景天空，已转为
`Assets/Scene/Sky/Tex_taoyuan_sky.png` 并绑定到 `SkyDusk.mat`；旧的反射探针 GUID 不再作为
天空盒纹理。导出过程保留原始像素和 sRGB 语义，没有把 HDR 反射探针误当成天空。

真实 D3D12 Play 证据：`sky-taoyuan-final-gameview-d3d12.png` 与
`sky-taoyuan-final-d3d12.json`。该次采样 `drawCalls=48`、`instancedDrawCalls=3`、
`gpuFrameTimeMs=3.792`、`cpuFrameTimeP95Ms=22.912`。天空源已对齐，但截图仍显示
XEngine 的 Battle Camera 可见范围缺少 Unity 对应地形，角色悬空且下半视野被天空填充；
因此本节点标记为“天空源已修正，E3 场景保真未通过”，后续必须先修场景几何/相机和光照遮罩。

## Battle Camera 坐标轴修正（2026-11-28）

`SanguoBattleDriver` 原来把 `(26, 0, 180)` 当作俯视角；XEngine 的 Euler 约定是
`(yaw, pitch, roll)`，这个值实际是水平镜头加 180° 翻滚，地形不进视锥。现在跟随相机使用
目标点 + 世界上方向的 `LookAt`，再补偿一次渲染坐标系的 180° roll；跟随距离调整为
`(0, 24, -65)`，让场景与战斗波次有足够取景空间。

真实 D3D12 Play 结果见 `camera-roll-fix-d3d12.json`、`camera-roll-fix-gameview.png`：
`staticBatchGroups=159`、`staticBatchInstances=734`、`dynamicBatchGroups=8`、
`dynamicBatchInstances=113`、`instancedDrawCalls=91`、`gpuFrameTimeMs=4.838`、
`cpuFrameTimeP95Ms=24.486`。这证明相机修正同时恢复了环境物件可见性和批处理统计；截图中的
墙体、城门、树木与黄昏天空已进入正确朝向，但材质/几何仍存在黑面、断片和 SpeedTree
差异，E3 继续保持未通过。
