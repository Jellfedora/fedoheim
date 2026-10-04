using HarmonyLib;

namespace FedoHud
{
    // Icône "!" flottante au-dessus d'une ruche pleine -- voir FloatingExclamationIcon.cs
    // pour l'implémentation partagée (billboard/throttle), et BeehiveTooltip.cs pour la
    // ligne de tooltip équivalente au survol. S'applique aussi au nid d'oiseaux, qui
    // réutilise le même composant `Beehive` (voir BeehiveTooltip.IsBirdNest) -- rien de
    // spécifique à faire ici, `m_maxHoney` désigne juste la capacité max quel que soit
    // l'item produit.
    internal static class BeehiveFullIndicator
    {
        [HarmonyPatch(typeof(Beehive), "Awake")]
        private static class BeehiveAwakePatch
        {
            private static void Postfix(Beehive __instance)
            {
                if (__instance.GetComponent<FloatingExclamationIcon>() != null)
                {
                    return;
                }

                var icon = __instance.gameObject.AddComponent<FloatingExclamationIcon>();
                icon.IsEnabledInConfig = () =>
                    FedoHudPlugin.Instance != null && FedoHudPlugin.Instance.ShowBeehiveFullIcon;
                icon.IsReady = () => IsFull(__instance);
            }
        }

        private static bool IsFull(Beehive beehive)
        {
            var nview = beehive.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null)
            {
                return false;
            }

            return zdo.GetInt(ZDOVars.s_level) >= beehive.m_maxHoney;
        }
    }
}
