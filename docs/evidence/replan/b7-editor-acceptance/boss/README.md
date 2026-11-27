# B7k / M7 Boss kernel接入

- `SanguoBattleStage.SpawnBoss` 将 Lu Bu 作为敌方 kernel actor 注册，参与寻敌、攻击、血量和胜负判定；场景侧使用已有 `Hero_LvBu.prefab` GUID `2c30308a-0bae-3fc3-7d56-74cbebc23598`。
- Boss 使用 `MonsterID=900001`、`ModelID=10109`、5000 HP / 180 ATK / 80 DEF；HUD 和 presenter 走同一套角色视图通道。
- C# 测试：47/47 通过；D3D12 编辑器截图已留档。
- 运行统计：`drawCalls=35`、`triangles=132658`、`renderablesCollected=2717`。截图机位主要展示四英雄，Boss 会在战场远端参与战斗；后续需要专门 Boss 取景和第二阶段技能。
