using static RuneLover.Casts.FutureCastStatus;

namespace RuneLover.Casts;

public static class CastsManager
{
    private static readonly List<CastInfo> All = [];

    public static List<CastInfo> GetAll() => All;

    public static void RegisterCastInfo(CastInfo CastInfo)
    {
        if (!All.Exists(x => x.Requirement.attackPath == CastInfo.Requirement.attackPath))
            All.Add(CastInfo);
        else DebugError("Cast with the same attackPath already exists");
    }

    // private static RuneType? prevAttackPart;
    // public static RuneType? PreviousAttackPart => prevAttackPart;
    private static readonly List<RuneType> currentChain = [];

    public static CastInfo? OnNewAttack(RuneType? newAttack)
    {
        if (!newAttack.HasValue) return null;
        // prevAttackPart = currentChain.Count > 0 ? currentChain.Last() : null;
        currentChain.Add(newAttack.Value);

        var ready = GetAll().FindAll(x => GetCastStatus(x) == Ready);
        if (ready.Count > 0)
        {
            currentChain.Clear();
            var castInfo = ready.First();
            m_localPlayer.Message(MessageHud.MessageType.Center, castInfo.Name);
            return castInfo;
        }

        var possible = GetAll().FindAll(x => GetCastStatus(x) == Posible);
        if (possible.Count == 0)
        {
            currentChain.Clear();
            currentChain.Add(newAttack.Value);
            return null;
        }

        Debug($"Possible Casts: {possible.Select(x => x.Name).GetString()}");

        return null;
    }

    private static FutureCastStatus GetCastStatus(CastInfo targetCast)
    {
        if (currentChain.Count == 0) return Posible;
        var remaning = targetCast.Requirement.attackPath.GetRemaining(currentChain);
        if (remaning == null) return Unavailable;
        if (remaning.Count > 0) return Posible;
        if (remaning.Count == 0) return Ready;
        return Unavailable;
    }
}