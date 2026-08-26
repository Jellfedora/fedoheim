using System;
using HarmonyLib;
using UnityEngine;

namespace FedoKnorri
{
    // Même technique que documentée pour un prefab custom cloné d'un prefab vanilla (voir
    // CLAUDE.md et FedoGuardian.GuardianPrefabPatch) : on ne touche jamais à
    // ZNetScene.m_namedPrefabs (le clone s'y ferait détruire tôt). On complète m_prefabs
    // (liste publique) et on patche GetPrefab (les deux surcharges)/HasPrefab en Postfix
    // auto-réparant.
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

        private static readonly int PrefabHash = PrefabName.GetStableHashCode();

        private static GameObject _clone;

        public static GameObject GetPrefab()
        {
            return GetOrCreate();
        }

        private static GameObject BuildClone()
        {
            GameObject source = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(SourcePrefabName) : null;
            if (source == null)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: prefab source '{SourcePrefabName}' introuvable, impossible de créer le compagnon.");
                return null;
            }

            // Enfant du conteneur racine désactivé : aucun script (ZNetView/Character compris)
            // ne s'exécute tant qu'il reste là -- cf. note dans CLAUDE.md sur le piège vécu
            // avec FedoGoldRabbit (ne jamais SetActive(false) sur le clone lui-même).
            var clone = UnityEngine.Object.Instantiate(source, FedoKnorriPlugin.TemplateRoot, worldPositionStays: false);
            clone.name = PrefabName;

            // Character.GetRadius() multiplie le rayon du collider par l'échelle du transform
            // pour tout personnage non-joueur (cf. CLAUDE.md, piège vécu par GuardHumanoid qui a
            // dû forcer IsPlayer()=true pour éviter ce même calcul sur un corps de joueur) -- le
            // compagnon (IsPlayer() reste false, c'est un Greyling) est donc redimensionné
            // correctement sans code supplémentaire.
            clone.transform.localScale = Vector3.one * FedoKnorriPlugin.Instance.CompanionScale.Value;

            UnityEngine.Object.DestroyImmediate(clone.GetComponent<MonsterAI>());

            var character = clone.GetComponent<Character>();
            if (character != null)
            {
                // Ignoré par tous les monstres sauvages (cf. CLAUDE.md, "Character.Faction.Boss") :
                // un compagnon pacifiste qui ne peut pas se défendre ne doit jamais être une
                // cible, plutôt que de le rendre invulnérable ou de lui donner une IA de combat.
                character.m_faction = Character.Faction.Boss;
                character.m_name = FedoKnorriPlugin.Instance.CompanionName.Value;
            }

            clone.AddComponent<CompanionAI>();
            clone.AddComponent<CompanionInteract>();

            return clone;
        }

        // Cf. commentaire équivalent dans FedoGuardian.GuardianPrefabPatch.GetOrCreate :
        // appelée depuis des Postfix sur des méthodes vanilla partagées (ZNetScene.Awake/
        // GetPrefab/HasPrefab), une exception ici casserait toute la chaîne de patches Harmony
        // dessus et peut bloquer le chargement du monde entier.
        private static GameObject GetOrCreate()
        {
            if (_clone != null)
            {
                return _clone;
            }

            try
            {
                _clone = BuildClone();
                if (_clone != null && ZNetScene.instance != null && !ZNetScene.instance.m_prefabs.Contains(_clone))
                {
                    ZNetScene.instance.m_prefabs.Add(_clone);
                }
            }
            catch (Exception e)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: échec de création du prefab du compagnon : {e}");
                _clone = null;
            }

            return _clone;
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetSceneAwakePatch
        {
            private static void Postfix()
            {
                GetOrCreate();
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.GetPrefab), typeof(int))]
        private static class GetPrefabByHashPatch
        {
            private static void Postfix(int hash, ref GameObject __result)
            {
                if (__result != null || hash != PrefabHash)
                {
                    return;
                }

                __result = GetOrCreate();
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.GetPrefab), typeof(string))]
        private static class GetPrefabByNamePatch
        {
            private static void Postfix(string name, ref GameObject __result)
            {
                if (__result != null || name != PrefabName)
                {
                    return;
                }

                __result = GetOrCreate();
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.HasPrefab))]
        private static class HasPrefabPatch
        {
            private static void Postfix(int hash, ref bool __result)
            {
                if (__result || hash != PrefabHash)
                {
                    return;
                }

                __result = true;
            }
        }
    }
}
