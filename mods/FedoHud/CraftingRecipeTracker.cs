using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FedoHud
{
    // Étend l'icône d'épingle (voir RecipeTracker.cs) au menu de fabrication
    // d'objets/inventaire -- le même écran "Tab" en 1.0, vérifié par réflexion.
    // Accroche sur InventoryGui.AddRecipeToList (privée), le point où chaque case de
    // recette est créée/rafraîchie ; m_availableRecipes (privé) garde la dernière
    // entrée ajoutée (InventoryGui.RecipeDataPair, struct publique : Recipe/ItemData/
    // InterfaceElement/CanCraft).
    internal static class CraftingRecipeTracker
    {
        // `InventoryGui.RecipeDataPair` est une struct PRIVÉE (malgré des membres publics)
        // -- CS0122 empêche tout cast direct depuis un autre assembly. Ses propriétés sont
        // donc lues par réflexion sur le type réel de l'objet boîté, une seule fois
        // (résolution paresseuse, mémorisée).
        private static readonly FieldInfoWrapper AvailableRecipesField = new FieldInfoWrapper(
            AccessTools.Field(typeof(InventoryGui), "m_availableRecipes"));

        private static System.Reflection.PropertyInfo _recipeProperty;
        private static System.Reflection.PropertyInfo _interfaceElementProperty;
        private static System.Reflection.PropertyInfo _itemDataProperty;
        private static bool _reflectionResolved;
        private static bool _reflectionWarned;

        [HarmonyPatch(typeof(InventoryGui), "AddRecipeToList")]
        private static class AddRecipeToListPatch
        {
            private static void Postfix(InventoryGui __instance)
            {
                try
                {
                    AddIcon(__instance);
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogWarning($"FedoHud: recipe tracker icon failed (crafting menu): {e.Message}");
                }
            }
        }

        private static void AddIcon(InventoryGui gui)
        {
            var list = AvailableRecipesField.GetValue(gui) as IList;
            if (list == null || list.Count == 0)
            {
                return;
            }

            object pair = list[list.Count - 1];
            ResolveReflection(pair.GetType());
            if (_recipeProperty == null || _interfaceElementProperty == null)
            {
                return;
            }

            var recipe = _recipeProperty.GetValue(pair) as Recipe;
            var element = _interfaceElementProperty.GetValue(pair) as GameObject;
            var itemData = _itemDataProperty?.GetValue(pair) as ItemDrop.ItemData;
            if (recipe == null || recipe.m_item == null || element == null)
            {
                return;
            }

            string key = recipe.m_item.m_itemData.m_shared.m_name;
            RecipeTracker.CreateOrUpdateIcon(element, key, () => BuildEntry(recipe, itemData, key));
        }

        // Le menu de fabrication n'affiche jamais le coût de TOUS les paliers de qualité
        // à la fois -- pour un objet déjà possédé, il ne montre que le coût de la
        // PROCHAINE amélioration (qualité actuelle + 1), voir InventoryGui.UpdateRecipe/
        // SetupRequirementList décompilés : `num = itemData.m_quality + 1` puis
        // `requirement.GetAmount(num)`. `Piece.Requirement.GetAmount` calcule un montant
        // DIFFÉRENT par palier via `m_amountPerLevel` (décompilé aussi) -- utiliser
        // `recipe.m_resources` brut (comme pour une pièce du marteau, qui n'a pas de
        // palier de qualité) affichait donc le coût combiné de tous les paliers au lieu
        // de celui réellement visé. Recalculé à chaque rafraîchissement de la case (voir
        // RecipeTracker.CreateOrUpdateIcon) pour rester à jour si l'objet change de
        // niveau pendant qu'il reste épinglé.
        //
        // `SetupRequirementList` filtre aussi chaque ressource selon la station de
        // fabrication active : une ressource marquée `m_upgraderResource` (réservée à un
        // améliorateur, ex. le meuble d'amélioration d'armes) n'apparaît QUE si la
        // station active a le même indicateur (`CraftingStation.m_upgrader`) -- sans
        // station (fabrication à la main, ex. Hache en pierre), une telle ressource
        // reste toujours masquée par le jeu, peu importe son montant. On reproduisait
        // GetAmount() mais pas ce filtre : un objet dont la recette contient une
        // ressource d'amélioration au montant non nul (même si vanilla ne l'affiche
        // jamais dans ce contexte) apparaissait donc à tort dans le panneau épinglé.
        private static RecipeTracker.PinnedEntry BuildEntry(Recipe recipe, ItemDrop.ItemData itemData, string key)
        {
            int targetQuality = (itemData?.m_quality ?? 0) + 1;
            var currentStation = Player.m_localPlayer != null ? Player.m_localPlayer.GetCurrentCraftingStation() : null;
            var scaled = new List<Piece.Requirement>();
            foreach (var requirement in recipe.m_resources)
            {
                if (requirement.m_resItem == null)
                {
                    continue;
                }

                bool stationMismatch = currentStation != null
                    ? currentStation.m_upgrader != requirement.m_upgraderResource
                    : requirement.m_upgraderResource;
                if (stationMismatch)
                {
                    continue;
                }

                int amount = requirement.GetAmount(targetQuality);
                if (amount <= 0)
                {
                    continue;
                }

                scaled.Add(new Piece.Requirement
                {
                    m_resItem = requirement.m_resItem,
                    m_amount = amount,
                });
            }

            return new RecipeTracker.PinnedEntry(key, scaled.ToArray());
        }

        private static void ResolveReflection(Type pairType)
        {
            if (_reflectionResolved)
            {
                return;
            }

            _reflectionResolved = true;
            _recipeProperty = pairType.GetProperty("Recipe");
            _interfaceElementProperty = pairType.GetProperty("InterfaceElement");
            _itemDataProperty = pairType.GetProperty("ItemData");

            if ((_recipeProperty == null || _interfaceElementProperty == null) && !_reflectionWarned)
            {
                FedoHudPlugin.Log?.LogWarning("FedoHud: InventoryGui.RecipeDataPair.Recipe/InterfaceElement introuvables par réflexion, icône de recette désactivée dans le menu de fabrication.");
                _reflectionWarned = true;
            }
        }

        // Petit wrapper pour ne pas s'exposer directement à un FieldInfo null (nom de
        // champ introuvable) à chaque appel -- log une seule fois plutôt qu'à chaque
        // recette affichée.
        private class FieldInfoWrapper
        {
            private readonly System.Reflection.FieldInfo _field;
            private bool _warned;

            public FieldInfoWrapper(System.Reflection.FieldInfo field)
            {
                _field = field;
            }

            public object GetValue(object instance)
            {
                if (_field == null)
                {
                    if (!_warned)
                    {
                        FedoHudPlugin.Log?.LogWarning("FedoHud: InventoryGui.m_availableRecipes introuvable par réflexion, icône de recette désactivée dans le menu de fabrication.");
                        _warned = true;
                    }

                    return null;
                }

                return _field.GetValue(instance);
            }
        }
    }
}
