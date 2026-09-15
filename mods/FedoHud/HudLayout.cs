using UnityEngine;

namespace FedoHud
{
    // Positionne les blocs de ce mod sous la minimap du jeu, quelle que soit sa taille
    // réelle à l'écran -- un décalage fixe deviné au jugé finissait par la chevaucher
    // pour certains réglages (vécu en jeu, voir CHANGELOG : le bloc compétences
    // recouvrait le coin de la minimap). `Minimap.m_smallRoot` (le cadre entier de la
    // mini-carte, y compris le nom du biome affiché dessous) est public, vérifié par
    // réflexion.
    internal static class HudLayout
    {
        private const float MarginBelowMinimap = 16f;

        // Repli si la minimap n'est pas mesurable pour une raison ou une autre (ne
        // devrait pas arriver en jeu, mais mieux vaut un défaut raisonnable qu'une
        // position cassée).
        private const float FallbackBelowMinimapY = -280f;
        private const float FallbackCenterX = -110f;

        // Garde-fou : quoi qu'il arrive, jamais moins bas que le sommet de l'écran
        // (un bloc ancré (1,1) dont le Y serait proche de 0 ou positif sortirait de
        // l'écran par le haut -- vécu en jeu suite à un bug de repère de coordonnées
        // corrigé depuis, voir CHANGELOG) ni plus bas qu'un point clairement absurde.
        // Même principe sur X : jamais au-delà du bord droit de l'écran (positif) ni
        // ridiculeusement loin vers la gauche.
        private const float MinY = -80f;
        private const float MaxAbsoluteY = -2000f;
        private const float MinX = -80f;
        private const float MaxAbsoluteX = -2000f;

        // `saved`/`defaultValue` viennent tous les deux de FedoHudPlugin (`SavedXxx
        // Position`/`DefaultXxxPosition`) -- tant qu'ils sont égaux, le joueur n'a
        // jamais glissé ce bloc ni cliqué "Reset positions" depuis que ce calcul
        // dynamique existe : on recalcule alors le Y sous la minimap à chaque création
        // (auto-adaptatif si la taille de la minimap change entre deux sessions). Dès
        // qu'un vrai Y a été enregistré (glissé ou recalculé puis sauvegardé par
        // Reset), il devient prioritaire -- jamais réécrasé tant que le joueur ne
        // redemande pas explicitement un reset.
        // `extraBelow` : décalage supplémentaire vers le bas (positif), pour un bloc qui
        // doit démarrer sous un AUTRE bloc déjà placé sous la minimap (ex. les
        // compétences sous le compteur de morts, tous les deux "juste sous la minimap"
        // par défaut sinon -- vécu en jeu, ils se chevauchaient).
        public static Vector2 ResolveTopRightPosition(Vector2 saved, Vector2 defaultValue, RectTransform relativeTo, float extraBelow = 0f)
        {
            if (saved != defaultValue)
            {
                return saved;
            }

            return new Vector2(defaultValue.x, GetMinimapEdge(relativeTo).y - extraBelow);
        }

        // Comme ResolveTopRightPosition, mais pour un bloc dont le pivot est
        // (0.5, 1) -- centré horizontalement sous la minimap plutôt qu'aligné sur son
        // bord droit (voir DeathCounterOverlay, seul bloc dans ce cas pour l'instant).
        public static Vector2 ResolveCenteredBelowMinimapPosition(Vector2 saved, Vector2 defaultValue, RectTransform relativeTo)
        {
            if (saved != defaultValue)
            {
                return saved;
            }

            return GetMinimapEdge(relativeTo);
        }

        // `x` = décalage (depuis le coin haut-droit de `relativeTo`) du CENTRE
        // horizontal de la minimap ; `y` = décalage de son bord inférieur. Les deux
        // partagent la même conversion écran -> repère local, donc calculés ensemble.
        private static Vector2 GetMinimapEdge(RectTransform relativeTo)
        {
            try
            {
                var minimapRoot = Minimap.instance != null ? Minimap.instance.m_smallRoot : null;
                var minimapRect = minimapRoot != null ? minimapRoot.GetComponent<RectTransform>() : null;
                if (minimapRect == null || relativeTo == null)
                {
                    return new Vector2(FallbackCenterX, FallbackBelowMinimapY);
                }

                var corners = new Vector3[4];
                minimapRect.GetWorldCorners(corners); // 0 = bas-gauche, 3 = bas-droite, dans l'ordre horaire
                Vector2 bottomLeftScreen = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
                Vector2 bottomRightScreen = RectTransformUtility.WorldToScreenPoint(null, corners[3]);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(relativeTo, bottomLeftScreen, null, out var bottomLeft)
                    || !RectTransformUtility.ScreenPointToLocalPointInRectangle(relativeTo, bottomRightScreen, null, out var bottomRight))
                {
                    return new Vector2(FallbackCenterX, FallbackBelowMinimapY);
                }

                // Les deux points sont relatifs au PIVOT de `relativeTo`, pas à son coin
                // haut-droit -- ce qu'attend `anchoredPosition` pour un rect ancré
                // (1,1). `.rect.xMax`/`.rect.yMax` (les bords droit/haut, dans le même
                // repère) corrigent cet écart quel que soit le pivot réel de
                // `relativeTo` -- confondre les deux plaçait un bloc bien plus haut que
                // prévu, jusqu'à sortir de l'écran par le haut (vécu en jeu).
                float centerX = (bottomLeft.x + bottomRight.x) / 2f - relativeTo.rect.xMax;
                float bottomY = bottomLeft.y - relativeTo.rect.yMax - MarginBelowMinimap;

                // Garde-fou : un calcul qui donnerait un résultat aberrant (mauvaise
                // détection de la minimap, résolution/mise à l'échelle inhabituelle...)
                // ne doit jamais pouvoir placer un bloc hors de l'écran.
                return new Vector2(Mathf.Clamp(centerX, MaxAbsoluteX, MinX), Mathf.Clamp(bottomY, MaxAbsoluteY, MinY));
            }
            catch
            {
                return new Vector2(FallbackCenterX, FallbackBelowMinimapY);
            }
        }
    }
}
