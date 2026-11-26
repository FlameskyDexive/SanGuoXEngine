# SanGuoXEngine 移植进度

- 计划：[`plans/2026-11-26-sanguo-taoyuan-port-plan.md`](plans/2026-11-26-sanguo-taoyuan-port-plan.md)（第 9 节是 M0 实测后的修订，第 10 节是 M1 的）
- 纪律：一个节点 = 代码 + 测试 + Unity/XEngine 并排截图（`evidence/mN/`）→ commit（`[feat]sanguo port function + …`）→ push
- 证据目录约定：`evidence/mN/<节点>.md` + 截图，分 `unity/` 与 `xengine/` 两侧
- 仓库：私有的 `FlameskyDexive/SanGuoXEngine`，在 XEngine 仓库里登记为子模块 `SanGuoXEngine`

## 里程碑

| 里程碑 | 状态 | 节点 / 说明 | 证据 |
|---|---|---|---|
| M0 脚手架与导出探针 | 完成 | 工程已建（引擎 1.1.0-a19）；harness、manifest、`.gitignore`、`AGENTS.md`、计划、`NoLuaGuardTests`（4/4，含反向验证）；AIBridge 探针 5/5 通过（闸门通过）；实时编辑器可打开工程 | `evidence/m0/probe.md` |
| M1 角色模型与 PBR 材质 | 完成 | 5 个 FBX、78 张贴图（+31 张派生图）、26 个材质、5 个 prefab 已导入；模型节点与 Unity 一一对应（位置差 ≤ 4.5 mm）；蒙皮经数据与渲染剪影双重核对（GPU 剪影重合 0.997–1.000）；Python 测试 70 个、C# 测试 8 个通过。Unity 参考图在 FourBones 质量等级下抓取 | `evidence/m1/heroes.md` |
| M2 动画 | 未开始 | 战斗剪辑导出（含 `RootT`/`RootQ` 根运动曲线）→ `AnimationClip` → `SanguoAnimator` | |
| M3 桃园场景 | 未开始 | 4046 对象 / 1378 组 LOD / 2 张光照贴图 / 天空 / IBL / LUT；引擎节点 LODGroup | |
| M4 战斗内核 | 未开始 | C# 重写：连段、技能时间轴、判定、HUD、顿帧 | |
| M5 特效 | 未开始 | PsEffect → 粒子 prefab（场景内 125 个粒子系统 + 技能特效），着色器映射，`SanguoEffect` | |
| M6 音频 | 未开始 | `Tools/wwise2ogg` + `SanguoAudio` | |
| M7 吕布 / 小兵 / 胜负 | 未开始 | Boss 状态机、小兵波次、结算 | |
| M8 打磨 / 性能 / 出包 | 未开始 | 自写着色器、性能预算、出包验收、gitlink | |

## 引擎缺口（在引擎 `main` 上单独成节点）

| 缺口 | 触发里程碑 | 状态 |
|---|---|---|
| 用户程序集无法解析 `Directory.Build.props` 声明的 NuGet 依赖（带 xUnit 测试的工程打不开） | M0 | **已修复**，引擎 `cfab4988a` |
| 内置着色器 `StandardAnisotropic` / `Unlit` / `StandardTransparent` 不做 GPU 蒙皮（`Variants` 未声明蒙皮关键字 + Slang 顶点阶段不蒙皮），蒙皮网格画在绑定姿态 | M1 | **已修复**，引擎 `fcf643108` |
| `Default/Standard` 的 Slang Prepass / GBuffer 只蒙皮位置，法线、切线与运动矢量起点仍是绑定姿态的值（影响 Deferred 路径，以及读取 Prepass 法线 / 运动矢量的 GTAO、SSGI、SSR、ScreenSpaceDecal；Forward 光照不受影响） | M2 / M3 | **已修复**，引擎 `f0f68d5a3` |
| LODGroup 组件（场景实测 1378 组） | M3 | 未开始 |
| 光照贴图不消费方向图 / 阴影遮罩（桃园有 2 张图集的方向图与阴影遮罩） | M3 | 未开始，先用颜色图 |
| 无立方体贴图导入器（天空 HDR → IBL、角色自带反射立方体贴图） | M1 / M3 | 未开始，先在移植仓库的 Editor 侧生成 |
| Cinemachine 无 Impulse（震屏） | M4 | 移植仓库内手写 |
| 无径向模糊 / 屏幕闪白后处理 | M5 | 先降级 |

## Lua 逻辑重写范围

只重写「桃园吕布 Boss 战」用到部分；原项目的帧同步、定点数、联网校验、绝大部分 Buff / 技能分支不移植。

| 已重写 | 状态 |
|---|---|
| — | |

## 未移植 / 有意降级

| 项 | 说明 |
|---|---|
| 交互式音乐层级 | 取固定战斗曲目循环播放 |
| 剧情过场（Timeline `Plot_*`） | 不在 Demo 范围 |
| 吕布 P2 变身形态 | 先做基础形态 |
| 方向光照贴图、阴影遮罩 | 引擎暂不消费；阴影靠实时级联 |
| 角色材质的边缘光、假皮肤、受击闪白、头发各向异性 | M1 先用 `Default/Standard` / `StandardAnisotropic` 近似，M8 再补自写着色器 |
| 角色材质自带的反射立方贴图 | M1 的截图场景里没有 IBL，金属件反光偏暗；M3 随场景接 `ReflectionProbe` |
| 头发的两遍（正面 + 背面）透明绘制 | 引擎是单遍 alpha 测试；吕布左侧的长翎短一截，M8 评估 |
| Unity 的 OneBone 质量档 | 引擎固定 4 骨混合，对应 Unity 的 FourBones 档；低端档的对齐未做 |

## 核对 Unity 时的注意点（M1 实测）

- jws2 的活动质量等级是 Fastest，**每顶点只用 1 根骨头**；`SkinnedMeshRenderer.BakeMesh` 也只取第一骨，不能当蒙皮基准。参考图一律在 FourBones 等级下抓。
- Unity 的「网格压缩 = Low」会把权重量化；引擎原样读取 FBX。两侧权重不同是 Unity 改的，不是导入错误。
- 引擎截图前要对每个材质的每个贴图槽 `EnsureLoaded()`，否则贴图被画成白色蒙版。
