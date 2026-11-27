# B7 editor Play spatial-query fast-fail node

日期：2026-11-28

## 变更

`SanguoSpatialHash.FindNearest` 在查询半径内没有候选时，原实现会再次遍历所有 cell；由于结果仍受原半径平方限制，这次扫描不能改变返回值。现在直接返回空结果，保持距离语义并移除每个敌人每个固定步的无效全表扫描。

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
frameTimeMs=50.2283
cpuFrameTimeP50Ms=48.7762
cpuFrameTimeP95Ms=57.2139
gpuFrameTimeMs=31.2133
sustainedFps=20.3420
lodRenderersHidden=319
```

该节点没有改变战斗目标距离语义，画面和战斗测试通过；单次 Play 采样仍受编辑器和 GPU 波动影响，60 FPS gate 继续保持未通过。
