using TMPro;
using UnityEngine;

namespace FedoHud
{
    // Résout une police TMP réutilisable pour un texte world-space (voir
    // BeehiveFullIndicator.cs) sans avoir à embarquer/référencer un asset de police
    // séparé -- reprend simplement la première police TMP déjà chargée par le jeu
    // (`Resources.FindObjectsOfTypeAll`, peu importe laquelle, il y en a toujours au
    // moins une une fois une partie chargée). Mise en cache : pas la peine de refaire
    // cette recherche à chaque ruche.
    internal static class HudFont
    {
        private static TMP_FontAsset _cached;

        public static TMP_FontAsset Resolve()
        {
            if (_cached != null)
            {
                return _cached;
            }

            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (fonts != null && fonts.Length > 0)
            {
                _cached = fonts[0];
            }

            return _cached;
        }
    }
}
