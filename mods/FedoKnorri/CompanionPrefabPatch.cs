using System;
using Jotunn.Managers;
using UnityEngine;

namespace FedoKnorri
{
    // Prefab custom cloné via Jotunn (PrefabManager) plutôt que par les patches Harmony
    // "auto-réparants" documentés dans mods/CLAUDE.md (Postfix maison sur ZNetScene.Awake/
    // GetPrefab/HasPrefab) : PrefabManager.CreateClonedPrefab s'occupe déjà d'instancier le
    // clone sous son propre conteneur désactivé (même principe que l'ancien
    // FedoKnorriPlugin.TemplateRoot, désormais inutile et retiré) et de l'enregistrer dans
    // ZNetScene.m_prefabs au bon moment -- plus besoin de reproduire ce mécanisme à la main
    // pour ce prefab.
    //
    // Point d'entrée : PrefabManager.OnVanillaPrefabsAvailable, qui se déclenche à chaque
    // chargement de partie une fois ZNetScene prêt (l'équivalent Jotunn de l'ancien Postfix
    // sur ZNetScene.Awake) -- trop tôt pour cloner "Greyling" au Awake() du plugin lui-même,
    // ZNetScene n'existe pas encore à ce moment.
    //
    // Source du clone : "Greyling", un vrai prefab ZNetScene (contrairement au Player utilisé
    // par FedoGuardian) déjà pourvu d'un Character correctement réglé (vie, vitesse,
    // animations, sons de pas...) séparé de son MonsterAI -- pas besoin de reconstituer ces
    // réglages à la main comme GuardHumanoid a dû le faire pour un Humanoid nu. On retire
    // uniquement MonsterAI (comportement de monstre sauvage : fuite, chasse, sommeil...) et on
    // ajoute notre propre CompanionAI par-dessus, en garde le Character/Animator/CharacterDrop
    // d'origine intacts.
    internal static class CompanionPrefabPatch
    {
        public const string PrefabName = "Fedo_Knorri";
        private const string SourcePrefabName = "Greyling";

        // Utilisé par CompanionAI.GetTier pour distinguer ce palier de ShamanCompanionPrefabPatch
        // à partir du seul hash de prefab de la ZDO d'un compagnon déjà invoqué.
        public static readonly int PrefabHash = PrefabName.GetStableHashCode();

        private static GameObject _clone;

        public static GameObject GetPrefab()
        {
            return _clone;
        }

        // Appelée une fois depuis FedoKnorriPlugin.Awake -- ne fait que s'abonner, la
        // construction elle-même attend que ZNetScene existe (voir CreatePrefab ci-dessous).
        public static void Init()
        {
            PrefabManager.OnVanillaPrefabsAvailable += CreatePrefab;
        }

        // Rappelée à chaque chargement de partie (comme l'était l'ancien Postfix sur
        // ZNetScene.Awake) -- une exception ici couperait la diffusion de l'événement Jotunn
        // aux autres abonnés éventuels (délégué multicast), d'où le try/catch, même principe
        // défensif que documenté dans l'ancienne version pour les Postfix Harmony partagés.
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
                    FedoKnorriPlugin.Log?.LogError($"FedoKnorri: prefab source '{SourcePrefabName}' introuvable, impossible de créer le compagnon.");
                    return;
                }

                // Character.GetRadius() multiplie le rayon du collider par l'échelle du
                // transform pour tout personnage non-joueur (cf. CLAUDE.md, piège vécu par
                // GuardHumanoid qui a dû forcer IsPlayer()=true pour éviter ce même calcul sur
                // un corps de joueur) -- le compagnon (IsPlayer() reste false, c'est un
                // Greyling) est donc redimensionné correctement sans code supplémentaire.
                clone.transform.localScale = Vector3.one * FedoKnorriPlugin.Instance.CompanionScale.Value;

                UnityEngine.Object.DestroyImmediate(clone.GetComponent<MonsterAI>());

                var character = clone.GetComponent<Character>();
                if (character != null)
                {
                    // Faction Players plutôt que Boss : le compagnon doit maintenant être une
                    // vraie cible pour les monstres sauvages (voir CompanionInvulnerabilityPatch
                    // pour ce qui, en échange, le protège toujours des joueurs) -- Players le
                    // rend naturellement hostile à leurs yeux, exactement comme un vrai joueur,
                    // sans avoir besoin de patcher BaseAI.IsEnemy à la main (l'ancienne approche,
                    // Boss + un tel patch, empêchait justement tout ciblage).
                    character.m_faction = Character.Faction.Players;
                    character.m_name = FedoKnorriPlugin.Instance.CompanionName.Value;
                }

                clone.AddComponent<CompanionAI>();
                clone.AddComponent<CompanionInteract>();

                _clone = clone;
            }
            catch (Exception e)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: échec de création du prefab du compagnon : {e}");
            }
        }
    }
}
