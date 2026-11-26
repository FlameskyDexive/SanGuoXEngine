# SanGuoXEngine：jws2 桃园 → XEngine 移植计划

- 日期：2026-11-26
- 状态：**已批准，执行中**（进度以 `docs/PORT-STATUS.md` 为准）
- 源项目：`F:\jws2\client`（Unity 2022.3.62f3，三国动作项目，只读）
- 宿主：XEngine 仓库的私有子模块 `SanGuoXEngine/`

## 1. Context（背景与目标）

把 Unity 2022.3.62f3 三国动作项目 `F:\jws2\client` 的「桃园场景 + 刘备/关羽/张飞/赵云 + 吕布 + 黄巾小兵」移植到 XEngine，在本仓库新增**私有子模块 `SanGuoXEngine`**。目的有二：① 用真实商业资产验证 XEngine 的 PBR 渲染能力；② 验证真 3D 动作战斗，做成可玩的「桃园吕布 Boss 战」。原作里三英战吕布在虎牢关（K0），桃园是 1-1 剧情关，所以遭遇战在桃园场景内自行编排。

已确认的决策：玩法 = 桃园吕布 Boss 战（四主角可切换、其余为 AI 队友，吕布为 Boss，小兵按波刷出）；**游戏代码纯 C#、零 Lua**；导出 = AIBridge 脚本（不改 jws2 源码）；仓库 = 私有 `FlameskyDexive/SanGuoXEngine`，由我创建；离线转换工具用 Python。

## 2. 语言纪律：零 Lua，游戏代码纯 C#

- **运行时与编辑器代码 100% C#**（.NET 10 + XEngine，协程用 XTask，遵守引擎分析器规则）。不引入 ToLua / NLua / MoonSharp 等任何 Lua 运行时；仓库不提交 `.lua`；运行时不读 Lua 表，也不读导出的 JSON 中间产物。
- **原 Lua 战斗逻辑不逐行翻译**（约 37 个 Entity 系统、20 Hz 定点帧同步），按「桃园吕布 Boss 战」范围用 C# 重写；单机逻辑用引擎固定步长的 float，不带定点数、KCP、帧同步。
- **原 Lua 数据表只作源数据**：离线 Python 抽取成 JSON 中间产物（`Export/`，不入库）→ C# 编辑器生成器 → XEngine 原生数据资产（`Assets/Data/*.asset`）+ 强类型 C# 定义。
- **Python 只存在于 `Tools/` 的离线转换工具**（导出驱动、Lua 表抽取、贴图通道重排、Wwise→OGG），不进包、不被运行时引用。
- **守卫**：`Assets/Scripts/Tests/NoLuaGuardTests.cs` 扫描 `Assets/` 与 `Packages/manifest.json`，出现 `.lua` 文件或 Lua 运行时包即测试失败；规则同时写进子模块的 `AGENTS.md`。

原 Lua 逻辑 → C#（`Assets/Scripts/`）对应表：

| 原模块（`StreamingAssets/LuaScripts/`） | C# 重写 |
|---|---|
| `Battle/Logic/Level/LevelManager` + `LevelLayoutData1011` | `Battle/BattleStage.cs`（出生点、波次、胜负） |
| `Entity/System/{Move,Drive,Rotate,Speed}` | `Combat/BattleMotor.cs` |
| `SkillBaseSystem / SkillActionSystem / SkillTimeSystem` + `SkillData.SkillEvent_Table` | `Combat/SkillCast.cs`、`Combat/SkillTimeline.cs` |
| `SkillMissileSystem / MissileManager` + `MissileData` | `Combat/HitShapes.cs` |
| `ColliderManager` + `LuaShoveManager` | `Combat/HitRegistry.cs`、`Combat/Shove.cs` |
| `StateSystem / AnimSystem` + `ModelSystem.cs`（原为 C#） | `Animation/SanguoAnimator.cs`、`Combat/ActorStateMachine.cs` |
| `AttrSystem` + `MonsterValueData` | `Combat/Health.cs` |
| `BuffManager`（仅霸体 / 减伤 / 无敌帧） | `Combat/Buffs.cs` |
| `Logic/AI/Decision_*` + `FuncNode_*` | `AI/SoldierBrain.cs`、`AI/LvBuBrain.cs`、`AI/AllyBrain.cs` |
| `Modules/Sound/Sound_Controller` + `ExpSoundSystem` | `Audio/SanguoAudio.cs` |
| `Battle/ExpressionScripts/Camera/*`（原为 C#） | `Camera/BattleCamera.cs` |

