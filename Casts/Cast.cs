using RuneLover.Casts.Mono;

namespace RuneLover.Casts;

public class Cast
{
    public readonly EffectTree EffectTree;
    public readonly AttackType AttackType;
    public readonly CastEffect MainEffect;
    public readonly Attack m_Attack;
    public readonly CastPrefabMono Prefab;

    public static Cast Construct(CastDefinition def)
    {
        var effectTree = def.EffectTree switch
        {
            RuneType.Fire => EffectTree.Attack,
            RuneType.Earth => EffectTree.Defense,
            RuneType.Water => EffectTree.Buff,
            _ => throw new ArgumentOutOfRangeException(nameof(def.EffectTree), def.EffectTree,
                "This rune can not be used as effect tree")
        };

        AttackType attackType;
        CastPrefabMono prefab = null;
        var attack = CastPaternManager.BaseAttack.Clone();
        switch (def.AttackType)
        {
            case RuneType.Fire:
                attackType = AttackType.Projectile;
                prefab = ProjectilePrefab;
                attack.m_attackType = Attack.AttackType.Projectile;
                break;
            case RuneType.Earth:
                attackType = AttackType.AoE;
                prefab = AoEPrefab;
                break;
            case RuneType.Water:
                attackType = AttackType.Dome;
                prefab = DomePrefab;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(def.AttackType), def.AttackType,
                    "This rune can not be used as attack type");
        }

        VisualSettings visualSettings = new();
        switch (def.MainEffect)
        {
            case RuneType.Fire:
                visualSettings.MainColor = new Color(0.75f, 0.04f, 0);
                visualSettings.SecondaryColor = new Color(0.84f, 0.65f, 0.115f);
                break;
            case RuneType.Earth:
                visualSettings.MainColor = new Color(0.61f, 0.4f, 0.23f);
                visualSettings.SecondaryColor = new Color(0.65f, 0.84f, 0.14f);
                break;
            case RuneType.Water:
                visualSettings.MainColor = new Color(0, 0.5f, 0.8f);
                visualSettings.SecondaryColor = new Color(0.12f, 0.79f, 0.8f);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(def.MainEffect));
        }

        visualSettings.Scale = attackType switch
        {
            AttackType.Projectile => new(1, 1, 3.75f),
            AttackType.Dome => new(5, 5, 5),
        };
        var mainEffect = SetUpMainEffect(effectTree, attackType, def.MainEffect, visualSettings);

        prefab = Instantiate(prefab, CastPaternManager.CastPrefabHolder);
        prefab.VisualSettings = visualSettings;
        prefab.InitReferences();
        prefab.ApplyVisualSettings();
        attack.m_attackProjectile = prefab.gameObject;

        return new Cast(effectTree, attackType, mainEffect, attack, prefab);
    }

    private static CastEffect SetUpMainEffect(EffectTree effectTree, AttackType attackType, RuneType mainEffectRune,
        VisualSettings visualSettings)
    {
        switch (effectTree)
        {
            case EffectTree.Attack: return ForAttackEffects();
            case EffectTree.Defense: throw new NotImplementedException("EffectTree.Defense not implemented");
            case EffectTree.Buff: throw new NotImplementedException("EffectTree.Buff not implemented");

            default:
                throw new ArgumentOutOfRangeException(nameof(effectTree), effectTree,
                    "This rune can not be used as effect tree");
        }

        CastEffect ForAttackEffects()
        {
            switch (attackType)
            {
                case AttackType.Projectile: return ProjectileCastEffect();
                case AttackType.AoE: throw new NotImplementedException("AttackType.AoE not implemented");
                case AttackType.Dome: throw new NotImplementedException("AttackType.Dome not implemented");
                default:
                    throw new ArgumentOutOfRangeException(nameof(attackType), attackType,
                        "This rune can not be used as attack type");
            }

            CastEffect ProjectileCastEffect()
            {
                switch (mainEffectRune)
                {
                    case RuneType.Fire:
                        return CastEffect.Flame;
                    case RuneType.Earth:
                        visualSettings.Scale = new Vector3(2, 2, 2);
                        return CastEffect.Burn;
                    case RuneType.Water:
                        visualSettings.Scale = new Vector3(1, 1, 1);
                        return CastEffect.WaterDrop;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mainEffectRune), mainEffectRune,
                            "This rune can not be used as main effect");
                }
            }
        }
    }

    private Cast(EffectTree effectTree, AttackType attackType, CastEffect mainEffect, Attack attack,
        CastPrefabMono prefab)
    {
        EffectTree = effectTree;
        AttackType = attackType;
        MainEffect = mainEffect;
        m_Attack = attack;
        Prefab = prefab;
    }

    public void Execute()
    {
        var pl = m_localPlayer;
        pl.Message(MessageHud.MessageType.Center, "Cast started");
        CastPaternManager._DEBUG_m_Attack = m_Attack.Clone();
        CastPaternManager.InCastExecution = true;
        var result = m_Attack.Start(pl,
            pl.m_body, pl.m_zanim, pl.m_animEvent, pl.m_visEquipment,
            CastPaternManager.TempWeapon, null, 999, 1);
        CastPaternManager.InCastExecution = false;
        pl.ClearActionQueue();
        pl.StartAttackGroundCheck();
        pl.m_currentAttack = m_Attack;
        pl.m_currentAttackIsSecondary = false;
        pl.m_lastCombatTimer = 0.0f;
        Debug($"{EffectTree} {AttackType} {MainEffect} casted -> {result.ToString().ToUpper()}");
    }
}