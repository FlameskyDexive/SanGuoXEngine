# B7j 编辑器 GameView 分辨率渲染比例

- 编辑器 Play 的 GameView 目标是 1280×720，但宿主 surface 常为 1920×1080；`SanguoBattleDriver` 在 `Application.IsEditor` 下将 Battle Camera `RenderScale` 设为 0.67，玩家包保持 1.0，避免编辑器 GPU 被隐藏的全分辨率 surface 放大。
- C# 测试：`dotnet test SanGuoXEngine.Tests.csproj -c Debug --no-restore`，46/46 通过；Python：`py -3 -m pytest -q`，108/108 通过。
- D3D12 截图：`battle-gameview-d3d12-1280x720.png`。
- 运行统计：`drawCalls=30`、`renderablesCollected=2712`、`gpuFrameTimeMs=29.45`、`cpuFrameTimeP95Ms=59.37`。GPU 成本下降，但编辑器 CPU gate 仍未通过，后续继续处理脚本/收集峰值。
