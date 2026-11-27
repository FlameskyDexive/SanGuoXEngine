# B7 editor Play shove-gate node

日期：2026-11-28

## 变更

`SanguoBattleStage.Shove` 只有每 4 个固定步才执行分离，因此 living scratch 也延后到 15 Hz 分离路径中构建；此前它在每个 60 Hz 固定步都会被清空并重填。

## 验证

- C#：`dotnet test SanGuoXEngine.Tests.csproj -c Debug --no-restore`，50/50 通过。
- Python：`py -3 -m pytest -q Tools/sanguo_import/tests`，98/98 通过。
- 编辑器：D3D12、TaoYuanBattle Play、等待 10 秒后 `runtime_stats`。
- 截图：`battle-d3d12-1280x720.png`。

## 原始统计

```text
drawCalls=31
renderablesCollected=2722
renderablesDrawn=29
frameTimeMs=47.7117
cpuFrameTimeP50Ms=49.4858
cpuFrameTimeP95Ms=59.7073
gpuFrameTimeMs=34.8194
sustainedFps=19.4996
lodRenderersHidden=319
```

画面和战斗测试通过；单次统计受编辑器波动影响，没有证明 60 FPS 改善，门槛仍保持未通过。
