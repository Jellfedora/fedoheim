using System;
using HarmonyLib;

namespace FedoGuardian
{
    // Cf. note CLAUDE.md : ItemDrop.ItemData.m_dropPrefab n'est renseigné qu'au runtime par
    // ItemDrop.Awake() -- une copie obtenue autrement (comme celle qu'Easy Spawner met dans
    // l'inventaire) peut se retrouver avec m_dropPrefab null. Or Humanoid.SetupVisEquipment fait
    // "m_rightItem.m_dropPrefab.name" sans vérifier la nullité : ça plante silencieusement pendant
    // la mise à jour visuelle, ce qui laisse l'objet fonctionnel (attaque, inventaire) mais
    // invisible en main. On corrige m_dropPrefab nous-mêmes juste avant que EquipItem ne s'en serve.
    //
    // Patch sur une méthode vanilla appelée à chaque équipement d'objet par le vrai joueur : une
    // exception non rattrapée ici casserait EquipItem pour tout le monde, pas seulement pour la
    // baguette. On protège large par précaution.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    internal static class SummonWandDropPrefabFixPatch
    {
        private static void Prefix(ItemDrop.ItemData item)
        {
            try
            {
                if (item == null || !SummonWandPrefabPatch.IsWand(item) || item.m_dropPrefab != null)
                {
                    return;
                }

                item.m_dropPrefab = SummonWandPrefabPatch.GetPrefab();
            }
            catch (Exception e)
            {
                FedoGuardianPlugin.Log?.LogError($"FedoGuardian: SummonWandDropPrefabFixPatch a levé une exception : {e}");
            }
        }
    }
}
