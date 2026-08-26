using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FedoCompanion
{
    // Même principe que FedoGuardian.SummonWandPrefabPatch : clone d'un item vanilla existant
    // (configurable, TrophyGreyling par défaut -- en attendant un vrai modèle dédié), renommé,
    // et enregistré dans ZNetScene.m_prefabs + ObjectDB.m_items avec le même Postfix
    // auto-réparant sur les méthodes de résolution par nom/hash.
    internal static class SummonItemPrefabPatch
    {
        public const string PrefabName = "Fedo_CompanionCharm";

        private static readonly int PrefabHash = PrefabName.GetStableHashCode();

        private static GameObject _clone;

        // Cf. commentaire équivalent dans SummonWandPrefabPatch.SharedData : identifié par
        // nom plutôt que par identité d'objet, ObjectDB.Awake() pouvant reconstruire un
        // nouveau clone (donc une nouvelle SharedData) plusieurs fois pendant le chargement.
        public static ItemDrop.ItemData.SharedData SharedData { get; private set; }

        public static bool IsSummonItem(ItemDrop.ItemData item)
        {
            return item?.m_shared != null && item.m_shared.m_name == FedoCompanionPlugin.Instance.SummonItemName.Value;
        }

        public static GameObject GetPrefab()
        {
            return GetOrCreate();
        }

        private static GameObject BuildClone()
        {
            string sourceName = FedoCompanionPlugin.Instance.SummonItemSourceItem.Value;

            GameObject source = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(sourceName) : null;
            if (source == null && ZNetScene.instance != null)
            {
                source = ZNetScene.instance.GetPrefab(sourceName);
            }

            if (source == null)
            {
                FedoCompanionPlugin.Log?.LogError($"FedoCompanion: prefab source '{sourceName}' introuvable, impossible de créer le charme d'invocation.");
                LogSimilarItemNames(sourceName);
                return null;
            }

            // Enfant du conteneur racine désactivé : aucun script (ItemDrop.Awake compris) ne
            // s'exécute tant qu'il reste là -- cf. note dans CompanionPrefabPatch pour le pourquoi.
            var clone = UnityEngine.Object.Instantiate(source, FedoCompanionPlugin.TemplateRoot, worldPositionStays: false);
            clone.name = PrefabName;

            var itemDrop = clone.GetComponent<ItemDrop>();
            if (itemDrop != null)
            {
                itemDrop.m_itemData.m_shared.m_name = FedoCompanionPlugin.Instance.SummonItemName.Value;
                itemDrop.m_itemData.m_shared.m_description = "Summons a tame Greyling companion when used.";

                // Forcé en Consumable quel que soit le type d'origine (Trophy par défaut n'a
                // pas de bouton "Utiliser" dans l'inventaire vanilla) : c'est ce type qui
                // garantit l'apparition de ce bouton, peu importe l'item source choisi comme
                // simple support visuel.
                itemDrop.m_itemData.m_shared.m_itemType = ItemDrop.ItemData.ItemType.Consumable;

                SharedData = itemDrop.m_itemData.m_shared;
            }

            return clone;
        }

        // Aide au diagnostic : si le nom configuré ne correspond à aucun prefab, on liste les
        // objets d'ObjectDB dont le nom contient un des "mots" du nom recherché, pour trouver
        // le vrai nom sans avoir à fouiller les fichiers d'assets à la main.
        private static void LogSimilarItemNames(string sourceName)
        {
            if (ObjectDB.instance == null)
            {
                return;
            }

            var keywords = System.Text.RegularExpressions.Regex.Matches(sourceName, "[A-Z][a-z]*")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Value)
                .Where(w => w.Length >= 3)
                .ToArray();

            if (keywords.Length == 0)
            {
                return;
            }

            var matches = ObjectDB.instance.m_items
                .Where(go => go != null && keywords.Any(k => go.name.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(go => go.name)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            FedoCompanionPlugin.Log?.LogWarning(matches.Count > 0
                ? $"FedoCompanion: noms de prefabs ressemblants trouvés dans ObjectDB -> {string.Join(", ", matches)}"
                : $"FedoCompanion: aucun prefab ressemblant à '{sourceName}' trouvé dans ObjectDB.m_items.");
        }

        // Cf. commentaire équivalent dans CompanionPrefabPatch.GetOrCreate : appelée depuis des
        // Postfix sur des méthodes vanilla partagées (ObjectDB.Awake/GetItemPrefab, ZNetScene...),
        // une exception ici casserait toute la chaîne de patches Harmony dessus et peut bloquer
        // le chargement du monde entier.
        //
        // L'enregistrement dans ZNetScene.m_prefabs/ObjectDB.m_items est refait à CHAQUE appel
        // (pas seulement à la construction du clone) : ObjectDB.Awake se déclenche une première
        // fois au menu principal, avant que ZNetScene.instance existe -- le clone était alors mis
        // en cache sans jamais atterrir dans m_prefabs, et le early-return sur _clone empêchait
        // tout nouvel essai au chargement réel de la partie (item invisible pour un spawner tiers
        // qui énumère m_prefabs, comme Easy Spawner, même si GetPrefab/HasPrefab restaient
        // fonctionnels via les Postfix auto-réparants ci-dessous).
        private static GameObject GetOrCreate()
        {
            try
            {
                if (_clone == null)
                {
                    _clone = BuildClone();
                }

                if (_clone == null)
                {
                    return null;
                }

                if (ZNetScene.instance != null && !ZNetScene.instance.m_prefabs.Contains(_clone))
                {
                    ZNetScene.instance.m_prefabs.Add(_clone);
                }

                if (ObjectDB.instance != null && !ObjectDB.instance.m_items.Contains(_clone))
                {
                    ObjectDB.instance.m_items.Add(_clone);
                }
            }
            catch (Exception e)
            {
                FedoCompanionPlugin.Log?.LogError($"FedoCompanion: échec de création du charme d'invocation : {e}");
                _clone = null;
            }

            return _clone;
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static class ObjectDbAwakePatch
        {
            private static void Postfix()
            {
                GetOrCreate();
            }
        }

        // Second point d'entrée nécessaire (cf. commentaire de GetOrCreate) : au chargement
        // réel d'une partie, ZNetScene.Awake tourne après le premier ObjectDB.Awake du menu
        // principal -- c'est cet appel-ci qui réussit enfin à ajouter le clone déjà construit à
        // ZNetScene.m_prefabs.
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

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.GetItemPrefab), typeof(int))]
        private static class GetItemPrefabByHashPatch
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

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.GetItemPrefab), typeof(string))]
        private static class GetItemPrefabByNamePatch
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

        // Résolution par SharedData -- probablement ce qu'utilise VisEquipment pour retrouver le
        // modèle visuel à accrocher en main. Basée sur un dictionnaire (m_itemByData) reconstruit
        // une seule fois par ObjectDB, jamais mis à jour pour un item ajouté après coup à m_items.
        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.GetItemPrefab), typeof(ItemDrop.ItemData.SharedData))]
        private static class GetItemPrefabBySharedDataPatch
        {
            private static void Postfix(ItemDrop.ItemData.SharedData sharedData, ref GameObject __result)
            {
                if (__result != null || sharedData == null || sharedData != SharedData)
                {
                    return;
                }

                __result = GetOrCreate();
            }
        }
    }
}
