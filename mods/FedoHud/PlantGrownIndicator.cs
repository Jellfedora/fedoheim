using System;
using HarmonyLib;

namespace FedoHud
{
    // Affiche l'icône "!" au-dessus d'une culture arrivée à maturité -- reconnue par son
    // identité de prefab (voir CropRegistry.cs), jamais en essayant d'attraper l'instant
    // précis où elle mûrit (Plant.Grow() peut très bien s'être déjà produit avant même
    // le lancement de cette session, voir CropRegistry.cs pour le détail). `Pickable.
    // Awake()` (privée) se déclenche localement pour chaque joueur dès que l'objet lui
    // est synchronisé par le réseau, indépendamment de quand/qui l'a fait pousser.
    internal static class PlantGrownIndicator
    {
        [HarmonyPatch(typeof(Pickable), "Awake")]
        private static class PickableAwakePatch
        {
            private static void Postfix(Pickable __instance)
            {
                try
                {
                    string name = __instance.gameObject.name.Replace("(Clone)", "");
                    if (!CropRegistry.IsGrownCropName(name))
                    {
                        return;
                    }

                    if (__instance.GetComponent<FloatingExclamationIcon>() != null)
                    {
                        return;
                    }

                    var icon = __instance.gameObject.AddComponent<FloatingExclamationIcon>();
                    icon.IsEnabledInConfig = () =>
                        FedoHudPlugin.Instance != null && FedoHudPlugin.Instance.ShowGrowthReadyIcon;
                    // Se cache tout seul une fois cueilli, au cas où l'objet ne serait
                    // pas immédiatement détruit (ressource à repousse, voir m_hideWhenPicked
                    // dans Pickable) -- GetPicked() est public.
                    icon.IsReady = () => !__instance.GetPicked();
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogWarning($"FedoHud: growth-ready icon failed: {e.Message}");
                }
            }
        }
    }
}
