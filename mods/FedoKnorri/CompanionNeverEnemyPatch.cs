using System;
using HarmonyLib;

namespace FedoKnorri
{
    // Faction.Boss (voir CompanionPrefabPatch) devrait déjà suffire à ce qu'aucun monstre
    // sauvage ne cible le compagnon -- observé en jeu : ça n'empêche pas systématiquement un
    // monstre de l'attaquer (Character.Damage reste bloqué net pour lui, voir
    // CompanionInvulnerabilityPatch, donc sans conséquence en pratique, mais visuellement un mob
    // ne devrait même pas essayer). Plutôt que de chercher pourquoi la seule faction ne suffit
    // pas dans tous les cas, on verrouille directement BaseAI.IsEnemy (les deux surcharges) pour
    // qu'aucune IA ne considère jamais le compagnon comme une cible valide, quelle que soit la
    // raison exacte.
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), typeof(Character))]
    internal static class CompanionNeverEnemyInstancePatch
    {
        // Patch sur une méthode vanilla appelée en permanence par toutes les IA du jeu pour
        // décider qui attaquer : une exception non rattrapée ici casserait le ciblage pour
        // tout le monde, pas seulement le compagnon. On protège large par précaution.
        private static void Postfix(Character other, ref bool __result)
        {
            try
            {
                if (__result && other != null && other.GetComponent<CompanionAI>() != null)
                {
                    __result = false;
                }
            }
            catch (Exception e)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: CompanionNeverEnemyInstancePatch a levé une exception : {e}");
            }
        }
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), typeof(Character), typeof(Character))]
    internal static class CompanionNeverEnemyStaticPatch
    {
        private static void Postfix(Character a, Character b, ref bool __result)
        {
            try
            {
                if (!__result)
                {
                    return;
                }

                bool involvesCompanion = (a != null && a.GetComponent<CompanionAI>() != null)
                    || (b != null && b.GetComponent<CompanionAI>() != null);

                if (involvesCompanion)
                {
                    __result = false;
                }
            }
            catch (Exception e)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: CompanionNeverEnemyStaticPatch a levé une exception : {e}");
            }
        }
    }
}
