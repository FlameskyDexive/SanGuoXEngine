# E5 follow-up：组件级视锥剔除

日期：2026-11-28

`MeshRenderer` 在动态 proxy 缓存仍有效时提供世界空间 AABB；SceneComponentRegistry 在
调用 `OnRenderCollect` 前先做一次相机视锥测试。没有可证明缓存边界的组件继续走原有
collect-then-cull 路径，因此 streaming、粒子和自定义组件不被强制套用错误边界。

## 编辑器 Play 验收

- D3D12，TaoYuanBattle，GameView 1280×720，HUD 30 Hz Demo 配置。
- `renderablesCollected=851`，相比上一暖机样本约 1789 明显下降。
- `meshRendererCollectMs=0.1130 ms`、`componentCollectMs=4.1484 ms`、
  `cpuFrameTimeP50Ms=16.6255 ms`、`cpuFrameTimeP95Ms=17.8803 ms`。
- GPU `2.30 ms`；GameView 和战斗帧截图仍显示角色、HUD 与场景。

E7 的 P95 门槛仍是 16.6 ms，当前节点尚未达到最终验收；该结果只证明组件级剔除
降低了收集工作量。
