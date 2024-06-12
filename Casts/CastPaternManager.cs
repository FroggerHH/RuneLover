namespace RuneLover.Casts;

public static class CastPaternManager
{
    private static readonly List<RuneType> CurrentChain = [];
    private static CastDefinition castInProgress;

    internal static Attack _DEBUG_m_Attack;
    internal static Attack BaseAttack;
    public static ItemData TempWeapon;
    public static Transform CastPrefabHolder;

    public static bool InCastExecution { get; set; } = false;

    public static void Init()
    {
        InitBaseAttack();
        InitTempWeapon();
        CastPrefabHolder = new GameObject("CastPrefabHolder").transform;
        CastPrefabHolder.gameObject.SetActive(false);
        DontDestroyOnLoad(CastPrefabHolder);
    }

    private static void InitTempWeapon()
    {
        TempWeapon = new()
        {
            m_shared = new()
            {
                m_name = "rl_TempWeapon",
                m_useDurability = false,
                m_useDurabilityDrain = 0f,
                m_attack = BaseAttack
            }
        };
        TempWeapon.m_shared.m_attack = BaseAttack;
        TempWeapon.m_shared.m_secondaryAttack = new Attack();
    }

    private static void InitBaseAttack()
    {
        BaseAttack = new Attack()
        {
            m_attackAnimation = "staff_summon",
            m_hitTerrain = true,
            m_speedFactor = 0.5f,
            m_speedFactorRotation = 0.3f,
            m_attackStamina = 0,
            m_attackRange = 2,
            m_attackHeight = 1.2f,
            m_projectileVel = 0,
            m_projectileVelMin = 0,
            m_launchAngle = -4,
            m_requiresReload = false
        };
    }

    public static RuneType? GetRuneTypeFromItem(ItemData item) =>
        item.m_dropPrefab.GetPrefabName() switch
        {
            "rl_Rune_Fire" => RuneType.Fire,
            "rl_Rune_Earth" => RuneType.Earth,
            _ => null
        };

    public static void OnNewAttack(RuneType newAttack)
    {
        var chainCount = CurrentChain.Count;
        if (chainCount == 0) CreateCastDef();
        chainCount++;
        CurrentChain.Add(newAttack);

        if (chainCount == 1) SetUpEffectTree(newAttack);
        else if (chainCount == 2) SetUpAttackType(newAttack);
        else if (chainCount == 3) SetUpMainEffect(newAttack);

        //TODO: Detect cast end 
        //TODO: Check if cast is valid
        else if (chainCount == 4)
        {
            ConstructAndExecuteCast();
        }
        else
        {
            CurrentChain.Clear();
            throw new Exception("Chain too long");
        }
    }

    private static void CreateCastDef()
    {
        castInProgress = new CastDefinition();
    }

    private static void SetUpEffectTree(RuneType rune)
    {
        //TODO: Turn to buff if casted with air
        castInProgress.EffectTree = rune;
    }

    private static void SetUpAttackType(RuneType rune)
    {
        castInProgress.AttackType = rune;
    }

    private static void SetUpMainEffect(RuneType rune)
    {
        castInProgress.MainEffect = rune;
    }

    private static void ConstructAndExecuteCast()
    {
        var cast = Cast.Construct(castInProgress);
        CurrentChain.Clear();
        castInProgress = null;

        var pl = m_localPlayer;
        // cast.Execute(pl.position(), pl.GetLookDir());
        cast.Execute();
    }
}

public enum RuneType
{
    Fire,
    Water,
    Earth

    //TODO: Add Air for buff spells
}

public enum EffectTree
{
    Attack,
    Defense,
    Buff

    //TODO: Add EffectTree for more Runes
}

public enum AttackType
{
    Projectile,
    AoE,
    Dome
}

public enum CastEffect
{
    //TODO: Cast effects are available so far only with fire rune
    Flame,
    Burn,
    WaterDrop,
}