## 3. 决定做法的硬事实（均已只读核实）

**jws2**

- 桃园运行时场景 `client/Assets/Scenes/GameScenes/C-ZhuoXian_TaoYuan.unity`（10 MB）。静态件来自 `Assets/GameLevels/Raw/Fbx/*_lod{1,2}.FBX`，LODGroup ≥10 组；只有 1 盏方向光（色 (1,0.891,0.825)、强度 1.6、前向 ≈ (0.17,−0.64,0.75)）；天空是两张 HDR 全景图（黄昏 / 夜雨）+ 6 套天空/LUT 预设（`*_paramConfig.asset`、`Lut0..5.png`，LUT 为条带格式）；同目录没有光照贴图（是否在别处加载由 M0 确认）。`TerrainInfo/*.bytes` 是可行走网格，不是高度图。
- 角色 `Assets/Actor/Hero/Hero_{ZhaoYun,GuanYu,LiuBei,ZhangFei,LvBu}/`，战斗材质在 `BattleMat/`。**Generic Biped 骨骼**，每个材质槽一个 SkinnedMeshRenderer，武器是独立 FBX。动作是拆分好的 `Actor/AnimationSplit/Animation_L/Hero_<拼音>/*.anim`（每人约 175 条，战斗用约 45 条，其余是 `NPC_Plot_*`）。控制器是「每个状态一个 Trigger」的扁平结构，运行时直接 `animator.Play`，第 1 层做受击混合 → 移植为「状态列表 + 代码驱动」，不需要搬过渡。
- 着色器 `F:\jws2\Shaders\RayShader\S_Standard_*`：`_M_Alpha` 打包 = R 金属度 / G AO / B 自发光遮罩 / A 光滑度；最小 GGX + Lambert；附加边缘光、受击闪白、头发各向异性、假皮肤。
- 特效 PsEffect：技能 prefab = `PsRoot(actionList)` → `PsActionEffect` → 粒子组 prefab（`PsEffect/Projects/1_Instance/Group*/PT_*.prefab`，标准 Unity ParticleSystem 的 YAML 文本）；共享贴图/网格在 `Assets/Effects/Common/`。
- 音频 Wwise 2019.2.14：bank 在 `client/trunk_WwiseProject/GeneratedSoundBanks/Windows/`，主角与 VO 在其下 `Chinese/`（`zhaoyun/guanyu/liubei/zhangfei/lvbu` 及 `_VO`），公共的在 `Windows/`（`Hits`、`Footstep`、`Monster`、`MUSIC`）；`SoundbanksInfo.xml` 逐事件列出内嵌媒体 ID；`.bnk` 是 DIDX/DATA 分块；ID = 小写名字的 **FNV-1**。触发源：`StreamingAssets/Config/effect-sound.cfg`（特效→事件）、`LuaScripts/Data/AnimSound.lua`（动作→事件）、`LevelInfoData.LevelSound`（音乐）。
- AIBridge：`client/.aibridge/cli/AIBridgeCLI.exe code execute --file <.aibridge/code/*.csx> --timeout <ms>`（实验性、单飞，可访问工程程序集）；驱动范例 `F:\jws2\tools\offline_test\lib\bridge.py`。音频解析雏形 `F:\jws2\tools\hero_audit\{audio_resolve,convert_media}.py`（只扫一层目录，漏了 `Chinese/`），自带 `bin\vgmstream-cli.exe`。本机没有 OGG 编码器。

