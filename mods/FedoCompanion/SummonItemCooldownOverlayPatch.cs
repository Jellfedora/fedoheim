using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FedoCompanion
{
    // Reconstitue le rendu "objet en recharge" (icône assombrie + compte à rebours) que Valheim
    // n'offre nativement pour aucun item quelconque -- vérifié par dump de réflexion sur
    // ItemDrop.ItemData/HotkeyBar.ElementData/InventoryGrid.Element : aucun champ de cooldown
    // générique n'existe, contrairement à la durabilité ou la quantité par exemple.
    //
    // InventoryGrid.Element est une classe imbriquée PRIVÉE (vérifié par réflexion :
    // IsNestedPublic=false, IsNestedAssembly=false) -- inaccessible depuis cet assembly autrement
    // que par réflexion, d'où les FieldInfo mis en cache une seule fois ci-dessous (même
    // technique que FedoGoldRabbit.ZsfxAudioClipsField pour un champ privé d'un type vanilla).
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    internal static class SummonItemCooldownOverlayPatch
    {
        private static readonly Color CooldownTint = new Color(0.35f, 0.35f, 0.35f, 1f);

        private static readonly FieldInfo ElementsField = AccessTools.Field(typeof(InventoryGrid), "m_elements");
        private static readonly Type ElementType = AccessTools.Inner(typeof(InventoryGrid), "Element");
        private static readonly FieldInfo PosField = AccessTools.Field(ElementType, "m_pos");
        private static readonly FieldInfo IconField = AccessTools.Field(ElementType, "m_icon");
        private static readonly FieldInfo AmountField = AccessTools.Field(ElementType, "m_amount");

        // Patch sur une méthode vanilla appelée à chaque rafraîchissement de N'IMPORTE QUELLE
        // grille d'inventaire (inventaire du joueur, coffres, marchands...) : une exception non
        // rattrapée ici casserait l'affichage de l'inventaire pour tout le monde. On protège
        // large, et on sort vite si la réflexion n'a pas trouvé ce qu'il faut (version du jeu
        // différente de celle vérifiée).
        private static void Postfix(InventoryGrid __instance, Player player)
        {
            try
            {
                if (ElementsField == null || PosField == null || IconField == null)
                {
                    return;
                }

                float remaining = GetRemainingCooldown(player);
                if (!(ElementsField.GetValue(__instance) is IEnumerable elements))
                {
                    return;
                }

                Inventory inventory = __instance.GetInventory();
                if (inventory == null)
                {
                    return;
                }

                foreach (object element in elements)
                {
                    ApplyCooldownVisual(inventory, element, remaining);
                }
            }
            catch (Exception e)
            {
                FedoCompanionPlugin.Log?.LogError($"FedoCompanion: SummonItemCooldownOverlayPatch a levé une exception : {e}");
            }
        }

        private static float GetRemainingCooldown(Player player)
        {
            // La grille affichée est toujours celle du joueur local qui a ouvert son propre
            // inventaire -- pas besoin de gérer le cas d'un autre joueur ici.
            return player != null ? SummonItemUsePatch.GetRemainingCooldown(player) : 0f;
        }

        private static void ApplyCooldownVisual(Inventory inventory, object element, float remaining)
        {
            if (remaining <= 0f)
            {
                // Jamais touché en dehors du cooldown : on laisse l'icône/le texte tels que
                // le reste de UpdateGui (déjà exécuté avant ce Postfix) les a réglés --
                // pas de "restauration" hasardeuse qui écraserait une teinte vanilla légitime
                // (ex: rareté d'un objet).
                return;
            }

            var pos = (Vector2i)PosField.GetValue(element);
            ItemDrop.ItemData item = inventory.GetItemAt(pos.x, pos.y);
            if (item == null || !SummonItemPrefabPatch.IsSummonItem(item))
            {
                return;
            }

            if (IconField.GetValue(element) is Image icon)
            {
                icon.color = CooldownTint;
            }

            if (AmountField != null && AmountField.GetValue(element) is TMP_Text amount)
            {
                amount.text = Mathf.CeilToInt(remaining).ToString();
                amount.gameObject.SetActive(true);
            }
        }
    }
}
