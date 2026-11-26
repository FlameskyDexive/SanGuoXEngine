# M3-4 桃园场景验收

日期：2026-11-26 · 后端：D3D12（Vulkan 离线整帧渲染描述符池耗尽，见"已知问题"） · 场景：`Assets/Scenes/TaoYuan.scene`（M3-3 `SanguoSceneBuilder` 生成）

## 并排对照（Unity jws2 | XEngine）

| 机位 | Unity 位姿 | 文件 |
|---|---|---|
| close（桃花石墙） | [38,179,470] → [55,176,545] | `close-side-by-side.png` |
| mid（火把石林） | [95,196,415] → [70,180,570] | `mid-side-by-side.png` |
| panorama（瀑布高原） | [300,280,330] → [110,160,590] | `panorama-side-by-side.png` |

三帧均为 Unity 参考（`Export/scene/frames/`，FOV 50，960×540，导出时 lodBias 0.4）与 XEngine 同位姿渲染（`Export/scene/xengine-frames/`，`SanguoCaptureStudio.CaptureSceneFrame` 离线管线，线性回读 → sRGB 编码，同 M1/M2 采集路径）。

## 场景数据核对（open_scene 探针）

| 指标 | Unity 导出 | XEngine 场景 | 一致性 |
|---|---|---|---|
| 对象数 | 4046 | 4047（+采集相机） | ✓ |
| MeshRenderer | 3272 | 3256 | ✓（差 16 = SpeedTree `.asset` 网格，已记录未实现） |
| 带网格绑定 | — | 2976（其余 280 = 内建 Quad/Cube/Sphere） | ✓ |
| 带材质绑定 | — | 3140 | ✓（42 个 Effects 材质属 M5） |
| 光照贴图烘焙对象 | 2083 | 2067 | ✓（同 SpeedTree 差值） |
| LODGroup | 1378 | 1378 | ✓ 精确 |
| 灯光 / 相机 | 1 / — | 1 / 1 | ✓ |
| 天空 | 全景 HDR | `Sanguo/SkyPanorama`（黄昏全景 HDR） | ✓ |
| 光照贴图页 | 2（color+dir+shadowmask） | 2 页 color（dir/shadowmask 已导入待消费） | 部分 |

## runtime_stats（mid 机位，`RenderStats.Begin/EndFrame` 括号帧）

```
drawCalls: 13720, triangles: 11702909, renderables drawn: 13704
  (culled 6424, lod-hidden 4664)
shadow drawCalls: 27687, shadow triangles: 8069637
```

- `lod-hidden 4664`：LODGroup 末级剔除按 Unity 规则生效（share×lodBias 0.4）。
- 远景主石林/远山（t_zx_shashidi01 size 398、i_zx_yuanshan01 size 373）选级实测 chosen=0 ✓。

## 本轮修复的关键问题（全部复现→定位→修复→回归）

1. **相机远裁剪面**：引擎默认 FarClipPlane=100 m，场景跨度 ~500 m，mid/panorama 主体全部被远裁剪。`CreateCamera` 设 4000 m（与 Unity 采集一致）后三帧主体全部出现。
2. **layer31 碰撞代理**：65 个 Unity 内建 Cube（墙体碰撞代理）在 Unity 里被相机 cullingMask 剔除；引擎相机现剔除 layer 31，不再渲染为实心立方体。
3. **天空材质 GUID**：`WriteNative` 只把 guid 写进 .meta 不回填资产对象，场景序列化时 CustomMaterial 曾为全零 GUID；改为显式引用 guid 变量。
4. **内建网格 GUID**：同上，`EnsureBuiltinMesh` 现返回 guid 而非资产实例。
5. **SkyPanorama.shader 未导入**：新建后首次刷新未触发导入（hash 缓存），touch 后正常；导入后天空全景正确渲染（黄昏云、日轮光晕）。

## 已知差异（按计划记录）

| 差异 | 归属 |
|---|---|
| **远景白化**：全场景几何/轮廓/放置正确，但中远景着色趋近纯白（近景纹理与烘焙阴影正常）。无 tone mapping（ACES）/LUT/Bloom 后期链，光照贴图 EXR 被 8-bit 量化，叠 Hemisphere 环境光后过曝 | M8 打磨（后期链）+ 引擎侧待查（远景光照采样） |
| 桃花/火焰/烟雾等粒子不在场（125 个粒子系统） | M5 特效 |
| Effects 材质 42 个未映射，对应渲染器不绘制 | M5 |
| SpeedTree `.asset` 网格 ×16 实例未实现，渲染器跳过 | 引擎缺口清单 |
| 光照贴图 dir/shadowmask 未消费（只用颜色图） | 引擎缺口清单 |
| 逐渲染器 Cast/ReceiveShadows 标志引擎不存在（用 GameObject.IsStatic 近似） | 记录 |

## 采集流程陷阱（复现实验记录）

- Vulkan 后端单帧 13.7k draw calls 时描述符池耗尽（262144 sets）→ 用 `--graphics d3d12` 采集。
- 离线 `camera.Render()` 不走 `RenderStats.BeginFrame/EndFrame`，统计需自 bracket。
- mcp_live 不带 `--keep` 时编辑器用完即退；带 `--keep` 的残留实例会占住工程令下一次启动挂起——采集前先清进程。
- `runtime_screenshot` 需要可见的 GameView 面板，默认布局没有；三帧采集走离线管线（与 M1/M2 同路径）。
