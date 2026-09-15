using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FedoHud
{
    // Liste, une fois pour toutes au chargement du réseau, les noms de prefabs "récolte
    // mûre" que peut produire une culture (`Plant.m_grownPrefabs`, public) -- permet de
    // reconnaître une culture arrivée à maturité par son IDENTITÉ (le nom du prefab),
    // plutôt qu'en essayant d'attraper l'instant précis où elle mûrit.
    //
    // `Plant.Grow()` (décompilé) ne s'exécute qu'UNE SEULE FOIS par culture, et peut
    // très bien s'être déjà produit avant même le lancement de cette session (observé en
    // jeu : des cultures déjà mûres dès la connexion) -- un patch posé sur Grow() ne
    // peut donc jamais détecter ce cas-là, puisqu'il n'a jamais tourné pendant que le
    // mod était actif pour cette culture précise. `ZNetScene.m_prefabs` (public,
    // `List<GameObject>`) contient tous les prefabs connus du jeu (vanilla et ajoutés
    // par d'autres mods) une fois la scène réseau chargée -- c'est une donnée statique,
    // pas un événement, donc ce problème ne se pose pas ici.
    internal static class CropRegistry
    {
        private static HashSet<string> _grownCropNames;

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class ZNetSceneAwakePatch
        {
            private static void Postfix(ZNetScene __instance)
            {
                try
                {
                    Build(__instance);
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogWarning($"FedoHud: crop registry build failed: {e.Message}");
                }
            }
        }

        private static void Build(ZNetScene scene)
        {
            var names = new HashSet<string>();
            if (scene.m_prefabs != null)
            {
                foreach (var prefab in scene.m_prefabs)
                {
                    var plant = prefab != null ? prefab.GetComponent<Plant>() : null;
                    if (plant == null || plant.m_grownPrefabs == null || PlantGrowth.IsTreeSapling(plant))
                    {
                        continue;
                    }

                    foreach (var grown in plant.m_grownPrefabs)
                    {
                        if (grown != null)
                        {
                            names.Add(grown.name);
                        }
                    }
                }
            }

            _grownCropNames = names;
        }

        // `name` doit déjà être débarrassé du suffixe "(Clone)" ajouté par
        // Object.Instantiate -- voir PlantGrownIndicator.cs.
        public static bool IsGrownCropName(string name)
        {
            return _grownCropNames != null && !string.IsNullOrEmpty(name) && _grownCropNames.Contains(name);
        }
    }
}