**XEngine**

- 已有：`Default/Standard`（`_MainTex`、`_NormalTex`、`_SurfaceTex`、`_EmissionTex`）、`StandardAnisotropic`、`Particle`、`Refraction`；级联阴影、双探针 IBL、`ReflectionProbe`（Baked/Realtime）、RGBM 光照贴图 + Photonic 烘焙；Bloom / ACES / LUT 条带；CPU 粒子全模块 + Trails；`AnimationClip` 可编程生成；Cinemachine；miniaudio 支持 OGG + AudioMixer。
- 缺口：无 LODGroup；无立方体贴图导入器；光照贴图不消费方向图/阴影遮罩；无径向模糊/闪白后处理；Cinemachine 无 Impulse；不读 Unity `.anim`/`.unity`；`Project.Create` 拒绝已存在的目录。
- 本机：没有 `gh`；有 `winget`、`git-lfs`、`py -3`、Unity 2022.3.62f3。

## 4. 总体路线

- **两条线分开提交**。移植内容全部在子模块（非引擎）；移植中暴露的引擎缺口在引擎 `main` 上单独成节点（测试 + 截图 + commit + push 之后才继续）。
- **导出**：AIBridge `.csx`（C# 脚本）在 jws2 编辑器内导出「已解析」的数据为 JSON（场景层级、角色骨架与材质引用、动画曲线、Unity 参考截图）。粒子组 prefab、材质属性直接离线读 YAML；Lua 表离线读成 JSON 中间产物（仅作源数据）。导出脚本与清单放移植仓库；只在 jws2 的 `.aibridge/code/`（其自身缓存目录）落脚本，不进 `Assets/`。
- **FBX-first**（沿用 Zonezero 已验证原则）：只拷贝原始 FBX/贴图，XEngine 原生资产由 C# 生成器产出。
- **贴图换算**：`_MainTex`→`_MainTex`；`_BumpMap`→`_NormalTex`；`_M_Alpha`→`_SurfaceTex`，其中 **R = 1−AO(G)、G = 1−光滑度(A)、B = 金属度(R)**（`StandardSurface.glsl` 里 `ao = 1 − R`，G 是感知粗糙度）；自发光遮罩 × `_EmissionColor` → `_EmissionTex`，强度取 `_EmissionScale`。
- 每个里程碑：Unity 与 XEngine 同机位并排截图 + 自动化测试 → 证据入库 → commit + push。

目标结构：

```
SanGuoXEngine/            # 子模块
  SanGuoXEngine.xengine  Directory.Build.props  Packages/manifest.json  .mcp.json  AGENTS.md  .gitignore
  Assets/{Scenes,Heroes,Soldiers,Environment,Effects,Audio,Data,Settings,Scripts,Editor}
  Tools/{unity_export/*.csx + run_export.py, sanguo_import/*.py, wwise2ogg/}   # 仅 Python 离线工具
  docs/{plans,evidence}/m0..m8   # 每个证据分 unity/ 与 xengine/ 两侧
  Export/                  # JSON 中间产物，gitignore
```

依赖包：`com.xengine.inputsystem`、`cinemachine`、`unityimporter`、`rpgvfx`（特效着色器的映射目标）；`gpu-animation` 选装（小兵群体）。不引入任何 Lua 相关包。

## 5. 里程碑（逐节点提交，标题 `[feat]sanguo port function + …`）

**M0 脚手架与导出探针**

