using RuneLover.Casts;

namespace RuneLover.Patch;

[HarmonyPatch, HarmonyWrapSafe]
[HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
public static class LookForCast
{
    [UsedImplicitly]
    private static bool Prefix(Player __instance, ItemData item, ref bool __result)
    {
        var pl = m_localPlayer;
        if (!__instance || __instance != pl) return true;
        if (!pl.CanConsumeItem(item)) return true;
        var runeType = CastPaternManager.GetRuneTypeFromItem(item);
        if (runeType is null) return true;
        __result = false;
        
        try
        {
            CastPaternManager.OnNewAttack(runeType.Value);
        }
        catch (Exception e)
        {
            DebugError(e);
        }

        return false;
    }
}