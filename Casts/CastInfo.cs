namespace RuneLover.Casts;

public struct CastInfo(string castNameId)
{
    public readonly string CastNameId = castNameId;

    public string Name => $"${ModName}_Cast_{CastNameId}".Localize();

    //TODO: Check requirements
    public CastRequirement Requirement;
    public Action<Player> OnActivate;

    public CastInfo(string castNameId, ICollection<RuneType> attackPath) : this(castNameId)
    {
        Requirement = new CastRequirement(attackPath);
    }

    //TODO: Visual effects

    public override string ToString() =>
        $"AttackPath: {string.Join(", ", Requirement.attackPath)}";
}

public struct CastRequirement()
{
    public List<RuneType>? attackPath = null;

    public int StaminaCost = 0;
    public int HealthCost = 0;
    public int EitrCost = 0;

    public CastRequirement(params RuneType[] attackPath) : this() => this.attackPath = attackPath.ToList();
    public CastRequirement(List<RuneType> attackPath) : this() => this.attackPath = attackPath;
    public CastRequirement(ICollection<RuneType> attackPath) : this() => this.attackPath = attackPath.ToList();

    public override string ToString() => $"AttackPath: {attackPath}, ";
}