1. `winget install GitHub.cli` → 你完成一次 `gh auth login` → `gh repo create FlameskyDexive/SanGuoXEngine --private`。
2. 目录尚不存在时用 `xengine-cli game-init --create-project`（内部 `Project.Create`，`XEngine.Editor/Projects/Project.cs`）建工程，`git init -b main` 并推送；父仓库 `git submodule add` 后只暂存 `SanGuoXEngine` 与 `.gitmodules` 提交（父工作区有大量无关改动，不碰）。
3. `harness-install`、manifest、`.gitignore`（含 `Export/`、`Tools/bin/`）、`AGENTS.md`（写明：零 Lua、Python 仅限 `Tools/`、jws2 只读、导出命令、验收流程）；本计划落盘 `docs/plans/`，建 `docs/PORT-STATUS.md`；落地 `NoLuaGuardTests`。
4. 导出探针 `Tools/unity_export/probe.csx`：只读打开运行时场景，输出光照贴图数量、渲染器 / LODGroup / 预制体实例统计、灯光、一个角色 prefab、一个材质、一条动画的曲线绑定；同时验证能否引用 `UnityEditor`、写文件、超时行为。**闸门**：失败两次则回退 Unity 批处理导出器（需往 jws2 加 Editor 脚本，届时先征得你同意）。

- 验收：子模块可被父仓库拉起；守卫测试通过；`docs/evidence/m0/probe.md`。

**M1 角色模型与 PBR 材质**

- 导出：5 人的模型/武器 FBX、SMR→网格名/骨骼/材质槽、材质属性、贴图路径与导入设置。
- `Tools/sanguo_import/`：拷贝 FBX/TGA，写 `unity-guid-map.json`（照 `ZonezeroTestProject/Packages/com.xengine.zonezero/Editor/ZonezeroAssetCopier.cs` 的 `CopyUnits` 与 `StableGuid`）；用 Pillow + numpy 做上面的通道重排。
- `Assets/Editor/Sanguo/SanguoMaterialGenerator.cs`、`SanguoHeroPrefabBuilder.cs`（照同目录 `ZonezeroNativeAssets.cs` 的 `BuildMaterialFromUnityYaml` / `GenerateCharacterPrefab` / `GenerateModelPrefab` / `WriteNativeAsset`）：身体、布料、武器用 `Default/Standard`，头发用 `Default/StandardAnisotropic` + 裁切。
- 验收：5 人站在中性光照台上；换算公式单测；与 Unity 参考图并排。

**M2 动画**

- 导出战斗剪辑（idle / run / NormalAttack_1..4 / SkillAttack_* / SA_* / dodge / RunAttack / hurt / hitdown / knockback / fly / standup / die / win / getin / getout）的曲线、循环标记、事件、帧率。
- `SanguoClipBuilder`：JSON → `AnimationClip`（`XEngine.Runtime/Resources/AnimationClip.cs` 的 `AnimBone` 曲线、`Wrap`、`Events`）→ 原生资产；`SanguoAnimator`（状态列表、两层、`Play` / `InsertHurtAnim` / 变速）。
- 已知坑直接规避：动画专用 FBX 要 `avatarDefinition:1`；`AssetRef.Res` 异步未就绪时先 `EnsureLoaded`；prefab 实例要 `Scene.Current.Add` 才会渲染。
- 验收：逐剪辑在固定时间点与 Unity 采样姿态做骨骼旋转数值比对（阈值写入证据）+ 关键姿态截图。

**M3 桃园场景**

- 导出：完整解析后的层级（变换、激活、静态标记）、MeshFilter→FBX 网格、渲染器材质、LODGroup、方向光、天空/LUT 预设、碰撞盒、出生点。
- 生成：`SanguoSceneMaterialGenerator`、`SanguoSceneBuilder`（写原生 `.scene`）。地面图集材质按实测结构决定走引擎地形还是移植仓库自写着色器；天空 = 全景 HDR 着色器 + `ReflectionProbe`；Volume = ACES + LUT 条带 + Bloom。
- 引擎节点（`main`）：**LODGroup 组件**（距离分级、末级剔除），含测试与截图证据，提交标题 `[feat]lod group function + …`；超预算则先在移植仓库内用 C# 临时脚本顶上。
- 验收：全景 / 中景 / 近景三组并排截图；`runtime_stats` 记录 draw call、三角形、帧时间。

