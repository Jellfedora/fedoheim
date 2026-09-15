using System;
using System.Collections.Generic;
using HarmonyLib;

namespace FedoHud
{
    // Ajoute, pour chaque emplacement occupé d'une broche de cuisson, le temps restant
    // avant que ce soit cuit (ou avant que ça brûle si déjà cuit) -- architecture
    // différente des autres tooltips de ce mod : `CookingStation.GetHoverText()` ne
    // renvoie rien d'exploitable (le vrai texte est assigné en continu à
    // `m_addFoodSwitch.m_hoverText`, un `Switch` public, depuis `UpdateCooking()`
    // privée). On patche donc `UpdateCooking()` elle-même en Postfix, et on complète ce
    // même champ juste après l'original -- rien à casser, on ajoute simplement à la
    // suite. Signatures vérifiées contre assembly_valheim.dll (1.0), rien deviné.
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
                    AppendLines(__instance);
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogWarning($"FedoHud: cooking station tooltip failed: {e.Message}");
                }
            }
        }

        private static void AppendLines(CookingStation station)
        {
            if (station.m_addFoodSwitch == null)
            {
                return;
            }

            var nview = station.GetComponent<ZNetView>();
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null || station.m_slots == null)
            {
                return;
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

            if (lines.Count == 0)
            {
                return;
            }

            string extra = string.Join("\n", lines);
            station.m_addFoodSwitch.m_hoverText = string.IsNullOrEmpty(station.m_addFoodSwitch.m_hoverText)
                ? extra
                : $"{station.m_addFoodSwitch.m_hoverText}\n{extra}";
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
