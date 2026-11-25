# M0 — AIBridge 导出探针（闸门）

- 日期：2026-11-26
- 结论：**闸门通过。** 导出走 AIBridge `code execute` 可行：不需要回退到 Unity 批处理，也不需要往 jws2 的
  `Assets/` 加任何脚本。
- 运行方式：jws2 的 Unity 2022.3.62f3 编辑器打开 `F:\jws2\client` 并空闲，然后

```powershell
py -3 Tools/unity_export/run_export.py status
py -3 Tools/unity_export/run_export.py run --all      # probe_env → probe_material → probe_hero → probe_clip → probe_scene
```

## 探针结果

| 脚本 | 耗时 | 验证的内容 | 结果 |
|---|---|---|---|
| `probe_env` | 1.5 s | 能引用 `UnityEditor`（`EditorSceneManager`、`AssetDatabase`、`AnimationUtility`、`AnimatorController`、`EditorJsonUtility`、`PrefabUtility`、`ShaderUtil`、`TextureImporter`、`AnimationMode`）；能写导出目录；`EditorJsonUtility.ToJson` 取到材质完整序列化（7975 字符） | 通过 |
| `probe_material` | 1.4 s | 按 `ShaderUtil` 枚举一个角色材质的全部属性与贴图导入设置 | 通过 |
| `probe_hero` | 1.4 s | 五个角色 prefab 的解剖（骨架、蒙皮网格、材质槽、控制器层） | 通过 |
| `probe_clip` | 1.1 s | 控制器、剪辑曲线、在预览场景里用 `AnimationMode` 采样姿态 | 通过 |
| `probe_scene` | 4.4 s | 只读打开桃园运行时场景做清单，之后恢复编辑器原状 | 通过 |

`probe_scene` 结束后编辑器回到原来的 `OfflineMain.unity`，无未保存改动（脚本在检测到任何场景有未保存改动时会
直接拒绝执行）。

## 桃园场景清单（`Assets/Scenes/GameScenes/C-ZhuoXian_TaoYuan.unity`）

| 项 | 实测 |
|---|---|
| 根对象 / 游戏对象 | 1 / 4046，其中 **3580 个属于预制体实例**，10 个未激活，0 个丢失脚本 |
| 渲染器 | 3272 `MeshRenderer`，125 `ParticleSystemRenderer`，21 `SkinnedMeshRenderer` |
| **LODGroup** | **1378 组**（离线粗估只有 10+） |
| 光照贴图 | **2 张 2048 图集**，每张带方向图与阴影遮罩（`…/ZhuoXian_TaoYuan/Lightmap-{0,1}_comp_light.exr`、`_comp_dir.png`、`_comp_shadowmask.png`）；**2083 个渲染器带光照贴图索引** |
| 光照贴图 UV | 场景 FBX 的 `.meta` 里 `generateSecondaryUV: 0`，第二套 UV 来自源 FBX 本身 |
| 灯光 | 1 盏方向光：色 (1, 0.891, 0.825)，强度 1.6，Hard 阴影，ForcePixel，Mixed 烘焙类型，欧拉角 (39.85, 12.66, 0) |
| RenderSettings | 环境光 Skybox 模式（天 (0.561,0.713,1) / 地平 (0.114,0.125,0.133) / 地面 (0.047,0.043,0.035)）；反射 Custom → `Tex_hdr_the_sky_dusk.hdr`；天空盒材质运行时赋值；雾关 |
| 碰撞体 | 65 `BoxCollider`，1 `MeshCollider`，1 `SphereCollider`；无 Terrain |
| 场景脚本 | 154 `PsInstance`、127 `CullingObject`、109 `RuntimeBillBoardItem`、15 `ModelSystem`、10 `SceneObjectMarker`、各 1 个 `GlobalParamSetter` / `SceneDetailManager` |
| 网格来源 / 材质 | 246 种网格（FBX 在 `GameLevels/Raw/Fbx/` 与 `GameLevels/Levels/Tilling/`，另有 214 个内置 Quad、65 个内置 Cube）/ 139 个材质 |
| 主要着色器 | `S_Standard_Scene` 1210、`_Lod1` 633、`S_Standard_Sequence` 598、`_Lod2` 317、`_Scene_Grass` 119、`S_Cj_Fire_01` 109、`Unlit/BillBoard` 109、`_Scene_Plant` 99、`_Scene_PBR_Plant` 84、`_TerrainAtla` 2 |
| 包围盒 | min (−525, −435, −30)，max (525, 616, 1120) |

## 五个角色

| | 赵云 | 关羽 | 刘备 | 张飞 | 吕布 |
|---|---|---|---|---|---|
| Transform 数 | 153 | 106 | 149 | 121 | 168 |
| 蒙皮网格数 | 7 | 5 | 4 | 5 | 6 |
| 材质 | Body/Cloth/Leg/武器 = `S_Standard_Hero`，Skin/Face = `S_Standard_Skin`，Hair = `S_Standard_Hair` + 第二槽 `Mat_ShadowCaster` | | | | |

