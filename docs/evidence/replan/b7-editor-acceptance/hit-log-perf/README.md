# B7 editor Play hit-log performance node

日期：2026-11-28

## 变更

`SanguoBattleDriver` 不再为每个命中事件单独写 `Debug.Log`。命中数在战斗驱动器中累计，并在原有两秒心跳日志中报告本周期命中数和累计命中数。这样保留战斗可审计信息，同时避免 234 个敌人战斗时持续把日志写入编辑器管线。

## 验证

- C#：`dotnet test SanGuoXEngine/SanGuoXEngine.Tests.csproj -c Debug --no-restore`，50/50 通过。
- Python：`py -3 -m pytest -q Tools/sanguo_import/tests`，98/98 通过。
- 编辑器：D3D12、TaoYuanBattle Play、等待 10 秒后 `runtime_stats`。
- 截图：`battle-d3d12-1280x720.png`。

## 原始统计

```text
drawCalls=31
instancedDrawCalls=0
renderablesCollected=2722
renderablesDrawn=29
triangles=105820
frameTimeMs=46.6533
cpuFrameTimeP50Ms=48.8775
cpuFrameTimeP95Ms=57.8008
gpuFrameTimeMs=38.0269
sustainedFps=19.8435
lodRenderersHidden=319
```

该节点保持画面和战斗逻辑正确，但 60 FPS 编辑器门槛仍未通过；后续继续处理 CPU 战斗热路径和渲染提交成本。
