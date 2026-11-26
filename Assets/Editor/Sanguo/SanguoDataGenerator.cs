using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using SanGuoXEngine.Data;

using XEngine.Editor;
using XEngine.Editor.Projects;
using XEngine.Runtime;

namespace SanGuoXEngine.Editor;

/// <summary>
/// Turns the offline JSON intermediates (<c>Tools/sanguo_import/extract_tables.py</c> →
/// <c>Export/data/*.json</c>) into native data assets under <c>Assets/Data/</c>, narrowed to the
/// 桃园 battle scope: the five heroes' skills, the stage's monsters (level 1011) with their
/// models, values, skills and the call bullets / missiles those skills reference.
///
/// The JSON files stay gitignored intermediates; the runtime only ever reads the generated
/// native assets. Zero Lua reaches the repository or the runtime (NoLuaGuardTests pins that).
/// </summary>
public static class SanguoDataGenerator
{
    public const string DataFolder = "Data";
    private static readonly string[] HeroNames = ["GuanYu", "ZhaoYun", "LiuBei", "ZhangFei", "LvBu"];

    private static Guid StableGuid(params string[] parts)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        byte[] digest = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes("xengine:sanguo:" + string.Join(":", parts)));
        return new Guid(digest);
    }

    [MenuItem("Sanguo/Import/6 Generate Battle Data")]
    private static void MenuData()
    {
        string rootPath = Project.Current?.RootPath ?? throw new InvalidOperationException("No project is open.");
        Debug.Log("[Sanguo] " + GenerateData(Path.Combine(rootPath, "Export/data"), Path.Combine(Project.Current!.AssetsPath, DataFolder)));
    }

    /// <summary>Reads the JSON intermediates and writes the native data assets. Returns a summary.</summary>
    public static string GenerateData(string jsonFolder, string assetsDataFolder)
    {
        List<string> warnings = new();
        var (skillTable, bulletTable, missileTable, monsterTable, modelTable, valueTable, layoutAsset)
            = BuildTables(jsonFolder, warnings);

        // ── write
        Directory.CreateDirectory(assetsDataFolder);
        WriteAsset(skillTable, $"{DataFolder}/SanguoSkills.asset", StableGuid("data", "skills"));
        WriteAsset(bulletTable, $"{DataFolder}/SanguoCallBullets.asset", StableGuid("data", "call-bullets"));
        WriteAsset(missileTable, $"{DataFolder}/SanguoMissiles.asset", StableGuid("data", "missiles"));
        WriteAsset(monsterTable, $"{DataFolder}/SanguoMonsters.asset", StableGuid("data", "monsters"));
        WriteAsset(modelTable, $"{DataFolder}/SanguoMonsterModels.asset", StableGuid("data", "monster-models"));
        WriteAsset(valueTable, $"{DataFolder}/SanguoMonsterValues.asset", StableGuid("data", "monster-values"));
        WriteAsset(layoutAsset, $"{DataFolder}/SanguoLevel1011.asset", StableGuid("data", "level-1011"));

        string summary = $"skills {skillTable.Skills.Count}, bullets {bulletTable.CallBullets.Count}, "
            + $"missiles {missileTable.Missiles.Count}, monsters {monsterTable.Monsters.Count}, "
            + $"models {modelTable.Models.Count}, values {valueTable.Values.Count}, "
            + $"layout items {layoutAsset.Items.Count}; {warnings.Count} warning(s)";
        foreach (string warning in warnings) summary += "\n  ! " + warning;
        return summary;
    }

    /// <summary>Pure part: parse the JSON intermediates into the populated table assets (no IO
    /// beyond reading the JSON, no project state) — unit-testable without an open project.</summary>
    public static (SanguoSkillTable skillTable, SanguoCallBulletTable bulletTable, SanguoMissileTable missileTable,
        SanguoMonsterTable monsterTable, SanguoMonsterModelTable modelTable, SanguoMonsterValueTable valueTable,
        SanguoLevelLayout layoutAsset) BuildTables(string jsonFolder, List<string> warnings)
    {
        List<JsonElement> skills = ReadRows(Path.Combine(jsonFolder, "skills.json"));
        List<JsonElement> callBullets = ReadRows(Path.Combine(jsonFolder, "call-bullets.json"));
        List<JsonElement> missiles = ReadRows(Path.Combine(jsonFolder, "missiles.json"));
        List<JsonElement> monsters = ReadRows(Path.Combine(jsonFolder, "monsters.json"));
        List<JsonElement> models = ReadRows(Path.Combine(jsonFolder, "monster-models.json"));
        List<JsonElement> values = ReadRows(Path.Combine(jsonFolder, "monster-values.json"));
        JsonElement layout = JsonDocument.Parse(File.ReadAllText(Path.Combine(jsonFolder, "level-1011.json"))).RootElement;

        // ── scope: hero skills by resource + the level monsters' own skills
        HashSet<int> monsterIDs = MonsterIDsOf(layout);
        Dictionary<int, JsonElement> monsterByid = IndexBy(monsters, "ID");
        HashSet<int> skillIDs = new();
        SanguoSkillTable skillTable = new() { Name = "SanguoSkills" };
        foreach (JsonElement row in skills)
        {
            string resource = Str(row, "SkillResource");
            bool heroSkill = false;
            foreach (string hero in HeroNames)
                if (resource.Contains($"Hero_{hero}/", StringComparison.Ordinal))
                    heroSkill = true;
            int id = Int(row, "ID");
            bool monsterSkill = monsterByid.TryGetValue(id, out _) == false
                && MonsterWantsSkill(monsterByid, monsterIDs, id);
            if (!heroSkill && !monsterSkill) continue;
            skillIDs.Add(id);
            skillTable.Skills.Add(ReadSkill(row));
        }

        // ── transitive call bullets + missiles: the events of the kept skills spawn bullets
        Dictionary<int, JsonElement> bulletByid = IndexBy(callBullets, "ID");
        Dictionary<int, JsonElement> missileByid = IndexBy(missiles, "ID");
        SanguoCallBulletTable bulletTable = new() { Name = "SanguoCallBullets" };
        SanguoMissileTable missileTable = new() { Name = "SanguoMissiles" };
        HashSet<int> bulletIDs = new();
        foreach (JsonElement row in skills)
        {
            if (!skillIDs.Contains(Int(row, "ID"))) continue;
            foreach (JsonElement ev in Enumerate(row, "SkillEvent_Table"))
            {
                if (Int(ev, "EventType") != 1) continue;
                int bulletId = Int(ev, "EventIntParam");
                if (bulletId == 0 || !bulletByid.TryGetValue(bulletId, out JsonElement bullet) || !bulletIDs.Add(bulletId))
                    continue;
                bulletTable.CallBullets.Add(ReadCallBullet(bullet));
                int missileId = Int(bullet, "BulletID");
                if (missileId != 0 && missileByid.TryGetValue(missileId, out JsonElement missile)
                    && missileTable.Missiles.Exists(m => m.ID == missileId) == false)
                    missileTable.Missiles.Add(ReadMissile(missile));
            }
        }

        // ── monsters + models + values of the stage
        SanguoMonsterTable monsterTable = new() { Name = "SanguoMonsters" };
        SanguoMonsterModelTable modelTable = new() { Name = "SanguoMonsterModels" };
        SanguoMonsterValueTable valueTable = new() { Name = "SanguoMonsterValues" };
        Dictionary<int, JsonElement> valueRows = new();
        foreach (JsonElement row in values)
        {
            int group = Int(row, "GroupID");
            if (group == 0 || valueRows.ContainsKey(group)) continue;
            valueRows[group] = row;
        }
        foreach (int monsterId in monsterIDs)
        {
            if (!monsterByid.TryGetValue(monsterId, out JsonElement monster))
            {
                warnings.Add($"monster {monsterId} missing from MonsterData");
                continue;
            }
            SanguoMonster entry = ReadMonster(monster);
            monsterTable.Monsters.Add(entry);
            foreach (int skillId in entry.Skills) skillIDs.Add(skillId);
            int modelId = entry.ModelID;
            foreach (JsonElement model in models)
            {
                if (Int(model, "ID") != modelId) continue;
                modelTable.Models.Add(ReadMonsterModel(model));
                break;
            }
            if (valueRows.TryGetValue(monsterId, out JsonElement value))
                valueTable.Values.Add(ReadMonsterValue(value));
            else
                warnings.Add($"monster {monsterId} has no MonsterValueData row (difficulty 1)");
        }

        // the monster skills above may reference skills filtered out in the first pass — keep them
        foreach (JsonElement row in skills)
            if (skillIDs.Contains(Int(row, "ID")) && skillTable.Skills.Exists(s => s.ID == Int(row, "ID")) == false)
                skillTable.Skills.Add(ReadSkill(row));

        // ── level layout
        SanguoLevelLayout layoutAsset = ReadLayout(layout);
        layoutAsset.Name = "SanguoLevel1011";

        return (skillTable, bulletTable, missileTable, monsterTable, modelTable, valueTable, layoutAsset);
    }

    #region readers

    private static SanguoSkill ReadSkill(JsonElement row) => new()
    {
        ID = Int(row, "ID"),
        BelongTo = Int(row, "BelongTo"),
        KeyPosition = Int(row, "KeyPosition"),
        AnimName = Str(row, "AnimName"),
        SkillResource = Str(row, "SkillResource"),
        SkillCD = Int(row, "SkillCD"),
        SwitchAllTime = Int(row, "SwitchAllTime"),
        ChooseType = Int(row, "ChooseType"),
        ChooseRadius = Float(row, "ChooseRadius"),
        CastRadius = Float(row, "CastRadius"),
        Events = [.. Enumerate(row, "SkillEvent_Table").Select(ReadSkillEvent)],
        Switches = [.. Enumerate(row, "SkillSwitch_Table").Select(ReadSkillSwitch)],
    };

    private static SanguoSkillEvent ReadSkillEvent(JsonElement e) => new()
    {
        EventTime = Int(e, "EventTime"),
        EventType = Int(e, "EventType"),
        EventIntParam = Int(e, "EventIntParam"),
        EventParam = Float(e, "EventParam"),
    };

    private static SanguoSkillSwitch ReadSkillSwitch(JsonElement e) => new()
    {
        SwitchStartTime = Int(e, "SwitchStartTime"),
        SwitchDuration = Int(e, "SwitchDuration"),
        SwitchMinPlayTime = Int(e, "SwitchMinPlayTime"),
        SwitchPutIn = Int(e, "SwitchPutIn"),
        SwitchPutOut = Int(e, "SwitchPutOut"),
    };

    private static SanguoMonster ReadMonster(JsonElement row) => new()
    {
        ID = Int(row, "ID"),
        MonsterType = Int(row, "MonsterType"),
        Camp = Int(row, "Camp"),
        AIID = Int(row, "AIID"),
        ModelID = Int(row, "ModelID"),
        Speed = Float(row, "Speed"),
        Skills = [.. Enumerate(row, "SkillIDData_Table").Select(e => Int(e, "ID")).Where(id => id != 0)],
    };

    private static SanguoMonsterModel ReadMonsterModel(JsonElement row) => new()
    {
        ID = Int(row, "ID"),
        Path = Str(row, "Path"),
        ShoveDiameterX = Float(row, "ShoveDiameterX"),
        ShoveDiameterZ = Float(row, "ShoveDiameterZ"),
        ModelScale = Float(row, "ModelScale"),
        DmgNumHeight = Float(row, "DmgNumHeight"),
    };

    private static SanguoMonsterValue ReadMonsterValue(JsonElement row) => new()
    {
        GroupID = Int(row, "GroupID"),
        Difficulty = Int(row, "Difficulty"),
        Hp = Int(row, "hplmt"),
        Atk = Int(row, "atk"),
        Def = Int(row, "def"),
    };

    private static SanguoCallBullet ReadCallBullet(JsonElement row) => new()
    {
        ID = Int(row, "ID"),
        BulletID = Int(row, "BulletID"),
        TrackerID = Int(row, "TrackerID"),
        MonsterID = Int(row, "MonsterID"),
        DelayTime = Int(row, "DelayTime"),
        Number = Int(row, "Number"),
        Radius = Float(row, "Radius"),
    };

    private static SanguoMissile ReadMissile(JsonElement row) => new()
    {
        ID = Int(row, "ID"),
        ShapeType = Int(row, "ShapeType"),
        ShapeParam1 = Float(row, "ShapeParam1"),
        ShapeParam2 = Float(row, "ShapeParam2"),
        ShapeParam3 = Float(row, "ShapeParam3"),
        ColliderY = Float(row, "ColliderY"),
        EffectCamp = Int(row, "EffectCamp"),
        TotalTime = Int(row, "TotalTime"),
        DamageStartFrame = Int(row, "DamageStartFrame"),
        DamageEndFrame = Int(row, "DamageEndFrame"),
        DamageIntervalFrame = Int(row, "DamageIntervalFrame"),
        MoveType = Int(row, "MoveType"),
        MoveSpeed = Float(row, "MoveSpeed"),
    };

    private static SanguoLevelLayout ReadLayout(JsonElement layout) => new()
    {
        LevelID = Int(layout, "LevelID"),
        Items = [.. Enumerate(layout, "LevelItemList").Select(ReadLevelItem)],
    };

    private static SanguoLevelItem ReadLevelItem(JsonElement item)
    {
        List<int> ids = [];
        List<int> weights = [];
        foreach (JsonElement weighted in Enumerate(item, "WeightMonsterIDList"))
        {
            ids.Add(Int(weighted, "MonsterID"));
            JsonElement weight = weighted.GetProperty("Weight");
            // FP.newV1 unwraps to a one-element array
            weights.Add(weight.ValueKind == JsonValueKind.Array && weight.GetArrayLength() > 0
                ? (int)MathF.Round(weight[0].GetSingle())
                : (int)MathF.Round(weight.GetSingle()));
        }
        return new SanguoLevelItem
        {
            LevelItemID = Int(item, "LevelItemID"),
            LevelItemType = Int(item, "LevelItemType"),
            PosX = FloatAt(item, "ItemPos", 0),
            PosZ = FloatAt(item, "ItemPos", 1),
            DirX = FloatAt(item, "ItemDir", 0),
            DirZ = FloatAt(item, "ItemDir", 1),
            Count = Int(item, "Count"),
            Row = Int(item, "Row"),
            Col = Int(item, "Col"),
            Gap = FloatAt(item, "Gap", 0),
            LeaderID = Int(item, "LeaderID"),
            RefreshCount = Int(item, "RefreshCount"),
            MonsterIDs = ids,
            MonsterWeights = weights,
        };
    }

    #endregion

    #region helpers

    private static bool MonsterWantsSkill(Dictionary<int, JsonElement> monsterByid, HashSet<int> monsterIDs, int skillId)
    {
        foreach (int monsterId in monsterIDs)
        {
            if (!monsterByid.TryGetValue(monsterId, out JsonElement monster)) continue;
            foreach (JsonElement entry in Enumerate(monster, "SkillIDData_Table"))
                if (Int(entry, "ID") == skillId) return true;
        }
        return false;
    }

    private static HashSet<int> MonsterIDsOf(JsonElement layout)
    {
        HashSet<int> ids = new();
        foreach (JsonElement item in Enumerate(layout, "LevelItemList"))
            foreach (JsonElement weighted in Enumerate(item, "WeightMonsterIDList"))
                if (weighted.TryGetProperty("MonsterID", out JsonElement id)) ids.Add(id.GetInt32());
        return ids;
    }

    private static List<JsonElement> ReadRows(string path)
        => [.. JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateArray()];

    private static Dictionary<int, JsonElement> IndexBy(List<JsonElement> rows, string key)
    {
        Dictionary<int, JsonElement> index = new();
        foreach (JsonElement row in rows) index[Int(row, key)] = row;
        return index;
    }

    private static IEnumerable<JsonElement> Enumerate(JsonElement row, string field)
        => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(field, out JsonElement value)
            && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    private static int Int(JsonElement row, string field)
        => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(field, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    private static float Float(JsonElement row, string field)
        => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(field, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number ? value.GetSingle() : 0f;

    private static float FloatAt(JsonElement row, string field, int index)
        => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(field, out JsonElement value)
            && value.ValueKind == JsonValueKind.Array && index < value.GetArrayLength()
            && value[index].ValueKind == JsonValueKind.Number ? value[index].GetSingle() : 0f;

    private static string Str(JsonElement row, string field)
        => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(field, out JsonElement value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static void WriteAsset(ScriptableObject asset, string enginePath, Guid guid)
        => SanguoHeroGenerator.WriteNative(asset, enginePath, guid, "ScriptableObjectImporter");

    #endregion
}
