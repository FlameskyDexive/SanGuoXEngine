using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using SanGuoXEngine.Data;
using SanGuoXEngine.Editor;

using Xunit;

namespace SanGuoXEngine.Tests;

/// <summary>
/// The M4-1 data pipeline's C# side: <see cref="SanguoDataGenerator.BuildTables"/> narrows the
/// extracted Lua JSON intermediates into the battle-scope table assets on synthetic fixtures.
/// </summary>
public sealed class SanguoDataGeneratorTests
{
    private static void WriteJson(string folder, string name, string json)
        => File.WriteAllText(Path.Combine(folder, name), json);

    private static string WriteFixtureFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "sanguo-data-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        // skills: two GuanYu skills (one spawning a bullet), one monster skill (500083), one unrelated
        WriteJson(folder, "skills.json", """
        [
          {"ID": 100001, "BelongTo": 100001, "KeyPosition": 1, "AnimName": "n1",
           "SkillResource": "Skill/Hero_GuanYu/Attack_01", "SkillCD": 0, "SwitchAllTime": 500,
           "ChooseType": 20, "ChooseRadius": 5, "CastRadius": 180,
           "SkillEvent_Table": [{"EventTime": 200, "EventType": 1, "EventIntParam": 900001, "EventParam": 2}],
           "SkillSwitch_Table": [{"SwitchStartTime": 0, "SwitchDuration": 500, "SwitchMinPlayTime": 0, "SwitchPutIn": 8, "SwitchPutOut": 100009}]},
          {"ID": 100002, "BelongTo": 100001, "KeyPosition": 2, "AnimName": "n2",
           "SkillResource": "Skill/Hero_GuanYu/Attack_02", "SkillCD": 0, "SwitchAllTime": 750,
           "ChooseType": 0, "ChooseRadius": 0, "CastRadius": 0,
           "SkillEvent_Table": [], "SkillSwitch_Table": []},
          {"ID": 500083, "BelongTo": 500083, "KeyPosition": 1, "AnimName": "a1",
           "SkillResource": "Skill/Monster_YellowArmy/Attack_01", "SkillCD": 0, "SwitchAllTime": 0,
           "ChooseType": 0, "ChooseRadius": 0, "CastRadius": 0,
           "SkillEvent_Table": [], "SkillSwitch_Table": []},
          {"ID": 999999, "BelongTo": 999999, "KeyPosition": 1, "AnimName": "x",
           "SkillResource": "Skill/Other/Whatever", "SkillCD": 0, "SwitchAllTime": 0,
           "ChooseType": 0, "ChooseRadius": 0, "CastRadius": 0,
           "SkillEvent_Table": [], "SkillSwitch_Table": []}
        ]
        """);

        WriteJson(folder, "call-bullets.json", """
        [
          {"ID": 900001, "BulletID": 910001, "TrackerID": 0, "MonsterID": 0, "DelayTime": 0, "Number": 1, "Radius": 2.5}
        ]
        """);

        WriteJson(folder, "missiles.json", """
        [
          {"ID": 910001, "ShapeType": 1, "ShapeParam1": 3.5, "ShapeParam2": 90, "ShapeParam3": 0,
           "ColliderY": 1, "EffectCamp": 2, "TotalTime": 600, "DamageStartFrame": 3,
           "DamageEndFrame": 12, "DamageIntervalFrame": 5, "MoveType": 1, "MoveSpeed": 24}
        ]
        """);

        WriteJson(folder, "monsters.json", """
        [
          {"ID": 101103, "MonsterType": 2, "Camp": 1, "AIID": 101102, "ModelID": 22056, "Speed": 8,
           "SkillIDData_Table": [{"ID": 500083}]},
          {"ID": 101172, "MonsterType": 51, "Camp": 2, "AIID": 101170, "ModelID": 4004, "Speed": 4.5,
           "SkillIDData_Table": []}
        ]
        """);

        WriteJson(folder, "monster-models.json", """
        [
          {"ID": 22056, "Path": "Assets/Actor/Monster/Monster_YellowArmy/Monster_YellowArmy.prefab",
           "ShoveDiameterX": 1.4, "ShoveDiameterZ": 1.4, "ModelScale": 1, "DmgNumHeight": 2.2}
        ]
        """);

        WriteJson(folder, "monster-values.json", """
        [
          {"GroupID": 101103, "Difficulty": 1, "PickDropGrounp": 0, "hplmt": 95, "atk": 58, "def": 24}
        ]
        """);

