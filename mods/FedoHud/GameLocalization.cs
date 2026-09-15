using System;

namespace FedoHud
{
    // Traduction générique dans la langue du jeu, avec repli sur la clé brute en cas
    // d'échec -- utilisé par SkillLocalization.cs (noms de compétences) et
    // RecipeTrackerOverlay.cs (noms de pièces/objets, même convention de clé "$..." que
    // pour les compétences).
    internal static class GameLocalization
    {
        public static string LocalizeOrRaw(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return key;
            }

            try
            {
                if (Localization.instance != null)
                {
                    return Localization.instance.Localize(key);
                }
            }
            catch (Exception e)
            {
                FedoHudPlugin.Log?.LogWarning($"FedoHud: failed to localize \"{key}\": {e.Message}");
            }

            return key;
        }
    }
}
