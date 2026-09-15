using System;
using HarmonyLib;

namespace FedoHud
{
    // Ajoute une ligne "Done in Xh" au texte de survol d'une fonderie/d'un four à
    // charbon -- les deux partagent la même classe `Smelter` côté jeu (pas de classe
    // "CharcoalKiln" séparée, vérifié par réflexion : le four à charbon n'est qu'un
    // `Smelter` configuré avec `m_maxFuel = 0`). Le survol du bâtiment passe par trois
    // callbacks privés distincts (fente minerai/bois, sortie, fente carburant) -- les
    // trois sont patchées pour que le compte à rebours apparaisse peu importe l'endroit
    // du bâtiment survolé. Signatures vérifiées contre assembly_valheim.dll (1.0), rien
    // deviné.
    internal static class SmelterTooltip
    {
        [HarmonyPatch(typeof(Smelter), "OnHoverAddOre")]
        private static class OnHoverAddOrePatch
        {
            private static void Postfix(Smelter __instance, ref string __result)
            {
                AppendLine(__instance, ref __result);
            }
        }

        [HarmonyPatch(typeof(Smelter), "OnHoverEmptyOre")]
        private static class OnHoverEmptyOrePatch
        {
            private static void Postfix(Smelter __instance, ref string __result)
            {
                AppendLine(__instance, ref __result);
            }
        }

        [HarmonyPatch(typeof(Smelter), "OnHoverAddFuel")]
        private static class OnHoverAddFuelPatch
        {
            private static void Postfix(Smelter __instance, ref string __result)
            {
                AppendLine(__instance, ref __result);
            }
        }

        private static void AppendLine(Smelter smelter, ref string result)
        {
            if (FedoHudPlugin.Instance == null || !FedoHudPlugin.Instance.ShowSmelterTooltip)
            {
                return;
            }

            try
            {
                string line = BuildSmelterLine(smelter);
                if (!string.IsNullOrEmpty(line))
                {
                    result = string.IsNullOrEmpty(result) ? line : $"{result}\n{line}";
                }
            }
            catch (Exception e)
            {
                FedoHudPlugin.Log?.LogWarning($"FedoHud: smelter tooltip failed: {e.Message}");
            }
        }

        // `null` si la file est vide -- rien à cuire, pas de compte à rebours pertinent.
        private static string BuildSmelterLine(Smelter smelter)
        {
            var nview = smelter.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null)
            {
                return null;
            }

            // ZDOVars.s_queued compte toutes les unités en file, y compris celle en cours
            // de cuisson.
            int queueSize = zdo.GetInt(ZDOVars.s_queued);
            if (queueSize <= 0)
            {
                return null;
            }

            // Smelter.IsActive() couvre déjà tout ce qui peut mettre la cuisson en pause
            // (carburant manquant, toit requis absent, fumée bloquée...) -- pas la peine
            // de dupliquer cette logique ici.
            if (!smelter.IsActive())
            {
                return FedoHudPlugin.Instance.SmelterPausedText;
            }

            // Reproduit Smelter.UpdateSmelter() (privée, tick 1x/s côté propriétaire ZDO) :
            // temps restant sur l'unité en cours + une cuisson complète par unité en
            // attente derrière elle.
            float bakeTimer = zdo.GetFloat(ZDOVars.s_bakeTimer);
            float remainingSeconds = (smelter.m_secPerProduct - bakeTimer) + (queueSize - 1) * smelter.m_secPerProduct;

            if (remainingSeconds <= 0)
            {
                return FedoHudPlugin.Instance.SmelterReadyText;
            }

            return $"{FedoHudPlugin.Instance.SmelterRemainingPrefix} {HudTimeFormat.FormatRemaining(remainingSeconds)}";
        }
    }
}
