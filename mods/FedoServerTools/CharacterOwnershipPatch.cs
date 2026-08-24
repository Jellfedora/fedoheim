using System;
using System.Reflection;
using HarmonyLib;

namespace FedoServerTools
{
    // Empêche un joueur de rejoindre avec un nom de personnage déjà lié à un AUTRE
    // compte Fedoheim (voir CLAUDE.md, "premier arrivé, premier servi" + section
    // "Connexion automatique") -- sans ce contrôle, n'importe qui pourrait recréer
    // localement un perso du même nom et se connecter sous cette identité aux yeux du
    // serveur et des autres joueurs (position publique, biome/armure rapportés,
    // attribués au mauvais compte).
    //
    // Même point d'accroche que ZNetJoinLeaveAnnouncePatches (RPC_PeerInfo, côté serveur
    // seulement) : c'est le moment où le nom du pair (peer.m_playerName) devient connu.
    // Ne se déclenche jamais pour l'hôte lui-même (voir PeerSteamId.cs, l'hôte n'a pas de
    // ZNetPeer) -- seulement pour de vraies connexions entrantes, exactement ce qu'on
    // veut protéger. `ServerToken` vide (défaut sur une install joueur) rend ce contrôle
    // inoffensif, même logique que le reporting périodique.
    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    internal static class CharacterOwnershipPatch
    {
        // GetPeer(ZRpc) est privée sur ZNet -- même style de reflection que
        // ZNetJoinLeaveAnnouncePatches.cs.
        private static readonly MethodInfo GetPeerByRpc = AccessTools.Method(typeof(ZNet), "GetPeer", new[] { typeof(ZRpc) });

        private static void Postfix(ZNet __instance, ZRpc rpc)
        {
            try
            {
                var plugin = FedoServerToolsPlugin.Instance;
                if (!__instance.IsServer() || GetPeerByRpc == null || plugin == null || string.IsNullOrWhiteSpace(plugin.ServerToken))
                {
                    return;
                }

                var peer = (ZNetPeer)GetPeerByRpc.Invoke(__instance, new object[] { rpc });
                if (peer == null || string.IsNullOrEmpty(peer.m_playerName))
                {
                    return;
                }

                string steamId = peer.m_socket?.GetHostName();
                bool allowed = CharacterOwnershipCheck.IsAllowed(plugin.ApiBaseUrl, plugin.ServerToken, peer.m_playerName, steamId);
                if (!allowed)
                {
                    FedoServerToolsPlugin.Log?.LogWarning($"FedoServerTools: kicking '{peer.m_playerName}' -- character name already linked to another Fedoheim account.");
                    __instance.Kick(peer.m_playerName);
                }
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: character ownership check failed: {e}");
            }
        }
    }
}
