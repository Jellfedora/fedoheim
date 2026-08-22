using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FedoGoldRabbit
{
    // Character.Awake tourne à chaque instanciation ou rechargement du prefab dédié (spawn,
    // rechargement de zone, reconnexion, redémarrage serveur...). Contrairement à FedoDeath, pas
    // besoin de tirage au sort ici : le prefab "Fedo_GoldRabbit" (cloné du Hare vanilla, voir
    // GoldRabbitPrefabPatch) EST le Lièvre Doré, toujours -- sa rareté est gérée en amont par sa
    // propre entrée dans la table de spawn du monde (GoldRabbitSpawnPatch).
    [HarmonyPatch(typeof(Character), "Awake")]
    internal static class GoldRabbitAwakePatch
    {
        private static void Postfix(Character __instance)
        {
            try
            {
                var nview = __instance.GetComponent<ZNetView>();
                var zdo = nview?.GetZDO();
                if (zdo == null || zdo.GetPrefab() != FedoGoldRabbitPlugin.GoldRabbitPrefabHash)
                {
                    return;
                }

                // Le nom/la table de loot ne sont pas des données persistées par le jeu : il faut
                // les réappliquer à chaque chargement, pas seulement à la création.
                ApplyGoldenState(__instance);

                // Le message/son d'apparition, en revanche, ne doit se déclencher qu'une seule
                // fois pour un individu donné -- pas à chaque rechargement de zone.
                if (!zdo.GetBool(FedoGoldRabbitPlugin.ZdoAnnounced, false))
                {
                    zdo.Set(FedoGoldRabbitPlugin.ZdoAnnounced, true);
                    FedoGoldRabbitPlugin.Instance.AnnounceGoldenSpawn(__instance.transform.position);
                }
            }
            catch (Exception e)
            {
                FedoGoldRabbitPlugin.Log?.LogError($"FedoGoldRabbit: GoldRabbitAwakePatch failed: {e}");
            }
        }

        private static void ApplyGoldenState(Character character)
        {
            character.m_name = FedoGoldRabbitPlugin.Instance.GoldenName.Value;

            // Faction Boss : alliée à toutes les factions sauf celle des joueurs en vanilla --
            // ignorée par tous les autres monstres (loups, sangliers...), hostile uniquement aux
            // joueurs. Même technique que le gardien de FedoDeath. m_boss (barre de vie / musique
            // de boss) est un champ séparé qu'on laisse à false : sans ça, on perdrait le lièvre à
            // chaque prédateur croisé avant même d'avoir pu le chasser nous-mêmes.
            character.m_faction = Character.Faction.Boss;

            var characterDrop = character.GetComponent<CharacterDrop>();
            var coinPrefab = FedoGoldRabbitPlugin.Instance.GetCoinPrefab();
            if (characterDrop != null && coinPrefab != null)
            {
                characterDrop.m_drops = new List<CharacterDrop.Drop>
                {
                    new CharacterDrop.Drop
                    {
                        m_prefab = coinPrefab,
                        m_amountMin = FedoGoldRabbitPlugin.Instance.DeathCoinAmountMin.Value,
                        m_amountMax = FedoGoldRabbitPlugin.Instance.DeathCoinAmountMax.Value,
                        m_chance = 1f,
                    },
                };
            }

            if (character.GetComponent<GoldRabbitBehaviour>() == null)
            {
                character.gameObject.AddComponent<GoldRabbitBehaviour>();
            }
        }
    }

    // AnimalAI.SetAlerted(true) est appelé dès que le lièvre repère un joueur et se met à fuir --
    // c'est le moment exact où le Lièvre Doré doit crier son excuse à la Lapin Blanc.
    [HarmonyPatch(typeof(AnimalAI), "SetAlerted")]
    internal static class GoldRabbitFleeShoutPatch
    {
        private static void Postfix(AnimalAI __instance, bool alert)
        {
            if (!alert)
            {
                return;
            }

            __instance.GetComponent<GoldRabbitBehaviour>()?.TryShout();
        }
    }
}
