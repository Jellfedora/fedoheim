using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FedoGuardian
{
    // Intercepte l'attaque principale (clic gauche) quand l'arme actuellement en main est la
    // baguette d'invocation : au lieu de vraiment frapper, ça fait apparaître un garde devant
    // l'utilisateur. Prefix qui renvoie false : la vraie attaque (dégâts, animation, endurance...)
    // ne s'exécute jamais dans ce cas.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class SummonWandUsePatch
    {
        private static readonly Dictionary<Humanoid, float> LastUse = new Dictionary<Humanoid, float>();

        // Patch sur une méthode vanilla appelée en permanence par le vrai joueur (chaque coup,
        // sur n'importe quelle arme) : une exception non rattrapée ici casserait StartAttack pour
        // tout le monde, pas seulement pour la baguette. On protège large par précaution.
        private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
        {
            try
            {
                return !ShouldSummon(__instance, secondaryAttack, ref __result);
            }
            catch (Exception e)
            {
                FedoGuardianPlugin.Log?.LogError($"FedoGuardian: SummonWandUsePatch a levé une exception : {e}");
                return true;
            }
        }

        // Renvoie true si on a pris la main (invocation ou cooldown), auquel cas __result est déjà
        // renseigné et la vraie méthode ne doit pas s'exécuter.
        private static bool ShouldSummon(Humanoid instance, bool secondaryAttack, ref bool result)
        {
            if (secondaryAttack)
            {
                return false;
            }

            ItemDrop.ItemData weapon = instance.GetCurrentWeapon();
            if (weapon == null || !SummonWandPrefabPatch.IsWand(weapon))
            {
                return false;
            }

            float cooldown = FedoGuardianPlugin.Instance.SummonWandCooldownSeconds.Value;
            if (LastUse.TryGetValue(instance, out float last) && Time.time - last < cooldown)
            {
                result = false;
                return true;
            }

            LastUse[instance] = Time.time;

            Vector3 forward = instance.transform.forward;
            Vector3 position = instance.transform.position + forward * FedoGuardianPlugin.Instance.SummonDistance.Value;
            GuardianSpawner.Spawn(position, instance.transform.rotation, female: false);

            // false, pas true : aucune vraie attaque ne démarre (pas d'Attack.Start, donc
            // InAttack() reste toujours false). Faire croire à l'appelant (le code d'input du
            // joueur) qu'une attaque a réussi le laisserait probablement attendre indéfiniment une
            // transition d'état qui n'arrivera jamais, bloquant les déplacements.
            result = false;
            return true;
        }
    }
}
