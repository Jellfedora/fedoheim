using System;
using HarmonyLib;

namespace FedoKnorri
{
    // Chaque graine se lie au premier joueur qui l'utilise avec succès (même principe "premier
    // arrivé, premier servi" que la liaison compte <-> personnage de
    // FedoServerTools/AutoConnect, voir CLAUDE.md) -- toute utilisation suivante par un AUTRE
    // joueur est bloquée (voir SummonItemUsePatch.ShouldSummon, qui appelle TryUse ci-dessous
    // avant même de regarder le cooldown).
    //
    // Réutilise ItemDrop.ItemData.m_crafterID/m_crafterName (champs publics vanilla) plutôt
    // qu'un vrai système à part : ce sont les champs que le jeu utilise normalement pour
    // attribuer un exemplaire d'item à qui l'a fabriqué ("Fabriqué par X" en infobulle) --
    // cette graine n'est de toute façon jamais craftable (voir SummonItemPrefabPatch), donc
    // rien d'autre ne les lit/écrit pour elle. Avantage concret : ce sont des champs PAR
    // EXEMPLAIRE d'ItemData (contrairement à m_shared, partagé par tous les exemplaires du même
    // item, voir SummonItemPrefabPatch.SharedData dans l'ancienne version) déjà sauvegardés et
    // rechargés automatiquement par le jeu avec chaque item -- pas besoin d'un mécanisme de
    // données personnalisées maison, qui n'existe de toute façon pas nativement sur
    // ItemDrop.ItemData (contrairement à Player.m_customData).
    internal static class SummonItemOwnershipPatch
    {
        // Renvoie true si `user` peut utiliser cet exemplaire précis (le lie à lui au passage
        // s'il n'était encore lié à personne). Renvoie false s'il appartient déjà à un autre
        // joueur -- l'appelant doit alors refuser l'action.
        public static bool TryUse(ItemDrop.ItemData item, Player user)
        {
            if (item.m_crafterID == 0L)
            {
                item.m_crafterID = user.GetPlayerID();
                item.m_crafterName = user.GetPlayerName();
                return true;
            }

            return item.m_crafterID == user.GetPlayerID();
        }

        // Ajoute "Appartient à : X" à l'infobulle une fois la graine liée à quelqu'un. Seul
        // endroit possible pour un texte propre à CET exemplaire : m_shared.m_name/m_description
        // (voir SummonItemPrefabPatch) sont un unique SharedData partagé par tous les exemplaires
        // de l'item, pas question d'y écrire un nom de joueur.
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int))]
        private static class TooltipPatch
        {
            // Patch sur une méthode vanilla appelée pour l'infobulle de N'IMPORTE QUEL item, pas
            // seulement la graine : une exception non rattrapée ici casserait l'affichage des
            // infobulles pour tout le monde. On protège large par précaution, même principe que
            // SummonItemCooldownOverlayPatch.
            private static void Postfix(ItemDrop.ItemData item, ref string __result)
            {
                try
                {
                    if (item == null || item.m_crafterID == 0L || !SummonItemPrefabPatch.IsSummonItem(item))
                    {
                        return;
                    }

                    string label = string.Format(FedoKnorriPlugin.Instance.SummonItemOwnerLabel.Value, item.m_crafterName);
                    __result += $"\n<color=orange>{label}</color>";
                }
                catch (Exception e)
                {
                    FedoKnorriPlugin.Log?.LogError($"FedoKnorri: SummonItemOwnershipPatch.TooltipPatch a levé une exception : {e}");
                }
            }
        }
    }
}
