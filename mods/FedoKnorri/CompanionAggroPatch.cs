using System;
using HarmonyLib;
using UnityEngine;

namespace FedoKnorri
{
    // Valheim n'a pas de vraie table d'aggro/menace comme un MMO -- une IA sauvage choisit
    // simplement le candidat le plus proche/perceptible parmi tout ce qu'elle considère comme un
    // ennemi (BaseAI.FindEnemy/FindClosestEnemy, qui filtrent via BaseAI.IsEnemy). Depuis que le
    // compagnon est en faction Players (voir CompanionPrefabPatch) et donc un candidat comme un
    // autre, on ne peut pas ajuster une "menace" qui n'existe pas -- seulement rendre le
    // compagnon MOINS SOUVENT considéré comme un candidat valide au moment où une IA sauvage
    // cherche une nouvelle cible, sans jamais l'exclure complètement (CompanionAggroChance,
    // 1 = jamais réduit, 0 = jamais choisi).
    //
    // Condition cruciale : `!__instance.HaveTarget()`. On ne s'applique QUE tant que cette IA n'a
    // pas encore de cible verrouillée -- une fois qu'un monstre a réellement choisi de
    // s'attaquer au compagnon, on ne retouche plus jamais IsEnemy pour lui, sous peine de le
    // faire "oublier" sa cible en pleine bagarre à chaque tick où le tirage au sort échoue
    // (un comportement qui aurait eu l'air d'un bug -- le monstre s'arrêtant et repartant sans
    // raison visible). Résultat : un monstre qui n'a pas encore de cible a une chance réduite de
    // choisir le compagnon plutôt que le joueur (qui, lui, reste toujours un candidat à 100%),
    // mais un monstre qui l'a déjà choisi va au bout de l'affrontement normalement.
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), typeof(Character))]
    internal static class CompanionAggroPatch
    {
        // Patch sur une méthode vanilla appelée en permanence par toutes les IA du jeu pour
        // décider qui attaquer : une exception non rattrapée ici casserait le ciblage pour tout
        // le monde, pas seulement le compagnon. On protège large par précaution.
        private static void Postfix(BaseAI __instance, Character other, ref bool __result)
        {
            try
            {
                if (!__result || other == null || other.GetComponent<CompanionAI>() == null)
                {
                    return;
                }

                if (__instance != null && __instance.HaveTarget())
                {
                    return;
                }

                if (UnityEngine.Random.value > FedoKnorriPlugin.Instance.CompanionAggroChance.Value)
                {
                    __result = false;
                }
            }
            catch (Exception e)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: CompanionAggroPatch a levé une exception : {e}");
            }
        }
    }
}
