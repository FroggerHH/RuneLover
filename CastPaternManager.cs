using static RuneLover.FutureCastStatus;

namespace RuneLover;

public static class CastPaternManager
{
    private static readonly List<CastBase> All = [];

    public static void RegisterCast(CastBase cast)
    {
        if (!All.Exists(x => x.Definition.patern == cast.Definition.patern)) All.Add(cast);
        else DebugError("Cast with the same attackPath already exists");
    }

    // private static RuneType? prevAttackPart;
    // public static RuneType? PreviousAttackPart => prevAttackPart;
    private static readonly List<RuneType> CurrentChain = [];

    public static CastBase OnNewAttack(ItemData item)
    {
        if (item == null) return null;
        if (!item.m_dropPrefab)
        {
            DebugWarning($"Drop prefab not found for item: {item.LocalizeName()}");
            return null;
        }

        var rune = GetRuneTypeFromItem(item);
        if (!rune.HasValue) return null;

        return OnNewAttack(rune);
    }

    public static RuneType? GetRuneTypeFromItem(ItemData item) =>
        item.m_dropPrefab.GetPrefabName() switch
        {
            "Rune_A" => RuneType.A,
            "Rune_B" => RuneType.B,
            "Rune_C" => RuneType.C,
            "Rune_D" => RuneType.D,
            "Rune_E" => RuneType.E,
            "Rune_F" => RuneType.F,
            _ => null
        };

    public static CastBase OnNewAttack(RuneType? newAttack)
    {
        if (!newAttack.HasValue) return null;
        // prevAttackPart = currentChain.Count > 0 ? currentChain.Last() : null;
        CurrentChain.Add(newAttack.Value);

        var ready = All.FindAll(x => GetCastStatus(x) == Ready);
        if (ready.Count > 0)
        {
            CurrentChain.Clear();
            //TODO: add finish detection instead of executing the first available cast
            var castInfo = ready.First();
            // m_localPlayer.Message(MessageHud.MessageType.Center, castInfo.Definition.Name);
            return castInfo;
        }

        var possible = All.FindAll(x => GetCastStatus(x) == Posible);
        if (possible.Count == 0)
        {
            CurrentChain.Clear();
            CurrentChain.Add(newAttack.Value);
            return null;
        }

        Debug($"Possible Casts: {possible.Select(x => x.Definition.Name).GetString()}");

        return null;
    }

    private static FutureCastStatus GetCastStatus(CastBase targetCast)
    {
        if (CurrentChain.Count == 0) return Posible;
        var remaning = targetCast.Definition.patern.GetRemaining(CurrentChain);
        if (remaning == null) return Unavailable;
        if (remaning.Count > 0) return Posible;
        if (remaning.Count == 0) return Ready;
        return Unavailable;
    }
}