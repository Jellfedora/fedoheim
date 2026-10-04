using System;
using System.Reflection;
using HarmonyLib;

namespace FedoHud
{
    // Ajoute une ligne "Next honey in Xh" au texte de survol d'une ruche -- même patron
    // que GrowthTooltip.cs pour les plantes, mais une ruche n'est pas une `Plant` : pas
    // de "temps de pose" équivalent, la progression est stockée différemment dans la
    // ZDO. Signatures vérifiées par réflexion contre assembly_valheim.dll (1.0), rien
    // deviné.
    //
    // Le nid d'oiseaux ("piece_birdnest") réutilise ce même composant `Beehive` que la
    // ruche -- seuls `m_name`/les textes (`m_checkText` etc.)/l'item produit diffèrent
    // au niveau du prefab, vérifié par décompilation. `IsBirdNest` distingue les deux
    // pour choisir le bon jeu de textes (plume plutôt que miel) sans dupliquer tout ce
    // fichier.
    internal static class BeehiveTooltip
    {
        private static bool IsBirdNest(Beehive beehive)
        {
            return beehive.m_checkText != null
                && beehive.m_checkText.IndexOf("birdnest", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // CheckBiome()/HaveFreeSpace() sont privées (conditions qui mettent la
        // production en pause -- mauvais biome / pas de place libre autour de la ruche) ;
        // simples vérifications sans effet de bord apparent, sûres à appeler par
        // réflexion à chaque survol.
        private static readonly MethodInfo CheckBiomeMethod =
            AccessTools.Method(typeof(Beehive), "CheckBiome");
        private static readonly MethodInfo HaveFreeSpaceMethod =
            AccessTools.Method(typeof(Beehive), "HaveFreeSpace");

        [HarmonyPatch(typeof(Beehive), nameof(Beehive.GetHoverText))]
        private static class BeehiveGetHoverTextPatch
        {
            private static void Postfix(Beehive __instance, ref string __result)
            {
                if (FedoHudPlugin.Instance == null || !FedoHudPlugin.Instance.ShowBeehiveTooltip)
                {
                    return;
                }

                try
                {
                    string line = BuildHoneyLine(__instance);
                    if (!string.IsNullOrEmpty(line))
                    {
                        __result = string.IsNullOrEmpty(__result) ? line : $"{__result}\n{line}";
                    }
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogWarning($"FedoHud: beehive tooltip failed: {e.Message}");
                }
            }
        }

        private static string BuildHoneyLine(Beehive beehive)
        {
            var nview = beehive.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null || ZNet.instance == null)
            {
                return null;
            }

            bool isBirdNest = IsBirdNest(beehive);
            string fullText = isBirdNest ? FedoHudPlugin.Instance.FeatherFullText : FedoHudPlugin.Instance.HoneyFullText;
            string pausedText = isBirdNest ? FedoHudPlugin.Instance.FeatherPausedText : FedoHudPlugin.Instance.HoneyPausedText;
            string remainingPrefix = isBirdNest ? FedoHudPlugin.Instance.FeatherRemainingPrefix : FedoHudPlugin.Instance.HoneyRemainingPrefix;

            int honeyLevel = zdo.GetInt(ZDOVars.s_level);
            if (honeyLevel >= beehive.m_maxHoney)
            {
                return fullText;
            }

            bool paused = !(bool)CheckBiomeMethod.Invoke(beehive, null)
                || !(bool)HaveFreeSpaceMethod.Invoke(beehive, null);
            if (paused)
            {
                return pausedText;
            }

            // Reproduit Beehive.UpdateBees() (privée, tickée toutes les 10s réelles côté
            // propriétaire ZDO uniquement) : progression déjà accumulée (`s_product`, en
            // secondes) + écoulé depuis le dernier tick traité (`s_lastTime`).
            long lastTicks = zdo.GetLong(ZDOVars.s_lastTime, ZNet.instance.GetTime().Ticks);
            double elapsedSeconds = (ZNet.instance.GetTime() - new DateTime(lastTicks)).TotalSeconds;
            double progress = zdo.GetFloat(ZDOVars.s_product) + elapsedSeconds;
            double remainingSeconds = beehive.m_secPerUnit - progress;

            if (remainingSeconds <= 0)
            {
                // Le tick réel (toutes les 10s côté propriétaire) n'est pas encore passé --
                // affiche quand même "prêt" plutôt qu'un temps négatif trompeur.
                return fullText;
            }

            return $"{remainingPrefix} {HudTimeFormat.FormatRemaining(remainingSeconds)}";
        }
    }
}