**M4 战斗内核（C# 重写，按第 2 节对应表）**

- 依 Zonezero 战斗骨架「抄结构不抄内容」：`.../com.xengine.zonezero/Runtime/` 下的 `Player/`、`Combat/`（`CombatMotor`、`AllyCombatAI`、`BattleFollowCamera`）。
- 玩家控制一人、1–4 键切换、其余三人 AI 队友；第三人称跟随相机 + 锁定吕布 + 手写震屏；普攻连段（取消窗口取 `SkillSwitch_Table`）；技能时间轴（`SkillEvent_Table` 毫秒事件）；扇形 / 圆形 / 盒形判定（用距离 + 夹角的命中注册表，不依赖物理查询）；伤害数字与血条（`hudmesh`）；顿帧、受击闪白、受击 / 倒地 / 浮空。
- 数据管线：`Tools/sanguo_import/extract_tables.py`（解析器改编自 `F:\jws2\tools\hero_audit\lua_parse.py`）把 `SkillData` / `MissileData` / `MonsterData` / `MonsterModelData` / `MonsterValueData` / `AnimSound` / `LevelLayoutData1011` 抽成 `Export/data/*.json`；`SanguoDataGenerator`（Editor 菜单）转成 `Assets/Data/*.asset`，强类型定义在 `Assets/Scripts/Data/`。
- 验收：`dotnet test`（连段状态机、技能时间轴→帧、判定几何、数据资产完整性）+ 可玩录屏帧。

**M5 特效**

- 离线转换粒子组 prefab：`UnityParticleReader.Read`（`Packages/com.xengine.unityimporter/Editor/`，公开 API）→ `SanguoParticleBuilder`（映射逻辑参照 `Tools/HovlConvert/HovlPrefabConverter.cs` 的 `ApplySystem`，该方法是私有的，需移植过来）；着色器映射表（jws2 `Effects/*` → `Default/Particle` / `Default/Refraction` / rpgvfx 的 Distortion、DissolveNoise、AddTrail；刀光用移植仓库自写着色器）。
- `PsRoot` 动作列表 → `SanguoEffect`（特效挂槽、延迟、缩放；震屏、FOV、闪白、暗屏；径向模糊先降级）。
- 接入普攻 / 技能 / 命中 / 受击。
- 验收：每个技能在相同归一化时间的 Unity / XEngine 帧条并排；overdraw 与 draw call 记录。

**M6 音频（独立，可与 M1–M5 并行）**

- `Tools/wwise2ogg/`（`py -3`，标准库）：读 `SoundbanksInfo.xml` + `.wwu` + `.bnk`（DIDX/DATA）+ 散 `.wem`（含 `Chinese/`）→ `vgmstream-cli` 解码（复用 jws2 自带的那份）→ `ffmpeg -c:a libvorbis` 编码（下载固定版本的 LGPL 构建，M6 第一步先确认该构建带 Vorbis 编码器）→ `Assets/Audio/Sfx/<bank>/<event>[.<switch>]/<mediaId>.ogg`、`Assets/Audio/Music/*.ogg`，外加 `Export/audio/wwise-events.json`（音量偏移、随机音高、循环、裁剪、衰减半径、总线、开关变体）与 `wwise-report.md`（未解析事件）。`effect-sound.cfg`、`AnimSound.lua` 也由工具离线读入映射表；再由 `SanguoDataGenerator` 转成原生 `AudioEventTable`，运行时只认原生资产。
- 范围：五人 SFX/VO、`Hits`、`Footstep`、`Monster`、战斗音乐主题曲（交互式音乐层级不移植，取固定曲目）。
- 运行时：`Audio/SanguoAudio.cs`（`PostEvent` / `SetSwitch` / `StopAll` / `SetBusVolume`，AudioSource 池化，总线映射到 AudioMixer 组）。
- 验收：pytest（FNV-1 向量、DIDX 解析、范围内事件映射 100%）；转码后时长与源一致（±20 ms）；游戏内触发日志。

