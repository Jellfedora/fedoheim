using System;
using System.Linq;
using HarmonyLib;

namespace FedoGoldRabbit
{
    // Ajoute une entrée de spawn ambiant pour le Lièvre Doré dans la première liste de spawn du
    // monde (partagée par toutes les zones), avec m_biome = All -- il peut donc apparaître dans
    // n'importe quel biome, pas seulement là où le Hare vanilla spawne naturellement (Meadows).
    [HarmonyPatch(typeof(SpawnSystem), "Awake")]
    internal static class GoldRabbitSpawnPatch
    {
        private static void Postfix(SpawnSystem __instance)
        {
            try
            {
                var list = __instance.m_spawnLists?.FirstOrDefault();
                if (list == null)
                {
                    return;
                }

                if (list.m_spawners.Any(s => s.m_name == FedoGoldRabbitPlugin.GoldRabbitPrefabName))
                {
                    return; // déjà ajoutée (liste partagée entre plusieurs zones)
                }

                var prefab = FedoGoldRabbitPlugin.Instance != null ? ZNetScene.instance?.GetPrefab(FedoGoldRabbitPlugin.GoldRabbitPrefabName) : null;
                if (prefab == null)
                {
                    FedoGoldRabbitPlugin.Log?.LogError("FedoGoldRabbit: Gold Rabbit prefab not registered yet, cannot add its spawn entry.");
                    return;
                }

                list.m_spawners.Add(new SpawnSystem.SpawnData
                {
                    m_name = FedoGoldRabbitPlugin.GoldRabbitPrefabName,
                    m_enabled = true,
                    m_prefab = prefab,
                    m_biome = Heightmap.Biome.All,
                    m_biomeArea = Heightmap.BiomeArea.Everything,
                    m_maxSpawned = FedoGoldRabbitPlugin.Instance.SpawnMaxPerZone.Value,
                    m_spawnInterval = FedoGoldRabbitPlugin.Instance.SpawnIntervalSeconds.Value,
                    m_spawnChance = FedoGoldRabbitPlugin.Instance.SpawnChancePercent.Value,
                    m_spawnDistance = FedoGoldRabbitPlugin.Instance.SpawnRadiusMin.Value,
                    m_spawnRadiusMin = FedoGoldRabbitPlugin.Instance.SpawnRadiusMin.Value,
                    m_spawnRadiusMax = FedoGoldRabbitPlugin.Instance.SpawnRadiusMax.Value,
                    m_groupSizeMin = 1,
                    m_groupSizeMax = 1,
                    m_groupRadius = 3f,
                    m_spawnAtNight = true,
                    m_spawnAtDay = true,
                    m_minAltitude = 1f,
                    m_maxAltitude = 1000f,
                    m_minTilt = 0f,
                    m_maxTilt = 35f,
                    m_inForest = true,
                    m_outsideForest = true,
                    m_canSpawnCloseToPlayer = false,
                    m_maxLevel = 1,
                    m_minLevel = 1,
                });

                FedoGoldRabbitPlugin.Log?.LogInfo("FedoGoldRabbit: registered the Gold Rabbit world spawn entry.");
            }
            catch (Exception e)
            {
                FedoGoldRabbitPlugin.Log?.LogError($"FedoGoldRabbit: failed to register the Gold Rabbit spawn entry: {e}");
            }
        }
    }
}
