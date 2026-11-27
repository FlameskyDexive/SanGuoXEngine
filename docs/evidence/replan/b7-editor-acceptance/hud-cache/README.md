# B7 editor Play HUD update cache node

日期：2026-11-28

## 变更

`SanguoBattleDriver` 缓存每个 actor 上一次血条百分比。固定步更新仍遍历 HUD，但只有血量变化或死亡时才调用 `SetBloodPercent`，避免每帧重复触发布局和材质更新。

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
frameTimeMs=53.8488
cpuFrameTimeP50Ms=47.4036
cpuFrameTimeP95Ms=54.8405
gpuFrameTimeMs=35.5999
sustainedFps=20.8897
lodRenderersHidden=319
```

画面和战斗测试通过；编辑器 60 FPS gate 仍未通过，后续继续处理 CPU 和渲染提交瓶颈。
