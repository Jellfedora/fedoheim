using System;
using System.Runtime.CompilerServices;
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
    // (arme en main), le charme n'étant pas destiné à être équipé. Avant même le cooldown, un
    // verrou de propriété (SummonItemOwnershipPatch) bloque toute utilisation par quelqu'un
    // d'autre que le premier joueur à avoir utilisé cet exemplaire précis.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
    internal static class SummonItemUsePatch
    {
        // Un wrapper de classe est nécessaire : ConditionalWeakTable exige une TValue
        // référence, un float ne peut pas y aller directement.
        private sealed class CooldownState
        {
            // HasUsed distingue "jamais utilisé" de "utilisé à Time.time == 0" (juste après le
            // chargement de la scène) -- sans lui, la toute première utilisation d'un joueur
            // dans les premières secondes après le démarrage du serveur pourrait être vue à
            // tort comme "encore en recharge".
            public bool HasUsed;
            public float LastUseTime;
        }

        // ConditionalWeakTable plutôt qu'un Dictionary<Humanoid, float> classique : une entrée
        // ne retient jamais son Humanoid en vie (clé à référence faible), et disparaît
        // automatiquement une fois celui-ci ramassé par le GC (peu après une déconnexion,
        // n'ayant plus d'autre référent) -- sinon la table grossissait d'une entrée par
        // connexion de joueur, jamais nettoyée, sur toute la durée de vie du serveur.
        private static readonly ConditionalWeakTable<Humanoid, CooldownState> LastUse = new ConditionalWeakTable<Humanoid, CooldownState>();

        // Utilisé par SummonItemCooldownOverlayPatch pour afficher le compte à rebours visuel
        // sur l'icône du charme dans l'inventaire. 0 = pas (ou plus) en recharge.
        public static float GetRemainingCooldown(Humanoid instance)
        {
            if (instance == null || !LastUse.TryGetValue(instance, out CooldownState state) || !state.HasUsed)
            {
                return 0f;
            }

            float remaining = FedoKnorriPlugin.Instance.SummonCooldownSeconds.Value - (Time.time - state.LastUseTime);
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

            // Verrou de propriété (voir SummonItemOwnershipPatch) : avant même le cooldown,
            // pour qu'un joueur qui n'est pas le propriétaire ne puisse ni invoquer/ranger le
            // compagnon de quelqu'un d'autre, ni faire tourner ce cooldown à sa place.
            if (!SummonItemOwnershipPatch.TryUse(item, owner))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, FedoKnorriPlugin.Instance.SummonItemNotOwnerMessage.Value);
                return true;
            }

            CooldownState state = LastUse.GetValue(instance, _ => new CooldownState());

            float cooldown = FedoKnorriPlugin.Instance.SummonCooldownSeconds.Value;
            if (state.HasUsed && Time.time - state.LastUseTime < cooldown)
            {
                return true;
            }

            state.HasUsed = true;
            state.LastUseTime = Time.time;

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
