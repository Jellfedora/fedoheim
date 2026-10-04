using System;
using System.IO;
using System.Reflection;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace FedoKnorri
{
    // Palier "évolué" de la graine d'invocation (voir CompanionTier) -- même structure que
    // SummonItemPrefabPatch pour le palier de base, cf. ce fichier pour le détail des choix
    // (Jotunn PrefabManager/ItemManager, icône par extension .jpg/.png, teinte violette par
    // réflexion de shader...), pas reexpliqué ici en détail.
    //
    // Différence propre à ce palier : en plus de cloner/enregistrer l'item, cette classe
    // enregistre aussi sa recette de craft (graine de base + miels, à l'établi) une fois
    // l'item lui-même créé avec succès -- ItemManager.Instance.AddRecipe, résolu par Jotunn au
    // même moment/de la même façon que le clonage de prefab (ObjectDB/ZNetScene disponibles),
    // donc rien de spécial à attendre en plus. Dépend implicitement que
    // SummonItemPrefabPatch.CreateItem se soit déjà exécuté avec succès CETTE frame-ci pour que
    // "Fedo_KnorriCharm" (l'ingrédient) soit déjà résolvable -- garanti par l'ordre
    // d'abonnement à PrefabManager.OnVanillaPrefabsAvailable dans FedoKnorriPlugin.Awake
    // (SummonItemPrefabPatch.Init() appelé avant celui-ci).
    internal static class ShamanSummonItemPrefabPatch
    {
        public const string PrefabName = "Fedo_KnorriShamanCharm";
        private const string RecipeName = "Fedo_Recipe_KnorriShamanCharm";

        // Même fichier que le palier de base (voir SummonItemPrefabPatch) -- pas de second
        // asset dédié pour l'instant, la teinte violette (ApplyPurpleTint, réutilisée telle
        // quelle) suffit à distinguer visuellement les deux dans l'inventaire pour le moment.
        private const string IconFileName = "knorri_seed.jpg";

        private static GameObject _clone;

        // Cf. commentaire équivalent dans SummonItemPrefabPatch.IsSummonItem : nom mis en cache
        // à la création plutôt que relu en direct depuis la config (rechargement à chaud) ou
        // comparé par identité de SharedData (cassé pour un exemplaire obtenu autrement qu'en
        // sortant tout juste du clonage, voir CHANGELOG) -- même technique, même raisons.
        private static string _createdWithName;

        public static bool IsSummonItem(ItemDrop.ItemData item)
        {
            return _createdWithName != null && item?.m_shared != null && item.m_shared.m_name == _createdWithName;
        }

        public static GameObject GetPrefab()
        {
            return _clone;
        }

        public static void Init()
        {
            PrefabManager.OnVanillaPrefabsAvailable += CreateItem;
        }

        private static void CreateItem()
        {
            if (_clone != null)
            {
                return;
            }

            try
            {
                string sourceName = FedoKnorriPlugin.Instance.ShamanSummonItemSourceItem.Value;

                GameObject clone = PrefabManager.Instance.CreateClonedPrefab(PrefabName, sourceName);
                if (clone == null)
                {
                    FedoKnorriPlugin.Log?.LogError($"FedoKnorri: prefab source '{sourceName}' introuvable, impossible de créer l'item d'invocation du chaman.");
                    return;
                }

                var itemDrop = clone.GetComponent<ItemDrop>();
                if (itemDrop == null)
                {
                    FedoKnorriPlugin.Log?.LogError($"FedoKnorri: '{sourceName}' n'a pas de composant ItemDrop, impossible d'en faire un item d'invocation.");
                    return;
                }

                itemDrop.m_itemData.m_shared.m_name = FedoKnorriPlugin.Instance.ShamanSummonItemName.Value;
                itemDrop.m_itemData.m_shared.m_description = FedoKnorriPlugin.Instance.ShamanSummonItemDescription.Value;
                _createdWithName = itemDrop.m_itemData.m_shared.m_name;

                // Cf. commentaire équivalent dans SummonItemPrefabPatch.CreateItem.
                itemDrop.m_itemData.m_shared.m_itemType = ItemDrop.ItemData.ItemType.Consumable;

                Sprite icon = LoadIcon();
                if (icon != null)
                {
                    itemDrop.m_itemData.m_shared.m_icons = new[] { icon };
                }

                SummonItemSparkleEffect.Attach(clone);
                SummonItemPrefabPatch.ApplyPurpleTint(clone);

                ItemManager.Instance.AddItem(new CustomItem(clone, fixReference: false));

                _clone = clone;

                RegisterRecipe();
            }
            catch (Exception e)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: échec de création de l'item d'invocation du chaman : {e}");
            }
        }

        // Recette d'évolution : graine de base (SummonItemPrefabPatch.PrefabName) + miels, à
        // l'établi -- entièrement configurable (nom de l'ingrédient miel, quantité, station,
        // niveau requis), au cas où l'ingrédient réel diffèrerait du nom vanilla attendu
        // ("Honey") sur une install donnée, même filet de sécurité que
        // SummonItemPrefabPatch.LogSimilarItemNames en cas de faute de frappe -- ici on se
        // contente de laisser Jotunn logger son propre avertissement si un ingrédient est
        // introuvable, la recette entière n'est de toute façon pas bloquante pour le reste du
        // mod si elle échoue.
        private static void RegisterRecipe()
        {
            var recipeConfig = new RecipeConfig
            {
                Name = RecipeName,
                Item = PrefabName,
                Amount = 1,
                CraftingStation = FedoKnorriPlugin.Instance.ShamanRecipeCraftingStation.Value,
                MinStationLevel = FedoKnorriPlugin.Instance.ShamanRecipeMinStationLevel.Value,
            };
            recipeConfig.AddRequirement(SummonItemPrefabPatch.PrefabName, 1);
            recipeConfig.AddRequirement(
                FedoKnorriPlugin.Instance.ShamanRecipeHoneyItem.Value,
                FedoKnorriPlugin.Instance.ShamanRecipeHoneyAmount.Value);

            ItemManager.Instance.AddRecipe(new CustomRecipe(recipeConfig));
        }

        // Cf. commentaire équivalent dans SummonItemPrefabPatch.LoadIcon.
        private static Sprite LoadIcon()
        {
            try
            {
                string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string path = Path.Combine(dllDir ?? "", IconFileName);
                if (!File.Exists(path))
                {
                    FedoKnorriPlugin.Log?.LogWarning($"FedoKnorri: '{IconFileName}' introuvable à côté de la DLL, l'item d'invocation du chaman gardera l'icône vanilla de '{FedoKnorriPlugin.Instance.ShamanSummonItemSourceItem.Value}'.");
                    return null;
                }

                Sprite sprite = AssetUtils.LoadSpriteFromFile(path);
                if (sprite == null)
                {
                    FedoKnorriPlugin.Log?.LogWarning($"FedoKnorri: échec du chargement de '{IconFileName}' en icône, l'item d'invocation du chaman gardera l'icône vanilla de '{FedoKnorriPlugin.Instance.ShamanSummonItemSourceItem.Value}'.");
                }

                return sprite;
            }
            catch (Exception e)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: échec du chargement de '{IconFileName}' en icône pour l'item du chaman : {e}");
                return null;
            }
        }
    }
}
