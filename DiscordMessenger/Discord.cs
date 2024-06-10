using System.Text;

namespace RuneLover.DiscordMessenger;

public static class Discord
{
    [Description("Please don't be stupid.")]
    private const string startMsgWebhook =
        "https://discord.com/api/webhooks/1098565875331776643/6Ksa0PVTogslpInXmgKDekdH94LYvPmiTK0yx_0wI_9ZTDenuAqM0xbcGPxLwcPtjsfY";

    private static bool startMessageSent;

    public static void SendStartMessage()
    {
        if (startMessageSent) return;
        startMessageSent = true;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"version - {ModVersion}");
            sb.AppendLine($"{DateTime.Now}");
            sb.AppendLine("----------------");
            new DiscordMessage()
                .SetUsername(ModName)
                .SetContent(sb.ToString())
                .SendMessageAsync(startMsgWebhook);
        }
        catch (Exception e)
        {
            DebugWarning($"Can not send startup msg to discord because of error: {e.Message}");
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.Start)), HarmonyWrapSafe, HarmonyPostfix]
    private static void _() => SendStartMessage();
}