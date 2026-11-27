# SanGuoXEngine 移植进度

- 计划：[`plans/2026-11-26-sanguo-taoyuan-port-plan.md`](plans/2026-11-26-sanguo-taoyuan-port-plan.md)（第 9、10、11 节分别是 M0、M1、M2 实测后的修订）
- 纪律：一个节点 = 代码 + 测试 + Unity/XEngine 并排截图（`evidence/mN/`）→ commit（`[feat]sanguo port function + …`）→ push
- 证据目录约定：`evidence/mN/<节点>.md` + 截图，分 `unity/` 与 `xengine/` 两侧
- 仓库：私有的 `FlameskyDexive/SanGuoXEngine`，在 XEngine 仓库里登记为子模块 `SanGuoXEngine`

## 里程碑

| 里程碑 | 状态 | 节点 / 说明 | 证据 |
|---|---|---|---|
| M0 脚手架与导出探针 | 完成 | 工程已建（引擎 1.1.0-a19）；harness、manifest、`.gitignore`、`AGENTS.md`、计划、`NoLuaGuardTests`（4/4，含反向验证）；AIBridge 探针 5/5 通过（闸门通过）；实时编辑器可打开工程 | `evidence/m0/probe.md` |
| M1 角色模型与 PBR 材质 | 完成 | 5 个 FBX、78 张贴图（+31 张派生图）、26 个材质、5 个 prefab 已导入（头发材质在 M2 改为双面，截图已重拍）；模型节点与 Unity 一一对应（位置差 ≤ 4.5 mm）；蒙皮经数据与渲染剪影双重核对（GPU 剪影重合 0.997–1.000）；Python 测试 70 个、C# 测试 8 个通过。Unity 参考图在 FourBones 质量等级下抓取 | `evidence/m1/heroes.md` |
| M2 动画 | 完成 | 124 条战斗剪辑（5 个控制器）已导入；按实测的 Unity 播放规则转换曲线（密曲线逐帧、根运动按帧）；根运动走引擎的模型根轨迹；`SanguoAnimator`（点名播放、加法受击层、变速）已验证。引擎里播放出的姿态对 Unity 采样：旋转最大差 0.396°、位置 2.08 mm、根运动 18.5 mm；Python 测试 90 个、C# 测试 22 个通过 | `evidence/m2/animation.md` |
| M3 桃园场景 | 完成 | 场景 `Assets/Scenes/TaoYuan.scene` 已生成并验收：4047 对象 / 1378 LODGroup（精确）/ 2 页光照贴图 / 全景黄昏天空 / 1 盏方向光；三机位并排对照（D3D12）+ runtime_stats（13720 drawCalls / 11.7M 三角 / lod-hidden 4664）见 `evidence/m3/scene.md`。已知差异：远景白化（无 ACES/LUT 后期，M8 打磨）、粒子 M5、Effects 材质 42 个 M5、SpeedTree ×16 与 dir/shadowmask 消费在引擎缺口清单 | `evidence/m3/scene.md` |
| M4 战斗内核 | 核心完成 | 数据管线（8 张 Lua 表 33963 行 → 7 张原生资产，Python 108 测试）+ 纯逻辑内核（判定几何/血量/状态机/移动/布阵/BattleStage/技能时间轴/连段窗口/导弹伤害帧，C# 45 测试）+ 场景侧驱动器（英雄 prefab/怪物 AI/输入/相机）+ `TaoYuanBattle.scene`。play 实测：234 怪全数追击接敌命中（日志为证）。引擎节点：描述符池规模化（Vulkan 256 池/D3D12 256 页）已落地。待 M4-5 收尾：可玩帧取景/M7 怪物 prefab/M7 HUD | `evidence/m4/battle.md` |
| M5 特效 | 未开始 | PsEffect → 粒子 prefab（场景内 125 个粒子系统 + 技能特效），着色器映射，`SanguoEffect` | |
| M6 音频 | 未开始 | `Tools/wwise2ogg` + `SanguoAudio` | |
| M7 吕布 / 小兵 / 胜负 | 未开始 | Boss 状态机、小兵波次、结算 | |
| M8 打磨 / 性能 / 出包 | 未开始 | 自写着色器、性能预算、出包验收、gitlink | |

## 重构计划进度

| 节点 | 状态 | 证据 |
|---|---|---|
| B0 编辑器性能基线 | 完成 | `evidence/replan/b0-baseline/`，D3D12 战斗基线 3097 draw / 0 instanced draw |
| B1 静态场景自动实例批处理 | 完成 | `evidence/replan/b1-static-batching/`，2818 draw / 56 instanced draw / 1515 static instances |
| B2 GPU Instancing 和动态合批 | 完成 | `evidence/replan/b2-gpu-instancing/`，2079 draw / 73 instanced draw / 3914 dynamic instances |
| B3 桃园场景接入 | 完成 | `evidence/replan/b3-taoyuan/`，完整桃园层级、静态标志、LOD、光照贴图和批次路径已接入；SpeedTree、粒子和效果材质列入 B6 |
| B4 角色和敌人接入 | 进行中 | `evidence/replan/b4-characters/`，四英雄保持真实 Prefab，敌人已切换到已导入真实士兵模型；逐 MonsterID Prefab 映射和 GPU 蒙皮实例仍待处理 |
| B5 战斗逻辑优化 | 完成 | `evidence/replan/b5-battle/`，B5c 接入怪物 GroupID/Atk 数据驱动近战伤害；固定 60 Hz 步进、复用战斗 scratch 缓冲；46 个子模块测试通过 |
| B6 视觉和特效 | 进行中 | `evidence/replan/b6-visual/`，Volume/HDR/ACES/Bloom 与战斗相机已接入，敌人材质和特效仍待处理 |
| B7 编辑器 Play 终验 | 进行中 | `evidence/replan/b7-editor-acceptance/`，B7f 增加静态 MeshRenderer 场景收集缓存，D3D12 8 秒复测 p95 55.38 ms；60 FPS gate 仍未通过 |