- 全部为 Generic 骨骼（`Animator.isHuman == false`），Avatar 来自模型 FBX，根运动开启。
- 控制器两层：`Base`（Override）与 `hurt`（Additive）。
- 每个蒙皮网格 1 个子网格、1 个材质槽，骨骼数最多 66；武器是挂在骨骼下的独立蒙皮网格。
- 材质 `Hero_LvBu_Body`：122 个属性；`_MainTex` → `Tex_LvBu_Body_D.tga`（512，sRGB），
  `_MetallicGlossMap` → `Tex_LvBu_Body_M_Alpha.tga`（512，线性），`_BumpMap` → `…_N_NRM.tga`（512，NormalMap）；
  自发光色 (1, 0.508, 0)、强度 3.8；每个材质自带反射立方体贴图 `_Unity_SpecCube`
  （`Assets/Actor/PublicTex/Tex_Glazed Patio_1.1_Cube.exr`）。

## 动画（吕布控制器 `Hero_LvBu.controller`）

- 2 层、**38 个状态、39 条过渡**；参数 40 个 Trigger + 1 个 Float。
- 37 个状态的动作都是拆分后的 `.anim` 文件：`Assets/Actor/AnimationSplit/Animation_L/Hero_LvBu/Hero_LvBu_<动作>.anim`
  （文件名带角色前缀，离线按 `<动作>.anim` 找不到），没有 BlendTree。
- `NormalAttack_1`：1.667 s @ 30 fps，不循环，1025 条曲线（580 旋转、438 位置、7 条根运动），21,505 个关键帧，
  0 个事件。根运动以 `UnityEngine.Animator` 上的 `RootT.x/y/z`、`RootQ.x/y/z/w` 曲线存储；骨骼路径相对
  Animator 根（如 `Bip001`、`Bip001/node_right_weapon`）。
- **姿态采样可行**：`EditorSceneManager.NewPreviewScene()` + `Object.Instantiate(prefab, scene)` +
  `AnimationMode.SampleAnimationClip`，在 `Bip001 Spine` 上 t=0 与 t=L/2 的旋转不同；预览场景随后关闭，
  不触碰已打开的场景。

## 对计划的影响

1. **LODGroup 是硬需求**（1378 组）——引擎 `main` 上的 LODGroup 节点确认要做。
2. **光照贴图存在**（之前「同目录没有光照贴图」的判断是错的）：M3 直接使用 Unity 的烘焙结果——导出每个渲染器的
   `lightmapIndex` / `lightmapScaleOffset`，颜色图转引擎的 RGBM；第二套 UV 来自 FBX，可由引擎直接导入。
   方向图与阴影遮罩引擎暂不消费，登记为引擎缺口；引擎自行烘焙（Photonic）改为可选对照项。
3. **3580 / 4046 个对象是预制体实例**——离线解析 YAML 需要自行还原预制体覆盖，选 AIBridge 是对的。
4. **根运动是 `RootT`/`RootQ` 曲线**——M2 的剪辑生成器要把它们映射到引擎的根运动。
5. **角色自带反射立方体贴图**——M1 先用场景 IBL 近似，差异记入证据。
6. 场景里还有 125 个粒子系统、109 个公告板、21 个蒙皮的场景小兵（`S_Standard_Sequence`），进入 M3 / M5 范围。

## 过程中踩到并修正的点

- 片段宿主把脚本包在 `object Execute()` 里，只能用局部函数；编译错误在 CLI 返回的 `data.compileErrors`，
  脚本必须放在 `.aibridge/code/` 下，返回值经 `returnValue` 回传。驱动 `run_export.py` 据此打印编译错误，并校验
  落盘的 JSON。
- Unity 2022.3 的真实签名（`probe_api` 用反射取得）：`ShaderUtil` 没有 `GetPropertyFlags` /
  `GetPropertyAttributes`，`GetRangeLimits(shader, index, defminmax)` 返回 `float`；`Animator.isHuman`；
  `AnimatorControllerParameter` 在 `UnityEngine`；`LightmapData` 没有 `mixedLightingMode`。
- 顺带发现并修复了一个引擎缺陷（带 xUnit 测试的工程打不开），见引擎仓库
  `docs/superpowers/evidence/scripting/2026-11-26-nuget-dependency-resolution.md`（`cfab4988a`）。

## 工程脚手架验收

- 守卫测试 `dotnet test SanGuoXEngine.Tests.csproj`：4 / 4 通过；反向验证（埋入 `.lua`、引用 `NLua` 的 C#、清单里
  加 Lua 包）后 3 / 4 失败并各自报出，清理后恢复 4 / 4。
- 实时编辑器打开工程：错误日志 0 条，脚本上下文加载 18 个程序集，Game 视图截图
  `xengine/project-open-gameview.png`。

## 原始输出

`export/` 下是提交进来的摘要：`probe_env.json`、`probe_scene.json`、`probe_hero.json`、`summary.json`。
大文档不入库，需要时重跑探针生成到 `Export/probe/`（`heroes.json` 342 KB、`scene.json` 432 KB、
`probe_clip.json` 61 KB、`probe_material.json` 24 KB）。
