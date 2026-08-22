using System;
using HarmonyLib;

namespace FedoDiscordLogs
{
    // Appelé une fois quand cette instance devient effectivement un serveur (dédié ou hôte).
    [HarmonyPatch(typeof(ZNet), "SetServer")]
    internal static class ZNetSetServerPatch
    {
        // On lit le nom directement depuis le paramètre "world" plutôt que via
        // ZNet.GetWorldName() : à cet instant précis (juste après SetServer), GetWorldName()
        // peut lever une NullReferenceException en interne.
        private static void Postfix(bool server, World world)
        {
            try
            {
                if (server)
                {
                    FedoDiscordLogsPlugin.Instance.OnServerStarted(world != null ? world.m_name : "?");
                }
            }
            catch (Exception e)
            {
                FedoDiscordLogsPlugin.Log?.LogError($"FedoDiscordLogs: SetServer patch failed: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(ZNet), "Awake")]
    internal static class ZNetAwakePatch
    {
        private static void Postfix()
        {
            try
            {
                ZNet.WorldSaveFinished -= FedoDiscordLogsPlugin.Instance.OnWorldSaved;
                ZNet.WorldSaveFinished += FedoDiscordLogsPlugin.Instance.OnWorldSaved;
            }
            catch (Exception e)
            {
                FedoDiscordLogsPlugin.Log?.LogError($"FedoDiscordLogs: Awake patch failed: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    internal static class ZNetDestroyPatch
    {
        private static void Prefix(ZNet __instance)
        {
            try
            {
                if (__instance.IsServer())
                {
                    FedoDiscordLogsPlugin.Instance.OnServerStopped();
                }
            }
            catch (Exception e)
            {
                FedoDiscordLogsPlugin.Log?.LogError($"FedoDiscordLogs: OnDestroy patch failed: {e}");
            }
        }
    }
}
