namespace RuneLover.Helpers;

[Description("Stolen from /MagicHeim/Helpers/ClassAnimationReplace")]
public static class AnimationReplace
{
    private static bool _firstInit;
    private static RuntimeAnimatorController _vanillaController;
    private static RuntimeAnimatorController _RlController;
    private static readonly Dictionary<string, AnimationClip> ExternalAnimations = new();
    private static readonly Dictionary<string, string> ReplacementMap = new();

    public static RuntimeAnimatorController RlWolfController;

    public static readonly Dictionary<RlAnimation, string> AnimationNames = new()
    {
        { RlAnimation.MageProjectile, "emote_thumbsup" },
    };

    public static void InitAnimations()
    {
        ExternalAnimations.Add("MageProjectile", bundle.LoadAsset<AnimationClip>("MageProjectileEdited"));
        ReplacementMap.Add("Thumbsup", "MageProjectile");
    }

    private static void SetReplacePlayerRac(Animator anim, RuntimeAnimatorController rac)
    {
        if (anim.runtimeAnimatorController == rac) return;
        anim.runtimeAnimatorController = rac;
        anim.Update(0f);
    }

    public static RuntimeAnimatorController SetMakeAoc(Dictionary<string, string> replacement,
        RuntimeAnimatorController original)
    {
        var aoc = new AnimatorOverrideController(original);
        var anims = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        foreach (var animation in aoc.animationClips)
        {
            var name = animation.name;
            if (replacement.TryGetValue(name, out var value))
            {
                var newClip = Instantiate(ExternalAnimations[value]);
                anims.Add(new KeyValuePair<AnimationClip, AnimationClip>(animation, newClip));
            }
            else
            {
                anims.Add(new KeyValuePair<AnimationClip, AnimationClip>(animation, animation));
            }
        }

        aoc.ApplyOverrides(anims);
        return aoc;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Start))]
    [HarmonyPriority(5000)]
    private static class TESTPATCHPLAYERANIMS
    {
        [HarmonyPostfix]
        private static void Postfix(ref Player __instance)
        {
            if (!_firstInit)
            {
                _firstInit = true;
                _RlController = SetMakeAoc(ReplacementMap, __instance.m_animator.runtimeAnimatorController);
                _vanillaController = SetMakeAoc(
                    new Dictionary<string, string>(), __instance.m_animator.runtimeAnimatorController);
            }
        }
    }

    [HarmonyPatch(typeof(ZSyncAnimation), nameof(ZSyncAnimation.RPC_SetTrigger))]
    private static class ZSyncAnimation_RPC_SetTrigger_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(ZSyncAnimation __instance, string name)
        {
            if (name.Contains("emote_")) SetReplacePlayerRac(__instance.m_animator, _RlController);
        }
    }
}