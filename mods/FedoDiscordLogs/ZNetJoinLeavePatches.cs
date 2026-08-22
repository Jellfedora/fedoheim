using System;
using System.Reflection;
using HarmonyLib;

namespace FedoDiscordLogs
{
    // ZNet.RPC_PeerInfo tourne des deux côtés d'une connexion (le client le reçoit aussi pour
    // le serveur) : IsServer() garantit qu'on ne logue que les vraies connexions de joueurs.
    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    internal static class ZNetPeerInfoPatch
    {
        // GetPeer(ZRpc) est privée sur ZNet, donc on passe par AccessTools pour l'appeler.
        private static readonly MethodInfo GetPeerByRpc = AccessTools.Method(typeof(ZNet), "GetPeer", new[] { typeof(ZRpc) });

        private static void Postfix(ZNet __instance, ZRpc rpc)
        {
            try
            {
                if (!__instance.IsServer() || GetPeerByRpc == null)
                {
                    return;
                }

                var peer = (ZNetPeer)GetPeerByRpc.Invoke(__instance, new object[] { rpc });
                if (peer == null || string.IsNullOrEmpty(peer.m_playerName))
                {
                    return;
                }

                FedoDiscordLogsPlugin.Instance.OnPlayerConnected(peer.m_playerName);
            }
            catch (Exception e)
            {
                FedoDiscordLogsPlugin.Log?.LogError($"FedoDiscordLogs: RPC_PeerInfo patch failed: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(ZNet), "Disconnect")]
    internal static class ZNetDisconnectPatch
    {
        private static void Prefix(ZNet __instance, ZNetPeer peer)
        {
            try
            {
                if (!__instance.IsServer() || peer == null || string.IsNullOrEmpty(peer.m_playerName))
                {
                    return;
                }

                FedoDiscordLogsPlugin.Instance.OnPlayerDisconnected(peer.m_playerName);
            }
            catch (Exception e)
            {
                FedoDiscordLogsPlugin.Log?.LogError($"FedoDiscordLogs: Disconnect patch failed: {e}");
            }
        }
    }
}
