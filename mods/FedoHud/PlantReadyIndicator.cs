using HarmonyLib;

namespace FedoHud
{
    // Icône "!" flottante au-dessus d'une culture prête à récolter -- jamais pour un
    // jeune arbre planté (voir PlantGrowth.IsTreeSapling). Voir
    // FloatingExclamationIcon.cs pour l'implémentation partagée (billboard/throttle), et
    // GrowthTooltip.cs pour la ligne de tooltip équivalente au survol.
    internal static class PlantReadyIndicator
    {
        [HarmonyPatch(typeof(Plant), "Awake")]
        private static class PlantAwakePatch
        {
            private static void Postfix(Plant __instance)
            {
                if (PlantGrowth.IsTreeSapling(__instance)
                    || __instance.GetComponent<FloatingExclamationIcon>() != null)
                {
                    return;
                }

                var icon = __instance.gameObject.AddComponent<FloatingExclamationIcon>();
                icon.IsEnabledInConfig = () =>
                    FedoHudPlugin.Instance != null && FedoHudPlugin.Instance.ShowGrowthReadyIcon;
                icon.IsReady = () =>
                {
                    double? remaining = PlantGrowth.GetRemainingSeconds(__instance);
                    return remaining.HasValue && remaining.Value <= 0;
                };
            }
        }
    }
}
