using HarmonyLib;

namespace FedoDeath
{
    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class PlayerDeathPatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer)
            {
                return;
            }

            FedoDeathPlugin.Instance.OnLocalPlayerDeath();
        }
    }
}
