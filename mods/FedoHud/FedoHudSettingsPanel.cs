using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FedoHud
{
    // Panneau d'options ouvert depuis le bouton ajouté au menu pause (voir
    // MenuButton.cs). Deux sections :
    // - "Options" : une case par réglage on/off déjà présent dans fedo.hud.cfg (voir
    //   FedoHudPlugin.ToggleOptions) -- cliquer une ligne bascule directement la
    //   ConfigEntry correspondante, BepInEx réécrit le .cfg tout seul dès qu'une valeur
    //   change (aucun code d'enregistrement à part nécessaire).
    // - "Skills to show" : une case par valeur de `Skills.SkillType` (voir
    //   FedoHudPlugin.IsSkillSelected/ToggleSkill) -- ajoute/retire ce nom de la liste
    //   `SkillsList` (une simple chaîne "A,B,C" en config, vide par défaut).
    //
    // Habillage : couleur plate façon bois/parchemin pour le fond (un sprite natif
    // cloné du vrai panneau "Paramètres" laissait transparaître le menu pause derrière,
    // testé en jeu -- abandonné), bouton "Fermer" cloné de Menu.m_settingsButton pour
    // garder ce bout de style natif (voir CreateActionButton) ; les cases à cocher
    // elles-mêmes sont de petits ronds dessinés par code (voir CreateCircleSprite)
    // façon boutons radio, faute de pouvoir récupérer proprement le sprite natif (privé,
    // sur une classe différente -- voir
    // Valheim.SettingsGui.GraphicsSettings.m_qualityTogglePrefab).
    //
    // Auto-suffisant (sa propre Canvas/GraphicRaycaster). Positions/tailles
    // approximatives, pas encore ajustées visuellement en jeu -- à retoucher ici si
    // besoin.
    internal static class FedoHudSettingsPanel
    {
        private const float RowHeight = 28f;
        // Les réglages on/off (section "Options") sont maintenant sur 2 colonnes au lieu
        // d'une seule -- la liste est longue (17 réglages) et chaque libellé est court,
        // une seule colonne pleine largeur ne faisait que gaspiller de la place et
        // rendait le panneau inutilement haut.
        private const int OptionColumns = 2;
        private const float SkillColumnWidth = 190f;
        private const int SkillColumns = 3;
        private const float BorderThickness = 4f;

        // Couleurs approximatives du thème "bois/parchemin" du jeu -- pas de sprite natif
        // récupéré ici (voir TryCopyNativeBackground), donc une teinte plate plutôt que
        // la vraie texture de bois.
        private static readonly Color PanelColor = new Color(0.16f, 0.11f, 0.07f, 1f);
        // Cadre doré autour du panneau + légère plaque derrière chaque grille de cases --
        // sans ça le panneau précédent était un simple pavé plat sans aucune séparation
        // visuelle claire entre les sections.
        private static readonly Color BorderColor = new Color(0.62f, 0.47f, 0.28f, 1f);
        private static readonly Color CardColor = new Color(1f, 1f, 1f, 0.035f);
        private static readonly Color DividerColor = new Color(0.95f, 0.72f, 0.30f, 0.5f);
        private static readonly Color TitleColor = new Color(0.95f, 0.72f, 0.30f);
        private static readonly Color LabelColor = new Color(0.90f, 0.75f, 0.55f);
        private static readonly Color RingColor = new Color(0.55f, 0.42f, 0.25f, 0.9f);
        private static readonly Color DotColor = new Color(0.95f, 0.72f, 0.30f, 1f);

        private static Sprite _ringSprite;
        private static Sprite _dotSprite;

        private static GameObject _root;

        public static void Toggle(Menu menu)
        {
            if (_root != null)
            {
                Hide();
                return;
            }

            Show(menu);
        }

        private static void Show(Menu menu)
        {
            try
            {
                _root = new GameObject("FedoHud_SettingsPanel", typeof(RectTransform));

                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = short.MaxValue;

                var scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                _root.AddComponent<GraphicRaycaster>();

                var options = FedoHudPlugin.Instance.ToggleOptions;
                var skillTypes = SkillTypes();
                int optionRows = Mathf.CeilToInt(options.Count / (float)OptionColumns);
                int skillRows = Mathf.CeilToInt(skillTypes.Length / (float)SkillColumns);

                float width = SkillColumns * SkillColumnWidth + 40f;
                float optionColumnWidth = (width - 40f) / OptionColumns;

                // Cadre doré derrière le fond principal -- juste un rectangle légèrement
                // plus grand dans la même teinte que les ronds de case à cocher, pour que
                // le panneau ait un contour net plutôt qu'un pavé plat sans bordure.
                var border = new GameObject("Border", typeof(RectTransform), typeof(Image));
                border.transform.SetParent(_root.transform, worldPositionStays: false);
                var borderRect = border.GetComponent<RectTransform>();
                borderRect.anchorMin = new Vector2(0.5f, 0.5f);
                borderRect.anchorMax = new Vector2(0.5f, 0.5f);
                borderRect.pivot = new Vector2(0.5f, 0.5f);
                borderRect.anchoredPosition = Vector2.zero;
                border.GetComponent<Image>().color = BorderColor;

                var background = new GameObject("Background", typeof(RectTransform));
                background.transform.SetParent(_root.transform, worldPositionStays: false);
                var bgRect = background.GetComponent<RectTransform>();
                bgRect.anchorMin = new Vector2(0.5f, 0.5f);
                bgRect.anchorMax = new Vector2(0.5f, 0.5f);
                bgRect.pivot = new Vector2(0.5f, 0.5f);
                bgRect.anchoredPosition = Vector2.zero;

                // Une couleur plate pleinement opaque plutôt qu'un sprite natif cloné :
                // testé en jeu, le sprite/couleur du vrai panneau "Paramètres" laissait
                // transparaître le menu pause derrière (effet de superposition non
                // maîtrisé -- voir CHANGELOG).
                var bgImage = background.AddComponent<Image>();
                bgImage.color = PanelColor;

                // Le repli sur `HudFont.Resolve()` (voir ce fichier) évite les
                // avertissements "Font Asset was not found" observés en jeu quand la
                // police du bouton natif n'est pas encore prête à cet instant.
                TMP_FontAsset font = menu.m_settingsButton != null
                    ? menu.m_settingsButton.GetComponentInChildren<TMP_Text>(includeInactive: true)?.font
                    : null;
                if (font == null)
                {
                    font = HudFont.Resolve();
                }

                // Poids normal + un peu d'espacement entre les lettres plutôt que
                // `FontStyles.Bold` -- ce dernier simule un gras en épaississant les
                // traits (la police n'a pas de vraie graisse grasse dédiée), ce qui
                // rendait le titre épais et grossier au lieu d'élégant.
                var title = CreateLabel(background.transform, font, "FedoHud", 28f, TitleColor, new Vector2(0f, -20f), width - 40f);
                title.characterSpacing = 3f;
                title.alignment = TextAlignmentOptions.Top;

                float y = -70f;
                const float leftMargin = 20f;

                // Chaque section suit le même patron : titre, fin liseré doré, petite
                // plaque légèrement plus claire derrière la grille de cases (sinon rien
                // ne délimite visuellement où une section s'arrête, une fois les cases
                // réparties sur plusieurs colonnes avec des espaces entre elles).
                CreateSectionLabel(background.transform, font, FedoHudPlugin.Instance.SettingsPanelOptionsLabel, new Vector2(leftMargin, y), width - 40f);
                y -= 24f;
                CreateDivider(background.transform, new Vector2(leftMargin, y), width - 40f);
                y -= 8f;

                float optionsCardTop = y;
                float optionsGridHeight = optionRows * RowHeight;
                CreateCard(background.transform, new Vector2(leftMargin, optionsCardTop), width - 40f, optionsGridHeight);

                for (int i = 0; i < options.Count; i++)
                {
                    var option = options[i];
                    int column = i % OptionColumns;
                    int row = i / OptionColumns;

                    bool GetValue() => option.Entry.Value;
                    void SetValue() => option.Entry.Value = !option.Entry.Value;

                    var position = new Vector2(leftMargin + column * optionColumnWidth, optionsCardTop - row * RowHeight);
                    CreateToggleRow(background.transform, font, option.Label, GetValue, SetValue, position, optionColumnWidth - 10f);
                }

                y = optionsCardTop - optionsGridHeight - 22f;
                CreateSectionLabel(background.transform, font, FedoHudPlugin.Instance.SettingsPanelSkillsLabel, new Vector2(leftMargin, y), width - 40f);
                y -= 24f;
                CreateDivider(background.transform, new Vector2(leftMargin, y), width - 40f);
                y -= 8f;

                float rowStartY = y;
                float skillsGridHeight = skillRows * RowHeight;
                CreateCard(background.transform, new Vector2(leftMargin, rowStartY), width - 40f, skillsGridHeight);

                for (int i = 0; i < skillTypes.Length; i++)
                {
                    var type = skillTypes[i];
                    int column = i % SkillColumns;
                    int row = i / SkillColumns;

                    bool GetValue() => FedoHudPlugin.Instance.IsSkillSelected(type);
                    void SetValue()
                    {
                        FedoHudPlugin.Instance.ToggleSkill(type);
                        // Sans ça, le bloc en jeu ne reconstruirait sa liste de lignes
                        // qu'au prochain Hud.Awake (rechargement de zone/reconnexion) --
                        // jamais tant qu'on reste dans la même session.
                        SkillsOverlay.Rebuild();
                    }

                    var position = new Vector2(leftMargin + column * SkillColumnWidth, rowStartY - row * RowHeight);
                    CreateToggleRow(background.transform, font, SkillLocalization.GetName(type), GetValue, SetValue, position, SkillColumnWidth - 10f);
                }

                y = rowStartY - skillsGridHeight - 20f;
                CreateActionButton(menu, background.transform, FedoHudPlugin.Instance.SettingsPanelResetPositionsLabel, new Vector2(0f, y), FedoHudPlugin.Instance.ResetOverlayPositions);
                y -= 54f;
                CreateActionButton(menu, background.transform, FedoHudPlugin.Instance.SettingsPanelCloseLabel, new Vector2(0f, y), Hide);

                // Hauteur dérivée du contenu réellement posé ci-dessus (bas du bouton
                // "Close" + marge) plutôt qu'une formule à part recalculée à la main --
                // les enfants sont ancrés au coin haut-gauche de ce rect (indépendant de
                // sizeDelta), donc rien ne bouge une fois cette taille appliquée.
                const float closeButtonHeight = 44f;
                float height = -(y - closeButtonHeight) + 30f;
                bgRect.sizeDelta = new Vector2(width, height);
                borderRect.sizeDelta = new Vector2(width + BorderThickness * 2f, height + BorderThickness * 2f);
            }
            catch (Exception e)
            {
                FedoHudPlugin.Log?.LogError($"FedoHud: settings panel creation failed: {e}");
                Hide();
            }
        }

        public static void Hide()
        {
            if (_root == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(_root);
            _root = null;
        }

        // `Enum.GetValues(typeof(Skills.SkillType))` ne renvoie que les valeurs
        // COMPILÉES dans ce jeu de base -- une compétence ajoutée par un autre mod (ex.
        // "Sailing"/"Exploration" via le SkillManager de Jotunn) utilise un entier
        // choisi à l'exécution, jamais un membre nommé de cet enum, donc invisible pour
        // Enum.GetValues (vérifié par réflexion contre assembly_valheim.dll). La vraie
        // liste vécue par le jeu est `Skills.m_skills` (public, List<Skills.SkillDef>)
        // sur l'instance du joueur local -- c'est elle que Jotunn complète, et c'est
        // elle qu'utilise le propre écran de compétences du jeu (SkillsDialog.Setup,
        // décompilé). Repli sur l'enum si aucun joueur n'est chargé (ne devrait pas
        // arriver, ce panneau n'étant accessible que depuis le menu pause en partie).
        private static Skills.SkillType[] SkillTypes()
        {
            var result = new System.Collections.Generic.List<Skills.SkillType>();
            var liveSkills = Player.m_localPlayer != null ? Player.m_localPlayer.GetSkills() : null;

            if (liveSkills != null && liveSkills.m_skills != null)
            {
                foreach (var def in liveSkills.m_skills)
                {
                    if (def != null && def.m_skill != Skills.SkillType.None && def.m_skill != Skills.SkillType.All && !result.Contains(def.m_skill))
                    {
                        result.Add(def.m_skill);
                    }
                }
            }

            if (result.Count > 0)
            {
                return result.ToArray();
            }

            var all = (Skills.SkillType[])Enum.GetValues(typeof(Skills.SkillType));
            foreach (var type in all)
            {
                if (type != Skills.SkillType.None && type != Skills.SkillType.All)
                {
                    result.Add(type);
                }
            }

            return result.ToArray();
        }

        private static TMP_Text CreateLabel(Transform parent, TMP_FontAsset font, string text, float fontSize, Color color, Vector2 anchoredPosition, float width)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(width, 40f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                tmp.font = font;
            }
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Top;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.text = text;
            return tmp;
        }

        // Libellé de section aligné à gauche (ex. "Options"/"Skills to show") --
        // `anchoredPosition` est la distance depuis le coin haut-gauche du panneau,
        // contrairement à CreateLabel (le titre, centré) ci-dessus.
        private static TMP_Text CreateSectionLabel(Transform parent, TMP_FontAsset font, string text, Vector2 anchoredPosition, float width)
        {
            var go = new GameObject("SectionLabel", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(width, 26f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                tmp.font = font;
            }
            tmp.fontSize = 18f;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.color = TitleColor;
            tmp.raycastTarget = false;
            tmp.text = text;
            return tmp;
        }

        // Petit liseré horizontal sous un titre de section -- `anchoredPosition` est le
        // coin haut-gauche de la barre, même repère que CreateSectionLabel ci-dessus.
        private static void CreateDivider(Transform parent, Vector2 anchoredPosition, float width)
        {
            var go = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, worldPositionStays: false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(width, 2f);
            go.GetComponent<Image>().color = DividerColor;
        }

        // Plaque légèrement plus claire que le fond, posée derrière une grille de cases
        // (Options/Skills) pour la délimiter visuellement -- sans elle, des cases
        // réparties sur plusieurs colonnes avec des espaces entre elles ne donnaient
        // aucun indice visuel de where une section commence/s'arrête.
        private static void CreateCard(Transform parent, Vector2 topLeft, float width, float contentHeight)
        {
            const float padding = 6f;
            var go = new GameObject("Card", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, worldPositionStays: false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x - padding, topLeft.y + padding);
            rect.sizeDelta = new Vector2(width + padding * 2f, contentHeight + padding * 2f);
            go.GetComponent<Image>().color = CardColor;
            go.GetComponent<Image>().raycastTarget = false;
            // Toujours en-dessous des lignes de la grille -- créée avant elles dans
            // Show(), mais explicite ici au cas où l'ordre d'appel changerait un jour.
            go.transform.SetAsFirstSibling();
        }

        // Ligne cliquable "○ Libellé" façon bouton radio -- voir CreateRadioSprites pour
        // le rendu du rond lui-même (dessiné par code, pas un sprite du jeu).
        // `anchoredPosition` est la distance depuis le coin haut-gauche du panneau (voir
        // CreateSectionLabel ci-dessus) -- pas le centre, contrairement à un ancrage
        // (0.5, 1) qui ferait déborder la ligne d'un côté ou de l'autre selon sa largeur
        // (bug observé en jeu, voir CHANGELOG).
        private static void CreateToggleRow(Transform parent, TMP_FontAsset font, string label, Func<bool> getValue, Action toggle, Vector2 anchoredPosition, float width)
        {
            var rowGo = new GameObject("ToggleRow", typeof(RectTransform), typeof(Image), typeof(Button));
            rowGo.transform.SetParent(parent, worldPositionStays: false);
            var rect = rowGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(width, RowHeight);

            // Quasi invisible : juste un Graphic pour que toute la ligne capte le clic
            // (voir DraggableAnchor.cs pour le même principe ailleurs dans ce mod).
            var background = rowGo.GetComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.02f);

            var button = rowGo.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick = new Button.ButtonClickedEvent();

            var ringGo = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            ringGo.transform.SetParent(rowGo.transform, worldPositionStays: false);
            var ringRect = ringGo.GetComponent<RectTransform>();
            ringRect.anchorMin = new Vector2(0f, 0.5f);
            ringRect.anchorMax = new Vector2(0f, 0.5f);
            ringRect.pivot = new Vector2(0f, 0.5f);
            ringRect.anchoredPosition = new Vector2(2f, 0f);
            ringRect.sizeDelta = new Vector2(18f, 18f);
            var ringImage = ringGo.GetComponent<Image>();
            ringImage.sprite = GetRingSprite();
            ringImage.raycastTarget = false;

            var dotGo = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            dotGo.transform.SetParent(ringGo.transform, worldPositionStays: false);
            var dotRect = dotGo.GetComponent<RectTransform>();
            dotRect.anchorMin = Vector2.zero;
            dotRect.anchorMax = Vector2.one;
            dotRect.offsetMin = Vector2.zero;
            dotRect.offsetMax = Vector2.zero;
            var dotImage = dotGo.GetComponent<Image>();
            dotImage.sprite = GetDotSprite();
            dotImage.raycastTarget = false;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(rowGo.transform, worldPositionStays: false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.offsetMin = new Vector2(26f, 0f);
            labelRect.offsetMax = Vector2.zero;

            var text = labelGo.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
            }
            text.fontSize = 16f;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.color = LabelColor;
            text.raycastTarget = false;
            text.text = label;

            void Refresh()
            {
                dotGo.SetActive(getValue());
            }

            Refresh();
            button.onClick.AddListener(() =>
            {
                toggle();
                Refresh();
            });
        }

        private static Sprite GetRingSprite()
        {
            if (_ringSprite == null)
            {
                _ringSprite = IconSprites.CreateCircle(32, 0.72f, 1f, RingColor);
            }

            return _ringSprite;
        }

        private static Sprite GetDotSprite()
        {
            if (_dotSprite == null)
            {
                _dotSprite = IconSprites.CreateCircle(32, 0f, 0.5f, DotColor);
            }

            return _dotSprite;
        }

        // Bouton cloné du vrai bouton "Paramètres" (Menu.m_settingsButton, public) pour
        // garder le même style visuel -- repli sur un bouton fait main si jamais ce
        // champ venait à manquer. `onClick` est vidé puis reconstruit dans tous les
        // cas : voir MenuButton.Create pour l'explication (écouteurs persistants).
        private static Button CreateActionButton(Menu menu, Transform parent, string label, Vector2 anchoredPosition, Action onClick)
        {
            var source = menu.m_settingsButton;
            GameObject buttonGo = source != null
                ? UnityEngine.Object.Instantiate(source.gameObject, parent, worldPositionStays: false)
                : new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.name = "Button";
            buttonGo.SetActive(true);

            var rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(220f, 44f);

            var button = buttonGo.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            var text = buttonGo.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (text != null)
            {
                text.text = label;
            }

            return button;
        }
    }
}
