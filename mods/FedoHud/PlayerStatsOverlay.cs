using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FedoHud
{
    // Un seul bloc déplaçable regroupant trois indicateurs (morts, poids, armure),
    // chacun avec l'icône déjà utilisée ailleurs dans le jeu pour la même chose (pas de
    // texte "Deaths"/"Weight" -- voir CHANGELOG) :
    // - Morts : la même tête de mort que l'épingle de mort sur la carte
    //   (`Minimap.m_icons`, public, indexé par `PinType.Death`).
    // - Poids : la même icône que dans l'inventaire natif.
    // - Armure : la même icône que dans l'inventaire natif (`Humanoid.GetBodyArmor()`,
    //   public -- exactement la valeur affichée par `InventoryGui.UpdateCharacterStats`,
    //   décompilée).
    //
    // Ni `InventoryGui.m_weight` ni `m_armor` (tous deux publics) n'ont de champ dédié
    // pour leur icône -- elle vit juste à côté dans la hiérarchie de la scène (donnée
    // d'UI, pas de code). On la retrouve en cherchant, parmi les frères du texte, le
    // premier `Image` avec un sprite assigné -- se dégrade silencieusement (pas
    // d'icône, juste le nombre) si jamais la disposition change.
    //
    // Même patron que les autres blocs de ce mod : créé une fois dans Hud.Awake,
    // déplaçable à la souris (cliquer-glisser directement, voir DraggableAnchor.cs), position
    // sauvegardée en local -- UNE seule position pour les trois indicateurs, voir
    // FedoHudPlugin.ResetOverlayPositions.
    internal static class PlayerStatsOverlay
    {
        // Hauteur du bloc + une marge -- exposée pour que SkillsOverlay.cs puisse
        // démarrer sous ce bloc plutôt que directement sous la minimap (les deux
        // "juste sous la minimap" par défaut se chevauchaient, vécu en jeu).
        public const float HeightWithMargin = 64f;

        private const float IconSize = 22f;
        private const float Gap = 4f;
        private const float ColumnGap = 4f;

        // Largeurs différentes par colonne : la mort et l'armure tiennent sur 3
        // chiffres ("999"), le poids affiche deux nombres ("actuel/max", jusqu'à 4
        // chiffres chacun) donc a besoin de bien plus de place.
        private const float DeathColumnWidth = 42f;
        private const float WeightColumnWidth = 92f;
        private const float ArmorColumnWidth = 42f;

        private static readonly Color NormalColor = new Color(1f, 1f, 1f, 0.85f);
        private static readonly Color EncumberedColor = new Color(0.95f, 0.35f, 0.25f, 1f);

        private static GameObject _root;
        private static GameObject _deathColumn;
        private static GameObject _weightColumn;
        private static GameObject _armorColumn;
        private static TMP_Text _deathText;
        private static TMP_Text _weightText;
        private static TMP_Text _armorText;

        private static Sprite _skullSprite;
        private static Sprite _weightIconSprite;
        private static Sprite _armorIconSprite;
        private static bool _weightIconResolved;
        private static bool _armorIconResolved;

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
                    FedoHudPlugin.Log?.LogError($"FedoHud: player stats overlay creation failed: {e}");
                }
            }
        }

        private static void Create(Hud hud)
        {
            if (hud.m_rootObject == null)
            {
                return;
            }

            var existing = hud.m_rootObject.transform.Find("FedoHud_PlayerStats");
            if (existing != null)
            {
                _root = existing.gameObject;
                _deathColumn = existing.Find("Death")?.gameObject;
                _weightColumn = existing.Find("Weight")?.gameObject;
                _armorColumn = existing.Find("Armor")?.gameObject;
                _deathText = _deathColumn != null ? _deathColumn.transform.Find("Text")?.GetComponent<TMP_Text>() : null;
                _weightText = _weightColumn != null ? _weightColumn.transform.Find("Text")?.GetComponent<TMP_Text>() : null;
                _armorText = _armorColumn != null ? _armorColumn.transform.Find("Text")?.GetComponent<TMP_Text>() : null;
                return;
            }

            // Voir HudFont.cs -- `hud.m_foodTime[0].font` (utilisé avant) n'est pas
            // toujours prêt à ce moment précis (Hud.Awake), produisant un avertissement
            // "Font Asset was not found" au lancement même si le texte finissait par
            // s'afficher correctement.
            TMP_FontAsset font = HudFont.Resolve();

            var go = new GameObject("FedoHud_PlayerStats", typeof(RectTransform));
            go.transform.SetParent(hud.m_rootObject.transform, worldPositionStays: false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            // Pivot au centre (pas au coin droit comme les autres blocs) : ce bloc est
            // plus court que la minimap est large, on le veut centré SOUS elle plutôt
            // qu'aligné sur son bord droit -- voir
            // HudLayout.ResolveCenteredBelowMinimapPosition.
            rect.pivot = new Vector2(0.5f, 1f);
            // Tant que le joueur n'a jamais glissé ce bloc, calculé sous la minimap du
            // jeu plutôt qu'un nombre fixe (voir HudLayout, sa taille peut varier selon
            // les réglages du joueur).
            rect.anchoredPosition = HudLayout.ResolveCenteredBelowMinimapPosition(
                FedoHudPlugin.Instance.SavedPlayerStatsPosition,
                FedoHudPlugin.Instance.DefaultPlayerStatsPosition,
                hud.m_rootObject.GetComponent<RectTransform>());
            float totalWidth = DeathColumnWidth + ColumnGap + WeightColumnWidth + ColumnGap + ArmorColumnWidth;
            rect.sizeDelta = new Vector2(totalWidth, HeightWithMargin - 16f);

            float x = 0f;
            _deathColumn = CreateColumn(rect, "Death", GetSkullSprite(), font, x, DeathColumnWidth, out _deathText);
            x += DeathColumnWidth + ColumnGap;
            _weightColumn = CreateColumn(rect, "Weight", GetWeightIconSprite(), font, x, WeightColumnWidth, out _weightText);
            x += WeightColumnWidth + ColumnGap;
            _armorColumn = CreateColumn(rect, "Armor", GetArmorIconSprite(), font, x, ArmorColumnWidth, out _armorText);

            go.AddComponent<DraggableAnchor>().OnDragEnd = pos => FedoHudPlugin.Instance?.SavePlayerStatsPosition(pos);

            _root = go;
        }

        private static GameObject CreateColumn(RectTransform parent, string name, Sprite icon, TMP_FontAsset font, float x, float width, out TMP_Text text)
        {
            const float textRowHeight = 20f;

            var columnGo = new GameObject(name, typeof(RectTransform));
            columnGo.transform.SetParent(parent, worldPositionStays: false);
            var columnRect = columnGo.GetComponent<RectTransform>();
            columnRect.anchorMin = new Vector2(0f, 1f);
            columnRect.anchorMax = new Vector2(0f, 1f);
            columnRect.pivot = new Vector2(0.5f, 1f);
            columnRect.anchoredPosition = new Vector2(x + width / 2f, 0f);
            columnRect.sizeDelta = new Vector2(width, IconSize + Gap + textRowHeight);

            float textOffsetY = 0f;
            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(columnGo.transform, worldPositionStays: false);
                var iconRect = iconGo.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0.5f, 1f);
                iconRect.anchorMax = new Vector2(0.5f, 1f);
                iconRect.pivot = new Vector2(0.5f, 1f);
                iconRect.anchoredPosition = Vector2.zero;
                iconRect.sizeDelta = new Vector2(IconSize, IconSize);
                var iconImage = iconGo.GetComponent<Image>();
                iconImage.sprite = icon;
                iconImage.raycastTarget = false;
                textOffsetY = IconSize + Gap;
            }

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(columnGo.transform, worldPositionStays: false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = new Vector2(0f, -textOffsetY);
            textRect.sizeDelta = new Vector2(0f, textRowHeight);

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                tmp.font = font;
            }
            tmp.fontSize = 13f;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.alignment = TextAlignmentOptions.Top;
            tmp.color = NormalColor;
            tmp.raycastTarget = false;
            tmp.text = "";

            text = tmp;
            return columnGo;
        }

        private static Sprite GetSkullSprite()
        {
            if (_skullSprite != null)
            {
                return _skullSprite;
            }

            var minimap = Minimap.instance;
            if (minimap == null || minimap.m_icons == null)
            {
                return null;
            }

            foreach (var sprite in minimap.m_icons)
            {
                if (sprite.m_name == Minimap.PinType.Death)
                {
                    _skullSprite = sprite.m_icon;
                    break;
                }
            }

            return _skullSprite;
        }

        private static Sprite GetWeightIconSprite()
        {
            if (_weightIconResolved)
            {
                return _weightIconSprite;
            }

            _weightIconResolved = true;
            _weightIconSprite = FindSiblingIconSprite(InventoryGui.instance != null ? InventoryGui.instance.m_weight : null);
            return _weightIconSprite;
        }

        private static Sprite GetArmorIconSprite()
        {
            if (_armorIconResolved)
            {
                return _armorIconSprite;
            }

            _armorIconResolved = true;
            _armorIconSprite = FindSiblingIconSprite(InventoryGui.instance != null ? InventoryGui.instance.m_armor : null);
            return _armorIconSprite;
        }

        // Cherche un frère nommé explicitement "...icon..." en priorité (ex.
        // "weight_icon"/"armor_icon", confirmé en jeu par un scan de la hiérarchie) --
        // un autre frère (ex. "bkg", le cadre en bois derrière le texte) a aussi une
        // Image avec un sprite assigné, donc prendre juste le premier trouvé ramassait
        // le mauvais. Repli sur le premier Image venu si jamais aucun nom ne contient
        // "icon" (disposition différente d'une version du jeu à l'autre).
        private static Sprite FindSiblingIconSprite(TMP_Text label)
        {
            var parent = label != null ? label.transform.parent : null;
            if (parent == null)
            {
                return null;
            }

            Sprite fallback = null;
            foreach (Transform child in parent)
            {
                if (child == label.transform)
                {
                    continue;
                }

                var image = child.GetComponent<Image>();
                if (image == null || image.sprite == null)
                {
                    continue;
                }

                if (child.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return image.sprite;
                }

                if (fallback == null)
                {
                    fallback = image.sprite;
                }
            }

            return fallback;
        }

        // Throttlé à ~1x/seconde par FedoHudPlugin -- aucun de ces trois indicateurs ne
        // change assez vite pour justifier un rafraîchissement par frame.
        public static void Refresh(bool showDeath, bool showWeight, bool showArmor)
        {
            if (_root == null)
            {
                return;
            }

            bool anyVisible = showDeath || showWeight || showArmor;
            _root.SetActive(anyVisible);
            if (!anyVisible)
            {
                return;
            }

            _deathColumn?.SetActive(showDeath);
            _weightColumn?.SetActive(showWeight);
            _armorColumn?.SetActive(showArmor);

            if (showDeath && _deathText != null)
            {
                RefreshDeath();
            }

            if (showWeight && _weightText != null)
            {
                RefreshWeight();
            }

            if (showArmor && _armorText != null)
            {
                RefreshArmor();
            }
        }

        private static void RefreshDeath()
        {
            var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
            _deathText.text = profile != null ? ((int)profile.GetStat(PlayerStatType.Deaths)).ToString() : "";
        }

        private static void RefreshWeight()
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                _weightText.text = "";
                return;
            }

            var inventory = player.GetInventory();
            if (inventory == null)
            {
                _weightText.text = "";
                return;
            }

            float current = inventory.GetTotalWeight();
            float max = player.GetMaxCarryWeight();
            _weightText.text = $"{current:0.#}/{max:0.#}";
            _weightText.color = player.IsEncumbered() ? EncumberedColor : NormalColor;
        }

        private static void RefreshArmor()
        {
            var player = Player.m_localPlayer;
            _armorText.text = player != null ? player.GetBodyArmor().ToString("0.#") : "";
        }

        // Appelé par le bouton "Reset positions" du panneau -- voir
        // FedoHudPlugin.ResetOverlayPositions. Recalcule le Y sous la minimap (voir
        // Create/HudLayout) plutôt que de reprendre un nombre fixe, et sauvegarde le
        // résultat pour que ça reste stable au prochain Hud.Awake.
        public static void ResetPosition()
        {
            if (Hud.instance == null)
            {
                return;
            }

            var pos = HudLayout.ResolveCenteredBelowMinimapPosition(
                FedoHudPlugin.Instance.DefaultPlayerStatsPosition,
                FedoHudPlugin.Instance.DefaultPlayerStatsPosition,
                Hud.instance.m_rootObject.GetComponent<RectTransform>());

            if (_root != null)
            {
                _root.GetComponent<RectTransform>().anchoredPosition = pos;
            }

            FedoHudPlugin.Instance.SavePlayerStatsPosition(pos);
        }
    }
}