**M7 吕布、小兵与胜负流程**

- 吕布用基础形态（普攻、两技能、无双）；Boss 状态机（接近、按距离与冷却选招、半血二阶段、霸体）。小兵用黄军刀兵 / 刀盾兵（骨骼动画），按关卡出生点分波刷出；同屏大规模群体用 `gpu-animation`（冲刺目标）。
- 胜负结算与重开；10 分钟连续运行无报错；20 / 50 小兵同屏的性能数据。

**M8 打磨、性能与出包**

- 移植仓库自写着色器补皮肤 SSS 与边缘光 / 受击闪白；可选：用引擎 Photonic 烘焙桃园光照贴图。
- 性能预算：1080p D3D12 满载帧时间 p95 ≤ 16.6 ms，Vulkan 抽测；门槛写进 `.xengine/game.json`。
- `xengine-cli build` + `game-capture` + `game-verify` 出包与证据；README、`PORT-STATUS.md`、引擎缺口清单；父仓库更新 gitlink。

## 6. 验证方式

- 单元测试：`dotnet test SanGuoXEngine.Tests.csproj`（`Assets/Scripts/Tests/`）、`py -3 -m pytest Tools`。
- 实时编辑器：`XEngine.Editor.exe --serve --project D:\Engine\XEngine\SanGuoXEngine` + MCP `runtime_eval` / `runtime_screenshot` / `runtime_stats`；编辑器被占用时用私有输出目录 `bin/M47/`。
- 无头：`xengine-cli run-headless --input-script`、`game-capture`。
- 画面对照：AIBridge 探针按固定机位渲染 Unity 参考图（机位参数写入 `shots.json`），XEngine 复现同机位；缩略图均值比对 + 目检。
- **PBR 验证清单**：① 通道换算单测 + 在用材质抽检像素；② 金属武器 / 铠甲的 IBL 反射生效；③ 级联阴影近景无断裂；④ 黄昏 / 夜雨两套天空六套预设的对照截图；⑤ 可选的烘焙光照贴图前后对照；⑥ 性能预算。

## 7. 风险与应对

- AIBridge `code execute` 是实验性功能 → M0 闸门 + 批处理回退；导出期间需独占 jws2 的 Unity 编辑器。
- `gh` 未安装且未认证 → M0 第 1 步需要你授权一次；不行就你手建空的私有仓库。
- 仓库体积（TGA 未压缩、粒子贴图多）→ 只拷在用资源、单文件 < 100 MB、目标 < 1 GB、不用 LFS；超限则在导入时无损转 PNG。
- 没有光照贴图会与 Unity 观感有差 → M0 确认后决定「实时阴影 + IBL」还是「引擎烘焙」。
- 头发 / 皮肤着色器、依赖自定义顶点流的粒子着色器只能近似 → 逐项目检，差异记入证据。
- Lua 逻辑重写会丢掉原帧同步与大部分 Buff / 技能分支 → 只保证 Demo 用到的招式与反馈，清单写进 `PORT-STATUS.md`。
- jws2 是商业项目资产 → 仓库保持私有，不发包、不进公开发布。

## 8. 需要你配合

1. M0 时完成一次 `gh auth login`（浏览器授权）。
2. 导出期间不要同时占用 jws2 的 Unity 编辑器。

## 9. M0 实测对计划的修订（2026-11-26）

以下事实由 `evidence/m0/probe.md` 的探针实测得出，与第 3 节的离线估算不一致时以本节为准。

