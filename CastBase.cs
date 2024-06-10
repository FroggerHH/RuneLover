namespace RuneLover;

public abstract class CastBase
{
    public static List<CastBase> AllInstances { get; private set; } = [];
    public CastDefinition Definition { get; } = new();

    public virtual CostType Cost => CostType.Stamina;
    public virtual int ManaCost => 1;

    [Flags]
    public enum CostType
    {
        None = 0,
        Eitr = 1,
        Stamina = 2,
        Health = 4,
    }

    protected CastBase()
    {
        AllInstances.Add(this);
    }

    protected internal CastBase Clone() => (CastBase)MemberwiseClone();

    public static implicit operator bool(CastBase cast) => cast is not null;

    public virtual bool Execute(bool showfailReson = false, bool skipCooldown = false)
    {
        if (!m_localPlayer) return false;
        if (!CanExecute(showfailReson, skipCooldown)) return false;
        StartCooldown(CalculateCooldown());
        return true;
    }

    public virtual bool CanExecute(bool showfailReson = false, bool skipCooldown = false)
    {
        if (!m_localPlayer) return false;
        if (!skipCooldown && _Internal_Cooldown > 0)
        {
            if (showfailReson)
                m_localPlayer.Message(MessageHud.MessageType.TopLeft, "$rl_msg_in_cooldown".Localize());
            return false;
        }

        return true;
    }

    private Coroutine _Internal_Cooldown_Coroutine;
    private float _Internal_Cooldown;

    public void StartCooldown(float time)
    {
        if (_Internal_Cooldown_Coroutine != null) GetPlugin().StopCoroutine(_Internal_Cooldown_Coroutine);
        _Internal_Cooldown = 0;

        //TODO: checks 

        if (time <= 0) return;
        _Internal_Cooldown_Coroutine = GetPlugin().StartCoroutine(Cooldown(time));
    }

    private IEnumerator Cooldown(float cooldown)
    {
        _Internal_Cooldown = cooldown;
        while (_Internal_Cooldown > 0)
        {
            _Internal_Cooldown -= Time.deltaTime;
            yield return null;
        }

        _Internal_Cooldown = 0;
    }

    public virtual int CalculateDuration()
    {
        var minValue = Definition.MinDuration.Value;
        var maxValue = Definition.MaxDuration.Value;
        return (int)Lerp(minValue, maxValue, Random.value);
    }

    public virtual float CalculateCooldown()
    {
        var minValue = Definition.MinCooldown.Value;
        var maxValue = Definition.MaxCooldown.Value;
        var cooldown = Lerp(minValue, maxValue, Random.value);

        return cooldown;
    }

    public virtual float CalculateValue()
    {
        var minValue = Definition.MinValue.Value;
        var maxValue = Definition.MaxValue.Value;
        var value = Lerp(minValue, maxValue, Random.value);
        return value;
    }

    protected virtual bool Add_SE(ObjectDB odb)
    {
        if (ObjectDB.instance == null || ObjectDB.instance.m_items.Count == 0 ||
            ObjectDB.instance.GetItemPrefab("Amber") == null) return false;
        return true;
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    private static class ObjectDBAwake
    {
        [UsedImplicitly, HarmonyPostfix]
        private static void Postfix(ObjectDB __instance)
        {
            foreach (var cast in AllInstances) cast.Add_SE(__instance);
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    private static class ObjectDBCopyOtherDB
    {
        [UsedImplicitly, HarmonyPostfix]
        private static void Postfix(ObjectDB __instance)
        {
            foreach (var cast in AllInstances) cast.Add_SE(__instance);
        }
    }
}