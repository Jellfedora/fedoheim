using System;
using HarmonyLib;

namespace FedoHud
{
    // Ajoute une ligne "Repousse dans Xh" au texte de survol d'une ressource sauvage
    // déjà cueillie (champignon, baies, ortie... -- `Pickable`, pas `Plant` qui gère les
    // cultures plantées). `Pickable.GetHoverText()` renvoie déjà "" une fois cueillie
    // (vérifié par décompilation : `if (m_picked || m_enabled == 0) return "";`) --
    // notre Postfix remplace directement `__result` dans ce cas, rien à casser côté
    // texte vanilla puisqu'il n'y en a pas. Signatures vérifiées contre
    // assembly_valheim.dll (1.0), rien deviné.
    internal static class PickableTooltip
    {
        [HarmonyPatch(typeof(Pickable), nameof(Pickable.GetHoverText))]
        private static class GetHoverTextPatch
        {
            private static void Postfix(Pickable __instance, ref string __result)
            {
                if (FedoHudPlugin.Instance == null || !FedoHudPlugin.Instance.ShowPickableTooltip)
                {
                    return;
                }

                try
                {
                    string line = BuildLine(__instance);
                    if (!string.IsNullOrEmpty(line))
                    {
                        __result = line;
                    }
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogWarning($"FedoHud: pickable tooltip failed: {e.Message}");
                }
            }
        }

        private static string BuildLine(Pickable pickable)
        {
            var nview = pickable.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null || ZNet.instance == null)
            {
                return null;
            }

            // Pas encore cueillie -- rien à ajouter (le texte vanilla normal, non vide,
            // reste tel quel).
            if (!zdo.GetBool(ZDOVars.s_picked, false))
            {
                return null;
            }

            // Reproduit Pickable.ShouldRespawn() (privée) : m_respawnTimeMinutes est en
            // MINUTES (pas secondes, contrairement aux autres tooltips de ce mod) --
            // vérifié par décompilation.
            long pickedTicks = zdo.GetLong(ZDOVars.s_pickedTime, 0);
            double elapsedMinutes = (ZNet.instance.GetTime() - new DateTime(pickedTicks)).TotalMinutes;
            double remainingMinutes = pickable.m_respawnTimeMinutes - elapsedMinutes;
            if (remainingMinutes <= 0)
            {
                return null;
            }

            return $"{FedoHudPlugin.Instance.PickableRemainingPrefix} {HudTimeFormat.FormatRemaining(remainingMinutes * 60.0)}";
        }
    }
}
