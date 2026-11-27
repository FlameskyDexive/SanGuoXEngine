# SanGuoXEngine 桃园 Demo 重构规划 v3

## 目标

重新建立一个以编辑器 Play 为核心的高性能、高保真桃园战斗 Demo。

- 画面对齐原作 Unity 游戏着色器、天空、烘焙光照、后处理、粒子和阴影。
- 保留完整桃园场景资产，使用静态批处理、GPU Instancing、动态合批、LOD、可见性和阴影预算达到稳定帧率。
- 编辑器 GameView 以 1280×720 验收，D3D12 为主后端，Vulkan 做回归抽测。
- 首个正式交付目标为 60 FPS，帧时间 p95 不超过 16.6 ms。
- PC 包和手机包放在编辑器 Play 验收完成之后。

## 当前问题

- M1、M2 只覆盖中性光照下的单角色和动画采样。
- M3 场景结构已导入，但已有对照帧明显过曝。
- M4 战斗帧中角色和敌人没有形成可用的视觉呈现，敌人仍使用立方体占位体。
- 桃园已有约 13,720 个 draw call、11.7M 三角形和 27,687 个阴影 draw call。
- 战斗帧已有约 3,097 个 draw call、4.16M 三角形。
- 战斗驱动器每帧遍历全部敌人，推挤逻辑为 O(N²)，攻击和导弹路径存在临时对象分配。
- 234 个敌人立即生成独立场景对象，每个对象拥有独立呈现回调。
- `Update` 目前将模拟步长限制在 20 Hz，战斗模拟和渲染没有稳定的固定步进关系。
- `GameObject.IsStatic` 当前主要被光照贴图和静态阴影路径消费，尚未成为静态批处理契约。

## 批处理架构

### 静态批处理

新增运行时 `StaticBatchRegistry`。

注册条件包括 `GameObject.IsStatic`、有效 Mesh 和材质、稳定 Transform、静态光照状态和可排序的 Shader Pass。

批次键包含 Mesh、子网格、材质状态哈希、Shader Pass、光照贴图页和 UV 变换、Rendering Layer、阴影状态、LOD 层级以及透明和双面状态。

批次建立时把世界变换应用到合并顶点，合并索引和顶点缓冲，保留光照贴图 UV，生成批次 AABB，并保存源对象到批次对象的调试映射。

批次在场景首次加载、空间分区激活、LOD 改变、静态对象变化或光照贴图变化时建立或失效。普通帧不重新合并顶点。

### GPU Instancing

新增 `GpuInstanceRegistry`，复用现有 `InstancedMeshRenderable`、`InstanceData` 和 `DrawIndexedInstanced` 路径。

树木、石块、围栏、火把、公告板、重复特效和相同敌人模型进入实例组。实例组按空间分块，每组独立计算 AABB，支持相机剔除和阴影剔除。

实例数据支持 Transform、颜色、受击参数、光照贴图信息、LOD 和效果 CustomData。实例增删只重建对应组，普通帧只更新 Transform 和 CustomData 缓冲。

### 动态合批

新增 `DynamicBatchBuilder`，用于小型、可移动、低顶点数物件。动态批次按照 Mesh、Material、Shader Pass 和渲染状态合并，在移动或材质状态改变时失效并重建。

角色、Boss 和复杂透明效果使用独立渲染或 GPU 蒙皮实例化路径。

### 动态蒙皮实例

复用现有 `SkinnedInstanceBatch`。近景英雄使用独立 GPU 蒙皮，中景敌人按 Mesh、Material 和动画状态分组，远景敌人使用低频动画或 GPU Instancing。颜色和阴影 Pass 均需验证实例化结果。

## 执行阶段

### B0 性能基线

建立完整桃园、四英雄、12 敌人、50 敌人和 234 逻辑敌人的编辑器 Play 基线，同时记录静态批处理关闭、开启、GPU Instancing 开启以及组合开启的结果。

每种配置执行三次，每次预热 300 帧并采集 600 帧。记录总帧时间、CPU、渲染线程、GPU、DrawCalls、InstancedDrawCalls、批次数、实例组数量、实例数量、批次构建时间、阴影成本、分配和 GC。

### B1 引擎静态批处理

- 新增 `StaticBatchRegistry`。
- 让 `GameObject.IsStatic` 参与批次注册。
- 修改 `MeshRenderer` 的渲染收集入口。
- 增加静态批次生命周期和失效机制。
- 处理 Lightmap UV、阴影、Rendering Layer 和调试对象 ID。
- 增加静态批次 Frame Debugger 标识。
- 扩展 `RenderStats`，记录静态批次数、静态批次 draw call、合并顶点字节数和批次构建耗时。

引擎测试覆盖相同 Mesh 和材质合并、不同 Lightmap 拆分、Transform 变化失效、材质变化失效、LOD 切换、ShadowCaster 一致性以及编辑器对象定位。

### B2 GPU Instancing 和动态合批

- 将重复静态对象自动转为实例组。
- 将小型动态对象接入动态合批。
- 增加实例组分块和分块 AABB。
- 增加实例数据池和缓冲复用。
- 验证 D3D12、Vulkan 和 OpenGL。
- 验证 Standard、Particle、透明裁剪和双面材质。
- 扩展 Frame Debugger 和 `RenderStats`。

