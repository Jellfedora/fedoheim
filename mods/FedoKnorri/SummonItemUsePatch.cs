using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FedoKnorri
{
    // Intercepte "Utiliser" (bouton de l'inventaire, ou touche assignée) quand l'item concerné
    // est le charme d'invocation : au lieu de la vraie logique de consommation (qui n'existe de
    // toute façon pas pour l'item source cloné, voir SummonItemPrefabPatch), ça fait apparaître
    // ou ranger le compagnon (interrupteur, un seul à la fois par joueur -- voir
    // CompanionAI.FindExistingCompanion). Prefix qui renvoie false : le vrai UseItem ne s'exécute
    // jamais dans ce cas -- même principe que FedoGuardian.SummonWandUsePatch, mais sur
    // Humanoid.UseItem (déclenché par le clic "Utiliser" en inventaire) plutôt que StartAttack
    // (arme en main), le charme n'étant pas destiné à être équipé.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
    internal static class SummonItemUsePatch
    {
        private static readonly Dictionary<Humanoid, float> LastUse = new Dictionary<Humanoid, float>();

        // Utilisé par SummonItemCooldownOverlayPatch pour afficher le compte à rebours visuel
        // sur l'icône du charme dans l'inventaire. 0 = pas (ou plus) en recharge.
        public static float GetRemainingCooldown(Humanoid instance)
        {
            if (instance == null || !LastUse.TryGetValue(instance, out float last))
            {
                return 0f;
            }

            float remaining = FedoKnorriPlugin.Instance.SummonCooldownSeconds.Value - (Time.time - last);
            return remaining > 0f ? remaining : 0f;
        }

        // Patch sur une méthode vanilla appelée pour absolument tout item utilisé (nourriture,
        // potions, objets à activer...), pas seulement le charme : une exception non rattrapée
        // ici casserait UseItem pour tout le monde. On protège large par précaution.
        private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item)
        {
            try
            {
                return !ShouldSummon(__instance, item);
            }
            catch (Exception e)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: SummonItemUsePatch a levé une exception : {e}");
                return true;
            }
        }

        // Renvoie true si on a pris la main (invocation ou cooldown), auquel cas la vraie
        // méthode ne doit pas s'exécuter.
        private static bool ShouldSummon(Humanoid instance, ItemDrop.ItemData item)
        {
            if (!SummonItemPrefabPatch.IsSummonItem(item))
            {
                return false;
            }

            var owner = instance as Player;
            if (owner == null)
            {
                return false;
            }

            float cooldown = FedoKnorriPlugin.Instance.SummonCooldownSeconds.Value;
            if (LastUse.TryGetValue(instance, out float last) && Time.time - last < cooldown)
            {
                return true;
            }

            LastUse[instance] = Time.time;

            GameObject existing = CompanionAI.FindExistingCompanion(owner);
            if (existing != null)
            {
                CompanionPoofEffect.Show(existing.transform.position);
                existing.GetComponent<ZNetView>()?.Destroy();
                return true;
            }

            Vector3 forward = instance.transform.forward;
            Vector3 position = instance.transform.position + forward * FedoKnorriPlugin.Instance.SummonDistance.Value;
            CompanionSpawner.Spawn(position, instance.transform.rotation, owner);

            return true;
        }
    }
}
