using System;
using System.Collections.Generic;
using HarmonyLib;

namespace FedoServerTools
{
    // Le réglage "Position publique" (Options > Jeu) est propre à chaque joueur, stocké
    // dans ses préférences locales -- ce mod n'a aucun moyen de le changer côté client.
    // On force donc seulement ce que GetPlayerList() renvoie côté serveur (m_publicPosition
    // à true pour tout le monde) : suffisant pour que le biome soit toujours rapporté (voir
    // FedoServerToolsPlugin.GetBiomeName), sans dépendre de ce que chaque joueur a coché.
    [HarmonyPatch(typeof(ZNet), "GetPlayerList")]
    internal static class ForcePublicPositionPatch
    {
        private static void Postfix(List<ZNet.PlayerInfo> __result)
        {
            try
            {
                if (FedoServerToolsPlugin.Instance == null || !FedoServerToolsPlugin.Instance.ForcePublicPosition)
                {
                    return;
                }

                for (int i = 0; i < __result.Count; i++)
                {
                    var info = __result[i];
                    if (!info.m_publicPosition)
                    {
                        info.m_publicPosition = true;
                        __result[i] = info;
                    }
                }
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: ForcePublicPosition patch failed: {e}");
            }
        }
    }
}
