using System;
using HarmonyLib;
using UnityEngine;

namespace FedoGoldRabbit
{
    // Clone le prefab vanilla "Hare" sous un nom dédié ("Fedo_GoldRabbit"), pour le rendre
    // spawnable via la console ("spawn Fedo_GoldRabbit") ou listable par un mod comme Easy Spawner,
    // sans dépendre de Jotunn ou d'un autre framework de prefabs custom.
    //
    // Le clone se fait détruire par le jeu peu après sa création (constaté en jeu -- vraisemblablement
    // lié à une transition de scène tôt dans le chargement, avant que DontDestroyOnLoad n'ait
    // vraiment d'effet). Plutôt que d'essayer de l'en empêcher, EnsureRegistered() est conçu pour
    // être auto-réparant : n'importe quel appelant (ZNetScene.Awake, GetPrefab, HasPrefab) peut
    // le rappeler à tout moment, et il recrée le clone à la volée si l'ancien n'est plus valide.
    internal static class GoldRabbitPrefabPatch
    {
        internal static GameObject RegisteredPrefab;
        private static bool _addedToPrefabsList;

        internal static GameObject EnsureRegistered(ZNetScene znetScene)
        {
            if (RegisteredPrefab != null || znetScene == null)
            {
                return RegisteredPrefab;
            }

            try
            {
                var harePrefab = znetScene.GetPrefab("Hare");
                if (harePrefab == null)
                {
                    FedoGoldRabbitPlugin.Log?.LogError("FedoGoldRabbit: vanilla prefab 'Hare' not found, cannot register the Gold Rabbit.");
                    return null;
                }

                var clone = UnityEngine.Object.Instantiate(harePrefab);
                clone.name = FedoGoldRabbitPlugin.GoldRabbitPrefabName;
                UnityEngine.Object.DontDestroyOnLoad(clone);

                // N'ajouter qu'une seule fois à la liste publique (pour l'affichage dans des mods
                // comme Easy Spawner) -- si ce clone-ci finit lui aussi détruit et qu'on doit en
                // recréer un autre plus tard, inutile d'accumuler des entrées mortes dans la liste.
                if (!_addedToPrefabsList)
                {
                    znetScene.m_prefabs.Add(clone);
                    _addedToPrefabsList = true;
                }

                RegisteredPrefab = clone;
                FedoGoldRabbitPlugin.Log?.LogInfo($"FedoGoldRabbit: registered prefab '{clone.name}'.");
                return clone;
            }
            catch (Exception e)
            {
                FedoGoldRabbitPlugin.Log?.LogError($"FedoGoldRabbit: failed to register the Gold Rabbit prefab: {e}");
                return null;
            }
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class GoldRabbitAwakeRegisterPatch
    {
        private static void Postfix(ZNetScene __instance)
        {
            GoldRabbitPrefabPatch.EnsureRegistered(__instance);
        }
    }

    // Complète ZNetScene.GetPrefab (les deux surcharges) avec notre clone quand la recherche
    // normale (via m_namedPrefabs, privé) ne le trouve pas.
    [HarmonyPatch(typeof(ZNetScene), "GetPrefab", typeof(int))]
    internal static class GoldRabbitGetPrefabByHashPatch
    {
        private static void Postfix(ZNetScene __instance, int hash, ref GameObject __result)
        {
            if (__result != null || hash != FedoGoldRabbitPlugin.GoldRabbitPrefabHash)
            {
                return;
            }

            __result = GoldRabbitPrefabPatch.EnsureRegistered(__instance);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "GetPrefab", typeof(string))]
    internal static class GoldRabbitGetPrefabByNamePatch
    {
        private static void Postfix(ZNetScene __instance, string name, ref GameObject __result)
        {
            if (__result != null || name != FedoGoldRabbitPlugin.GoldRabbitPrefabName)
            {
                return;
            }

            __result = GoldRabbitPrefabPatch.EnsureRegistered(__instance);
        }
    }

    // ZNetScene.CreateObject (appelé quand une ZDO doit être matérialisée dans le monde, ex. après
    // un SpawnSystem.Spawn()) vérifie HasPrefab AVANT d'appeler GetPrefab -- il faut donc aussi lui
    // faire connaître notre clone, sans quoi les Lièvres Dorés créés par la table de spawn
    // n'apparaîtraient jamais réellement dans le monde.
    [HarmonyPatch(typeof(ZNetScene), "HasPrefab")]
    internal static class GoldRabbitHasPrefabPatch
    {
        private static void Postfix(ZNetScene __instance, int hash, ref bool __result)
        {
            if (__result || hash != FedoGoldRabbitPlugin.GoldRabbitPrefabHash)
            {
                return;
            }

            __result = GoldRabbitPrefabPatch.EnsureRegistered(__instance) != null;
        }
    }
}
