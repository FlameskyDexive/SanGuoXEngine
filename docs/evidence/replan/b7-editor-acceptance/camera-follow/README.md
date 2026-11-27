# B7l Battle Camera follow

- The battle camera keeps the authored pitch/roll and follows the leader with a bounded third-person offset (`+Y 18`, `-Z 30`).
- Vulkan editor Play capture shows all four heroes in the GameView at 960×540; the camera no longer collapses onto the leader or loses the party.
- Runtime capture: `drawCalls=31`, `triangles=105820`, `renderablesDrawn=29`, `gpuFrameTimeMs=38.25`, `cpuFrameTimeP95Ms=35.84` in the Vulkan run.
- Tests: SanGuoXEngine C# 50/50 and Tools pytest 108/108.
- D3D12 replay was attempted after the Vulkan capture but hit the existing `CommandBuffer has already been submitted` render-thread failure before a fresh frame; this node does not claim a D3D12 performance gate.