### B3 桃园场景接入

场景生成器为静态物件保留 `IsStatic`、Mesh、子网格、材质状态、LightmapIndex、LightmapScaleOffset、LODGroup、阴影标志和 Rendering Layer。

桃园运行时按空间分区建立静态批次。树木、石块、围栏、旗帜、火把和公告板优先接入 GPU Instancing。完整桃园近景、中景和全景必须继续可见。

### B4 角色和敌人接入

- 移除战斗中的立方体敌人占位体。
- 使用 `MonsterModelData` 绑定黄巾敌人模型。
- 相同敌人模型进入 GPU 蒙皮实例组。
- 近景敌人使用真实动画和材质。
- 远景敌人降低动画更新频率。
- 四英雄和吕布保留独立高质量渲染。
- 集中式 `SanguoBattleRuntime` 更新角色视图，减少每个角色独立 `LateUpdate`。

### B5 战斗逻辑优化

- 使用固定 60 Hz 模拟。
- 使用空间哈希处理目标选择、命中查询和推挤。
- 预分配技能、导弹、命中和推挤缓冲。
- 移除每帧临时 `List`、`HashSet` 和 `SanguoMissile`。
- 234 敌人保持完整逻辑状态，只有相机可见范围进入高质量呈现。
- 使用 XTask 管理延迟特效、顿帧和相机震动。

### B6 视觉和特效

修复 HDR 天空、光照贴图解码、ACES 或等效色调映射、LUT、Bloom、方向光照贴图、阴影遮罩、角色边缘光、皮肤、头发、金属反射和关键战斗粒子。

### B7 编辑器 Play 终验

- 1280×720 GameView。
- D3D12 主验收，Vulkan 回归。
- 总帧时间 p95 不超过 16.6 ms，p99 不超过 25 ms。
- 稳态每帧分配为 0 B，600 帧内无 Gen0 GC。
- 静态批次、GPU Instancing 和动态合批统计可见。
- 桃园近景、中景、全景、四英雄、Boss、敌人、特效、HUD 和音频完整运行。
- 30 秒战斗和 10 分钟稳定性测试通过。

PC 包和手机包在 B7 通过后单独规划。

## 追加执行节点（编辑器优先）

### B7g GPU 实例缓冲生命周期（已完成）

静态 MeshRenderer 和动态合批继续沿用同一套 GPU instancing draw，但 CPU 侧实例数据改为从
`ArrayPool<InstanceData>` 租用。`InstancedMeshRenderable.InstanceCount` 单独保存有效实例数，
因此池数组的多余容量不会被上传或绘制；CommandBuffer 在 `UpdateBuffer` 编码阶段复制 payload，
完成当前批次后才归还数组。验收必须同时通过静态/动态批处理单测、编辑器构建和 D3D12
1280×720 截图；该节点不宣称 60 FPS gate 已通过。

### B7h 静态场景分块批处理（已完成首版，需随 B7 继续调优）

继续沿用 Unity 的 `GameObject.IsStatic` 契约，在场景级建立按 Mesh、材质状态、子网格、光照
贴图、RenderingLayer、阴影状态和空间块划分的静态实例组。静态组只在场景首次加载或依赖
变化时重建，普通帧执行块 AABB 剔除并更新可见实例范围；编辑器 SceneView 过滤、LOD、对象
定位和资源卸载回退到逐 Renderer 路径。首版 D3D12 验收已将战斗帧 `renderablesCollected`
从 2809 降到 2712，instanced draw 和画面保持稳定；Vulkan 回归、进一步减少批次数以及
60 FPS gate 仍属于 B7 后续调优。

### B7i 角色 GPU 蒙皮实例（进行中）

已先完成 `MonsterID → ModelID` 固化、模型缩放/伤害高度绑定和共享材质变体缓存；当前源包
仍缺真实 YellowArmy、EliteMageArmy 和骑兵 FBX/Prefab，因此 xiaobingyuanjing 只作为几何
回退。下一步必须补齐真实 prefab，再按 Mesh、材质、动画状态分组 GPU 蒙皮；每实例独立骨骼
调色板、受击参数和阴影 pass，近景英雄保持独立高质量路径。

### B7j 编辑器 GameView 渲染比例（已完成首版）

编辑器 Play 以 1280×720 GameView 为验收目标；当宿主 surface 为 1920×1080 时，Battle Camera
在编辑器使用 0.67 RenderScale，玩家包保持原生比例。该节点只降低编辑器 GPU 负担，不能替代
CPU 优化和 B7 的 60 FPS gate。

## 证据和提交

证据目录使用：

```text
docs/evidence/replan/b0-baseline/
docs/evidence/replan/b1-static-batching/
docs/evidence/replan/b2-gpu-instancing/
docs/evidence/replan/b3-taoyuan/
docs/evidence/replan/b4-characters/
docs/evidence/replan/b5-battle/
docs/evidence/replan/b7-editor-acceptance/
```

每个节点必须完成实现、测试、截图、性能原始数据、对照说明、`PORT-STATUS.md` 更新、子模块 commit 和 push，再进入下一个节点。

子模块使用 `sub/sanguo-demo-rebuild` 分支。引擎能力直接提交到引擎 `main`。父仓库只在子模块节点验收后更新 gitlink。父仓库现有无关脏改动不参与本任务。
