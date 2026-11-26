# M4 收尾与后续里程碑交接（给 Codex 的执行手册）

写于 2026-11-27。前置文档（必读，按序）：
1. `docs/plans/2026-11-26-sanguo-taoyuan-port-plan.md` —— 总计划（里程碑、对应表、验收方式）
2. `docs/PORT-STATUS.md` —— 各里程碑状态表（已更新到本文档对应状态）
3. `docs/evidence/m4/battle.md` —— M4 验收证据（含运行日志、stats、已知缺口表）
4. 父仓库 `AGENTS.md` 与本仓库 `AGENTS.md` —— 硬性纪律（分支/提交格式/零 Lua/jws2 只读）

## 0. 当前状态（一句话 + 提交链）

桃园场景已完整移植渲染（M3），战斗内核已落地并在 play 模式实测（234 怪追击/接敌/命中，日志与 stats 为证）。

- 子模块 `SanGuoXEngine` @ `2613a8c`（已推送 github.com/FlameskyDexive/SanGuoXEngine，私有）
- 父仓库 `XE` @ gitlink `c7ae4b23a` 后又跟到 `da0b80818` 之后（父 main 已推送）
- 引擎 `XE` main 上的相关节点：`cfab4988a`（NuGet）、`fcf643108`（GPU 蒙皮）、`f0f68d5a3`（Prepass 法线蒙皮）、`fa2cf7d08`（LODGroup）、`962df7285`（**描述符池规模化**，本次）
- 测试基线：Python `py -3 -m pytest Tools -q` = **108 绿**；C# `dotnet test SanGuoXEngine.Tests.csproj` = **45 绿**（其中含数据资产/战斗内核/技能规则三组）

## 1. 下一步（按序执行，每步一节点：代码+测试+截图证据→commit→push→父仓库 gitlink）

### 步骤 A：战斗帧取景修复（M4-5 收尾）

现状：play 模式战斗循环活着（日志 `[SanguoBattle] hero hit … (hp 1091)` 等），但验收帧里**英雄/怪物没入画**。原因排查方向：

1. `SanguoBattleDriver.FollowCamera`（`Assets/Scripts/Battle/SanguoBattleDriver.cs`）：目标位 `(leader.X, 192, leader.Y - 12)`，按 `deltaTime * 6f` 插值——检查是否收敛、朝向是否看向英雄（相机 euler 固定 (26,180,0)，即 +Z 朝向，与 leader.Position + (0,12,-12) 的"在英雄身后"假设是否一致）。
2. 英雄 prefab 实例位置 = 内核 `heroes[i].Position`（出生点 (111.5,777.8) 附近，y=180）。怪物占位立方体出生在 (111,704) 一带。
3. 快速验证手段：进入 play 后用 `runtime_eval` 读 4 个英雄与首怪的 `Transform.Position`，与内核坐标对照。

验收：play 中一张 960×540 帧（`CaptureSceneFrame` 离线路径）里能看到英雄模型与怪物占位体；截图入 `docs/evidence/m4/`。

### 步骤 B：怪物资产导入 + 数值驱动伤害（原 M7 前半）

- 黄巾兵模型在 jws2：`Assets/Actor/Monster/Monster_YellowArmy/`（怪物 101103/101104/101111/101113，ModelID 22056/22082/22035/22079，见 `Export/data/monsters.json`）。用与 M1 英雄相同的导入路径（`import_heroes.py` 的 FBX/贴图拷贝 + `SanguoHeroGenerator` 式 prefab 生成）做 `import_monsters.py`。
- 当前占位：驱动器用内建 Cube（guid `0331af3b-03ea-d9e2-7e45-ca749920713c`，Unlit 材质染色）。
- 伤害数值：`SanguoBattleDriver.MonsterAttackOf` 现在是常量 20（英雄防 25 → 每击 1 点）。改为查 `SanguoMonsterValues`（资产 `Assets/Data/SanguoMonsterValues.asset`，`GroupID == 怪物ID`）的 `Atk`，英雄侧同理。
- 吕布不在关卡 1011（原作在虎牢关）；吕布 Boss 与小兵波次编排按总计划 §5 M7 自行合成。

### 步骤 C：HUD（血条/伤害数字）

