namespace RuneLover.Casts;

public static class Casts
{
    private static bool _inited;

    public static void Init()
    {
        if (_inited) return;
        _inited = true;

        //Быстрые ноги пизды не боятся
        var fastLegs = new CastInfo("fastLegs");
        fastLegs.Requirement = new(RuneType.A, RuneType.B, RuneType.B);

        fastLegs.OnActivate = (caster) =>
        {
            
        };


        CastsManager.RegisterCastInfo(fastLegs);
    }
}