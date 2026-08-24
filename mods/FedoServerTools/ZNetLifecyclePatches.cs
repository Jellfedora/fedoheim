using System;
using HarmonyLib;

namespace FedoServerTools
{
    // Démarre la boucle de rapport vers l'API dès que cette instance devient
    // effectivement un serveur (dédié ou hôte d'une partie solo) -- voir
    // FedoServerToolsPlugin.OnServerStarted -- et, indépendamment (voir
    // DiscordWebhook.cs), annonce le démarrage sur Discord si configuré. On lit le nom
    // du monde directement depuis le paramètre `world` plutôt que via
    // ZNet.GetWorldName() : à cet instant précis (juste après SetServer), GetWorldName()
    // peut lever une NullReferenceException en interne.
    [HarmonyPatch(typeof(ZNet), "SetServer")]
    internal static class ZNetSetServerPatch
    {
        private static void Postfix(bool server, World world)
        {
            try
            {
                if (server)
                {
                    FedoServerToolsPlugin.Instance.OnServerStarted();
                    FedoServerToolsPlugin.Instance.AnnounceServerStarted(world != null ? world.m_name : "?");
                    // AnnounceHostConnected (voir FedoServerToolsPlugin) n'est PAS
                    // appelé ici -- testé en jeu : à ce stade, Game.instance.
                    // GetPlayerProfile() ne renvoie encore rien d'exploitable, l'annonce
                    // ne partait donc jamais. Voir HudAwakeAnnounceHostPatch ci-dessous.
                }
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: SetServer patch failed: {e}");
            }
        }
    }

    // Voir FedoServerToolsPlugin.AnnounceHostConnected -- l'hôte n'a pas de ZNetPeer et
    // ne déclenche donc jamais ZNetPeerInfoAnnouncePatch (voir
    // ZNetJoinLeaveAnnouncePatches.cs). Hud.Awake (plutôt que ZNet.SetServer ci-dessus)
    // garantit que le profil du joueur local est bien chargé au moment de l'appel.
    [HarmonyPatch(typeof(Hud), "Awake")]
    internal static class HudAwakeAnnounceHostPatch
    {
        private static void Postfix()
        {
            try
            {
                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    FedoServerToolsPlugin.Instance.AnnounceHostConnected();
                }
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: Hud.Awake host-connected announce failed: {e}");
            }
        }
    }

    // S'abonne à WorldSaveFinished dès que ZNet existe -- désabonnement puis
    // réabonnement pour ne jamais s'accumuler sur des rechargements de scène successifs.
    [HarmonyPatch(typeof(ZNet), "Awake")]
    internal static class ZNetAwakePatch
    {
        private static void Postfix()
        {
            try
            {
                ZNet.WorldSaveFinished -= FedoServerToolsPlugin.Instance.AnnounceWorldSaved;
                ZNet.WorldSaveFinished += FedoServerToolsPlugin.Instance.AnnounceWorldSaved;
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: Awake patch failed: {e}");
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
                    FedoServerToolsPlugin.Instance.AnnounceServerStopped();
                    // Voir FedoServerToolsPlugin.AnnounceHostDisconnected -- symétrique
                    // de AnnounceHostConnected ci-dessus (ZNetSetServerPatch).
                    FedoServerToolsPlugin.Instance.AnnounceHostDisconnected();
                }
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: OnDestroy patch failed: {e}");
            }
        }
    }
}