| 第 3 节原记录 | 实测 | 影响 |
|---|---|---|
| LODGroup ≥10 组 | **1378 组** | 引擎 LODGroup 节点是硬需求 |
| 同目录没有光照贴图，是否在别处加载待确认 | **有**：2 张 2048 图集，各带方向图与阴影遮罩，2083 个渲染器在用；第二套 UV 来自 FBX 源文件 | M3 直接用 Unity 的烘焙结果（导出 `lightmapIndex` / `lightmapScaleOffset`，颜色图转 RGBM）；引擎不消费的方向图 / 阴影遮罩登记为缺口；Photonic 自烘焙降为可选对照 |
| 拆分剪辑每人约 175 条 | 文件名带角色前缀：`Hero_LvBu_<动作>.anim`；吕布控制器 38 个状态、39 条过渡，全部是 `.anim`，无 BlendTree | M2 按控制器状态枚举剪辑 |
| 根运动（未涉及） | 以 `RootT` / `RootQ` 曲线存储在 Animator 上 | M2 的剪辑生成器要映射根运动 |
| （未涉及） | 每个角色材质自带反射立方体贴图 `_Unity_SpecCube` | M1 先用场景 IBL 近似 |
| （未涉及） | 场景内 125 个粒子系统、109 个公告板、21 个蒙皮的场景小兵 | 进入 M3 / M5 |

M0 中另修复了一个引擎缺陷（带 xUnit 测试的工程打不开），在引擎 `main` 上以 `cfab4988a` 提交，
所以本工程的测试仍按原计划放在默认 `*.Tests` 单元里。

## 10. M1 实测对计划的修订（2026-11-26）

以下事实由 `evidence/m1/heroes.md` 的对比与审计得出，与第 3、5 节不一致时以本节为准。

| 原计划 / 原记录 | 实测 | 影响 |
|---|---|---|
| `_M_Alpha`：R 金属度 / G AO / B 自发光 / A 光滑度（照注释） | 通道顺序与注释一致，以 `CustomedHeroMetallicSetup` 的代码为准 | 无 |
| 引擎 `Default/Standard` 的 `_SurfaceTex`：R 遮蔽量（着色器里 `ao = 1 − R`）、G 感知粗糙度、B 金属度 | 同上 | 重排公式：R = 1 − AO、G = 1 − 光滑度、B = 金属度 |
| 贴图 TGA 转 PNG 以控制体积 | 78 张源贴图 71.9 MB → PNG 28.5 MB，连同派生图与 FBX 共 44 MB | 不需要 LFS，也不需要再压 |
| 材质由 `SanguoMaterialGenerator` + `SanguoHeroPrefabBuilder` 两个类生成 | 合并为 `SanguoHeroGenerator`（材质 + prefab）；截图工具 `SanguoCaptureStudio` 放在同一个 Editor 程序集 | 名称以代码为准 |
| 头发用 `Default/StandardAnisotropic` + 裁切 | 该着色器在引擎里不做 GPU 蒙皮（`Unlit`、`StandardTransparent` 同） | 引擎缺陷，已在引擎 `main` 以 `fcf643108` 修复 |
| （未涉及） | jws2 的活动质量等级 Fastest 只用 1 根骨头蒙皮；`BakeMesh` 也只取第一骨 | Unity 参考图统一在 FourBones 等级下抓取；**`BakeMesh` 不能当作蒙皮基准** |
| （未涉及） | Unity 的「网格压缩 = Low」会把权重量化（落在 1/31 网格上）；引擎原样读取 FBX | 引擎与 Unity 的权重差异来自 Unity，静止姿态下影响 ≤ 1.86 mm |
| （未涉及） | XEngine 的方向光沿节点 −Z 照射（Unity 是 +Z）；相机目标回读为线性字节，Vulkan 自下而上、D3D12 自上而下；`AssetRef.Res` 异步，材质的贴图要逐个 `EnsureLoaded` | 写进 `SanguoCaptureStudio`，后续所有截图沿用 |
| PBR 验证清单 ②③④（金属 IBL、级联阴影、天空预设）在 M1 做 | 这三项需要场景、探针和天空 | 移到 M3；M1 只完成 ①（通道换算） |
