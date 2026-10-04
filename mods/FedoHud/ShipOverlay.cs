using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FedoHud
{
    // Petit bloc affichant l'état de la coque (pourcentage de vie, via le même
    // `WearNTear` que n'importe quelle pièce endommageable) et la vitesse réelle (m/s,
    // pas juste le réglage Stop/Lente/Moitié/Pleine/Marche arrière déjà visible via
    // l'icône vanilla du jeu) du bateau piloté par le joueur local -- rien de tout ça
    // n'est montré numériquement par le HUD natif. `Player.GetControlledShip()` (public)
    // renvoie le bateau seulement pendant que ce joueur tient effectivement le
    // gouvernail (pas juste "être à bord") -- c'est ce qui pilote la visibilité de ce
    // bloc, contrairement aux autres blocs de ce mod qui ne dépendent que d'un réglage.
    // Signatures vérifiées par décompilation contre assembly_valheim.dll (1.0), rien
    // deviné.
    internal static class ShipOverlay
    {
        private static GameObject _root;
        private static TMP_Text _hullText;
        private static TMP_Text _speedText;

        private static readonly Color NormalColor = new Color(1f, 1f, 1f, 0.85f);
        private static readonly Color DamagedColor = new Color(0.95f, 0.65f, 0.25f, 1f);
        private static readonly Color CriticalColor = new Color(0.95f, 0.35f, 0.25f, 1f);

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
                    FedoHudPlugin.Log?.LogError($"FedoHud: ship overlay creation failed: {e}");
                }
            }
        }

        private static void Create(Hud hud)
        {
            if (hud.m_rootObject == null)
            {
                return;
            }

            var existing = hud.m_rootObject.transform.Find("FedoHud_Ship");
            if (existing != null)
            {
                _root = existing.gameObject;
                _hullText = existing.Find("Hull")?.GetComponent<TMP_Text>();
                _speedText = existing.Find("Speed")?.GetComponent<TMP_Text>();
                return;
            }

            TMP_FontAsset font = HudFont.Resolve();

            var go = new GameObject("FedoHud_Ship", typeof(RectTransform));
            go.transform.SetParent(hud.m_rootObject.transform, worldPositionStays: false);

            var rect = go.GetComponent<RectTransform>();
            // Coin haut-gauche par défaut -- le coin haut-droit est déjà occupé par la
            // minimap/les autres blocs de ce mod (voir HudLayout), et ce bloc-ci n'a de
            // toute façon pas besoin d'être calé sous elle : contextuel, jamais affiché
            // en même temps que le joueur consulte activement la minimap au corps à
            // corps.
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = FedoHudPlugin.Instance.SavedShipPosition;
            rect.sizeDelta = new Vector2(140f, 52f);

            var background = go.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.5f);
            background.raycastTarget = false;

            _hullText = CreateLine(rect, "Hull", font, -8f);
            _speedText = CreateLine(rect, "Speed", font, -28f);

            go.AddComponent<DraggableAnchor>().OnDragEnd = pos => FedoHudPlugin.Instance?.SaveShipPosition(pos);

            _root = go;
        }

        private static TMP_Text CreateLine(Transform parent, string name, TMP_FontAsset font, float y)
        {
            var lineGo = new GameObject(name, typeof(RectTransform));
            lineGo.transform.SetParent(parent, worldPositionStays: false);
            var rect = lineGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(8f, y);
            rect.sizeDelta = new Vector2(-16f, 18f);

            var text = lineGo.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
            }
            text.fontSize = 14f;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.color = NormalColor;
            text.raycastTarget = false;
            text.text = "";
            return text;
        }

        // Throttlé à ~1x/seconde par FedoHudPlugin (comme les autres blocs de ce mod) --
        // seule la visibilité (apparition/disparition en montant/quittant le
        // gouvernail) reste vérifiée à chaque frame, voir FedoHudPlugin.
        public static void Refresh()
        {
            if (_root == null || !_root.activeSelf)
            {
                return;
            }

            var ship = Player.m_localPlayer != null ? Player.m_localPlayer.GetControlledShip() : null;
            if (ship == null)
            {
                return;
            }

            RefreshHull(ship);
            RefreshSpeed(ship);
        }

        private static void RefreshHull(Ship ship)
        {
            if (_hullText == null)
            {
                return;
            }

            var wearNTear = ship.GetComponent<WearNTear>();
            if (wearNTear == null)
            {
                _hullText.text = "";
                return;
            }

            float percentage = wearNTear.GetHealthPercentage() * 100f;
            _hullText.text = $"{FedoHudPlugin.Instance.ShipHullPrefix} {percentage:0}%";
            _hullText.color = percentage <= 25f ? CriticalColor : (percentage <= 50f ? DamagedColor : NormalColor);
        }

        private static void RefreshSpeed(Ship ship)
        {
            if (_speedText == null)
            {
                return;
            }

            // `Ship.GetSpeed()` (publique) : composante avant de la vélocité réelle du
            // corps rigide, en unités Unity/seconde (= mètres/seconde dans ce jeu) --
            // distincte du réglage Stop/Lente/Moitié/Pleine/Marche arrière (déjà visible
            // via l'icône vanilla), négative en marche arrière.
            float speed = ship.GetSpeed();
            _speedText.text = $"{FedoHudPlugin.Instance.ShipSpeedPrefix} {speed:0.0} m/s";
        }

        // Visibilité contrôlée par FedoHudPlugin (réglage ET état "au gouvernail"), pas
        // seulement par le réglage comme les autres blocs -- voir RefreshShipOverlay.
        public static void SetVisible(bool visible)
        {
            _root?.SetActive(visible);
        }

        public static void ResetPosition()
        {
            var defaultPos = FedoHudPlugin.Instance.DefaultShipPosition;
            if (_root != null)
            {
                _root.GetComponent<RectTransform>().anchoredPosition = defaultPos;
            }

            FedoHudPlugin.Instance.SaveShipPosition(defaultPos);
        }
    }
}
