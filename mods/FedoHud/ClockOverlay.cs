using System;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace FedoHud
{
    // Horloge affichée en haut au centre de l'écran, calquée sur le cycle jour/nuit du
    // jeu -- purement locale (voir FedoHudPlugin.GetCurrentGameTime pour le calcul,
    // dérivé indépendamment de EnvMan.GetDayFraction()). Tourne sur toute
    // installation avec un joueur local (client comme hôte d'une partie solo/hébergée),
    // aucun appel réseau. Le texte lui-même est réutilisé/repositionné plutôt que recréé
    // à chaque rafraîchissement (voir FedoHudPlugin.RefreshClockOverlay).
    // Déplaçable à la souris (cliquer-glisser directement, voir DraggableAnchor.cs, partagé avec
    // SkillsOverlay.cs), position sauvegardée en local.
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
            // Voir HudFont.cs -- `hud.m_foodTime[0].font` (utilisé avant) n'est pas
            // toujours prêt à ce moment précis (Hud.Awake), produisant un avertissement
            // "Font Asset was not found" au lancement même si le texte finissait par
            // s'afficher correctement.
            var font = HudFont.Resolve();
            if (font != null)
            {
                text.font = font;
            }
            text.fontSize = 24f;
            text.alignment = TextAlignmentOptions.Top;
            text.color = new Color(1f, 1f, 1f, 0.85f);
            // Repassé à vrai en permanence par DraggableAnchor.cs juste en dessous (pour
            // détecter le survol -- voir ce fichier pour le compromis que ça implique).
            text.raycastTarget = false;
            text.text = "";

            go.AddComponent<DraggableAnchor>().OnDragEnd = pos => FedoHudPlugin.Instance?.SaveClockPosition(pos);

            _text = text;
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

        // Appelé par le bouton "Reset positions" du panneau -- déplace l'horloge déjà à
        // l'écran immédiatement, sans attendre un rechargement de Hud (voir
        // FedoHudPlugin.ResetOverlayPositions).
        public static void ResetPosition(Vector2 anchoredPosition)
        {
            if (_text != null)
            {
                _text.rectTransform.anchoredPosition = anchoredPosition;
            }
        }
    }
}
