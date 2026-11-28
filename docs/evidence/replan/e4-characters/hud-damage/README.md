# E4 HUD 血条与伤害数字

HUD 伤害数字此前只创建了 digit widget，却没有给 digit sprite 分配合并材质槽，导致 `_digitSlot=-1`、数字被 `Fill()` 静默跳过。本节点在 `com.xengine.hudmesh` 修复了 `HudObject.PopNumber` 的 digit slot 预绑定，并加入回归测试。

## 测试

- `dotnet test Packages/com.xengine.hudmesh/Tests~/XEngine.Hudmesh.Tests.csproj --no-restore --verbosity minimal`：48/48 通过。
- `dotnet test SanGuoXEngine.Tests.csproj --no-restore --verbosity minimal`：51/51 通过。
- `py -3 -m pytest -q`：108/108 通过。

## 编辑器 Play 验收

环境：D3D12、GameView 1280×720、`TaoYuanBattle` Play。通过 live eval 触发一次真实 `PopHudNumber(actor, 123)`，等待 HUD 更新后截图。

- 截图：[editor-gameview.png](editor-gameview.png)
- 原始命令结果：[runtime-result.json](runtime-result.json)
- 原始帧统计：[runtime-stats-d3d12.json](runtime-stats-d3d12.json)

截图中角色群前方可见白色 `+123` 伤害数字；统计为 `drawCalls=70`、`instancedDrawCalls=3`、`renderablesDrawn=67`，较无数字帧多出 HUD 合并网格提交。该节点证明 HUD 数字渲染链路已闭合；E7 CPU P95 仍为 36.119 ms，本节点不宣称最终性能门禁通过。
