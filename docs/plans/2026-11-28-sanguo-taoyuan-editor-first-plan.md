# 桃园战斗 Demo 移植重规划 v4：编辑器优先、可测性能、分阶段保真

日期：2026-11-28

## 1. 交付边界

本计划只把 XEngine 编辑器 Play 的桃园战斗 Demo 做到可运行、可对照、可测性能。编辑器 GameView 是唯一主验收面；PC 包、手机包、AssetBundle 发布和平台工具链全部延后到编辑器终验通过之后。

主验收固定为 D3D12、1280×720 GameView、Battle Camera、同一场景和同一战斗脚本。Vulkan 只做回归抽测，不用 Vulkan 单次结果替代 D3D12 性能结论。

每个节点都必须完成：代码 → 单测/脚本测试 → 编辑器 Play 截图 → `runtime_stats` 原始 JSON → 对照说明 → 子模块 commit/push。引擎能力单独在 XEngine `main` 提交并推送；父仓库只更新已验收的子模块 gitlink。

## 2. 当前事实与停止条件

已完成的基础包括 M0–M3、战斗内核、固定步进、对象池、空间哈希、集中式角色视图更新、静态场景实例组、共享技能粒子和编辑器 RenderScale 首版。它们不等于最终验收。

当前仍然打开的硬问题：

- 角色和场景画面与 Unity 参考差异大，特别是光照贴图、天空/后处理、材质变体、头发和粒子。
- 编辑器 Play 稳态帧时间仍远高于 16.6 ms；当前实测需要分别报告批次构建尖峰和预热后的稳态 p50/p95。
- 首版跨 Mesh 世界空间合批在完整光照/材质状态下出现不可接受的视觉回归，因此暂时只启用同 Mesh 的 GPU Instancing；跨 Mesh 合批必须有完整 Lightmap、UV、材质属性和阴影 pass 契约后才重新开启。
- 不可读导入 Mesh 必须保留同 Mesh GPU Instancing 回退，不能退回逐 Renderer。
- 敌人真实 prefab、Boss 专属形态、完整特效材质、音频和最终 HUD 仍未完成。

任何节点出现以下情况立即停止并回退：画面白块/UV 错位/阴影 pass 不一致、draw/帧时间回归超过基线、编辑器进 Play 后反复重建批次、资源卸载后批次引用已释放对象、截图与原始 stats 不能对应。

## 3. 运行时渲染策略

### 3.1 静态对象契约

`GameObject.IsStatic` 只表示 Transform 和渲染状态在当前场景生命周期内稳定。注册时必须同时满足有效 Mesh、材质和 Shader Pass、LOD 当前级别可见、Layer/RenderingLayer、子网格、透明/双面、阴影状态、Lightmap 页与 UV 变换兼容。

首个可交付策略沿用 Unity 的可靠路径：按 Mesh、材质状态、PropertyState、子网格、空间块建立静态实例组，使用 `InstancedMeshRenderable`/`DrawIndexedInstanced`。每组保存 AABB、实例变换和源对象调试映射；普通帧只做 AABB 剔除和实例数据上传，不复制顶点。

静态 registry 会接管所有通过契约校验的 `MeshRenderer`，即使某个空间块只有一个实例也保留一个静态 proxy，避免每帧重新遍历静态组件和重建 `PropertyState`。单例 proxy 透传原始 renderable，继续走普通 draw；只有同 Mesh、同材质状态且至少两个实例的组才升级为 GPU Instancing。材质或导入状态不完整时 registry 保持不完整标记并让源 Renderer 回退，不能为了计数减少而强行切换 shader 变体。

跨 Mesh 世界空间合批是后续实验节点，必须先完成：每种 VertexLayout 的缺省通道策略、Lightmap UV 烘焙、材质属性快照、ShadowCaster/Depth/Motion/Picking 一致性、64K/32-bit 索引上限、资源生命周期和可逆回退。未满足时按同 Mesh Instancing 或逐 Renderer 回退。

当前编辑器实现明确采用两条有证据的路径：同 Mesh、同材质状态的对象走 GPU Instancing；小型可移动 Mesh 走同 Mesh 动态批。跨 Mesh CPU 合批暂不默认开启。2026-11-28 的实验捕获表明，只复制位置、法线、切线、UV 和索引还不足以保持桃园画面，光照贴图、材质变体和深度/阴影状态会出现白化或几何回归。因此不能因为“能合成一个 Mesh”就把静态对象从原路径隐藏；跨 Mesh 节点必须先有完整契约、失败时保留源 Renderer，并用同机位截图和 Frame Debugger 逐 pass 对照通过后才可启用。

### 3.2 GPU Instancing 与动态合批

树、石块、围栏、旗帜、火把、公告板、重复特效和相同敌人模型优先进入实例组；组按 64 m 空间块拆分并独立剔除。实例数据包含 Transform、颜色/受击参数、LOD 和 GI 所需数据；池化数组必须保存有效 `InstanceCount`，不能把租用数组容量当绘制数量。

