using System;
using HarmonyLib;
using UnityEngine;

namespace FedoDeath
{
    // Capture la tombe au moment précis de sa création (avant que Player.CreateTombStone n'ait
    // fini d'y déplacer l'inventaire du joueur). On la consomme immédiatement après dans le
    // patch de CreateTombStone -- tout se passe dans la même pile d'appels, donc aucun risque de
    // se faire écraser par un autre Setup() entre-temps.
    [HarmonyPatch(typeof(TombStone), "Setup")]
    internal static class TombStoneSetupCapturePatch
    {
        internal static TombStone LastCreated;
        internal static string LastOwnerName;
        internal static long LastOwnerUID;

        private static void Postfix(TombStone __instance, string ownerName, long ownerUID)
        {
            LastCreated = __instance;
            LastOwnerName = ownerName;
            LastOwnerUID = ownerUID;
        }
    }

    [HarmonyPatch(typeof(Player), "CreateTombStone")]
    internal static class PlayerCreateTombStonePatch
    {
        private static void Postfix(Player __instance)
        {
            try
            {
                if (__instance != Player.m_localPlayer)
                {
                    return;
                }

                var tombstone = TombStoneSetupCapturePatch.LastCreated;
                TombStoneSetupCapturePatch.LastCreated = null;
                if (tombstone == null)
                {
                    // Rien à protéger (mort les mains vides) : pas de gardien à invoquer.
                    return;
                }

                // Garde-fou : ce doit être la tombe qu'on vient tout juste de créer ici, pas une
                // tombe existante croisée par hasard ailleurs sur la carte.
                if (Vector3.Distance(tombstone.transform.position, __instance.transform.position) > 2f)
                {
                    return;
                }

                string ownerName = TombStoneSetupCapturePatch.LastOwnerName;
                long ownerUID = TombStoneSetupCapturePatch.LastOwnerUID;
                var pendingInventory = tombstone.GetComponent<Container>()?.GetInventory();
                Vector3 position = tombstone.transform.position;
                Quaternion rotation = tombstone.transform.rotation;
                GameObject tombstonePrefab = __instance.m_tombstone;

                tombstone.GetComponent<ZNetView>()?.Destroy();

                FedoDeathPlugin.Instance.SpawnGuardian(position, rotation, pendingInventory, ownerName, ownerUID, tombstonePrefab);
            }
            catch (Exception e)
            {
                FedoDeathPlugin.Log?.LogError($"FedoDeath: CreateTombStone patch failed: {e}");
            }
        }
    }
}
