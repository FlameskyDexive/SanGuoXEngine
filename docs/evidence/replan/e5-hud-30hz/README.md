# E5 follow-up：桃园 HUD 30 Hz CPU 预算

日期：2026-11-28

桃园战斗 Demo 将 `HudManager.RenderFrameInterval` 设为 `2`。HUD 合并网格每两帧重建一次，
但缓存的 `MeshRenderable` 仍然每帧提交；战斗模拟、角色、相机和场景渲染保持原有帧率。
通用 Hudmesh 默认仍为每帧更新，避免把 Demo 的预算策略强加给其他项目。

## 编辑器 Play 验收

- D3D12，TaoYuanBattle，GameView 1280×720。
- 截图：`editor-gameview-1280x720.png`、`editor-play-battle-960x540.png`。
- 原始命令和 stats：`commands.json`、`runtime-stats-d3d12.json`。
- `otherComponentCollectMs=1.6192 ms`，`componentCollectMs=3.7640 ms`，相比 60 Hz
  HUD 样本的约 2.52/4.84 ms 明显下降。
- `cpuFrameTimeP50Ms=16.4998 ms`、`cpuFrameTimeP95Ms=20.0820 ms`；P95 仍未达到 E7
  的 16.6 ms 门槛，不能宣称最终性能通过。
- GameView 中角色、血条和场景仍可见；静态实例统计保持有效。
