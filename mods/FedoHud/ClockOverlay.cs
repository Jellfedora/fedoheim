using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FedoHud
{
    // Horloge affichée en haut au centre de l'écran, calquée sur le cycle jour/nuit du
    // jeu -- purement locale (voir FedoHudPlugin.GetCurrentGameTime pour le calcul,
    // dérivé indépendamment de EnvMan.GetDayFraction() ; FedoServerTools calcule la même
    // chose de son côté pour son propre rapport à l'API/launcher, mais les deux mods ne
    // partagent pas ce calcul -- deux installations distinctes). Tourne sur toute
    // installation avec un joueur local (client comme hôte d'une partie solo/hébergée),
    // aucun appel réseau. Le texte lui-même est réutilisé/repositionné plutôt que recréé
    // à chaque rafraîchissement (voir FedoHudPlugin.RefreshClockOverlay).
    // Déplaçable à la souris (Maj + glisser, voir DragHandler ci-dessous), position
    // sauvegardée en local.
    internal static class ClockOverlay
    {
        private static TMP_Text _text;

        // Hud.Awake tourne une fois par instanciation du HUD (début de partie/rechargement
        // de scène) -- c'est là que le jeu assigne déjà ses propres éléments d'UI
        // (m_rootObject, m_foodTime...), donc le point d'accroche le plus fiable pour y
        // greffer un élément custom sans dépendre d'un ordre d'exécution particulier.
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
                    FedoHudPlugin.Log?.LogError($"FedoHud: clock overlay creation failed: {e}");
                }
            }
        }

        private static void Create(Hud hud)
        {
            if (hud.m_rootObject == null)
            {
                return;
            }

            // Un rechargement de scène recrée le Hud (donc rappelle Awake) -- si un enfant
            // du même nom existe déjà sous ce m_rootObject, c'est un résidu d'une instance
            // précédente déjà nettoyée par Unity avec elle ; sinon on le recrée simplement.
            var existing = hud.m_rootObject.transform.Find("FedoHud_Clock");
            if (existing != null)
            {
                _text = existing.GetComponent<TMP_Text>();
                return;
            }

            var go = new GameObject("FedoHud_Clock", typeof(RectTransform));
            go.transform.SetParent(hud.m_rootObject.transform, worldPositionStays: false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            // Reprend la position sauvegardée par un éventuel glisser d'une session
            // précédente (voir FedoHudPlugin.SavedClockPosition) -- par défaut, celle du
            // .cfg généré au tout premier lancement (0, -18).
            rect.anchoredPosition = FedoHudPlugin.Instance.SavedClockPosition;
            rect.sizeDelta = new Vector2(220f, 40f);

            var text = go.AddComponent<TextMeshProUGUI>();
            // Réutilise la police déjà chargée par un élément du HUD existant (la jauge de
            // faim) plutôt que de fournir/référencer un asset de police séparé, jamais
            // garanti d'être initialisé au bon moment sinon.
            if (hud.m_foodTime != null && hud.m_foodTime.Length > 0 && hud.m_foodTime[0] != null)
            {
                text.font = hud.m_foodTime[0].font;
            }
            text.fontSize = 24f;
            text.alignment = TextAlignmentOptions.Top;
            text.color = new Color(1f, 1f, 1f, 0.85f);
            // Faux en temps normal (ne doit jamais intercepter un clic de gameplay à cet
            // endroit de l'écran) -- DragHandler ci-dessous ne le repasse à vrai que
            // pendant que Maj est maintenu, la fenêtre où un déplacement est possible.
            text.raycastTarget = false;
            text.text = "";

            go.AddComponent<DragHandler>();

            _text = text;
        }

        // Glisser-déposer réservé à Maj+clic : le reste du temps, l'horloge reste
        // "traversable" par les clics de gameplay normaux à cet endroit de l'écran (voir
        // raycastTarget dans Create ci-dessus). Position finale écrite dans le .cfg local
        // via FedoHudPlugin.SaveClockPosition -- jamais envoyée au serveur/à l'API, c'est
        // une préférence purement locale à cette installation.
        private class DragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            private RectTransform _rect;
            private Graphic _graphic;
            private bool _dragging;
            private Vector2 _dragStartAnchoredPos;
            private Vector2 _dragStartPointerPos;

            private void Awake()
            {
                _rect = GetComponent<RectTransform>();
                _graphic = GetComponent<Graphic>();
            }

            private void Update()
            {
                if (_graphic != null)
                {
                    _graphic.raycastTarget = Input.GetKey(KeyCode.LeftShift);
                }
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                _dragging = Input.GetKey(KeyCode.LeftShift);
                if (!_dragging)
                {
                    return;
                }

                _dragStartAnchoredPos = _rect.anchoredPosition;
                _dragStartPointerPos = eventData.position;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (!_dragging)
                {
                    return;
                }

                _rect.anchoredPosition = _dragStartAnchoredPos + (eventData.position - _dragStartPointerPos);
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (!_dragging)
                {
                    return;
                }

                _dragging = false;
                FedoHudPlugin.Instance?.SaveClockPosition(_rect.anchoredPosition);
            }
        }

        public static void SetText(string value)
        {
            if (_text != null)
            {
                _text.text = value ?? "";
            }
        }

        public static void SetVisible(bool visible)
        {
            if (_text != null)
            {
                _text.gameObject.SetActive(visible);
            }
        }
    }
}