小型可移动、低顶点 Mesh 才进入动态合批；移动、材质属性、LOD、光照状态或透明排序变化时只失效对应组。透明、复杂粒子、角色蒙皮和 Boss 保持独立路径或专用 GPU 蒙皮实例。

### 3.2.1 Static 标记的运行时行为（本次用户要求的硬规则）

桃园 Demo 不把 `GameObject.IsStatic` 当成“每个物件永远单独 Draw”或“导入期盲目焊成一个大 Mesh”。它在编辑器 Play 首帧建立场景级 registry，随后按 Unity 风格的兼容状态做运行时批处理：

1. `IsStatic` 且通过 Mesh、材质/Shader Pass、PropertyState、子网格、Layer、RenderingLayer、光照页/UV 和阴影状态检查的对象进入静态实例组。
2. 同 Mesh、同材质状态、同子网格、同空间块的对象用 `DrawIndexedInstanced`；单例也由静态 proxy 接管，从每帧组件遍历中移除，但不强行切换到不兼容的 shader 变体。
3. 静态 Transform、材质、Lightmap 或 LOD 状态发生变化时必须让 registry 失效并在下一次收集重建；动态对象的移动只失效自己的缓存，不能触发整场静态重建。
4. 未勾选 Static 的小型同 Mesh 对象进入运行时兼容分组，视锥裁剪后升级为 GPU Instancing；透明、蒙皮、BlendShape、不同光照状态和不可读 Mesh 回退到各自安全路径。
5. 跨 Mesh CPU 动态合批只有在顶点布局、缺省通道、Lightmap UV、材质属性和 Shadow/Depth/Motion/Picking 全部等价时才开启；否则保持同 Mesh Instancing 或逐 Renderer，不能为了减少 draw 把场景焊白或破坏阴影。

验收必须同时看到：`staticBatchGroups/staticBatchInstances`、`dynamicBatchGroups/dynamicBatchInstances`、相同机位截图和 Transform 变更后的重建证据。任何“统计变少但画面变白/UV 错/阴影错”的实现都回退。

### 3.3 角色与敌人

近景四英雄和 Boss 保留独立高质量 GPU 蒙皮；中景相同 Mesh/材质/动画状态按骨骼调色板分组；远景降低动画更新频率并使用低 LOD。每个实例必须验证颜色、受击参数、深度、阴影和不同姿态，不得只用静止同姿态截图。

当前编辑器实现还提供场景级 `SkinnedPaletteAtlas`：不含 BlendShape 的 skinned 子网格把骨骼矩阵写入共享纹理行，GPU-instancing custom data 选择行；含 BlendShape 或不兼容 shader 的对象继续走原始单体纹理路径。atlas 必须在 D3D12、Vulkan、OpenGL 同机位截图中证明角色姿态和材质一致后才可作为默认编辑器路径。

## 4. 节点顺序

### E0 编辑器基线与证据协议

固定场景、分辨率、后端、相机、质量设置和战斗时刻。每项配置预热 300 帧、采集 600 帧、重复 3 次，记录 CPU/GPU/render thread、draw、instanced draw、批次数、renderables、阴影、GC、managed/private memory 和批次构建耗时。把冷启动、批次构建尖峰和稳态分别报告。

### E1 编辑器静态实例稳定性

锁定 `IsStatic` 注册、空间块、LOD/SceneView 过滤、Transform/材质/光照变化失效和资源卸载回退。补齐不可读 Mesh、不同 Lightmap 页、不同 UV scale/offset、材质变体、ShadowCaster 和对象定位测试。编辑器 Play 截图必须与 E0 同机位对照。

验收：静态源不重复提交；同 Mesh 组为一个或少量 instanced draw；不可读 Mesh 不退回逐 Renderer；无每帧重建和无资源释放后引用；静态 Transform 改动会触发一次 registry 重建并显示新矩阵。

### E2 GPU Instancing 与动态合批

将可重复环境物件、重复 VFX 和相同敌人模型接入实例组；为动态小 Mesh 增加按材质/Shader Pass/状态的动态合批。扩展 `RenderStats` 和 Frame Debugger 标识，D3D12 主测、Vulkan 回归、OpenGL 最小烟测。

验收：实例数据池零持续增长；不同姿态、阴影和透明裁剪结果正确；稳态分配和 GC 记录可解释。

### E3 桃园场景保真

先修天空 HDR、曝光、ACES/LUT/Bloom、光照贴图解码、阴影遮罩、SpeedTree 和环境材质，再处理粒子材质。每个机位保留 Unity/XEngine 并排图和差异清单。画面回归失败时不进入角色性能节点。

### E4 角色、敌人和 Boss

导入并绑定真实黄巾兵 prefab，移除立方体占位；按 MonsterID/ModelID/ModelScale/DmgNumHeight 数据驱动。接入 GPU 蒙皮实例、HUD 血条/伤害数字、吕布 Boss 基础形态和胜负判定。所有近/中/远景都必须在编辑器 GameView 可见。

### E5 战斗 CPU 与内存

保持固定 60 Hz 模拟、空间哈希和对象池；继续清理每帧 List/HashSet/临时对象，集中更新角色视图，使用 XTask 管理延迟特效和相机震动。以 ETW/编辑器 stats 证明 CPU p95、分配和 GC 改善，不能只凭对象数量推断。

