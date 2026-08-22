using System;
using HarmonyLib;
using UnityEngine;

namespace FedoGuardian
{
    // Même technique que documentée pour un prefab custom cloné d'un prefab vanilla : on ne
    // touche jamais à ZNetScene.m_namedPrefabs (le clone s'y ferait détruire tôt, probablement au
    // moment d'une transition de scène). On complète m_prefabs (liste publique, suffisant pour
    // qu'un spawner comme Easy Spawner liste le prefab) et on patche GetPrefab (les deux
    // surcharges)/HasPrefab en Postfix auto-réparant : si le résultat original est vide, on
    // recrée le clone à la volée si besoin puis on le mémorise.
    //
    // Source du clone : Game.instance.m_playerPrefab (le vrai prefab Player), pas un prefab
    // ZNetScene classique -- Player n'y est normalement pas enregistré comme prefab spawnable.
    //
    // On retire ensuite les composants Player/PlayerController (comme le fait le mod VikingNPC/
    // Settlers, dont on s'est inspiré) plutôt que de les garder et de les neutraliser par-dessus :
    // Player embarque trop de logique pensée pour un humain aux commandes (animation de réveil,
    // liste statique des joueurs, messages HUD, montée de compétences...) qui se comporte mal une
    // fois piloté par une IA. Un Humanoid nu (notre GuardHumanoid, quasiment vide) fournit déjà
    // tout ce qu'il faut : inventaire, équipement, attaque, mouvement partagé avec Character.
    internal static class GuardianPrefabPatch
    {
        public const string MalePrefabName = "FedoGuardian_Male";
        public const string FemalePrefabName = "FedoGuardian_Female";

        public static GameObject GetMalePrefab()
        {
            return GetOrCreate(MalePrefabName, 0, ref _maleClone);
        }

        public static GameObject GetFemalePrefab()
        {
            return GetOrCreate(FemalePrefabName, 1, ref _femaleClone);
        }

        private static readonly int MaleHash = MalePrefabName.GetStableHashCode();
        private static readonly int FemaleHash = FemalePrefabName.GetStableHashCode();

        private static GameObject _maleClone;
        private static GameObject _femaleClone;

        private static GameObject BuildClone(string name, int playerModelIndex)
        {
            GameObject source = Game.instance != null ? Game.instance.m_playerPrefab : null;
            if (source == null)
            {
                FedoGuardianPlugin.Log?.LogError($"FedoGuardian: Game.instance.m_playerPrefab introuvable, impossible de créer {name}.");
                return null;
            }

            // Instancié comme enfant du conteneur racine désactivé (FedoGuardianPlugin.TemplateRoot) :
            // Unity ne déclenche jamais Awake/OnEnable/Start sur un objet inactif-en-hiérarchie,
            // donc aucun script (Player, ZNetView...) ne s'exécute réellement sur ce gabarit tant
            // qu'il reste là. Pas de DontDestroyOnLoad ici : un objet avec un parent hérite déjà de
            // celui du parent (root est lui-même en DontDestroyOnLoad), et Unity refuse/avertit si
            // on l'appelle explicitement sur un objet non-racine.
            var clone = UnityEngine.Object.Instantiate(source, FedoGuardianPlugin.TemplateRoot, worldPositionStays: false);
            clone.name = name;

            var visEquipment = clone.GetComponent<VisEquipment>();
            visEquipment.SetModel(playerModelIndex);

            // DestroyImmediate, pas Destroy : ce gabarit peut être Instantiate() à nouveau pour un
            // vrai spawn dans la toute même frame (clic sur la baguette -> GetOrCreate crée le
            // gabarit -> Spawn l'instancie tout de suite). Destroy() est différé en fin de frame ;
            // le vrai spawn aurait alors copié un Player toujours présent au moment du clonage,
            // donnant un garde avec DEUX cerveaux (le Player fantôme lisant les mêmes touches que
            // le joueur réel, en plus de notre IA) -- probablement la cause des déplacements
            // erratiques observés, pour le garde comme pour le joueur.
            UnityEngine.Object.DestroyImmediate(clone.GetComponent<PlayerController>());
            UnityEngine.Object.DestroyImmediate(clone.GetComponent<Player>());

            var guardHumanoid = clone.AddComponent<GuardHumanoid>();
            guardHumanoid.m_defaultItems = new GameObject[0];

            clone.AddComponent<GuardAI>();
            clone.AddComponent<GuardianInteract>();

            return clone;
        }

        // Appelée depuis des Postfix sur des méthodes vanilla partagées avec d'autres mods
        // (ZNetScene.Awake/GetPrefab/HasPrefab) : une exception non rattrapée ici casserait toute
        // la chaîne de patches Harmony sur cette méthode, y compris ceux d'autres mods, et peut
        // bloquer le chargement du monde entier. On protège donc large, quitte à simplement
        // renvoyer null (et retenter au prochain appel) en cas de souci.
        private static GameObject GetOrCreate(string name, int playerModelIndex, ref GameObject cache)
        {
            if (cache != null)
            {
                return cache;
            }

            try
            {
                cache = BuildClone(name, playerModelIndex);
                if (cache != null && ZNetScene.instance != null && !ZNetScene.instance.m_prefabs.Contains(cache))
                {
                    ZNetScene.instance.m_prefabs.Add(cache);
                }
            }
            catch (Exception e)
            {
                FedoGuardianPlugin.Log?.LogError($"FedoGuardian: échec de création du prefab {name} : {e}");
                cache = null;
            }

            return cache;
        }

        private static GameObject GetPrefabByHash(int hash)
        {
            if (hash == MaleHash)
            {
                return GetOrCreate(MalePrefabName, 0, ref _maleClone);
            }

            if (hash == FemaleHash)
            {
                return GetOrCreate(FemalePrefabName, 1, ref _femaleClone);
            }

            return null;
        }

        // Tentative d'enregistrement précoce : si Game.instance est déjà prêt à ce moment,
        // les deux prefabs apparaissent tout de suite dans ZNetScene.m_prefabs (utile pour un
        // spawner qui construit sa liste une seule fois). Purement best-effort -- les Postfix
        // ci-dessous restent le filet de sécurité si ça échoue ou si le clone est détruit entre
        // temps.
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetSceneAwakePatch
        {
            private static void Postfix()
            {
                GetOrCreate(MalePrefabName, 0, ref _maleClone);
                GetOrCreate(FemalePrefabName, 1, ref _femaleClone);
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.GetPrefab), typeof(int))]
        private static class GetPrefabByHashPatch
        {
            private static void Postfix(int hash, ref GameObject __result)
            {
                if (__result != null)
                {
                    return;
                }

                __result = GetPrefabByHash(hash);
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.GetPrefab), typeof(string))]
        private static class GetPrefabByNamePatch
        {
            private static void Postfix(string name, ref GameObject __result)
            {
                if (__result != null || string.IsNullOrEmpty(name))
                {
                    return;
                }

                __result = GetPrefabByHash(name.GetStableHashCode());
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.HasPrefab))]
        private static class HasPrefabPatch
        {
            private static void Postfix(int hash, ref bool __result)
            {
                if (__result)
                {
                    return;
                }

                __result = hash == MaleHash || hash == FemaleHash;
            }
        }
    }
}