- 用 `@inxep/hudmesh` 包（已装 0.1.0）。**注意**：该包对当前引擎 API 有编译漂移记录（UIFontSystem/Register/SetOwner）——先确认 PackageCache 里它是否编译通过（M4-4 期间日志显示 `XEngine.Hudmesh.Runtime compiled successfully`，说明当前引擎已兼容；若有回归再处理）。
- 头顶血条：世界坐标 → 屏幕投影（`Camera.WorldToScreen` 类 API，若无则手写 VP 投影），hudmesh 文本/条绘制。
- 伤害数字：命中时在 `DmgNumHeight`（MonsterModelData 列，已进 `SanguoMonsterModels.asset`）高度弹数字。

### 步骤 D：M5 特效 → M6 音频 → M8 打磨（按总计划）

- M5：`Packages/com.xengine.unityimporter` 的 `UnityParticleReader.Read` → `SanguoParticleBuilder`；42 个 Effects 材质映射（当前跳过，渲染器不画）。
- M6：Wwise→OGG（`Tools/wwise2ogg`，vgmstream-cli 在 `F:\jws2\tools\hero_audit\bin\`，本机无 OGG 编码器需下载 LGPL ffmpeg）；技能事件 EventType=5（音效）在 `SanguoSkillRules`/驱动器里落地。
- M8：ACES/LUT/Bloom 后期链（修"远景白化"——已记录：无 tone map + 光照贴图 EXR 8-bit 量化 + Hemisphere 环境光过曝）。

## 2. 环境与命令（全部实测有效）

```bash
# 编辑器二进制（唯一有效路径！bin/Debug 下是 9 月旧残留，用错 = 旧代码跑一切）
E=/d/Engine/XEngine/Build/Editor/Debug/net10.0/XEngine.Editor.exe
# 离线资产刷新（编辑器跑完即退）
"$E" --refresh-assets --project "D:/Engine/XEngine/SanGuoXEngine"
# 构建（改了引擎/编辑器源码后必须）
dotnet build /d/Engine/XEngine/XEngine.Editor/XEngine.Editor.csproj -c Debug   # 输出到 Build/Editor/Debug
# 测试
cd /d/Engine/XEngine/SanGuoXEngine
py -3 -m pytest Tools -q
dotnet test SanGuoXEngine.Tests.csproj --nologo -v q      # 新增 .cs 后需先跑一次 refresh 让编辑器重生成 csproj
# 实时编辑器驱动（MCP over stdio；启动新编辑器，跑完即退；--keep 则保留）
py -3 Tools/xengine_live/mcp_live.py [--graphics vulkan|d3d12] --wait-compile 300 \
    --eval-file Tools/xengine_live/evals/<x>.cs --out <result.json>
