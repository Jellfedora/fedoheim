using System;
using HarmonyLib;

namespace FedoServerTools
{
    // Démarre la boucle de rapport dès que cette instance devient effectivement un
    // serveur (dédié ou hôte d'une partie solo) — voir FedoServerToolsPlugin.OnServerStarted.
    [HarmonyPatch(typeof(ZNet), "SetServer")]
    internal static class ZNetSetServerPatch
    {
        private static void Postfix(bool server)
        {
            try
            {
                if (server)
                {
                    FedoServerToolsPlugin.Instance.OnServerStarted();
                }
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: SetServer patch failed: {e}");
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
                    FedoServerToolsPlugin.Instance.OnServerStopping();
                }
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: OnDestroy patch failed: {e}");
            }
        }
    }
}
