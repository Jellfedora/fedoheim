using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FedoHud
{
    // Icône d'épingle (étoile) réutilisée par deux points d'accroche : le menu du
    // marteau (`BuildUiPieceButton.Setup`, ci-dessous) et le menu de fabrication
    // d'objets/inventaire (`InventoryGui.AddRecipeToList`, voir
    // CraftingRecipeTracker.cs -- même écran "Tab" que l'inventaire, un seul menu pour
    // les deux en 1.0, vérifié par réflexion). Épingler ajoute l'entrée au panneau
    // d'ingrédients déplaçable (voir RecipeTrackerOverlay.cs), indépendamment du
    // système "Favoris" natif du marteau (`FavoritePieceList`, qui ne fait que filtrer
    // un onglet -- aucun équivalent côté fabrication d'objets, vérifié par réflexion).
    //
    // `Piece.m_resources` et `Recipe.m_resources` sont exactement le même type
    // (`Piece.Requirement[]`), d'où cette structure commune `PinnedEntry` qui marche
    // pour les deux sans code séparé.
    internal static class RecipeTracker
    {
        public readonly struct PinnedEntry
        {
            public readonly string DisplayNameKey;
            public readonly Piece.Requirement[] Resources;

            public PinnedEntry(string displayNameKey, Piece.Requirement[] resources)
            {
                DisplayNameKey = displayNameKey;
                Resources = resources;
            }
        }

        private const string IconName = "FedoHud_PinIcon";

        private static readonly Color UnpinnedColor = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color PinnedColor = new Color(0.4f, 0.85f, 1f, 1f);

        private static Sprite _iconSprite;

        // Nom interne stable (Piece.m_name / le nom de l'objet fabriqué) comme clé --
        // indépendante de la langue du joueur, contrairement au texte localisé.
        private static readonly Dictionary<string, PinnedEntry> PinnedEntries = new Dictionary<string, PinnedEntry>();

        public static IReadOnlyDictionary<string, PinnedEntry> Pinned => PinnedEntries;

        [HarmonyPatch(typeof(BuildUiPieceButton), nameof(BuildUiPieceButton.Setup))]
        private static class SetupPatch
        {
            private static void Postfix(BuildUiPieceButton __instance, Piece pieceInfo)
            {
                try
                {
                    // `m_repairPiece` marque l'entrée spéciale "réparer" du menu du
                    // marteau (bascule un mode d'outil, pas une vraie pièce/recette --
                    // vérifié par réflexion contre assembly_valheim.dll) : jamais
                    // épinglable, elle n'a pas d'ingrédients à afficher.
                    if (pieceInfo == null || pieceInfo.m_repairPiece)
                    {
                        return;
                    }

                    CreateOrUpdateIcon(__instance.gameObject, pieceInfo.m_name, () => new PinnedEntry(pieceInfo.m_name, pieceInfo.m_resources));
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogWarning($"FedoHud: recipe tracker icon failed (build menu): {e.Message}");
                }
            }
        }

        // Logique de création/mise à jour de l'icône, partagée avec
        // CraftingRecipeTracker.cs pour le menu de fabrication -- `buildEntry` n'est
        // appelé qu'au moment d'épingler (pas à chaque rafraîchissement de case).
        public static void CreateOrUpdateIcon(GameObject cell, string key, Func<PinnedEntry> buildEntry)
        {
            if (FedoHudPlugin.Instance == null || !FedoHudPlugin.Instance.ShowRecipeTrackerIcon || string.IsNullOrEmpty(key))
            {
                return;
            }

            var existing = cell.transform.Find(IconName);
            GameObject go;
            Image icon;
            Button buttonComponent;

            if (existing != null)
            {
                go = existing.gameObject;
                icon = go.GetComponent<Image>();
                buttonComponent = go.GetComponent<Button>();
            }
            else
            {
                go = new GameObject(IconName, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(cell.transform, worldPositionStays: false);

                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-2f, -2f);
                rect.sizeDelta = new Vector2(16f, 16f);

                icon = go.GetComponent<Image>();
                icon.sprite = GetIconSprite();

                buttonComponent = go.GetComponent<Button>();
                buttonComponent.targetGraphic = icon;
            }

            // Rebranché à chaque appel (les cases sont réutilisées/recréées selon le
            // menu) -- referme sur `key`/`buildEntry`, ceux actuellement assignés à cette
            // case, jamais une closure obsolète d'un appel précédent.
            buttonComponent.onClick = new Button.ButtonClickedEvent();
            buttonComponent.onClick.AddListener(() =>
            {
                Toggle(key, buildEntry);
                RefreshIconVisual(icon, key);
            });

            RefreshIconVisual(icon, key);
            RefreshPinnedDataIfChanged(key, buildEntry);
        }

        // Une pièce du marteau n'a pas de palier de qualité -- `buildEntry` renvoie
        // toujours le même tableau (référence stable), donc rien ne change ici. Une
        // recette de fabrication, elle, cible un palier de qualité différent selon le
        // niveau actuel de l'objet déjà possédé (voir CraftingRecipeTracker.BuildEntry) :
        // sans ça, un objet épinglé puis amélioré en jeu resterait affiché avec les
        // quantités de l'ancien palier. Ne redessine le panneau que si les quantités ont
        // réellement changé -- pas à chaque rafraîchissement de case (souvent plusieurs
        // fois par seconde tant que le menu reste ouvert).
        private static void RefreshPinnedDataIfChanged(string key, Func<PinnedEntry> buildEntry)
        {
            if (!PinnedEntries.TryGetValue(key, out var current))
            {
                return;
            }

            var updated = buildEntry();
            if (EntriesEqual(current, updated))
            {
                return;
            }

            PinnedEntries[key] = updated;
            RecipeTrackerOverlay.Rebuild();
        }

        private static bool EntriesEqual(PinnedEntry a, PinnedEntry b)
        {
            if (a.Resources.Length != b.Resources.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Resources.Length; i++)
            {
                if (a.Resources[i].m_resItem != b.Resources[i].m_resItem || a.Resources[i].m_amount != b.Resources[i].m_amount)
                {
                    return false;
                }
            }

            return true;
        }

        private static void RefreshIconVisual(Image icon, string key)
        {
            icon.color = PinnedEntries.ContainsKey(key) ? PinnedColor : UnpinnedColor;
        }

        private static void Toggle(string key, Func<PinnedEntry> buildEntry)
        {
            if (PinnedEntries.ContainsKey(key))
            {
                PinnedEntries.Remove(key);
            }
            else
            {
                PinnedEntries[key] = buildEntry();
            }

            RecipeTrackerOverlay.Rebuild();
        }

        public static void Unpin(string key)
        {
            if (PinnedEntries.Remove(key))
            {
                RecipeTrackerOverlay.Rebuild();
            }
        }

        private static Sprite GetIconSprite()
        {
            if (_iconSprite == null)
            {
                _iconSprite = IconSprites.CreateStar(16, 5, 0.45f, 1f, Color.white);
            }

            return _iconSprite;
        }
    }
}
