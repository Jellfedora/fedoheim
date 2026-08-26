using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FedoKnorri
{
    // Reconstitue le rendu "objet en recharge" (icône assombrie + compte à rebours) que Valheim
    // n'offre nativement pour aucun item quelconque -- vérifié par dump de réflexion sur
    // ItemDrop.ItemData/HotkeyBar.ElementData/InventoryGrid.Element : aucun champ de cooldown
    // générique n'existe, contrairement à la durabilité ou la quantité par exemple. Deux patches
    // séparés dans ce fichier : la grille d'inventaire (ci-dessous) ET la barre de raccourcis
    // (SummonItemHotkeyBarCooldownPatch, plus bas) sont deux systèmes UI vanilla distincts, sans
    // rendu partagé -- corriger l'un sans l'autre laisse l'item apparaître disponible dans
    // l'endroit non patché pendant tout le cooldown (repéré en jeu pour la barre de raccourcis).
    //
    // InventoryGrid.Element et HotkeyBar.ElementData sont des classes imbriquées PRIVÉES
    // (vérifié par réflexion : IsNestedPublic=false, IsNestedAssembly=false) -- inaccessibles
    // depuis cet assembly autrement que par réflexion, d'où les FieldInfo mis en cache une seule
    // fois ci-dessous (même technique que FedoGoldRabbit.ZsfxAudioClipsField pour un champ privé
    // d'un type vanilla).
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    internal static class SummonItemCooldownOverlayPatch
    {
        // internal (pas private) : réutilisée telle quelle par SummonItemHotkeyBarCooldownPatch
        // ci-dessous, même rendu voulu dans les deux endroits.
        internal static readonly Color CooldownTint = new Color(0.35f, 0.35f, 0.35f, 1f);

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
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: SummonItemCooldownOverlayPatch a levé une exception : {e}");
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

    // Même reconstitution que ci-dessus, mais pour la barre de raccourcis (HotkeyBar) --
    // séparée de l'inventaire côté vanilla, avec sa propre classe imbriquée ElementData (elle
    // aussi PRIVÉE, vérifié par réflexion) et son propre Postfix : sans ce second patch, l'item
    // se grisait bien avec le compte à rebours dans l'inventaire mais restait affiché comme
    // disponible dans la barre du bas pendant tout le cooldown (repéré en jeu).
    //
    // HotkeyBar.m_items (List<ItemDrop.ItemData>) et HotkeyBar.m_elements
    // (List<HotkeyBar.ElementData>) sont deux listes parallèles, indexées de la même façon --
    // vérifié par dump de réflexion, pas de lien direct plus simple entre les deux (pas de champ
    // "item" sur ElementData elle-même).
    [HarmonyPatch(typeof(HotkeyBar), "UpdateIcons")]
    internal static class SummonItemHotkeyBarCooldownPatch
    {
        private static readonly FieldInfo ElementsField = AccessTools.Field(typeof(HotkeyBar), "m_elements");
        private static readonly FieldInfo ItemsField = AccessTools.Field(typeof(HotkeyBar), "m_items");
        private static readonly Type ElementDataType = AccessTools.Inner(typeof(HotkeyBar), "ElementData");
        private static readonly FieldInfo IconField = AccessTools.Field(ElementDataType, "m_icon");
        private static readonly FieldInfo AmountField = AccessTools.Field(ElementDataType, "m_amount");
        // Cache interne de HotkeyBar (int, -1 = "jamais initialisé" selon le decompile vanilla) :
        // évite de réécrire amount.text si le nombre réel d'objets empilés n'a pas changé depuis
        // la dernière frame -- voir RestoreDefaultVisual pour pourquoi on doit le réinitialiser
        // nous-mêmes.
        private static readonly FieldInfo StackTextField = AccessTools.Field(ElementDataType, "m_stackText");

        // Patch sur une méthode vanilla appelée à chaque rafraîchissement de la barre de
        // raccourcis du joueur local uniquement (pas de notion de grille tierce ici, contrairement
        // à InventoryGrid) -- même protection large par précaution qu'au-dessus. Contrairement à
        // SummonItemCooldownOverlayPatch, on ne sort plus tôt quand remaining <= 0 : il faut
        // pouvoir restaurer l'affichage normal (voir ApplyCooldownVisual/RestoreDefaultVisual),
        // pas seulement appliquer la recharge.
        private static void Postfix(HotkeyBar __instance, Player player)
        {
            try
            {
                if (ElementsField == null || ItemsField == null || IconField == null)
                {
                    return;
                }

                float remaining = player != null ? SummonItemUsePatch.GetRemainingCooldown(player) : 0f;

                if (!(ElementsField.GetValue(__instance) is IList elements) || !(ItemsField.GetValue(__instance) is IList items))
                {
                    return;
                }

                int count = Math.Min(elements.Count, items.Count);
                for (int i = 0; i < count; i++)
                {
                    ApplyCooldownVisual(elements[i], items[i] as ItemDrop.ItemData, remaining);
                }
            }
            catch (Exception e)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: SummonItemHotkeyBarCooldownPatch a levé une exception : {e}");
            }
        }

        private static void ApplyCooldownVisual(object element, ItemDrop.ItemData item, float remaining)
        {
            if (item == null || !SummonItemPrefabPatch.IsSummonItem(item))
            {
                return;
            }

            if (remaining <= 0f)
            {
                RestoreDefaultVisual(element);
                return;
            }

            if (IconField.GetValue(element) is Image icon)
            {
                icon.color = SummonItemCooldownOverlayPatch.CooldownTint;
            }

            if (AmountField != null && AmountField.GetValue(element) is TMP_Text amount)
            {
                amount.text = Mathf.CeilToInt(remaining).ToString();
                amount.gameObject.SetActive(true);
            }
        }

        // Contrairement à InventoryGrid.UpdateGui (qui réécrit icon.color/amount.text à CHAQUE
        // frame, quel que soit l'état -- vérifié par désassemblage), HotkeyBar.UpdateIcons ne
        // touche JAMAIS icon.color, et ne réécrit amount.text que si le nombre réel d'objets
        // empilés a changé depuis la dernière frame (jamais notre cas : ce nombre, lui, ne
        // bouge pas pendant tout le cooldown). Sans cette restauration explicite, l'icône
        // restait grisée et le texte figé sur le dernier chiffre du compte à rebours
        // indéfiniment une fois le cooldown terminé (repéré en jeu) -- vanilla n'a tout
        // simplement aucune raison de jamais y retoucher tout seul. m_stackText remis à -1
        // (la valeur "jamais initialisé" que vanilla lui donne elle-même au départ) pour forcer
        // HotkeyBar à se re-synchroniser de lui-même dès la frame suivante, plutôt que de
        // deviner ici le texte exact qu'il afficherait (format d'empilement, pluriel...).
        private static void RestoreDefaultVisual(object element)
        {
            if (IconField.GetValue(element) is Image icon)
            {
                icon.color = Color.white;
            }

            StackTextField?.SetValue(element, -1);
        }
    }
}
