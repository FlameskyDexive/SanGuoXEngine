# B6c 技能共享粒子反馈

- 在命中粒子之外新增共享技能粒子系统，使用 GPU-instanced ParticleSystemComponent；技能开始和导弹事件复用同一系统，不为每个技能/敌人创建独立粒子对象。
- Boss 与波次角色仍使用统一 kernel/presenter 通道，粒子对象共享材质和有限粒子容量。
- C# 测试：47/47 通过；D3D12 编辑器截图已留档。
- 运行统计（Boss 基础形态同时开启）：`drawCalls=35`、`triangles=132658`、`renderablesCollected=2717`。完整 42 Effects 材质、原作技能贴图和音频仍待资源接入，60 FPS gate 未通过。