# 多步编排（play 进入→等待→采集→退出必须同一个 cmds 文件；跨调用编辑器即退）
py -3 Tools/xengine_live/mcp_live.py --cmds Export/scene/battle-capture-cmds.json --out r.json
# jws2 数据再抽取
py -3 Tools/sanguo_import/extract_tables.py
```

evals 目录（`Tools/xengine_live/evals/`）现有可用脚本：`generate_scene / generate_battle_scene / generate_data / generate_scene_materials / generate_clips / capture_poses / open_scene / capture_frames / enter_play / set_pose / capture_battle_play` 等。**runtime_eval 里工程类型必须走反射**（`assembly.GetType("SanGuoXEngine.Editor.SanguoSceneBuilder")` 模式，见各脚本），不能直接写类型名。

## 3. 已踩过的坑（每条都花过真实调试时间，务必先读）

### 资产/编辑器
- **`.meta` 目录**：Assets 下任何 `*.meta` 若是**目录**而非文件，整个资产库每次 open/refresh 都报 `Access to the path is denied`（Project.Open 失败 → 编辑器退回启动器 → `Project.Current == null` → 所有 eval NRE）。曾因 `Assets/Scripts/Data.meta` 被 mkdir 成目录而全工程打不开。修法：删目录、写正式 DefaultImporter meta 文件。
- **编辑器二进制**：`Build/Editor/Debug/net10.0/`（Directory.Build.props 重定向）；`XEngine.Editor/bin/Debug/net10.0/` 是 2026-09 的旧残留。用错路径 = 三个月前的代码跑资产导入/游戏逻辑。
- **WriteNative 的 guid 只写进 .meta，不回填资产对象的 AssetID** —— 序列化场景/资产里引用别的资产时必须显式持有 guid 变量（天空材质、内建网格都栽过）。
- **新建文件首个 refresh 可能不导入**（hash 缓存）：touch 文件后重刷。
- **引擎默认 FarClipPlane = 100 m**。桃园场景跨 ~500 m，mid/panorama 主体全被远裁——症状曾伪装成"LOD 剔除/天空盒照片"（天空 HDR 全景是实拍照片，地形被裁后其地平带铺满画面）。战斗相机/采集相机都要 `FarClipPlane = 4000`。
- **layer31 = 碰撞代理层**：65 个 Unity 内建 Cube 在此层，Unity 相机 cullingMask 剔除；引擎相机需 `CullingMask.RemoveLayer(31)`，否则画面出现实心大立方体。
- 内建 Quad/Cube/Sphere 引擎没有现成库——场景生成器已程序化写出 `Assets/Environment/Builtin/*.mesh`（guid 用 stable 方案）。没有 `Mesh.CreatePrimitive`。
- 光照贴图/天空 HDR 已在 `Assets/Scene/Lightmaps|Sky/`（线性导入 meta 已写）；dir/shadowmask 已拷贝但引擎尚未消费方向图/阴影遮罩（引擎缺口清单）。

### play 模式/采集
- **无可见 GameView = 编辑器空闲节流**（`EditorApplication.SetIdleTickRate`：非渲染态 15Hz 甚至更低）→ play 循环几乎不走（战斗时间 2s/17s 墙钟）。修法：进 play 前反射调 `GameViewPanel.Open()`（private static；`enter_play.cs` 已含）。开 GameView 后 `runtime_screenshot` 也可用（走 `GameViewPanel.ActiveInstance`）。
- `EditorApplication.EnterPlayMode/ExitPlayMode` 是**实例**方法（`EditorApplication.Instance`，命名空间 XEngine.Editor.Core）。
- 离线 `camera.Render()` 不带 `RenderStats.BeginFrame/EndFrame`——要 stats 自己括号（`capture_frames.cs` 有样例）。play 模式下 RenderStats.Last 天然有效。
- **Vulkan 单帧 ~14k draws 会耗尽描述符池**——已在引擎 `962df7285` 把每帧溢出池上限 64→256（≈1M sets）。D3D12 RTV 池 16→256 页同批修复。若再现池增长告警日志且持续增长，说明有真泄漏（每帧 RT 未释放）而非规模问题。
- 采集用 `SanguoCaptureStudio.CaptureSceneFrame`（反射调；内部 MagickImage 写 PNG，线性回读 → sRGB 编码；Vulkan 读回底行先翻转、D3D12 不翻）。
- 采集前所有 AssetRef 必须 `EnsureLoaded()`（网格、每个材质、天空贴图、光照贴图页），否则白模/黑屏。
- mcp_live 不带 `--keep` 跑完即杀编辑器；**残留编辑器会占 `Temp/EditorProject.lock`（FileShare.None）导致下一次启动工程打不开**——启动前先杀干净 `XEngine.Editor` 进程。多步流程（进 play→等待→截图→退出）必须写在**同一个** `--cmds` JSON 里。
- `--graphics vulkan` 与 `d3d12` 行为差异：离线整帧大场景 Vulkan 曾耗尽描述符（已修）；D3D12 的离线采集曾在 play 模式 120s 超时——超时就换 Vulkan 或减帧。
- `runtime_eval` 的 C# 是独立 submission：工程类型只能反射；`return` 返回值（不是 `returnValue=`）；包裹 try/catch 才能拿到 TargetInvocationException 的内层堆栈。

### 代码/仓库
- **绝不往含反斜杠的文本用 bash heredoc**（`\\x00` 会变成真换行/真字节）——用 Write/Edit 工具；别用 `str.index` 按猜测的边界切源文件。
- 引擎 `MonoBehaviour` 每帧钩子是 `Update()/LateUpdate()/FixedUpdate()`（不是 OnUpdate）；游戏逻辑受 ShouldTickGameplay 门控（play 模式或 [ExecuteAlways]）。
- 编辑器程序集**不引用**游戏程序集——编辑器侧要把游戏组件加进场景时用 `Type.GetType("SanGuoXEngine.Battle.SanguoBattleDriver, SanGuoXEngine.Game")` + `AddComponent(Type)`（`GenerateBattleScene` 有样例）。
- jws2 的 SkillData 是"主文件 = schema(propertyTable 61 列) + 索引(setmetatable {ID,PageIndex,LineIndex})，行数据在 -pageN.lua"；`lua_parse.py` 已处理（含数组-of-具名表保持数组、空 `{}` = 空列表）。Lua 表绝不入库、运行时绝不读（NoLuaGuardTests 把关）。
- 连段键：SwitchPutIn **1** = 普攻链（内核常量 `AttackChainKey`），8/9 = 闪避等；窗口 [SwitchMinPlayTime, +max(1,SwitchDuration)]。技能事件 EventType=1 的 EventIntParam → CallBulletData.ID → 其 BulletID → MissileData.ID（生成器已做传递闭包）。
- 关卡 1011 怪物：101103/101104/101111/101113 黄巾刀盾兵；101122 张角（黄巾 Boss）；**101171/101172 是友军（camp 2，张飞/刘备 prefab）**；101160/101161/101163/101165 装饰（无数值行）。吕布不在 1011。
- 引擎 `Float2`（XEngine.Vector）与内核自己的 `SanGuoXEngine.Battle.Float2` 是两个类型；内核类型不依赖场景命名空间（纯逻辑可测）。
- 引擎 `Mesh` 无 `CreatePrimitive`；索引是 `uint[]`。
- 父仓库工作区有大量无关脏文件——**只 add 自己的明确路径**，绝不 stash/restore 用户工作区；不强推 main；引擎改动只走引擎 main（`[feat]xxx function + 简介`），子模块提交 `[feat]sanguo port function + 简介`，每节点推完把父仓库 gitlink 单独一提交跟上。
- GitHub 推送偶发 Connection reset/timeout：等 15–20 秒重试即可。

## 4. 本仓库关键文件地图

```
Assets/Scripts/Battle/     SanguoHitShapes / SanguoHealth / SanguoStateMachine(+Motor+Registry+FieldActor)
                           / SanguoBattleStage(+LevelSpawn) / SanguoSkillTimeline(+SkillRules+CastState+Hit)
                           / SanguoActorPresenter / SanguoBattleDriver
Assets/Scripts/Data/       SanguoBattleData.cs（强类型 + 7 张 ScriptableObject 表）
Assets/Scripts/Animation/  SanguoAnimator（M2：Play/InsertHurt/Speed；需 SetControllerAsset）
Assets/Scripts/Tests/      SanguoBattleKernelTests / SanguoSkillTimelineTests / SanguoDataGeneratorTests
                           / SanguoClipBuilderTests / SanguoHeroGeneratorTests / NoLuaGuardTests
Assets/Editor/Sanguo/      SanguoSceneBuilder(场景+战斗场景变体) / SanguoDataGenerator / SanguoSceneGenerator
                           / SanguoHeroGenerator(WriteNative 在此) / SanguoClipBuilder / SanguoCaptureStudio
Assets/Data/               7 张生成资产（SanguoSkills/CallBullets/Missiles/Monsters/MonsterModels/MonsterValues/Level1011）
Assets/Scenes/             TaoYuan.scene(纯场景) / TaoYuanBattle.scene(+驱动器)
Assets/Environment/        SkyPanorama.shader / SkyDusk.mat / Builtin/{Quad,Cube,Sphere}.mesh
Assets/Scene/              Fbx|Textures|Materials|Lightmaps|Sky（M3-2 导入的桃园资产）
Tools/sanguo_import/       lua_parse / extract_tables / import_scene_assets / upgrade_legacy_fbx / …
Tools/unity_export/        AIBridge .csx 导出脚本 + config.json（jws2_client 路径）
Tools/xengine_live/        mcp_live.py + evals/*
Export/                    gitignored 中间产物（scene/、data/、import/）
docs/evidence/m0..m4/      各里程碑证据；docs/PORT-STATUS.md 状态表
```

## 5. 验收纪律（不变）

每节点：实现 + 测试（Python/C#）+ Unity 对照或 play 截图证据 → `docs/evidence/<m>/…` → PORT-STATUS 更新 → 子模块 `[feat]sanguo port function + 简介` commit+push → 父仓库 gitlink 单独提交 `[feat]sanguo port function + …` push。引擎侧改动一律引擎 main `[feat]xxx function + 简介`。jws2 工程只读；仓库零 Lua；商业资产不入公开仓（子模块保持私有）。
