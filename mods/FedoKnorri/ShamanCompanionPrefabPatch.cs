using System;
using Jotunn.Managers;
using UnityEngine;

namespace FedoKnorri
{
    // Palier "évolué" du compagnon (voir CompanionTier), obtenu via la recette de
    // ShamanSummonItemPrefabPatch (graine de base + 5 miels à l'établi) -- même technique et
    // même structure que CompanionPrefabPatch pour le palier de base, cf. ce fichier pour le
    // détail des choix (Jotunn PrefabManager plutôt que patches Harmony auto-réparants, pas de
    // TemplateRoot maison nécessaire, etc.), pas reexpliqué ici.
    //
    // Source du clone : "Greydwarf_Shaman", comme Greyling déjà pourvu d'un Character/Animator
    // réglé et d'un MonsterAI séparé qu'il suffit de retirer -- CompanionAI se pose dessus à
    // l'identique du palier de base, seul le soin diffère par palier (voir
    // CompanionAI.HealAmount/HealCooldownSeconds), tout le reste du comportement (suivi,
    // ramassage, un seul compagnon par joueur, renommage, despawn à la déconnexion...) est déjà
    // partagé sans rien à refaire ici.
    internal static class ShamanCompanionPrefabPatch
    {
        public const string PrefabName = "Fedo_KnorriShaman";
        private const string SourcePrefabName = "Greydwarf_Shaman";

        // Utilisé par CompanionAI.GetTier pour distinguer ce palier de CompanionPrefabPatch à
        // partir du seul hash de prefab de la ZDO d'un compagnon déjà invoqué.
        public static readonly int PrefabHash = PrefabName.GetStableHashCode();

        private static GameObject _clone;

        public static GameObject GetPrefab()
        {
            return _clone;
        }

        // Appelée une fois depuis FedoKnorriPlugin.Awake -- ne fait que s'abonner, la
        // construction elle-même attend que ZNetScene existe (voir CreatePrefab).
        public static void Init()
        {
            PrefabManager.OnVanillaPrefabsAvailable += CreatePrefab;
        }

        private static void CreatePrefab()
        {
            if (_clone != null)
            {
                return;
            }

            try
            {
                GameObject clone = PrefabManager.Instance.CreateClonedPrefab(PrefabName, SourcePrefabName);
                if (clone == null)
                {
                    FedoKnorriPlugin.Log?.LogError($"FedoKnorri: prefab source '{SourcePrefabName}' introuvable, impossible de créer le compagnon chaman.");
                    return;
                }

                clone.transform.localScale = Vector3.one * FedoKnorriPlugin.Instance.ShamanCompanionScale.Value;

                UnityEngine.Object.DestroyImmediate(clone.GetComponent<MonsterAI>());

                var character = clone.GetComponent<Character>();
                if (character != null)
                {
                    // Cf. commentaire équivalent dans CompanionPrefabPatch : faction Players
                    // pour être une vraie cible pour les monstres sauvages, protégé des joueurs
                    // uniquement (voir CompanionInvulnerabilityPatch).
                    character.m_faction = Character.Faction.Players;

                    // Même config CompanionName que le palier de base (pas de config séparée) :
                    // c'est censé être le MÊME compagnon, juste évolué -- son nom par défaut
                    // reste donc le même tant qu'il n'a jamais été renommé, et ApplySavedName
                    // (appelé par CompanionSpawner juste après le spawn) écrase de toute façon
                    // ce défaut par le dernier nom personnalisé connu si le joueur en avait
                    // choisi un, quel que soit le palier sur lequel il l'avait fait.
                    character.m_name = FedoKnorriPlugin.Instance.CompanionName.Value;
                }

                clone.AddComponent<CompanionAI>();
                clone.AddComponent<CompanionInteract>();

                _clone = clone;
            }
            catch (Exception e)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: échec de création du prefab du compagnon chaman : {e}");
            }
        }
    }
}
