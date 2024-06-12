using RuneLover.Casts;

namespace RuneLover.Patch;

[HarmonyPatch(typeof(Player)), HarmonyWrapSafe]
public static class FixesForCastExecution
{
    [HarmonyPatch(nameof(Player.IsWeaponLoaded)), HarmonyPrefix]
    [UsedImplicitly]
    private static bool _(Player __instance, ref bool __result)
    {
        if (CastPaternManager.InCastExecution)
        {
            __result = true;
            return false;
        }

        return true;
    }
}