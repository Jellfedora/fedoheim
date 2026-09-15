using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FedoSignColor
{
    // Picker de couleur (carré saturation/luminosité + bande de teinte + champ hexa +
    // aperçu) affiché à côté de la fenêtre de saisie de texte d'un panneau, plus une
    // rangée de pastilles rapides pour les couleurs les plus utilisées.
    //
    // `Sign.Interact()` (décompilé) appelle `TextInput.instance.RequestText(this,
    // "$piece_sign_input", m_characterLimit)` -- `TextInput` est un système générique de
    // saisie de texte réutilisé ailleurs dans le jeu (renommer un objet, un point de
    // carte...), donc on ne montre le picker que quand `topic` correspond exactement à
    // celui des panneaux, jamais pour les autres usages.
    //
    // `TextInput.m_inputField` (public, type `GUIFramework.GuiInputField`, qui hérite de
    // `TMP_InputField` -- vérifié par réflexion) expose directement `.text`, aucune
    // réflexion nécessaire pour le lire/l'écrire.
    internal static class SignColorPicker
    {
        private const string SignTopic = "$piece_sign_input";
        private const string PaletteName = "FedoSignColor_Picker";
        private const int SvSize = 140;
        private const float HueWidth = 22f;
        private const float Gap = 8f;

        private static GameObject _root;
        private static TextInput _activeTextInput;

        private static float _hue = 0f;
        private static float _saturation = 1f;
        private static float _value = 1f;
        private static bool _updatingFromHex;

        private static RawImage _svImage;
        private static RectTransform _svHandle;
        private static RectTransform _hueHandle;
        private static Image _preview;
        private static TMP_InputField _hexField;

        [HarmonyPatch(typeof(TextInput), nameof(TextInput.RequestText))]
        private static class RequestTextPatch
        {
            private static void Postfix(TextInput __instance, string topic)
            {
                try
                {
                    DestroyPicker();

                    if (FedoSignColorPlugin.Instance == null || !FedoSignColorPlugin.Instance.EnableColorPicker)
                    {
                        return;
                    }

                    if (topic != SignTopic || __instance.m_panel == null)
                    {
                        return;
                    }

                    CreatePicker(__instance);
                }
                catch (Exception e)
                {
                    FedoSignColorPlugin.Log?.LogWarning($"FedoSignColor: showing the color picker failed: {e.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(TextInput), nameof(TextInput.Hide))]
        private static class HidePatch
        {
            private static void Postfix()
            {
                DestroyPicker();
            }
        }

        private static void CreatePicker(TextInput textInput)
        {
            var palette = FedoSignColorPlugin.Instance.ParsedPalette;

            _activeTextInput = textInput;
            InitializeColorFromText(textInput.m_inputField.text);

            // `m_panel` (vérifié en jeu) est bien plus large/grand que la boîte en bois
            // visible à l'écran -- s'ancrer directement dessus plaçait le picker hors de
            // la boîte. Le champ de texte, lui, EST visuellement dans cette boîte : son
            // parent direct y correspond, donc on s'ancre dessus à la place.
            var dialogRect = textInput.m_inputField.transform.parent as RectTransform;
            var parentRect = dialogRect != null ? dialogRect : textInput.m_panel.GetComponent<RectTransform>();

            var root = new GameObject(PaletteName, typeof(RectTransform));
            root.transform.SetParent(parentRect, worldPositionStays: false);

            var rootRect = root.GetComponent<RectTransform>();
            // Centré sous la boîte de dialogue (titre/champ/bouton Annuler restent tous
            // au-dessus) plutôt que dans un coin -- pour ne recouvrir aucun élément
            // existant de la boîte, quelle que soit sa largeur réelle. Position
            // approximative, pas encore ajustée visuellement en jeu au-delà de ce
            // premier retour ; à retoucher ici si besoin.
            rootRect.anchorMin = new Vector2(0.5f, 0f);
            rootRect.anchorMax = new Vector2(0.5f, 0f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, -12f);
            float width = SvSize + Gap + HueWidth;
            rootRect.sizeDelta = new Vector2(width, 260f);

            float y = 0f;

            // Rangée de pastilles rapides (voir FedoSignColorPlugin.ParsedPalette).
            if (palette.Count > 0)
            {
                const float swatchSize = 20f;
                const float swatchGap = 4f;
                for (int i = 0; i < palette.Count; i++)
                {
                    CreateSwatch(rootRect, palette[i], i * (swatchSize + swatchGap), y, swatchSize);
                }

                y -= swatchSize + 10f;
            }

            // Carré saturation/luminosité, à gauche.
            var svGo = new GameObject("SVSquare", typeof(RectTransform), typeof(RawImage));
            svGo.transform.SetParent(rootRect, worldPositionStays: false);
            var svRect = svGo.GetComponent<RectTransform>();
            svRect.anchorMin = new Vector2(0f, 1f);
            svRect.anchorMax = new Vector2(0f, 1f);
            svRect.pivot = new Vector2(0f, 1f);
            svRect.anchoredPosition = new Vector2(0f, y);
            svRect.sizeDelta = new Vector2(SvSize, SvSize);
            _svImage = svGo.GetComponent<RawImage>();
            _svImage.texture = ColorWheelUtil.CreateSaturationValueTexture(_hue, SvSize);
            svGo.AddComponent<NormalizedDragArea>().OnChanged = uv =>
            {
                _saturation = uv.x;
                _value = uv.y;
                PositionHandle(_svHandle, uv.x * SvSize, uv.y * SvSize);
                RefreshPreview();
            };

            _svHandle = CreateHandle(svRect, "SVHandle", new Vector2(SvSize, SvSize));
            PositionHandle(_svHandle, _saturation * SvSize, _value * SvSize);

            // Bande de teinte, à droite du carré.
            var hueGo = new GameObject("HueSlider", typeof(RectTransform), typeof(RawImage));
            hueGo.transform.SetParent(rootRect, worldPositionStays: false);
            var hueRect = hueGo.GetComponent<RectTransform>();
            hueRect.anchorMin = new Vector2(0f, 1f);
            hueRect.anchorMax = new Vector2(0f, 1f);
            hueRect.pivot = new Vector2(0f, 1f);
            hueRect.anchoredPosition = new Vector2(SvSize + Gap, y);
            hueRect.sizeDelta = new Vector2(HueWidth, SvSize);
            hueGo.GetComponent<RawImage>().texture = ColorWheelUtil.CreateHueTexture(1, SvSize);
            hueGo.AddComponent<NormalizedDragArea>().OnChanged = uv =>
            {
                _hue = 1f - uv.y;
                _svImage.texture = ColorWheelUtil.CreateSaturationValueTexture(_hue, SvSize);
                PositionHandle(_hueHandle, HueWidth / 2f, uv.y * SvSize);
                RefreshPreview();
            };

            _hueHandle = CreateHandle(hueRect, "HueHandle", new Vector2(HueWidth, SvSize));
            PositionHandle(_hueHandle, HueWidth / 2f, (1f - _hue) * SvSize);

            y -= SvSize + 10f;

            // Aperçu + champ hexa, sous le carré/la bande.
            var previewGo = new GameObject("Preview", typeof(RectTransform), typeof(Image));
            previewGo.transform.SetParent(rootRect, worldPositionStays: false);
            var previewRect = previewGo.GetComponent<RectTransform>();
            previewRect.anchorMin = new Vector2(0f, 1f);
            previewRect.anchorMax = new Vector2(0f, 1f);
            previewRect.pivot = new Vector2(0f, 1f);
            previewRect.anchoredPosition = new Vector2(0f, y);
            previewRect.sizeDelta = new Vector2(28f, 28f);
            _preview = previewGo.GetComponent<Image>();

            var hexGo = new GameObject("HexField", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            hexGo.transform.SetParent(rootRect, worldPositionStays: false);
            var hexRect = hexGo.GetComponent<RectTransform>();
            hexRect.anchorMin = new Vector2(0f, 1f);
            hexRect.anchorMax = new Vector2(0f, 1f);
            hexRect.pivot = new Vector2(0f, 1f);
            hexRect.anchoredPosition = new Vector2(36f, y);
            hexRect.sizeDelta = new Vector2(width - 36f, 28f);
            hexGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var hexTextGo = new GameObject("Text", typeof(RectTransform));
            hexTextGo.transform.SetParent(hexGo.transform, worldPositionStays: false);
            var hexTextRect = hexTextGo.GetComponent<RectTransform>();
            hexTextRect.anchorMin = Vector2.zero;
            hexTextRect.anchorMax = Vector2.one;
            hexTextRect.offsetMin = new Vector2(6f, 2f);
            hexTextRect.offsetMax = new Vector2(-6f, -2f);
            var hexText = hexTextGo.AddComponent<TextMeshProUGUI>();
            hexText.fontSize = 16f;
            hexText.color = Color.white;
            hexText.alignment = TextAlignmentOptions.MidlineLeft;

            _hexField = hexGo.GetComponent<TMP_InputField>();
            _hexField.textComponent = hexText;
            _hexField.characterLimit = 6;
            _hexField.text = ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(_hue, _saturation, _value));
            _hexField.onEndEdit.AddListener(OnHexEdited);
            // Contre un curseur/une sélection invisibles sur le fond sombre du champ --
            // le joueur doit pouvoir voir ce qu'il sélectionne pour le copier.
            _hexField.caretColor = Color.white;
            _hexField.selectionColor = new Color(1f, 1f, 1f, 0.4f);

            y -= 28f;

            rootRect.sizeDelta = new Vector2(width, -y + 20f);

            _root = root;
            RefreshPreview();
        }

        private static RectTransform CreateHandle(RectTransform parent, string name, Vector2 areaSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, worldPositionStays: false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(10f, 10f);

            var image = go.GetComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = false;

            return rect;
        }

        private static void PositionHandle(RectTransform handle, float x, float y)
        {
            if (handle != null)
            {
                handle.anchoredPosition = new Vector2(x, y);
            }
        }

        private static void CreateSwatch(RectTransform parent, Color color, float x, float y, float size)
        {
            var go = new GameObject("Swatch", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, worldPositionStays: false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(size, size);

            var image = go.GetComponent<Image>();
            image.color = color;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                Color.RGBToHSV(color, out _hue, out _saturation, out _value);
                _svImage.texture = ColorWheelUtil.CreateSaturationValueTexture(_hue, SvSize);
                PositionHandle(_svHandle, _saturation * SvSize, _value * SvSize);
                PositionHandle(_hueHandle, HueWidth / 2f, (1f - _hue) * SvSize);
                RefreshPreview();
            });
        }

        private static void OnHexEdited(string value)
        {
            if (!ColorUtility.TryParseHtmlString("#" + value.TrimStart('#'), out var color))
            {
                return;
            }

            _updatingFromHex = true;
            Color.RGBToHSV(color, out _hue, out _saturation, out _value);
            _svImage.texture = ColorWheelUtil.CreateSaturationValueTexture(_hue, SvSize);
            PositionHandle(_svHandle, _saturation * SvSize, _value * SvSize);
            PositionHandle(_hueHandle, HueWidth / 2f, (1f - _hue) * SvSize);
            RefreshPreview();
            _updatingFromHex = false;
        }

        // Appelé à chaque interaction avec le picker (pastille, glissement du carré/de
        // la bande, validation du champ hexa) -- pas de bouton "Appliquer" séparé,
        // chaque changement de couleur se répercute directement sur le champ de texte
        // du panneau.
        private static void RefreshPreview()
        {
            var color = Color.HSVToRGB(_hue, _saturation, _value);
            if (_preview != null)
            {
                _preview.color = color;
            }

            if (_hexField != null && !_updatingFromHex)
            {
                _hexField.SetTextWithoutNotify(ColorUtility.ToHtmlStringRGB(color));
            }

            ApplyCurrentColor(color);
        }

        private static void ApplyCurrentColor(Color color)
        {
            if (_activeTextInput == null)
            {
                return;
            }

            try
            {
                var field = _activeTextInput.m_inputField;
                string current = field.text ?? "";
                string inner = StripLeadingColorTag(current);
                string hex = ColorUtility.ToHtmlStringRGB(color);
                // Pas de balise fermante : `<color=...>` seul colore tout ce qui suit
                // jusqu'à la fin du texte, exactement ce que veut ce mod -- inutile et
                // pas demandé par l'utilisateur.
                string newText = $"<color=#{hex}>{inner}";
                if (newText == current)
                {
                    return;
                }

                // Interagir avec le picker (carré/bande/pastille) retire le focus du
                // champ de texte -- écrire `.text` seul laissait l'affichage vide/périmé
                // (vécu en jeu) tant que rien ne force le champ à se redessiner.
                // `ActivateInputField()` (la même méthode que `TextInput.Show()` appelle
                // à l'ouverture, publique) reprend le focus et force ce rafraîchissement
                // au passage.
                field.text = newText;
                field.caretPosition = newText.Length;
                field.ForceLabelUpdate();
                field.ActivateInputField();
            }
            catch (Exception e)
            {
                FedoSignColorPlugin.Log?.LogWarning($"FedoSignColor: applying a color failed: {e.Message}");
            }
        }

        // Retire une balise <color=...> déjà présente EN TÊTE du texte (ex. si le joueur
        // choisit une nouvelle couleur après une autre) -- pour ne jamais en empiler
        // plusieurs. Pas de balise fermante à chercher : ce mod n'en écrit jamais (voir
        // ApplyCurrentColor). Ignore tout ce qui n'a pas exactement cette forme (ex. une
        // balise tapée à la main plus loin dans le texte) plutôt que de risquer de
        // casser du texte que le joueur a lui-même mis en forme.
        private static string StripLeadingColorTag(string text)
        {
            if (string.IsNullOrEmpty(text) || !text.StartsWith("<color=", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }

            int closeBracket = text.IndexOf('>');
            return closeBracket < 0 ? text : text.Substring(closeBracket + 1);
        }

        // Lit une éventuelle balise <color=...> déjà en tête du texte pour que le picker
        // reparte de la couleur déjà appliquée à ce panneau, plutôt que de toujours
        // revenir au rouge par défaut -- gênant pour retoucher un panneau déjà coloré.
        // `ColorUtility.TryParseHtmlString` accepte aussi bien un code hexa ("#ff8800")
        // qu'un nom de couleur standard ("white"), donc ça marche même pour un panneau
        // coloré à la main avant d'installer ce mod.
        private static void InitializeColorFromText(string text)
        {
            _hue = 0f;
            _saturation = 1f;
            _value = 1f;

            if (string.IsNullOrEmpty(text) || !text.StartsWith("<color=", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            int closeBracket = text.IndexOf('>');
            if (closeBracket < 0)
            {
                return;
            }

            string tagValue = text.Substring("<color=".Length, closeBracket - "<color=".Length);
            string htmlColor = tagValue.StartsWith("#") ? tagValue : "#" + tagValue;
            if (ColorUtility.TryParseHtmlString(htmlColor, out var color) || ColorUtility.TryParseHtmlString(tagValue, out color))
            {
                Color.RGBToHSV(color, out _hue, out _saturation, out _value);
            }
        }

        private static void DestroyPicker()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }

            _activeTextInput = null;
            _svImage = null;
            _svHandle = null;
            _hueHandle = null;
            _preview = null;
            _hexField = null;
        }
    }
}
