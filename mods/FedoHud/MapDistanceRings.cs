using System;
using System.Collections.Generic;
using TMPro;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FedoHud
{
    // Dessine des anneaux concentriques sur la mini-carte ET la grande carte (M), centrés
    // sur le vrai centre du monde (coordonnées (0,0)) -- utile pour juger de sa distance
    // au centre, plusieurs biomes (océan, Mistlands, Ashlands...) démarrant à une distance
    // fixe de ce point. Contrairement aux autres blocs de ce mod, rien n'est déplaçable ni
    // sauvegardé : la position est toujours recalculée depuis `Minimap`, jamais un choix du
    // joueur.
    //
    // Conversion monde -> écran reproduite depuis `Minimap.WorldToMapPoint`/
    // `MapPointToLocalGuiPos` (privées) : signatures et corps vérifiés par décompilation
    // contre assembly_valheim.dll (1.0), rien deviné. Le point clé : (0,0,0) en coordonnées
    // monde tombe TOUJOURS exactement au centre (0.5, 0.5) de la texture de carte (le calcul
    // `p.x / m_pixelSize + m_textureSize/2`, une fois divisé par `m_textureSize`, s'annule
    // pour p.x = 0) -- indépendant du zoom/de la taille de texture, donc jamais besoin de
    // recalculer cette constante.
    //
    // Les vrais pins du jeu (`Minimap.UpdatePins`) suivent exactement le même calcul pour se
    // positionner en enfant de `m_pinRootSmall`/`m_pinRootLarge` -- nos anneaux répliquent la
    // même convention (ancre/pivot identiques) pour tomber au bon endroit sans dépendre d'un
    // repère caché. Contrairement aux vrais pins, le jeu ne masque pas visuellement les
    // enfants de ce parent quand ils sortent de la zone visible (il les DÉTRUIT quand
    // `IsPointVisible` devient faux, plutôt que de compter sur un clip) -- un
    // `RectMask2D` ajouté sur un conteneur dédié (taille exacte de la carte visible) rend le
    // même service pour nos anneaux sans avoir à les recréer/détruire à chaque frame.
    internal static class MapDistanceRings
    {
        private class RingSet
        {
            public RectTransform MaskContainer;
            public readonly List<(RectTransform Rect, TMP_Text Label)> Rings = new List<(RectTransform, TMP_Text)>();
        }

        private static RingSet _small;
        private static RingSet _large;

        [HarmonyPatch(typeof(Minimap), "Awake")]
        private static class MinimapAwakePatch
        {
            private static void Postfix(Minimap __instance)
            {
                try
                {
                    Create(__instance);
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogError($"FedoHud: map distance rings creation failed: {e}");
                }
            }
        }

        private static void Create(Minimap minimap)
        {
            _small = null;
            _large = null;

            if (minimap.m_pinRootSmall != null && minimap.m_mapImageSmall != null)
            {
                _small = BuildSet(minimap.m_pinRootSmall);
            }

            if (minimap.m_pinRootLarge != null && minimap.m_mapImageLarge != null)
            {
                _large = BuildSet(minimap.m_pinRootLarge);
            }
        }

        private static RingSet BuildSet(RectTransform pinRoot)
        {
            var maskGo = new GameObject("FedoHud_DistanceRingsMask", typeof(RectTransform));
            maskGo.transform.SetParent(pinRoot, worldPositionStays: false);
            var maskRect = maskGo.GetComponent<RectTransform>();
            // Même convention d'ancre/pivot que les pins du jeu (voir commentaire de tête) :
            // (0,0) sur les deux axes -- `anchoredPosition`/`sizeDelta` deviennent alors
            // directement comparables aux valeurs 0..largeur/0..hauteur renvoyées par
            // `MapPointToLocalGuiPos`.
            maskRect.anchorMin = Vector2.zero;
            maskRect.anchorMax = Vector2.zero;
            maskRect.pivot = Vector2.zero;
            maskRect.anchoredPosition = Vector2.zero;
            // Premier enfant de `pinRoot` -- passe donc sous les vrais pins/le marqueur
            // joueur (ajoutés plus tard, à la suite) dans l'ordre de rendu UI.
            maskGo.transform.SetAsFirstSibling();

            // `RectMask2D` : clip rectangulaire simple, cohérent avec la zone réellement
            // considérée "visible" par le jeu lui-même (voir `Minimap.IsPointVisible`, un
            // test purement rectangulaire sur `uvRect` -- le cadre rond de la mini-carte
            // n'est qu'un habillage visuel par-dessus, pas un vrai masque).
            maskGo.AddComponent<RectMask2D>();

            var set = new RingSet { MaskContainer = maskRect };

            int count = FedoHudPlugin.Instance.MapDistanceRingCount;
            TMP_FontAsset font = HudFont.Resolve();
            for (int i = 0; i < count; i++)
            {
                set.Rings.Add(BuildRing(maskRect, font));
            }

            return set;
        }

        private static (RectTransform, TMP_Text) BuildRing(Transform parent, TMP_FontAsset font)
        {
            var ringGo = new GameObject("Ring", typeof(RectTransform));
            ringGo.transform.SetParent(parent, worldPositionStays: false);
            var ringRect = ringGo.GetComponent<RectTransform>();
            ringRect.anchorMin = Vector2.zero;
            ringRect.anchorMax = Vector2.zero;
            ringRect.pivot = new Vector2(0.5f, 0.5f);

            var ring = ringGo.AddComponent<RingGraphic>();
            ring.color = new Color(1f, 1f, 1f, 0.6f);
            ring.raycastTarget = false;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(parent, worldPositionStays: false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            // Même convention d'ancre que `ringRect` ci-dessus (voir son commentaire) --
            // sans ça, `anchoredPosition` serait relatif à un point fractionnaire du
            // conteneur (qui change de taille selon le zoom) plutôt qu'un offset en
            // pixels bruts depuis son coin (0,0), et le texte dériverait de l'anneau.
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.zero;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(80f, 16f);

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                label.font = font;
            }
            label.fontSize = 10f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 1f, 1f, 0.85f);
            label.raycastTarget = false;

            return (ringRect, label);
        }

        // Anneau dessiné en maillage (pas un sprite étiré) : un trait d'épaisseur FIXE en
        // unités de canvas, quel que soit le diamètre -- de quelques pixels sur la
        // mini-carte à des dizaines de milliers en grande carte zoomée. Vécu en jeu : un
        // sprite 32px en `Image.Type.Sliced` ne fige que sa bordure 9-slice, alors que
        // l'arc du cercle passe presque entièrement par la zone centrale étirée -- il
        // devenait une bande floue énorme et octogonale sur la grande carte.
        // `MaskableGraphic` : reste découpé par le `RectMask2D` du conteneur comme une
        // `Image` classique.
        private class RingGraphic : MaskableGraphic
        {
            private const float ThicknessPx = 2f;

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();

                Rect r = GetPixelAdjustedRect();
                float outer = Mathf.Min(r.width, r.height) / 2f;
                if (outer <= 0f)
                {
                    return;
                }

                float inner = Mathf.Max(0f, outer - ThicknessPx);
                Vector2 center = r.center;
                // Assez de segments pour que la corde ne s'écarte jamais de plus d'environ
                // un pixel du vrai cercle, même pour un anneau immense (zoom max).
                int segments = Mathf.Clamp(Mathf.CeilToInt(Mathf.PI * Mathf.Sqrt(outer * 2f)), 48, 1024);

                Color32 c = color;
                for (int i = 0; i <= segments; i++)
                {
                    float angle = i * 2f * Mathf.PI / segments;
                    var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    vh.AddVert(center + dir * outer, c, Vector2.zero);
                    vh.AddVert(center + dir * inner, c, Vector2.zero);

                    if (i > 0)
                    {
                        int k = i * 2;
                        vh.AddTriangle(k - 2, k, k - 1);
                        vh.AddTriangle(k - 1, k, k + 1);
                    }
                }
            }
        }

        public static void SetVisible(bool visible)
        {
            if (_small?.MaskContainer != null)
            {
                _small.MaskContainer.gameObject.SetActive(visible);
            }

            if (_large?.MaskContainer != null)
            {
                _large.MaskContainer.gameObject.SetActive(visible);
            }
        }

        public static void Refresh()
        {
            var minimap = Minimap.instance;
            if (minimap == null)
            {
                return;
            }

            UpdateSet(_small, minimap.m_mapImageSmall, minimap);
            UpdateSet(_large, minimap.m_mapImageLarge, minimap);
        }

        private static void UpdateSet(RingSet set, RawImage mapImage, Minimap minimap)
        {
            if (set == null || mapImage == null)
            {
                return;
            }

            Rect uvRect = mapImage.uvRect;
            Rect rect = mapImage.rectTransform.rect;
            if (uvRect.width <= 0f || uvRect.height <= 0f || rect.width <= 0f)
            {
                return;
            }

            set.MaskContainer.sizeDelta = new Vector2(rect.width, rect.height);

            // Voir Minimap.WorldToMapPoint/MapPointToLocalGuiPos (privées, reproduites ici) :
            // mx = p.x / (m_pixelSize * m_textureSize) + 0.5 -- donc un déplacement d'UN
            // mètre en monde déplace `mx` de 1 / (m_pixelSize * m_textureSize) en UV, puis
            // `MapPointToLocalGuiPos` convertit cet UV en pixels via `/ uvRect.width *
            // rect.width`. Les deux facteurs combinés donnent directement des pixels par
            // mètre.
            float pixelsPerMeter = rect.width / (uvRect.width * minimap.m_textureSize * minimap.m_pixelSize);

            // (0,0,0) en coordonnées monde tombe toujours exactement au centre (0.5, 0.5)
            // de la texture de carte (voir commentaire de tête) -- pas besoin d'appeler
            // WorldToMapPoint pour ça.
            Vector2 originPos = new Vector2(
                (0.5f - uvRect.xMin) / uvRect.width * rect.width,
                (0.5f - uvRect.yMin) / uvRect.height * rect.height);

            float step = FedoHudPlugin.Instance.MapDistanceRingStep;
            string suffix = FedoHudPlugin.Instance.MapDistanceRingLabelSuffix;

            for (int i = 0; i < set.Rings.Count; i++)
            {
                float radiusMeters = (i + 1) * step;
                float diameterPx = 2f * radiusMeters * pixelsPerMeter;

                var (ringRect, label) = set.Rings[i];
                ringRect.anchoredPosition = originPos;
                ringRect.sizeDelta = new Vector2(diameterPx, diameterPx);

                label.text = $"{(int)radiusMeters}{suffix}";
                label.rectTransform.anchoredPosition = originPos + new Vector2(0f, diameterPx / 2f);
            }
        }
    }
}
