using System;
using System.Collections.Generic;
using HarmonyLib;

namespace FedoHud
{
    // Ajoute, pour chaque emplacement occupé d'une broche/cuisinière, le temps restant
    // avant que ce soit cuit (ou avant que ça brûle si déjà cuit).
    //
    // Deux points d'affichage possibles côté jeu pour une même station, d'où deux
    // patches -- vécu en jeu : le premier seul suffisait sur la broche en bois, mais
    // restait invisible sur la cuisinière en fer (le viseur du joueur n'y retombe pas
    // forcément sur le même point).
    // 1) `m_addFoodSwitch.m_hoverText` (un `Switch` public), réassigné en continu par le
    //    jeu depuis `UpdateCooking()` (privée, patchée en Postfix) -- affiché en
    //    survolant le point d'interaction "ajouter à cuire" lui-même.
    // 2) `CookingStation.GetHoverText()` (publique, interface `Hoverable`) -- affichée en
    //    survolant la station elle-même. Renvoie volontairement "" tant que
    //    `m_addFoodSwitch` existe (vérifié par décompilation) puisque le jeu compte sur
    //    le point (1) pour ce cas -- notre Postfix y injecte quand même nos lignes,
    //    sans que ça casse rien puisque ce texte était vide de toute façon.
    // Signatures vérifiées contre assembly_valheim.dll (1.0), rien deviné.
    internal static class CookingStationTooltip
    {
        [HarmonyPatch(typeof(CookingStation), "UpdateCooking")]
        private static class UpdateCookingPatch
        {
            private static void Postfix(CookingStation __instance)
            {
                if (FedoHudPlugin.Instance == null || !FedoHudPlugin.Instance.ShowCookingTooltip)
                {
                    return;
                }

                try
                {
                    AppendToSwitch(__instance);
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogWarning($"FedoHud: cooking station tooltip failed: {e.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.GetHoverText))]
        private static class GetHoverTextPatch
        {
            private static void Postfix(CookingStation __instance, ref string __result)
            {
                if (FedoHudPlugin.Instance == null || !FedoHudPlugin.Instance.ShowCookingTooltip)
                {
                    return;
                }

                try
                {
                    string extra = BuildLines(__instance);
                    if (!string.IsNullOrEmpty(extra))
                    {
                        __result = string.IsNullOrEmpty(__result) ? extra : $"{__result}\n{extra}";
                    }
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogWarning($"FedoHud: cooking station tooltip failed: {e.Message}");
                }
            }
        }

        private static void AppendToSwitch(CookingStation station)
        {
            if (station.m_addFoodSwitch == null)
            {
                return;
            }

            string extra = BuildLines(station);
            if (string.IsNullOrEmpty(extra))
            {
                return;
            }

            station.m_addFoodSwitch.m_hoverText = string.IsNullOrEmpty(station.m_addFoodSwitch.m_hoverText)
                ? extra
                : $"{station.m_addFoodSwitch.m_hoverText}\n{extra}";
        }

        private static string BuildLines(CookingStation station)
        {
            var nview = station.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null || station.m_slots == null)
            {
                return null;
            }

            var lines = new List<string>();
            for (int i = 0; i < station.m_slots.Length; i++)
            {
                string itemName = zdo.GetString("slot" + i, "");
                if (string.IsNullOrEmpty(itemName))
                {
                    continue;
                }

                // Même clé de chaîne que le nom ci-dessus ("slotN"), mais dans le
                // dictionnaire des floats de la ZDO -- pas de collision, ZDO stocke
                // chaque type de valeur séparément.
                float cookedTime = zdo.GetFloat("slot" + i, 0f);
                // `CookingStation.Status` (NotDone/Done/Burnt) est un enum privé --
                // inaccessible directement, d'où ces entiers bruts. Numérotation par
                // défaut du compilateur en 1.0 (0/1/2 dans l'ordre de déclaration),
                // vérifiée par décompilation IL au moment de la recherche mais pas
                // garantie de rester stable à travers une future mise à jour du jeu.
                int status = zdo.GetInt("slotstatus" + i, 0);

                var conversion = FindConversion(station, itemName);
                if (conversion == null)
                {
                    continue;
                }

                string itemLabel = GameLocalization.LocalizeOrRaw(itemName);

                if (status == 0)
                {
                    float remaining = conversion.m_cookTime - cookedTime;
                    if (remaining > 0)
                    {
                        lines.Add($"{itemLabel}: {FedoHudPlugin.Instance.CookingRemainingPrefix} {HudTimeFormat.FormatRemaining(remaining)}");
                    }
                }
                else if (status == 1 && station.m_canOvercookItems)
                {
                    float remaining = conversion.m_cookTime * 2f - cookedTime;
                    if (remaining > 0)
                    {
                        lines.Add($"{itemLabel}: {FedoHudPlugin.Instance.CookingBurnPrefix} {HudTimeFormat.FormatRemaining(remaining)}");
                    }
                }
            }

            return lines.Count == 0 ? null : string.Join("\n", lines);
        }

        // Décompilation : la même entrée sert avant ET après cuisson (le nom d'item en
        // ZDO ne change pas), donc on matche sur `m_from` OU `m_to`.
        private static CookingStation.ItemConversion FindConversion(CookingStation station, string itemName)
        {
            if (station.m_conversion == null)
            {
                return null;
            }

            foreach (var conversion in station.m_conversion)
            {
                if ((conversion.m_from != null && conversion.m_from.gameObject.name == itemName)
                    || (conversion.m_to != null && conversion.m_to.gameObject.name == itemName))
                {
                    return conversion;
                }
            }

            return null;
        }
    }
}
