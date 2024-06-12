namespace RuneLover.Casts;

public abstract class CastBase
{
    public static List<CastBase> AllInstances { get; private set; } = [];

    public string Name;
    public List<RuneType> patern = [];

    protected CastBase() => AllInstances.Add(this);

    public static implicit operator bool(CastBase cast) => cast is not null;
}