        WriteJson(folder, "level-1011.json", """
        {
          "LevelID": 1011,
          "LevelItemList": [
            {"LevelItemID": 1001, "LevelItemType": 1, "ItemPos": [111.459, 777.805], "ItemDir": [0, -1],
             "ActiveDefault": true},
            {"WeightMonsterIDList": [{"MonsterID": 101103, "Weight": [100]}], "LevelItemID": 2010,
             "LevelItemType": 2, "ItemPos": [111.26, 702.06], "ItemDir": [0, 1], "Count": 8,
             "Row": 4, "Col": 2, "Gap": [2], "LeaderID": 2001, "RefreshCount": -1}
          ]
        }
        """);
        return folder;
    }

    [Fact]
    public void BuildTables_KeepsHeroAndMonsterSkills_DropsUnrelated()
    {
        List<string> warnings = new();
        string folder = WriteFixtureFolder();
        try
        {
            var (skillTable, bulletTable, missileTable, monsterTable, modelTable, valueTable, layout)
                = SanguoDataGenerator.BuildTables(folder, warnings);

            Assert.Equal(new[] { 100001, 100002, 500083 }, new[] { skillTable.Skills[0].ID, skillTable.Skills[1].ID, skillTable.Skills[2].ID });
            Assert.DoesNotContain(skillTable.Skills, s => s.ID == 999999);   // out of scope
            Assert.Equal(3, skillTable.Skills.Count);                        // 2 hero rows + the monster skill

            SanguoSkill guanyu = skillTable.Skills[0];
            Assert.Equal("n1", guanyu.AnimName);
            Assert.Equal(500, guanyu.SwitchAllTime);
            Assert.Equal(20, guanyu.ChooseType);
            Assert.Equal(5f, guanyu.ChooseRadius);
            Assert.Equal(180f, guanyu.CastRadius);
            SanguoSkillEvent @event = Assert.Single(guanyu.Events);
            Assert.Equal((200, 1, 900001, 2f), (@event.EventTime, @event.EventType, @event.EventIntParam, @event.EventParam));
            SanguoSkillSwitch @switch = Assert.Single(guanyu.Switches);
            Assert.Equal((0, 500, 0, 8, 100009),
                (@switch.SwitchStartTime, @switch.SwitchDuration, @switch.SwitchMinPlayTime, @switch.SwitchPutIn, @switch.SwitchPutOut));

            // the GuanYu event spawns bullet 900001 → missile 910001; the monster skill has no events
            SanguoCallBullet bullet = Assert.Single(bulletTable.CallBullets);
            Assert.Equal((900001, 910001, 1, 2.5f), (bullet.ID, bullet.BulletID, bullet.Number, bullet.Radius));
            SanguoMissile missile = Assert.Single(missileTable.Missiles);
            Assert.Equal((910001, 1, 3.5f, 600), (missile.ID, missile.ShapeType, missile.ShapeParam1, missile.TotalTime));
            Assert.Equal((3, 12, 5), (missile.DamageStartFrame, missile.DamageEndFrame, missile.DamageIntervalFrame));

            // only monsters referenced by the LAYOUT are scoped in (101172 sits unused in MonsterData)
            SanguoMonster yellowArmy = Assert.Single(monsterTable.Monsters);
            Assert.Equal((101103, 2, 1, 101102, 22056, 8f),
                (yellowArmy.ID, yellowArmy.MonsterType, yellowArmy.Camp, yellowArmy.AIID, yellowArmy.ModelID, yellowArmy.Speed));
            Assert.Equal(new[] { 500083 }, yellowArmy.Skills.ToArray());
            SanguoMonsterModel model = Assert.Single(modelTable.Models);
            Assert.Equal(22056, model.ID);
            Assert.Equal((1.4f, 2.2f), (model.ShoveDiameterX, model.DmgNumHeight));
            SanguoMonsterValue value = Assert.Single(valueTable.Values);
            Assert.Equal((101103, 95, 58, 24), (value.GroupID, value.Hp, value.Atk, value.Def));
            Assert.Empty(warnings);

            // the layout keeps both items; vectors destructure into x/z pairs and the weight unwraps
            Assert.Equal(1011, layout.LevelID);
            Assert.Equal(2, layout.Items.Count);
            SanguoLevelItem marker = layout.Items[0];
            Assert.Equal((1001, 1, 111.459f, 777.805f, 0f, -1f), (marker.LevelItemID, marker.LevelItemType, marker.PosX, marker.PosZ, marker.DirX, marker.DirZ));
            SanguoLevelItem spawn = layout.Items[1];
            Assert.Equal((2010, 2, 8, 4, 2, 2f, 2001, -1), (spawn.LevelItemID, spawn.LevelItemType, spawn.Count, spawn.Row, spawn.Col, spawn.Gap, spawn.LeaderID, spawn.RefreshCount));
            Assert.Equal([101103], spawn.MonsterIDs);
            Assert.Equal([100], spawn.MonsterWeights);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
