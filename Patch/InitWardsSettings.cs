using UnityEngine.SceneManagement;

namespace RuneLover.Patch;

[HarmonyPatch, HarmonyWrapSafe] 
file class InitWardsSettings
{
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))] 
    [HarmonyPostfix]
    internal static void Init(ZNetScene __instance)
    {
        if (SceneManager.GetActiveScene().name != "main") return;
        if (!ZNet.instance.IsServer()) return;

    }
}