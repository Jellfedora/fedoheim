using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace FedoKnorri
{
    // Intercepte "Utiliser" (bouton de l'inventaire, ou touche assignée) quand l'item concerné
    // est l'une des deux graines d'invocation (base ou chaman, voir CompanionTier/ResolveTier) :
    // au lieu de la vraie logique de consommation (qui n'existe de toute façon pas pour l'item
    // source cloné, voir SummonItemPrefabPatch), ça fait apparaître/évoluer/ranger le compagnon
    // (un seul à la fois par joueur, tous paliers confondus -- voir
    // CompanionAI.FindExistingCompanion). Prefix qui renvoie false : le vrai UseItem ne s'exécute
    // jamais dans ce cas -- même principe que FedoGuardian.SummonWandUsePatch, mais sur
    // Humanoid.UseItem (déclenché par le clic "Utiliser" en inventaire) plutôt que StartAttack
    // (arme en main), les graines n'étant pas destinées à être équipées. Avant même le cooldown,
    // un verrou de propriété (SummonItemOwnershipPatch) bloque toute utilisation par quelqu'un
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

        // Pénalité de mort (voir CompanionSpawner.Spawn/ApplyDeathCooldown, abonné sur
        // Character.m_onDeath) : clé = PlayerID stable, PAS l'objet Humanoid/Player comme
        // LastUse ci-dessus -- contrairement au cooldown normal (un simple anti-spam de clic,
        // qui peut repartir de zéro à chaque reconnexion sans conséquence), celui-ci est une
        // vraie sanction pour la mort du compagnon et doit survivre à une déco/reco. Un
        // Dictionary classique convient ici (pas de risque de fuite façon LastUse) : la clé est
        // un long, pas une référence d'objet à retenir en vie, et le nombre de joueurs
        // distincts d'un serveur reste borné.
        private static readonly Dictionary<long, float> DeathCooldownEndTime = new Dictionary<long, float>();

        public static void ApplyDeathCooldown(long ownerId)
        {
            DeathCooldownEndTime[ownerId] = Time.time + FedoKnorriPlugin.Instance.DeathCooldownSeconds.Value;
        }

        private static float GetDeathCooldownRemaining(long ownerId)
        {
            if (!DeathCooldownEndTime.TryGetValue(ownerId, out float endTime))
            {
                return 0f;
            }

            float remaining = endTime - Time.time;
            return remaining > 0f ? remaining : 0f;
        }

        // Utilisé par SummonItemCooldownOverlayPatch pour afficher le compte à rebours visuel
        // sur l'icône du charme dans l'inventaire/la barre de raccourcis. 0 = pas (ou plus) en
        // recharge. Renvoie le plus grand des deux cooldowns (normal ou pénalité de mort) : les
        // deux se comportent identiquement à l'affichage (icône grisée + compte à rebours), pas
        // besoin de les distinguer visuellement, juste de montrer le bon temps restant.
        public static float GetRemainingCooldown(Humanoid instance)
        {
            if (instance == null)
            {
                return 0f;
            }

            float normal = 0f;
            if (LastUse.TryGetValue(instance, out CooldownState state) && state.HasUsed)
            {
                normal = FedoKnorriPlugin.Instance.SummonCooldownSeconds.Value - (Time.time - state.LastUseTime);
                if (normal < 0f)
                {
                    normal = 0f;
                }
            }

            float death = instance is Player player ? GetDeathCooldownRemaining(player.GetPlayerID()) : 0f;

            return Mathf.Max(normal, death);
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

        // Renvoie le palier correspondant si cet item est l'une de nos deux graines
        // d'invocation, sinon null -- point d'entrée unique pour distinguer les deux (voir
        // CompanionTier), réutilisé aussi par SummonItemOwnershipPatch et
        // SummonItemCooldownOverlayPatch pour ne pas dupliquer ce OR à chaque endroit.
        public static CompanionTier? ResolveTier(ItemDrop.ItemData item)
        {
            if (SummonItemPrefabPatch.IsSummonItem(item))
            {
                return CompanionTier.Knorri;
            }

            if (ShamanSummonItemPrefabPatch.IsSummonItem(item))
            {
                return CompanionTier.Shaman;
            }

            return null;
        }

        // Renvoie true si on a pris la main (invocation, évolution, rangement ou cooldown),
        // auquel cas la vraie méthode ne doit pas s'exécuter.
        private static bool ShouldSummon(Humanoid instance, ItemDrop.ItemData item)
        {
            CompanionTier? tier = ResolveTier(item);
            if (tier == null)
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

            // Pénalité de mort avant même le cooldown normal : si le compagnon vient de mourir,
            // aucune des deux graines ne doit pouvoir en réinvoquer un tout de suite, peu
            // importe le palier utilisé.
            if (GetDeathCooldownRemaining(owner.GetPlayerID()) > 0f)
            {
                return true;
            }

            // Un seul cooldown partagé entre les deux graines (même Humanoid en clé, cf.
            // LastUse) : ce sont deux formes du même compagnon, pas deux emplacements
            // d'invocation séparés -- alterner entre les deux ne doit pas permettre de
            // contourner le délai.
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
                CompanionTier existingTier = CompanionAI.GetTier(existing);

                if (existingTier == tier.Value)
                {
                    // Même palier : rangement classique, interrupteur.
                    CompanionPoofEffect.Show(existing.transform.position);
                    existing.GetComponent<ZNetView>()?.Destroy();
                    return true;
                }

                if (tier.Value < existingTier)
                {
                    // Pas de retour en arrière : utiliser la graine de base pendant qu'un
                    // chaman est déjà dehors ne fait rien au compagnon actuel, juste un message.
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, FedoKnorriPlugin.Instance.CannotDowngradeMessage.Value);
                    return true;
                }

                // Évolution : le compagnon actuel disparaît et le nouveau palier prend sa place
                // AU MÊME ENDROIT (pas devant le joueur comme une invocation depuis rien) --
                // CompanionSpawner.Spawn réapplique le dernier nom personnalisé connu
                // (ApplySavedName), donc un compagnon renommé garde son nom en évoluant.
                Vector3 evolvePosition = existing.transform.position;
                Quaternion evolveRotation = existing.transform.rotation;
                CompanionPoofEffect.Show(evolvePosition);
                existing.GetComponent<ZNetView>()?.Destroy();
                CompanionSpawner.Spawn(evolvePosition, evolveRotation, owner, tier.Value);
                return true;
            }

            Vector3 forward = instance.transform.forward;
            Vector3 position = instance.transform.position + forward * FedoKnorriPlugin.Instance.SummonDistance.Value;
            CompanionSpawner.Spawn(position, instance.transform.rotation, owner, tier.Value);

            return true;
        }
    }
}
