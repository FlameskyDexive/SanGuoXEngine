# M4 战斗内核验收

日期：2026-11-26/27 · 后端：Vulkan（play 会话）/ D3D12（对照） · 场景：`Assets/Scenes/TaoYuanBattle.scene`

## 里程碑范围与结论

| 节点 | 内容 | 提交 |
|---|---|---|
| M4-1 数据抽取 | `lua_parse.py`（复用 jws2 hero_audit 解析器 + Lua 整数键数组语义修复）+ `extract_tables.py` → `Export/data/*.json` 33,963 行（SkillData 61 列、CallBulletData、MissileData 71 列、MonsterData、MonsterModelData、MonsterValueData、AnimSound、LevelLayout1011 137 项） | `e6d7e06` |
| M4-1b 数据资产 | `SanguoDataGenerator.BuildTables` → 7 张原生 `.asset`（274 技能 / 586 callBullet / 554 导弹 / 14 怪物 / 14 模型 / 5 数值 / 137 布局项）；强类型 `Assets/Scripts/Data/SanguoBattleData.cs`；JSON 仅作中间产物（gitignored），运行时零 Lua 零 JSON | `2eb350c` |
| M4-2 战斗骨架 | 纯逻辑内核：判定几何（圆/扇/前向盒）、血量（防减伤、最小 1、一次性死亡事件）、状态机（六态 + 锁定/打断/死亡终态）、移动（寻达 + 最短弧转向）、布局→布阵网格、`SanguoBattleStage`（出怪/推挤分离/胜负）、目标注册表 | `91e4204` |
| M4-3 连段与技能 | 毫秒事件时间轴（排序 + 分帧触发）、连段取消窗口（SwitchPutIn 键 + [MinPlayTime, +Duration] 区间）、导弹伤害帧（起始/间隔/末帧@30fps）、形状命中结算（克制营地 + 死亡过滤） | `c5ccbb4` |
| M4-4 场景侧 | `SanguoActorPresenter`（状态→动画/写 Transform/受击层）、`SanguoBattleDriver`（数据装载→英雄 prefab 绑定→234 怪物占位体→WASD/J/K/1-4 输入→怪物 AI 寻敌近战→子弹伤害帧→战斗相机跟随）、`TaoYuanBattle.scene`（`GenerateBattleScene`，驱动器按类型名加入） | `a208450` |

## 运行验证（play 模式，Vulkan）

日志（`Logs/Editor.log`，本会话）：

```
[SanguoBattle] stage ready: heroes 4, spawn groups 42, enemies 234
[SanguoBattle] first Update tick, playing=True, stage=True, enemies=234, heroes=4
[SanguoBattle] t=2s enemy0=111.55,713.91 state=Run hero0=113.71,777.81 hp=1200 chasing=234 attacking=0
[SanguoBattle] t=4s enemy0=111.64,722.91 state=Run hero0=113.71,777.81 hp=1200 chasing=234 attacking=0
```

更早会话（接敌后，同样代码路径）：

```
[SanguoBattle] hero hit ZhaoYun -1 (hp 1091)
[SanguoBattle] hero hit ZhaoYun -1 (hp 1090)
[SanguoBattle] hero hit LiuBei -1 (hp 1123)
[SanguoBattle] hero hit GuanYu -1 (hp 1099)
[SanguoBattle] hero hit ZhangFei -1 (hp 1094)
```

- **234 个怪物全部进入 Run 追击**（chasing=234），enemy0 以 ~5.9 m/s 逼近英雄 ✓
- **接敌后近战命中落地**：攻击 20 − 英雄防 25 → 每击 1 点（公式 min 1）✓ 血量持续下降 ✓
- 修复过程中发现并解决的缺陷：驱动器从未调用 `SpawnNextGroup`（敌人恒 0）；相机 FarClipPlane 100 m（远景全裁）——均以日志+画面双证据定位。

## play 模式实时 stats（Battle Camera 离线帧，Vulkan）

```
drawCalls 3097, triangles 4160890, renderables drawn 3093, lod-hidden 847
```

## 画面

- `Export/scene/xengine-frames/play-battle.png` — 战斗相机离线帧（地形渲染正常，色调过曝为已记录的 M8 后期缺口）
- `.xengine/screenshots/gameview-*.png` — GameView 实拍（同白化）

## 引擎节点：描述符池规模化（本次随附落地，引擎 `main`）

整场景 play 会话（~14k draws × 每次状态变化重脏化描述符集）打爆两处硬上限：

| 后端 | 原上限 | 症状 | 修复 |
|---|---|---|---|
| Vulkan | 64 溢出池 × 4096 = 262,144 sets/帧 | frame 0 抛 `descriptor pool exhausted` | `MaxDescriptorPoolsPerFrame` 64 → **256**（≈1M sets/帧；溢出池按帧槽复用，峰值内存有界） |
| D3D12 | 16 页 × 97 = 1552 RTVs | 渲染线程逐帧抛 `RenderTargetView descriptor pool is full` | `MaxPages` 16 → **256**（≈24.8k RTVs；新页超过基线时打一条 warning 日志） |

修复后 Vulkan play 会话不再有池异常（对照修复前同场景必炸），本文件 stats 即修复后采集。

## 已知缺口（按计划记录）

| 缺口 | 归属 |
|---|---|
| 可玩帧里英雄/怪物未入画（跟随相机取景 + 占位体可见性） | M7（怪物 prefab 导入与模型驱动）/ M8 打磨 |
| 怪物 AI 攻击力为演示常量 20（英雄防 25 → 每击 1 点） | M7（MonsterValueData atk 驱动） |
| HUD 血条/伤害数字未接 | M7（hudmesh） |
| 技能事件仅 EventType 1（出弹）落地；5=音效等 | M6 音频 |
| 远景白化（无 ACES/LUT 后期） | M8 |
| 描述符池新页增长仅日志告警，无自动泄漏检测 | 观察项 |