### E6 特效、音频和交互

按优先级接入命中/技能/场景粒子、42 个效果材质、Wwise→OGG 音频、技能音效事件、HUD 和相机反馈。每一类效果都有可复现触发脚本与截图。

### E7 编辑器最终验收

D3D12 1280×720：预热后稳态 p95 ≤ 16.6 ms、p99 ≤ 25 ms；600 帧内无 Gen0 GC，稳态每帧分配目标 0 B；draw/实例/阴影/内存统计可追溯；30 秒战斗和 10 分钟稳定性通过。Vulkan 做同机位回归。E7 未通过前禁止出 PC 包和手机包。

### E8 出包（最后）

只在 E7 通过后规划 PC、Android/iOS/HarmonyOS 包：AssetBundle/Addressables 收集器、平台 Shader 变体、纹理压缩、安装/启动/热更新和包体性能单独验收，不回写编辑器基线。

## 5. 证据和目录

## 6. 当前验收台账

- E2 GPU Instancing/动态合批：探针通过，D3D12 记录 `staticBatchGroups=6`、`staticBatchInstances=112`、`dynamicBatchGroups=1`、`dynamicBatchInstances=63`；默认桃园机位仍需真实环境物件材质/光照保真复验。
- E1 静态 Transform 失效：已通过引擎单测 10/10 与 D3D12 Play。静态物件移动后 `Scene.StructureVersion` 递增，registry 下一次收集重新建立 proxy；证据见 `docs/evidence/replan/e1-static-transform-invalidation/`，引擎提交 `2767ec0ff`。
- E4 敌人 prefab/缩放/索敌：已通过 50/50 子模块测试与 D3D12 1280×720 Play。首波敌人从约 54 m 外进入战斗，真实 prefab 不再被巨大四边形遮挡；采样 `dynamicBatchGroups=1`、`dynamicBatchInstances=31`，证据见 `docs/evidence/replan/e4-characters/enemy-prefab-scale/`。敌人最终材质、动作、Boss 专属形态和 HUD 仍开放。
- E2 follow-up 每实例 Lightmap ST：已通过 D3D12 编辑器 Play 验收。批次 key 不再把
  `_LightmapScaleOffset` 当整组状态，Standard shader 从 `InstanceData.CustomData` 读取
  每实例 ST；真实战斗机位记录 `instancedDrawCalls=2`、`staticBatchGroups=3`、
  `staticBatchInstances=6`，截图和原始 stats 见 `docs/evidence/replan/e2-lightmap-instancing/`。
- E3 场景保真：EXR 线性光照图已修复，默认编辑器路径 GPU 约 2.20 ms；Tonemap/Bloom A/B 可用但 GPU 约 10.09 ms，暂不默认开启。天空色调、方向光照图/阴影遮罩、SpeedTree 和效果材质仍开放。
- E5 战斗性能：预热样本 D3D12 CPU P95 约 17.64–19.65 ms、GPU 约 2.25 ms；新增 `ComponentCollectMs` 后确认动态组件回调约 4.65 ms、静态 registry 约 0.027 ms，继续优化动态收集与编辑器 CPU 尖峰。
- E5 HUD proxy 缓存：已在 `com.xengine.hudmesh` 按材质槽复用 `MeshRenderable`，真实 Play
  样本 `componentCollectMs=4.4029 ms`、`renderCollectMs=4.9761 ms`；截图和原始 stats
  见 `docs/evidence/replan/e5-hud-renderable-cache/`。单次 P95=22.50 ms，E7 仍未通过。
- E5 HUD 30 Hz 预算：桃园 Demo 将 HUD 网格重建间隔设为 2 帧，真实 Play 样本将
  `otherComponentCollectMs` 降至 `1.6192 ms`、`componentCollectMs` 降至 `3.7640 ms`；
  GameView 截图和原始 stats 见 `docs/evidence/replan/e5-hud-30hz/`。P95=20.08 ms，
  仍未通过 E7。
- E5 组件级视锥剔除：动态 MeshRenderer 使用已验证 proxy AABB 在组件回调前剔除，
  暖机样本 `renderablesCollected=851`、`meshRendererCollectMs=0.1130 ms`、
  `cpuFrameTimeP95Ms=17.8803 ms`；证据见 `docs/evidence/replan/e5-component-culling/`。
- E7 编辑器终验：未通过；PC/手机包保持冻结。

```text
docs/evidence/replan/e0-editor-baseline/
docs/evidence/replan/e1-static-instancing/
docs/evidence/replan/e2-gpu-instancing/
docs/evidence/replan/e3-taoyuan-visual/
docs/evidence/replan/e4-characters/
docs/evidence/replan/e5-battle-performance/
docs/evidence/replan/e6-vfx-audio/
docs/evidence/replan/e7-editor-final/
```

每个目录保存 README、原始 `runtime_stats`/测试输出、截图和已知限制。任何“未通过”“部分通过”“只完成静态审计”都必须原样记录，不能用旧截图冒充当前运行时验收。
