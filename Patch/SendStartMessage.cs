using RuneLover.DiscordMessenger;

namespace RuneLover.Patch;

[HarmonyPatch(typeof(Game), nameof(Game.Start))]
[HarmonyWrapSafe]
file static class SendStartMessage
{
    [HarmonyPostfix]
    private static void _() => Discord.SendStartMessage();
}