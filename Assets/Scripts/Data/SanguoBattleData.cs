using System.Collections.Generic;

using XEngine.Runtime;

namespace SanGuoXEngine.Data;

#region skill rows

/// <summary>One entry of a skill's millisecond event timeline (SkillData.SkillEvent_Table).</summary>
public sealed class SanguoSkillEvent
{
    public int EventTime;
    public int EventType;
    public int EventIntParam;
    public float EventParam;
}

/// <summary>One entry of a skill's cancel/switch window table (SkillData.SkillSwitch_Table).</summary>
public sealed class SanguoSkillSwitch
{
    public int SwitchStartTime;
    public int SwitchDuration;
    public int SwitchMinPlayTime;
    public int SwitchPutIn;
    public int SwitchPutOut;
}

/// <summary>One SkillData row, narrowed to what the battle kernel consumes.</summary>
public sealed class SanguoSkill
{
    public int ID;
    public int BelongTo;
    public int KeyPosition;
    public string AnimName = "";
    public string SkillResource = "";
    public int SkillCD;
    public int SwitchAllTime;
    public int ChooseType;
    public float ChooseRadius;
    public float CastRadius;
    public List<SanguoSkillEvent> Events = [];
    public List<SanguoSkillSwitch> Switches = [];
}

/// <summary>The SkillData sheet: every battle-scope skill of the five heroes and the stage's monsters.</summary>
public sealed class SanguoSkillTable : ScriptableObject
{
    public List<SanguoSkill> Skills = [];
}

#endregion

#region monster rows

/// <summary>One MonsterData row (a spawnable actor kind).</summary>
public sealed class SanguoMonster
{
    public int ID;
    public int MonsterType;
    public int Camp;
    public int AIID;
    public int ModelID;
    public float Speed;
    public List<int> Skills = [];
}

public sealed class SanguoMonsterTable : ScriptableObject
{
    public List<SanguoMonster> Monsters = [];
}

/// <summary>One MonsterModelData row: what the actor looks like and how big its shove circle is.</summary>
public sealed class SanguoMonsterModel
{
    public int ID;
    public string Path = "";
    public float ShoveDiameterX;
    public float ShoveDiameterZ;
    public float ModelScale;
    public float DmgNumHeight;
}

public sealed class SanguoMonsterModelTable : ScriptableObject
{
    public List<SanguoMonsterModel> Models = [];
}

/// <summary>One MonsterValueData row: the attributes of a monster group at one difficulty.</summary>
public sealed class SanguoMonsterValue
{
    public int GroupID;
    public int Difficulty;
    public int Hp;
    public int Atk;
    public int Def;
}

public sealed class SanguoMonsterValueTable : ScriptableObject
{
    public List<SanguoMonsterValue> Values = [];
}

#endregion

#region call bullets and missiles

/// <summary>One CallBulletData row: what a skill event spawns (a missile, another monster, …).</summary>
public sealed class SanguoCallBullet
{
    public int ID;
    public int BulletID;
    public int TrackerID;
    public int MonsterID;
    public int DelayTime;
    public int Number;
    public float Radius;
}

public sealed class SanguoCallBulletTable : ScriptableObject
{
    public List<SanguoCallBullet> CallBullets = [];
}

/// <summary>One MissileData row, narrowed to the hit-shape and movement fields the kernel consumes.</summary>
public sealed class SanguoMissile
{
    public int ID;
    public int ShapeType;
    public float ShapeParam1;
    public float ShapeParam2;
    public float ShapeParam3;
    public float ColliderY;
    public int EffectCamp;
    public int TotalTime;
    public int DamageStartFrame;
    public int DamageEndFrame;
    public int DamageIntervalFrame;
    public int MoveType;
    public float MoveSpeed;
}

public sealed class SanguoMissileTable : ScriptableObject
{
    public List<SanguoMissile> Missiles = [];
}

#endregion

#region level layout

/// <summary>One spawn item of the stage layout: monsters (Type 2), spawn markers (Type 1), …</summary>
public sealed class SanguoLevelItem
{
    public int LevelItemID;
    public int LevelItemType;
    public float PosX;
    public float PosZ;
    public float DirX;
    public float DirZ;
    public int Count;
    public int Row;
    public int Col;
    public float Gap;
    public int LeaderID;
    public int RefreshCount;
    public List<int> MonsterIDs = [];
    public List<int> MonsterWeights = [];
}

/// <summary>LevelLayoutData1011: the 桃园 stage's spawn layout.</summary>
public sealed class SanguoLevelLayout : ScriptableObject
{
    public int LevelID;
    public List<SanguoLevelItem> Items = [];
}

#endregion
