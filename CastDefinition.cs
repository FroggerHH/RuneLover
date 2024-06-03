using BepInEx.Configuration;

namespace RuneLover;

public class CastDefinition
{
    public string Name;
    public string Animation;
    public float AnimationTime;
    public Sprite Icon;
    public List<RuneType> patern = [];

    public int CachedHashName => _cachedHashName ??= Name.GetStableHashCode();
    private int? _cachedHashName = null;
    public ConfigEntry<float> MinDuration;
    public ConfigEntry<float> MaxDuration;
    public ConfigEntry<float> MinCooldown;
    public ConfigEntry<float> MaxCooldown;
    public ConfigEntry<float> MinValue;
    public ConfigEntry<float> MaxValue;

    public void InitConfig(float minDuration, float maxDuration, float minCooldown, float maxCooldown, float minValue,
        float maxValue)
    {
        MinDuration = config($"Cast_{Name}", "MinDuration", minDuration,
            new ConfigDescription("Minimum duration of the cast. In seconds", new AcceptableValueRange<float>(1, 600)));
        MaxDuration = config($"Cast_{Name}", "MaxDuration", maxDuration,
            new ConfigDescription("Maximum duration of the cast. In seconds", new AcceptableValueRange<float>(1, 600)));

        MinCooldown = config($"Cast_{Name}", "MinCooldown", minCooldown,
            new ConfigDescription("Minimum cooldown of the cast. In seconds", new AcceptableValueRange<float>(1, 600)));
        MaxCooldown = config($"Cast_{Name}", "MaxCooldown", maxCooldown,
            new ConfigDescription("Maximum cooldown of the cast. In seconds", new AcceptableValueRange<float>(1, 600)));

        MinValue = config($"Cast_{Name}", "MinValue", minValue,
            new ConfigDescription("Minimum value of the cast. In seconds", new AcceptableValueRange<float>(1, 600)));
        MaxValue = config($"Cast_{Name}", "MaxValue", maxValue,
            new ConfigDescription("Maximum value of the cast. In seconds", new AcceptableValueRange<float>(1, 600)));
    }

    //TODO: Visual effects

    public string LocalizedName => $"$rl_cast_{Name.ToLower()}".Localize();
    public string LocalizedDescription => $"$rl_cast_{Name.ToLower()}_desc".Localize();


    public override string ToString() => $"[CastDefinition] {Name}";
}