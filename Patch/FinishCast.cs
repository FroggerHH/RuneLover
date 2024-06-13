using RuneLover.Casts;

namespace RuneLover.Patch;

[HarmonyPatch, HarmonyWrapSafe]
[HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
public static class FinishCast
{
    [UsedImplicitly]
    private static bool Prefix(Attack __instance, Humanoid character, ref bool __result)
    {
        var pl = m_localPlayer;
        if (!pl || character != pl) return true;
        if (CastPaternManager.CurrentChain.Count == 0) return true;
        __result = false;

        try
        {
            CastPaternManager.ConstructAndExecuteCast();
        }
        catch (Exception e)
        {
            DebugError(e);
        }

        return false;
    }
}