using System;
using HarmonyLib;

namespace FedoHud
{
    // Ajoute une ligne "pousse dans Xh" au texte de survol d'une plante en cours de
    // pousse (graine plantée au cultivateur : carotte/navet/lin/houblon..., et les jeunes
    // arbres plantés -- voir PlantGrowth.cs pour le calcul partagé avec
    // PlantReadyIndicator.cs, qui affiche une icône "!" une fois prête, sauf pour les
    // arbres).
    internal static class GrowthTooltip
    {
        [HarmonyPatch(typeof(Plant), nameof(Plant.GetHoverText))]
        private static class PlantGetHoverTextPatch
        {
            private static void Postfix(Plant __instance, ref string __result)
            {
                if (FedoHudPlugin.Instance == null || !FedoHudPlugin.Instance.ShowGrowthTooltip)
                {
                    return;
                }

                try
                {
                    string line = BuildGrowthLine(__instance);
                    if (!string.IsNullOrEmpty(line))
                    {
                        __result = string.IsNullOrEmpty(__result) ? line : $"{__result}\n{line}";
                    }
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogWarning($"FedoHud: growth tooltip failed: {e.Message}");
                }
            }
        }

        private static string BuildGrowthLine(Plant plant)
        {
            double? remaining = PlantGrowth.GetRemainingSeconds(plant);
            if (remaining == null)
            {
                return null;
            }

            if (remaining.Value <= 0)
            {
                return FedoHudPlugin.Instance.GrowthReadyText;
            }

            return $"{FedoHudPlugin.Instance.GrowthRemainingPrefix} {HudTimeFormat.FormatRemaining(remaining.Value)}";
        }
    }
}
