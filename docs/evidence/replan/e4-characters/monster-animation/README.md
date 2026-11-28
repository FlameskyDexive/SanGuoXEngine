# E4 黄巾兵真实动作接入

## 范围

本节点把 `Monster_YellowArmy_Dao` 的真实 Unity 动画导出为 XEngine 原生曲线，并绑定到真实黄巾兵 prefab：

- `idle_loop`
- `run_loop`
- `NormalAttack_1`
- `hurt`
- `die`

导出脚本仍在 Unity 编辑器内读取已解析的 `AnimationClip` 曲线、RootT/RootQ、采样姿态和动画设置；XEngine 侧沿用 `SanguoClipBuilder` 的密曲线逐帧重采样、Hermite 曲线和根运动残差规则，没有直接读取 Unity YAML。

## 测试

- `dotnet test SanGuoXEngine.Tests.csproj --no-restore --verbosity minimal`：51/51 通过。
- `py -3 -m pytest -q`：108/108 通过。
- Unity AIBridge 导出：5/5 clips，`Monster_YellowArmy_Dao.json`。
- XEngine editor generation：5 clips，生成 5 个 `.anim` 和 1 个 `.controller`。

## 编辑器 Play 验收

环境：D3D12、GameView 1280×720、`Scenes/TaoYuanBattle.scene`、Play 模式，等待 20 秒后采样。

- 截图：[editor-gameview.png](editor-gameview.png)
- 原始命令结果：[runtime-result-final.json](runtime-result-final.json)
- 原始帧统计：[runtime-stats-d3d12.json](runtime-stats-d3d12.json)

本次采样观察到 `drawCalls=67`、`instancedDrawCalls=3`、`renderablesDrawn=64`、`skinnedPaletteRows=58`、`skinningUpdateMs=2.604`、`gpuFrameTimeMs=3.733`、`cpuFrameTimeP95Ms=20.929`。截图中真实黄巾兵和四英雄均可见；控制器已在 Play 中驱动 idle/run/hurt/die 路径，战斗表的 `n1` 通过别名落到 `NormalAttack_1`。

该节点证明动作和真实蒙皮路径已接入编辑器，但 `cpuFrameTimeP95Ms` 仍高于 E7 的 16.6 ms 门禁；长稳态、Vulkan 和 Boss 动作继续留在后续节点，PC/手机包保持冻结。