## 引擎缺口（在引擎 `main` 上单独成节点）

| 缺口 | 触发里程碑 | 状态 |
|---|---|---|
| 用户程序集无法解析 `Directory.Build.props` 声明的 NuGet 依赖（带 xUnit 测试的工程打不开） | M0 | **已修复**，引擎 `cfab4988a` |
| 内置着色器 `StandardAnisotropic` / `Unlit` / `StandardTransparent` 不做 GPU 蒙皮（`Variants` 未声明蒙皮关键字 + Slang 顶点阶段不蒙皮），蒙皮网格画在绑定姿态 | M1 | **已修复**，引擎 `fcf643108` |
| `Default/Standard` 的 Slang Prepass / GBuffer 只蒙皮位置，法线、切线与运动矢量起点仍是绑定姿态的值（影响 Deferred 路径，以及读取 Prepass 法线 / 运动矢量的 GTAO、SSGI、SSR、ScreenSpaceDecal；Forward 光照不受影响） | M2 / M3 | **已修复**，引擎 `f0f68d5a3` |
| LODGroup 组件（场景实测 1378 组） | M3 | **已修复**，引擎 `fa2cf7d08`：按相机做 Unity 公式的屏幕相对高度选级（实测 268/268 一致，含 lodBias），末级空层级剔除，`QualitySettings.LodBias`，采集跳过隐藏层级，`runtime_stats` 上报 |
| 光照贴图不消费方向图 / 阴影遮罩（桃园有 2 张图集的方向图与阴影遮罩） | M3 | 未开始，先用颜色图 |
| 无立方体贴图导入器（天空 HDR → IBL、角色自带反射立方体贴图） | M1 / M3 | 未开始，先在移植仓库的 Editor 侧生成 |
| Cinemachine 无 Impulse（震屏） | M4 | 移植仓库内手写 |
| 编辑模式（非播放）下 Animator 不自动绑定控制器（要先 `SetControllerAsset`），`Animator.Update` 不施加根运动 | 截图工具 | 未处理；`SanguoCaptureStudio.CapturePoses` 自己绑定并步进 |
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
| 头发的两次透明叠加 | 引擎是单遍双面（`_Cull = 0`）的 alpha 测试不透明；发片双面可见，但少了透明叠加的层次 |
| 受击加法层的合成 | 引擎按「剪辑 − 首帧」叠加，Unity 侧没有合成姿态的采样，没有数值比对 |
| 角色材质自带的反射立方贴图 | M1 的截图场景里没有 IBL，金属件反光偏暗；M3 随场景接 `ReflectionProbe` |
| 头发的两遍（正面 + 背面）透明绘制 | 引擎是单遍 alpha 测试；吕布左侧的长翎短一截，M8 评估 |
| Unity 的 OneBone 质量档 | 引擎固定 4 骨混合，对应 Unity 的 FourBones 档；低端档的对齐未做 |

## 核对 Unity 时的注意点（M1 实测）

- jws2 的活动质量等级是 Fastest，**每顶点只用 1 根骨头**；`SkinnedMeshRenderer.BakeMesh` 也只取第一骨，不能当蒙皮基准。参考图一律在 FourBones 等级下抓。
- Unity 的「网格压缩 = Low」会把权重量化；引擎原样读取 FBX。两侧权重不同是 Unity 改的，不是导入错误。
- 引擎截图前要对每个材质的每个贴图槽 `EnsureLoaded()`，否则贴图被画成白色蒙版。

## 核对 Unity 时的注意点（M2 实测）

- **Unity 编辑模式下渲染蒙皮网格会滞后**：采样后立刻渲染，画出来的可能是上一次的蒙皮。渲染前把每个
  `SkinnedMeshRenderer` 关一下再开；并用「采样关节的投影是否落在身体上」验证。
- **Unity 不按存储的关键帧播放曲线**：关键帧密的曲线逐帧求值、帧间线性，其余才是 Hermite。引擎侧由
  `SanguoClipBuilder` 按同一规则转换。
- 采样（`AnimationMode.SampleAnimationClip`）会把角色放到 `RootT` 的绝对位置；运行时是按增量移动。参考帧要减去
  起点偏移，相机跟着角色走。
- 编辑模式下 `Animator` 的用法与播放模式不同：先 `SetControllerAsset`，再 `HasState` / `CrossFade`；`Update(dt)`
  只推进姿态，根运动要另外施加。


