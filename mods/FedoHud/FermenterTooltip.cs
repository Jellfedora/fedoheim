using System;
using HarmonyLib;

namespace FedoHud
{
    // Ajoute une ligne "Ready in Xh" au texte de survol d'un fermenteur -- même patron
    // que GrowthTooltip.cs/BeehiveTooltip.cs. Contrairement aux deux autres, aucune
    // réflexion n'est nécessaire ici : tout ce qui est utile (durée totale, contenu en
    // cours, moment de départ) est public sur `Fermenter`/`ZDOVars`. Signatures vérifiées
    // contre assembly_valheim.dll (1.0), rien deviné -- voir mods/CLAUDE.md.
    internal static class FermenterTooltip
    {
        [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.GetHoverText))]
        private static class FermenterGetHoverTextPatch
        {
            private static void Postfix(Fermenter __instance, ref string __result)
            {
                if (FedoHudPlugin.Instance == null || !FedoHudPlugin.Instance.ShowFermenterTooltip)
                {
                    return;
                }

                try
                {
                    string line = BuildFermentLine(__instance);
                    if (!string.IsNullOrEmpty(line))
                    {
                        __result = string.IsNullOrEmpty(__result) ? line : $"{__result}\n{line}";
                    }
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogWarning($"FedoHud: fermenter tooltip failed: {e.Message}");
                }
            }
        }

        private static string BuildFermentLine(Fermenter fermenter)
        {
            var nview = fermenter.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null || ZNet.instance == null)
            {
                return null;
            }

            // 0 = fermenteur vide (ZDOVars.s_content stocke le hash de l'item en cours de
            // fermentation) -- rien à ajouter dans ce cas.
            if (zdo.GetInt(ZDOVars.s_content, 0) == 0)
            {
                return null;
            }

            long startTicks = zdo.GetLong(ZDOVars.s_startTime, ZNet.instance.GetTime().Ticks);
            double elapsedSeconds = (ZNet.instance.GetTime() - new DateTime(startTicks)).TotalSeconds;
            double remainingSeconds = fermenter.m_fermentationDuration - elapsedSeconds;

            if (remainingSeconds <= 0)
            {
                return FedoHudPlugin.Instance.FermenterReadyText;
            }

            return $"{FedoHudPlugin.Instance.FermenterRemainingPrefix} {HudTimeFormat.FormatRemaining(remainingSeconds)}";
        }
    }
}
