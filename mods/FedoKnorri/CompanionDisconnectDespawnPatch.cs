using System;
using HarmonyLib;
using UnityEngine;

namespace FedoKnorri
{
    // Sans ce patch, le compagnon d'un joueur qui se déconnecte reste dans le monde, gelé sur
    // place (voir CompanionAI.ResolveOwner/TryReclaimOwnership : son AI s'arrête simplement,
    // faute de propriétaire valide dans Player.GetAllPlayers(), mais l'objet lui-même n'est
    // jamais détruit) jusqu'à ce que ce même joueur se reconnecte. Voulu ici à la place : la
    // déconnexion du propriétaire range le compagnon comme s'il avait re-cliqué sur la graine
    // lui-même -- même geste que SummonItemUsePatch (poof + ZNetView.Destroy()).
    //
    // Deux points d'entrée distincts, cf. mods/FedoServerTools/ZNetJoinLeaveAnnouncePatches.cs
    // et ZNetLifecyclePatches.cs pour le même besoin (détecter un départ de joueur) : un joueur
    // distant qui quitte a un ZNetPeer (ZNet.Disconnect), mais l'hôte d'une partie solo/co-op
    // n'en a AUCUN pour lui-même (voir CLAUDE.md, "PeerSteamId.Resolve distingue deux cas") --
    // sa propre sortie ne se voit qu'à la fermeture de ZNet (ZNet.OnDestroy).
    internal static class CompanionDisconnectDespawnPatch
    {
        private static void DespawnCompanionOf(Player owner)
        {
            if (owner == null)
            {
                return;
            }

            GameObject companion = CompanionAI.FindExistingCompanion(owner);
            if (companion == null)
            {
                return;
            }

            CompanionPoofEffect.Show(companion.transform.position);
            companion.GetComponent<ZNetView>()?.Destroy();
        }

        // ZNet.Disconnect tourne côté serveur pour un vrai départ de pair distant (IsServer(),
        // même garde que ZNetDisconnectAnnouncePatch) -- Prefix, pas Postfix : le personnage du
        // pair qui se déconnecte doit encore être chargé à cet instant pour être retrouvé via
        // ZNetScene.FindInstance(peer.m_characterID) (ZDOID stable de SON personnage, pas à
        // confondre avec le PlayerID stocké sur le ZDO du compagnon -- ZNetPeer n'expose que le
        // premier).
        [HarmonyPatch(typeof(ZNet), "Disconnect")]
        private static class RemotePlayerDisconnectPatch
        {
            private static void Prefix(ZNet __instance, ZNetPeer peer)
            {
                try
                {
                    if (!__instance.IsServer() || peer == null || ZNetScene.instance == null)
                    {
                        return;
                    }

                    GameObject characterObj = ZNetScene.instance.FindInstance(peer.m_characterID);
                    Player owner = characterObj != null ? characterObj.GetComponent<Player>() : null;
                    DespawnCompanionOf(owner);
                }
                catch (Exception e)
                {
                    FedoKnorriPlugin.Log?.LogError($"FedoKnorri: CompanionDisconnectDespawnPatch (Disconnect) a levé une exception : {e}");
                }
            }
        }

        // Couvre le cas de l'hôte lui-même qui quitte (solo/co-op) -- sans ZNetPeer pour s'en
        // apercevoir via le patch ci-dessus. Player.m_localPlayer, pas ResolveOwner/une
        // recherche par ZDOID : c'est littéralement l'hôte qui ferme sa propre session.
        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        private static class HostQuitPatch
        {
            private static void Prefix(ZNet __instance)
            {
                try
                {
                    if (!__instance.IsServer())
                    {
                        return;
                    }

                    DespawnCompanionOf(Player.m_localPlayer);
                }
                catch (Exception e)
                {
                    FedoKnorriPlugin.Log?.LogError($"FedoKnorri: CompanionDisconnectDespawnPatch (OnDestroy) a levé une exception : {e}");
                }
            }
        }
    }
}
