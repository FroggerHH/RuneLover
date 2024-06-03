using RuneLover.Helpers;
using RuneLover.Managers;

namespace RuneLover.Casts;

public sealed class WanderingHands : CastBase
{
    public static WanderingHands Instance { get; private set; }
    public GameObject Prefab;

    public override CostType Cost => CostType.Eitr;

    public WanderingHands()
    {
        if (Instance) throw new Exception($"{this.GetType().FullName} already exists");
        Instance = this;
        Definition.Name = "WanderingHands";
        AnimationSpeedManager.Add(AnimSpeedManager);
        Definition.patern = [RuneType.A, RuneType.B, RuneType.B]; 
        Definition.Animation = AnimationReplace.AnimationNames[RlAnimation.MageProjectile];

        Definition.InitConfig(1.5f, 6, 0f, 0.6f, 1f, 15f);

        Definition.Icon = bundle.LoadAsset<Sprite>("WanderingHands_Icon");
        Prefab = bundle.LoadAsset<GameObject>("WanderingHands_Prefab");
    }

    public override bool Execute(bool showfailReson = false, bool skipCooldown = false)
    {
        if (!base.Execute(showfailReson, skipCooldown)) return false;
        var pl = m_localPlayer;
        pl.Message(MessageHud.MessageType.Center, Definition.LocalizedName);
        var effect = pl.m_seman.AddStatusEffect("WanderingHands_Buff".GetStableHashCode(), true) as SE_WanderingHands;
        if (effect) effect.SetLevel(CalculateDuration(), CalculateValue());

        return true;
    }

    private double AnimSpeedManager(Character c, double speed)
    {
        if (!c.InAttack() || !c.m_nview.IsOwner()) return speed;
        var se = c.m_seman.GetStatusEffect(Definition.CachedHashName) as SE_WanderingHands;
        if (se == null) return speed;
        return speed * (1 + se.asBonus / 100f);
    }
}

file class SE_WanderingHands : StatusEffect
{
    public int asBonus;

    public SE_WanderingHands()
    {
        name = "WanderingHands_Buff";
        m_tooltip = "";
        m_icon = WanderingHands.Instance.Definition.Icon;
        m_name = "$rl_WanderingHands";
        m_ttl = 100000;
        m_startEffects = new EffectList
        {
            m_effectPrefabs =
            [
                new EffectList.EffectData()
                {
                    m_attach = true, m_enabled = true, m_inheritParentRotation = true,
                    m_inheritParentScale = true, m_scale = true, m_prefab = WanderingHands.Instance.Prefab
                }
            ]
        };
    }

    public override string GetTooltipString() =>
        $"\nMove speed Increase: {asBonus}%".Localize();

    public override void SetLevel(int ttl, float value)
    {
        base.SetLevel(ttl, value);
        m_ttl = ttl;
        asBonus = (int)value;
    }
}

file static class DB_Patches
{
    private static void Add_SE(ObjectDB odb)
    {
        if (ObjectDB.instance == null || ObjectDB.instance.m_items.Count == 0 ||
            ObjectDB.instance.GetItemPrefab("Amber") == null) return;

        if (!odb.m_StatusEffects.Exists(se => se.name == "WanderingHands_Buff"))
            odb.m_StatusEffects.Add(CreateInstance<SE_WanderingHands>());
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    private static class ObjectDBAwake
    {
        [HarmonyPostfix]
        private static void Postfix(ObjectDB __instance) => Add_SE(__instance);
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    private static class ObjectDBCopyOtherDB
    {
        [HarmonyPostfix]
        private static void Postfix(ObjectDB __instance) => Add_SE(__instance);
    }
}