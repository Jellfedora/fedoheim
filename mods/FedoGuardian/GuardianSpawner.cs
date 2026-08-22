using UnityEngine;

namespace FedoGuardian
{
    // Le prefab gabarit (GuardianPrefabPatch) est volontairement inactif (SetActive(false)),
    // exactement comme les prefabs vanilla stockés dans ZNetScene -- Object.Instantiate() d'un
    // objet inactif produit un clone lui aussi inactif, sur lequel Awake() ne se déclenche jamais
    // tant qu'on ne l'active pas explicitement. Un vrai spawn doit donc impérativement rappeler
    // SetActive(true) après Instantiate(), sans quoi le résultat est un objet fantôme : pas
    // d'Awake, pas de ZDO, invisible, sans IA -- très probablement ce qui se passait avec Easy
    // Spawner si son propre code de spawn ne fait pas cette étape pour un prefab qu'il ne connaît
    // pas nativement.
    internal static class GuardianSpawner
    {
        public static GuardHumanoid Spawn(Vector3 position, Quaternion rotation, bool female)
        {
            GameObject prefab = female ? GuardianPrefabPatch.GetFemalePrefab() : GuardianPrefabPatch.GetMalePrefab();
            if (prefab == null)
            {
                FedoGuardianPlugin.Log?.LogError("FedoGuardian: impossible de créer le prefab du garde, abandon du spawn.");
                return null;
            }

            var instance = Object.Instantiate(prefab, position, rotation);
            instance.SetActive(true);
            instance.GetComponent<ZNetView>()?.ClaimOwnership();

            return instance.GetComponent<GuardHumanoid>();
        }
    }
}
