using System;

using SanGuoXEngine.Data;

namespace SanGuoXEngine.Battle;

/// <summary>One battle actor's vital state: attributes, damage intake and death.</summary>
public sealed class SanguoHealth
{
    private readonly int _atk;
    private readonly int _def;

    public int MaxHp { get; }
    public int Hp { get; private set; }
    public bool Dead => Hp <= 0;

    /// <summary>Raised once when hp reaches zero.</summary>
    public event Action? Died;

    public SanguoHealth(int maxHp, int atk, int def)
    {
        MaxHp = Math.Max(1, maxHp);
        Hp = MaxHp;
        _atk = atk;
        _def = def;
    }

    /// <summary>The attributes of a MonsterValueData row (hp = hplmt).</summary>
    public static SanguoHealth FromValue(SanguoMonsterValue value)
        => new(Math.Max(1, value.Hp), value.Atk, value.Def);

    /// <summary>
    /// jws2's damage intake, simplified to the demo formula: the raw hit loses the defender's
    /// defence and never lands below 1. <paramref name="multiplier"/> is the skill's damage
    /// coefficient (SkillEvent event param / call-bullet scale; 1 for a plain attack).
    /// </summary>
    public int ApplyHit(int attackerAtk, float multiplier = 1f)
    {
        if (Dead) return 0;
        int damage = Math.Max(1, (int)MathF.Round(attackerAtk * multiplier - _def));
        Hp = Math.Max(0, Hp - damage);
        if (Dead) Died?.Invoke();
        return damage;
    }
}
