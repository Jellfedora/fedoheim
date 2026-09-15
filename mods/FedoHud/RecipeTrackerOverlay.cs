using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FedoHud
{
    // Panneau déplaçable listant, pour chaque pièce épinglée (voir RecipeTracker.cs),
    // ses ressources requises (`Piece.m_resources`, public) avec la quantité déjà
    // possédée par le joueur (`Inventory.CountItems`, public) -- rafraîchi
    // périodiquement comme les autres blocs de ce mod (voir
    // FedoHudPlugin.RefreshRecipeTracker). Reconstruit entièrement à chaque
    // épinglage/désépinglage (voir Rebuild, appelé par RecipeTracker.Toggle/Unpin) --
    // la liste change rarement, pas besoin d'un système de lignes ajoutées/retirées à
    // la volée.
    //
    // `Inventory.CountItems(name, quality, matchWorldLevel)` : les valeurs de
    // `quality`/`matchWorldLevel` utilisées ici (-1 / false, "toutes qualités
    // confondues") sont une hypothèse raisonnable non re-vérifiée en jeu -- à corriger
    // ici si les quantités affichées semblent fausses.
    internal static class RecipeTrackerOverlay
    {
        private class IngredientRow
        {
            public string ItemInternalName;
            public int RequiredAmount;
            public TMP_Text Label;
        }

        private class PieceGroup
        {
            public string PieceName;
            public List<IngredientRow> Ingredients = new List<IngredientRow>();
        }

        private static GameObject _root;
        private static TMP_FontAsset _font;
        private static readonly List<PieceGroup> Groups = new List<PieceGroup>();

        [HarmonyPatch(typeof(Hud), "Awake")]
        private static class HudAwakePatch
        {
            private static void Postfix(Hud __instance)
            {
                // Voir HudFont.cs -- `__instance.m_foodTime[0].font` (utilisé avant)
                // n'est pas toujours prêt à ce moment précis (Hud.Awake), produisant un
                // avertissement "Font Asset was not found" au lancement même si le texte
                // finissait par s'afficher correctement.
                _font = HudFont.Resolve();

                // Un rechargement de scène détruit tout ce qui était affiché --
                // reconstruit seulement s'il reste des pièces épinglées (l'état de
                // RecipeTracker, lui, n'est pas remis à zéro par un rechargement).
                Rebuild();
            }
        }

        public static void Rebuild()
        {
            DestroyRoot();
            Groups.Clear();

            if (RecipeTracker.Pinned.Count == 0)
            {
                return;
            }

            try
            {
                Create();
            }
            catch (Exception e)
            {
                FedoHudPlugin.Log?.LogError($"FedoHud: recipe tracker overlay creation failed: {e}");
                DestroyRoot();
            }
        }

        private static void Create()
        {
            _root = new GameObject("FedoHud_RecipeTracker", typeof(RectTransform));

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue - 1;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _root.AddComponent<GraphicRaycaster>();

            const float rowHeight = 22f;
            const float headerHeight = 26f;
            const float width = 260f;

            float height = 20f;
            foreach (var kv in RecipeTracker.Pinned)
            {
                height += headerHeight + kv.Value.Resources.Length * rowHeight;
            }

            var background = new GameObject("Background", typeof(RectTransform));
            background.transform.SetParent(_root.transform, worldPositionStays: false);
            var bgRect = background.GetComponent<RectTransform>();
            // Ancré/pivoté en haut à DROITE par défaut (voir FedoHudPlugin pour la
            // position par défaut) -- les enfants (en-têtes, lignes d'ingrédients) restent
            // positionnés normalement à l'intérieur, relatifs à ce rect, pas à l'écran :
            // rien à changer côté CreateHeader/CreateIngredientLabel.
            bgRect.anchorMin = new Vector2(1f, 1f);
            bgRect.anchorMax = new Vector2(1f, 1f);
            bgRect.pivot = new Vector2(1f, 1f);
            // Tant que le joueur n'a jamais glissé ce bloc, calculé sous la minimap du
            // jeu plutôt qu'un nombre fixe (voir HudLayout, sa taille peut varier selon
            // les réglages du joueur) -- ce panneau vit dans sa propre Canvas (voir
            // Create ci-dessus), mais la conversion passe par l'espace écran donc
            // fonctionne quelle que soit la Canvas d'origine de la minimap. Décalé sous
            // PlayerStatsOverlay ET SkillsOverlay (tous les deux aussi "juste sous la
            // minimap" par défaut) pour ne jamais les recouvrir -- vécu en jeu.
            bgRect.anchoredPosition = HudLayout.ResolveTopRightPosition(
                FedoHudPlugin.Instance.SavedRecipeTrackerPosition,
                FedoHudPlugin.Instance.DefaultRecipeTrackerPosition,
                _root.GetComponent<RectTransform>(),
                PlayerStatsOverlay.HeightWithMargin + SkillsOverlay.HeightWithMargin);
            bgRect.sizeDelta = new Vector2(width, height);

            var bgImage = background.AddComponent<Image>();
            bgImage.sprite = IconSprites.CreateCircle(4, 0f, 1f, Color.white); // carré 4x4, voir SkillsOverlay pour le même besoin
            // Même fond que SkillsOverlay (noir semi-transparent) plutôt que le brun
            // utilisé avant -- cohérence visuelle entre les deux blocs, l'un juste
            // au-dessus de l'autre par défaut.
            bgImage.color = new Color(0f, 0f, 0f, 0.5f);
            // Repassé à vrai en permanence par DraggableAnchor.cs juste en dessous (pour
            // détecter le survol -- voir ce fichier pour le compromis que ça implique).
            bgImage.raycastTarget = false;

            float y = -8f;
            foreach (var kv in RecipeTracker.Pinned)
            {
                var entry = kv.Value;
                var group = new PieceGroup { PieceName = GameLocalization.LocalizeOrRaw(entry.DisplayNameKey) };

                CreateHeader(background.transform, group.PieceName, new Vector2(8f, y), width - 16f, kv.Key);
                y -= headerHeight;

                foreach (var requirement in entry.Resources)
                {
                    if (requirement.m_resItem == null)
                    {
                        continue;
                    }

                    string itemName = requirement.m_resItem.m_itemData.m_shared.m_name;
                    var row = new IngredientRow
                    {
                        ItemInternalName = itemName,
                        RequiredAmount = requirement.m_amount,
                        Label = CreateIngredientLabel(background.transform, new Vector2(16f, y), width - 24f),
                    };
                    group.Ingredients.Add(row);
                    y -= rowHeight;
                }

                Groups.Add(group);
            }

            background.AddComponent<DraggableAnchor>().OnDragEnd = pos => FedoHudPlugin.Instance?.SaveRecipeTrackerPosition(pos);

            Refresh();
        }

        // Petite croix "x" pour désépingler directement depuis le panneau, sans avoir à
        // retrouver la pièce dans le menu du marteau.
        private static void CreateHeader(Transform parent, string pieceName, Vector2 anchoredPosition, float width, string pieceKey)
        {
            var go = new GameObject("Header", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(width, 22f);

            var label = go.AddComponent<TextMeshProUGUI>();
            if (_font != null)
            {
                label.font = _font;
            }
            label.fontSize = 15f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.color = new Color(0.95f, 0.72f, 0.30f);
            label.raycastTarget = false;
            label.text = pieceName;

            var closeGo = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(go.transform, worldPositionStays: false);
            var closeRect = closeGo.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.anchoredPosition = Vector2.zero;
            closeRect.sizeDelta = new Vector2(16f, 16f);

            var closeImage = closeGo.GetComponent<Image>();
            closeImage.sprite = IconSprites.CreateCross(16, 0.22f, new Color(0.8f, 0.3f, 0.3f, 0.9f));

            var closeButton = closeGo.GetComponent<Button>();
            closeButton.targetGraphic = closeImage;
            closeButton.onClick.AddListener(() => RecipeTracker.Unpin(pieceKey));
        }

        private static TMP_Text CreateIngredientLabel(Transform parent, Vector2 anchoredPosition, float width)
        {
            var go = new GameObject("Ingredient", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(width, 18f);

            var label = go.AddComponent<TextMeshProUGUI>();
            if (_font != null)
            {
                label.font = _font;
            }
            label.fontSize = 13f;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.color = new Color(0.90f, 0.75f, 0.55f);
            label.raycastTarget = false;
            return label;
        }

        // Throttlé à ~1x/seconde par FedoHudPlugin -- inutile de recompter l'inventaire
        // à chaque frame.
        public static void Refresh()
        {
            if (_root == null || Player.m_localPlayer == null)
            {
                return;
            }

            var inventory = Player.m_localPlayer.GetInventory();
            foreach (var group in Groups)
            {
                foreach (var row in group.Ingredients)
                {
                    int have = inventory != null ? inventory.CountItems(row.ItemInternalName, -1, false) : 0;
                    string name = GameLocalization.LocalizeOrRaw(row.ItemInternalName);
                    row.Label.text = $"{name} {have}/{row.RequiredAmount}";
                    row.Label.color = have >= row.RequiredAmount
                        ? new Color(0.55f, 0.85f, 0.45f)
                        : new Color(0.90f, 0.75f, 0.55f);
                }
            }
        }

        public static void SetVisible(bool visible)
        {
            _root?.SetActive(visible);
        }

        // Appelé par le bouton "Reset positions" du panneau -- voir
        // FedoHudPlugin.ResetOverlayPositions. `_root` (donc sa propre Canvas, voir
        // Create) n'existe que tant qu'au moins une pièce est épinglée -- sans lui,
        // impossible de recalculer un Y dans le bon repère d'échelle (celui de CETTE
        // Canvas, pas celui de hud.m_rootObject) : on se contente alors de remettre la
        // config sur ses valeurs sentinelles, pour que Create() recalcule proprement au
        // prochain épinglage.
        public static void ResetPosition()
        {
            var defaultPos = FedoHudPlugin.Instance.DefaultRecipeTrackerPosition;
            var background = _root != null ? _root.transform.Find("Background") : null;
            if (background == null)
            {
                FedoHudPlugin.Instance.SaveRecipeTrackerPosition(defaultPos);
                return;
            }

            var pos = HudLayout.ResolveTopRightPosition(
                defaultPos,
                defaultPos,
                _root.GetComponent<RectTransform>(),
                PlayerStatsOverlay.HeightWithMargin + SkillsOverlay.HeightWithMargin);
            ((RectTransform)background).anchoredPosition = pos;
            FedoHudPlugin.Instance.SaveRecipeTrackerPosition(pos);
        }

        private static void DestroyRoot()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }
    }
}
