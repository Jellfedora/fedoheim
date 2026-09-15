using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FedoHud
{
    // Bloc affichant le niveau + une barre de progression pour une liste de compétences
    // configurable (voir FedoHudPlugin.ParsedSkillTypes, réglage "SkillsList") -- même
    // principe que ClockOverlay.cs : créé une fois dans Hud.Awake, déplaçable à la
    // souris (cliquer-glisser directement, voir DraggableAnchor.cs,
    // partagé avec ClockOverlay),
    // position sauvegardée en local. Toutes les valeurs viennent de `Skills`,
    // entièrement publique (vérifié par réflexion contre assembly_valheim.dll 1.0,
    // aucune réflexion nécessaire dans ce fichier).
    internal static class SkillsOverlay
    {
        private class Row
        {
            public Skills.SkillType Type;
            public TMP_Text Label;
            public Image Fill;
        }

        private const float RowHeight = 26f;

        private static GameObject _root;
        private static readonly List<Row> Rows = new List<Row>();
        private static Sprite _whiteSprite;

        // Hauteur réelle du bloc (variable selon le nombre de compétences choisies, voir
        // Create) + une marge -- exposée pour que RecipeTrackerOverlay.cs puisse démarrer
        // sous CE bloc plutôt que directement sous PlayerStatsOverlay (les deux "juste
        // sous le bloc précédent" par défaut se chevauchaient, vécu en jeu). Zéro tant
        // qu'aucune compétence n'est sélectionnée -- le bloc n'existe pas dans ce cas,
        // rien à éviter.
        public static float HeightWithMargin
        {
            get
            {
                int count = FedoHudPlugin.Instance.ParsedSkillTypes.Count;
                return count == 0 ? 0f : 16f + count * RowHeight + 16f;
            }
        }

        [HarmonyPatch(typeof(Hud), "Awake")]
        private static class HudAwakePatch
        {
            private static void Postfix(Hud __instance)
            {
                try
                {
                    Create(__instance);
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogError($"FedoHud: skills overlay creation failed: {e}");
                }
            }
        }

        // Appelé par FedoHudSettingsPanel.cs quand la liste de compétences change
        // (case cochée/décochée dans le panneau en jeu) -- sans ça, le bloc ne
        // reconstruirait sa liste de lignes qu'au prochain Hud.Awake (rechargement de
        // zone/reconnexion), donc jamais tant qu'on reste dans la même session.
        public static void Rebuild()
        {
            if (Hud.instance != null)
            {
                Create(Hud.instance);
            }
        }

        private static void Create(Hud hud)
        {
            if (hud.m_rootObject == null)
            {
                return;
            }

            // Un rechargement de scène recrée le Hud (donc rappelle Awake), ou un
            // changement de la liste de compétences depuis le panneau en jeu (voir
            // Rebuild()) -- dans les deux cas, un enfant du même nom peut déjà exister.
            // `DestroyImmediate` plutôt que `Destroy` : ce dernier ne supprime l'objet
            // qu'à la fin de la frame, donc l'ancien et le nouveau bloc coexisteraient
            // brièvement (observé en jeu comme un affichage brouillé le temps que Unity
            // nettoie, avant que le rafraîchissement périodique ne repeuple le nouveau).
            var existing = hud.m_rootObject.transform.Find("FedoHud_Skills");
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            Rows.Clear();
            _root = null;

            var skillTypes = FedoHudPlugin.Instance.ParsedSkillTypes;
            if (skillTypes.Count == 0)
            {
                return;
            }

            var go = new GameObject("FedoHud_Skills", typeof(RectTransform));
            go.transform.SetParent(hud.m_rootObject.transform, worldPositionStays: false);

            var rect = go.GetComponent<RectTransform>();
            // Ancré/pivoté en haut à DROITE par défaut (voir FedoHudPlugin pour la
            // position par défaut) -- les enfants (lignes, barres) restent positionnés
            // normalement à l'intérieur, leur `anchoredPosition` étant relative au rect
            // de ce parent, pas à l'écran : rien à changer côté CreateRow.
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            // Reprend la position sauvegardée par un éventuel glisser d'une session
            // précédente -- tant que ce n'est jamais arrivé, calculée sous la minimap du
            // jeu plutôt qu'un nombre fixe (voir HudLayout, sa taille peut varier selon
            // les réglages du joueur).
            rect.anchoredPosition = HudLayout.ResolveTopRightPosition(
                FedoHudPlugin.Instance.SavedSkillsPosition,
                FedoHudPlugin.Instance.DefaultSkillsPosition,
                hud.m_rootObject.GetComponent<RectTransform>(),
                PlayerStatsOverlay.HeightWithMargin);
            rect.sizeDelta = new Vector2(220f, 16f + skillTypes.Count * RowHeight);

            var background = go.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.5f);
            // Repassé à vrai en permanence par DraggableAnchor.cs juste en dessous (pour
            // détecter le survol -- voir ce fichier pour le compromis que ça implique).
            background.raycastTarget = false;

            // Voir HudFont.cs -- `hud.m_foodTime[0].font` (utilisé avant) n'est pas
            // toujours prêt à ce moment précis (Hud.Awake), produisant un avertissement
            // "Font Asset was not found" au lancement même si le texte finissait par
            // s'afficher correctement.
            TMP_FontAsset font = HudFont.Resolve();

            float y = -8f;
            foreach (var type in skillTypes)
            {
                Rows.Add(CreateRow(go.transform, font, type, y));
                y -= RowHeight;
            }

            go.AddComponent<DraggableAnchor>().OnDragEnd = pos => FedoHudPlugin.Instance?.SaveSkillsPosition(pos);

            _root = go;
        }

        private static Row CreateRow(Transform parent, TMP_FontAsset font, Skills.SkillType type, float y)
        {
            var rowGo = new GameObject($"Row_{type}", typeof(RectTransform));
            rowGo.transform.SetParent(parent, worldPositionStays: false);
            var rowRect = rowGo.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(8f, y);
            // Assez haut pour laisser un petit espace entre le nom (en haut, 16px) et la
            // barre de progression (en bas, 6px) au lieu de les coller l'un à l'autre.
            rowRect.sizeDelta = new Vector2(204f, RowHeight);

            // Petite croix "x" pour retirer directement cette compétence de la liste
            // affichée, sans repasser par le panneau d'options -- même principe que le
            // désépinglage d'une recette (voir RecipeTrackerOverlay.CreateHeader).
            var removeGo = new GameObject("Remove", typeof(RectTransform), typeof(Image), typeof(Button));
            removeGo.transform.SetParent(rowGo.transform, worldPositionStays: false);
            var removeRect = removeGo.GetComponent<RectTransform>();
            removeRect.anchorMin = new Vector2(1f, 0.5f);
            removeRect.anchorMax = new Vector2(1f, 0.5f);
            removeRect.pivot = new Vector2(1f, 0.5f);
            removeRect.anchoredPosition = Vector2.zero;
            removeRect.sizeDelta = new Vector2(14f, 14f);

            var removeImage = removeGo.GetComponent<Image>();
            removeImage.sprite = IconSprites.CreateCross(16, 0.22f, new Color(0.8f, 0.3f, 0.3f, 0.9f));

            var removeButton = removeGo.GetComponent<Button>();
            removeButton.targetGraphic = removeImage;
            removeButton.onClick.AddListener(() =>
            {
                FedoHudPlugin.Instance.ToggleSkill(type);
                Rebuild();
            });

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(rowGo.transform, worldPositionStays: false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0f, 1f);
            // Rétréci à droite (voir offsetMax) pour laisser la place à la croix
            // "Remove" ci-dessus plutôt que de passer dessous.
            labelRect.offsetMin = new Vector2(0f, -16f);
            labelRect.offsetMax = new Vector2(-18f, 0f);

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                label.font = font;
            }
            label.fontSize = 14f;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.color = Color.white;
            label.raycastTarget = false;

            var barBackgroundGo = new GameObject("BarBackground", typeof(RectTransform));
            barBackgroundGo.transform.SetParent(rowGo.transform, worldPositionStays: false);
            var barBackgroundRect = barBackgroundGo.GetComponent<RectTransform>();
            barBackgroundRect.anchorMin = new Vector2(0f, 0f);
            barBackgroundRect.anchorMax = new Vector2(1f, 0f);
            barBackgroundRect.pivot = new Vector2(0f, 0f);
            barBackgroundRect.anchoredPosition = Vector2.zero;
            barBackgroundRect.sizeDelta = new Vector2(0f, 6f);

            var barBackgroundImage = barBackgroundGo.AddComponent<Image>();
            barBackgroundImage.sprite = GetWhiteSprite();
            barBackgroundImage.color = new Color(1f, 1f, 1f, 0.15f);
            barBackgroundImage.raycastTarget = false;

            var barFillGo = new GameObject("BarFill", typeof(RectTransform));
            barFillGo.transform.SetParent(barBackgroundGo.transform, worldPositionStays: false);
            var barFillRect = barFillGo.GetComponent<RectTransform>();
            barFillRect.anchorMin = Vector2.zero;
            barFillRect.anchorMax = Vector2.one;
            barFillRect.offsetMin = Vector2.zero;
            barFillRect.offsetMax = Vector2.zero;

            var fillImage = barFillGo.AddComponent<Image>();
            // Sprite explicite plutôt que de compter sur le repli par défaut de `Image`
            // sans sprite assigné -- `Type.Filled` a un comportement peu fiable sans ça
            // selon les cas.
            fillImage.sprite = GetWhiteSprite();
            fillImage.color = new Color(0.85f, 0.65f, 0.25f, 0.95f);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 0f;
            fillImage.raycastTarget = false;

            return new Row { Type = type, Label = label, Fill = fillImage };
        }

        private static Sprite GetWhiteSprite()
        {
            if (_whiteSprite == null)
            {
                var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var pixels = new Color[16];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = Color.white;
                }

                texture.SetPixels(pixels);
                texture.Apply();
                _whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            }

            return _whiteSprite;
        }

        // Throttlé à ~1x/seconde par FedoHudPlugin -- inutile de relire les compétences à
        // chaque frame, elles ne progressent pas plus vite que ça de façon perceptible.
        public static void Refresh()
        {
            if (_root == null)
            {
                return;
            }

            var skills = Player.m_localPlayer != null ? Player.m_localPlayer.GetSkills() : null;
            if (skills == null)
            {
                foreach (var row in Rows)
                {
                    row.Label.text = SkillLocalization.GetName(row.Type);
                    row.Fill.fillAmount = 0f;
                }
                return;
            }

            // GetSkillLevel(type) force l'initialisation paresseuse de l'entrée pour
            // cette compétence -- doit être appelé pour CHAQUE ligne avant GetSkillList()
            // ci-dessous, sinon une compétence encore jamais utilisée n'y apparaîtrait
            // pas du tout.
            var levels = new float[Rows.Count];
            for (int i = 0; i < Rows.Count; i++)
            {
                levels[i] = skills.GetSkillLevel(Rows[i].Type);
            }

            var skillList = skills.GetSkillList();
            for (int i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                float progress = 0f;
                foreach (var skill in skillList)
                {
                    if (skill.m_info.m_skill == row.Type)
                    {
                        progress = skill.GetLevelPercentage();
                        break;
                    }
                }

                row.Label.text = $"{SkillLocalization.GetName(row.Type)} {(int)levels[i]} ({Mathf.RoundToInt(progress * 100f)}%)";
                row.Fill.fillAmount = progress;
            }
        }

        public static void SetVisible(bool visible)
        {
            _root?.SetActive(visible);
        }

        // Appelé par le bouton "Reset positions" du panneau -- voir
        // FedoHudPlugin.ResetOverlayPositions. Recalcule le Y sous la minimap (voir
        // Create/HudLayout) plutôt que de reprendre un nombre fixe, et sauvegarde le
        // résultat pour que ça reste stable au prochain Hud.Awake. Si aucune compétence
        // n'est sélectionnée, `_root` est `null` (voir Create) : rien à déplacer, mais
        // la position est quand même sauvegardée pour être reprise à la prochaine
        // sélection.
        public static void ResetPosition()
        {
            if (Hud.instance == null)
            {
                return;
            }

            var pos = HudLayout.ResolveTopRightPosition(
                FedoHudPlugin.Instance.DefaultSkillsPosition,
                FedoHudPlugin.Instance.DefaultSkillsPosition,
                Hud.instance.m_rootObject.GetComponent<RectTransform>(),
                PlayerStatsOverlay.HeightWithMargin);

            if (_root != null)
            {
                _root.GetComponent<RectTransform>().anchoredPosition = pos;
            }

            FedoHudPlugin.Instance.SaveSkillsPosition(pos);
        }
    }
}
