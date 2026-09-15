using System;
using System.Collections.Generic;
using HarmonyLib;

namespace FedoHud
{
    // Ajoute des lignes au texte de survol d'un animal : temps avant l'âge adulte pour
    // un bébé (voir BuildGrowUpLine -- indépendant du statut apprivoisé/sauvage, un
    // bébé grandit qu'il soit apprivoisé ou non), puis, seulement si l'animal est
    // apprivoisé (`Tameable.IsTamed()`, le composant `Tameable` existe qu'il soit
    // apprivoisé ou non) : temps avant la faim, et reproduction (compte à rebours si
    // gestante, sinon progression en points d'amour -- la reproduction dépend d'un
    // tirage aléatoire périodique, pas d'un minuteur déterministe, voir
    // BuildProcreationLine). Le jeu affiche déjà un statut de faim basique
    // ($hud_tamehungry/$hud_tamehappy/...) via `Tameable.GetHoverText()` -- notre ligne
    // de faim n'apparaît que tant que l'animal n'a pas encore faim, pour ne pas
    // dupliquer ce texte vanilla. Signatures vérifiées contre assembly_valheim.dll
    // (1.0), rien deviné.
    internal static class TameableTooltip
    {
        [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText))]
        private static class TameableGetHoverTextPatch
        {
            private static void Postfix(Tameable __instance, ref string __result)
            {
                if (FedoHudPlugin.Instance == null || !FedoHudPlugin.Instance.ShowTameableTooltip)
                {
                    return;
                }

                try
                {
                    string line = BuildLines(__instance);
                    if (!string.IsNullOrEmpty(line))
                    {
                        __result = string.IsNullOrEmpty(__result) ? line : $"{__result}\n{line}";
                    }
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogWarning($"FedoHud: tameable tooltip failed: {e.Message}");
                }
            }
        }

        private static string BuildLines(Tameable tameable)
        {
            var nview = tameable.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null || ZNet.instance == null)
            {
                return null;
            }

            var lines = new List<string>();

            string growUpLine = BuildGrowUpLine(tameable);
            if (growUpLine != null)
            {
                lines.Add(growUpLine);
            }

            // Faim/reproduction ne concernent qu'un animal déjà apprivoisé -- un bébé pas
            // encore apprivoisé peut quand même avoir la ligne de croissance ci-dessus.
            if (tameable.IsTamed())
            {
                string hungerLine = BuildHungerLine(tameable, zdo);
                if (hungerLine != null)
                {
                    lines.Add(hungerLine);
                }

                string procreationLine = BuildProcreationLine(tameable, zdo);
                if (procreationLine != null)
                {
                    lines.Add(procreationLine);
                }
            }

            return lines.Count > 0 ? string.Join("\n", lines) : null;
        }

        // `null` si ce n'est pas un bébé (pas de composant `Growup`, présent uniquement
        // sur le prefab bébé, jamais sur l'adulte -- sa seule présence suffit à savoir
        // "c'est un bébé") ou si le compte à rebours est déjà écoulé (sur le point de
        // grandir, pas de ligne trompeuse).
        private static string BuildGrowUpLine(Tameable tameable)
        {
            if (!FedoHudPlugin.Instance.ShowGrowUpTooltip)
            {
                return null;
            }

            var growup = tameable.GetComponent<Growup>();
            if (growup == null)
            {
                return null;
            }

            var baseAi = tameable.GetComponent<BaseAI>();
            if (baseAi == null)
            {
                return null;
            }

            // Reproduit Growup.GrowUpdate() (privée, tick toutes les 10s) : tout public,
            // aucune réflexion nécessaire ici contrairement à Plant/Beehive.
            double elapsedSeconds = baseAi.GetTimeSinceSpawned().TotalSeconds;
            double remainingSeconds = growup.m_growTime - elapsedSeconds;
            if (remainingSeconds <= 0)
            {
                return null;
            }

            return $"{FedoHudPlugin.Instance.GrowUpRemainingPrefix} {HudTimeFormat.FormatRemaining(remainingSeconds)}";
        }

        // `null` si déjà affamé -- le texte vanilla ($hud_tamehungry) le dit déjà.
        private static string BuildHungerLine(Tameable tameable, ZDO zdo)
        {
            if (tameable.IsHungry())
            {
                return null;
            }

            long lastFeedingTicks = zdo.GetLong(ZDOVars.s_tameLastFeeding, 0);
            if (lastFeedingTicks == 0)
            {
                return null;
            }

            double elapsedSeconds = (ZNet.instance.GetTime() - new DateTime(lastFeedingTicks)).TotalSeconds;
            double remainingSeconds = tameable.m_fedDuration - elapsedSeconds;
            if (remainingSeconds <= 0)
            {
                return null;
            }

            return $"{FedoHudPlugin.Instance.HungryInPrefix} {HudTimeFormat.FormatRemaining(remainingSeconds)}";
        }

        // `null` si l'animal ne se reproduit pas (pas de composant `Procreation`, ex. un
        // compagnon type Knorri). Gestante : compte à rebours déterministe jusqu'à la
        // naissance. Pas gestante : `Procreation.Procreate()` (privée) retente sa chance
        // toutes les ~10s avec une probabilité fixe -- pas de temps restant fiable
        // possible dans ce cas, seulement la progression en points d'amour déjà acquis.
        private static string BuildProcreationLine(Tameable tameable, ZDO zdo)
        {
            var procreation = tameable.GetComponent<Procreation>();
            if (procreation == null)
            {
                return null;
            }

            long pregnantTicks = zdo.GetLong(ZDOVars.s_pregnant, 0);
            if (pregnantTicks != 0)
            {
                double elapsedSeconds = (ZNet.instance.GetTime() - new DateTime(pregnantTicks)).TotalSeconds;
                double remainingSeconds = procreation.m_pregnancyDuration - elapsedSeconds;
                return remainingSeconds > 0
                    ? $"{FedoHudPlugin.Instance.PregnantRemainingPrefix} {HudTimeFormat.FormatRemaining(remainingSeconds)}"
                    : FedoHudPlugin.Instance.PregnantReadyText;
            }

            int lovePoints = procreation.GetLovePoints();
            return $"{FedoHudPlugin.Instance.LoveProgressPrefix} {lovePoints}/{procreation.m_requiredLovePoints}";
        }
    }
}
