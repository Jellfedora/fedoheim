using System;
using HarmonyLib;

namespace FedoServerTools
{
    // Contrepartie côté client du forçage serveur (voir FedoServerToolsPlugin.
    // ForcePublicPositionOnPeers) : celui-ci écrit directement l'état que voit le
    // serveur, mais rien ne garantit qu'un simple champ écrit côté serveur déclenche
    // la même diffusion aux autres clients qu'un vrai changement fait par le joueur
    // (pas vérifié faute de pouvoir décompiler). Ce patch reproduit donc un vrai clic
    // sur la case "Position publique" (Options > Jeu) via l'API du jeu elle-même
    // (Minimap.OnTogglePublicPosition, publique), pour passer par le chemin normal
    // -- y compris sa diffusion réseau, quelle qu'elle soit.
    //
    // Tourne uniquement côté client (jamais sur le serveur dédié lui-même, qui n'a pas
    // de Minimap local) : sans danger à distribuer dans le modpack joueur, contrairement
    // au reste de ce mod, puisqu'aucun jeton n'est nécessaire pour ça -- voir README.
    [HarmonyPatch(typeof(Game), "Start")]
    internal static class ClientPublicPositionPatch
    {
        private static void Postfix()
        {
            try
            {
                if (ZNet.instance == null || ZNet.instance.IsServer())
                {
                    return;
                }

                FedoServerToolsPlugin.Instance.ForceOwnPublicPosition();
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: ClientPublicPosition patch failed: {e}");
            }
        }
    }
}
