using System;
using BepInEx.Logging;

namespace FedoServerTools
{
    // Dépendance douce sur le mod tiers shudnal/Seasons (Thunderstore) -- jamais
    // référencé en dur ailleurs dans ce mod, tout usage de son API reste isolé à ce
    // fichier. `IsLoaded` ne touche qu'à BepInEx.Bootstrap (déjà référencé par ce
    // projet pour Harmony) : c'est le seul membre de cette classe qu'il est sûr
    // d'appeler sans savoir si Seasons est chargé. `GetCurrentSeasonKey`, lui, référence
    // des types Seasons.* -- son corps n'est résolu par le JIT qu'à son premier appel
    // réel (résolution par méthode, pas par assembly), donc sûr tant qu'il n'est jamais
    // appelé sans avoir vérifié `IsLoaded` d'abord. Ce mod doit rester pleinement
    // fonctionnel (saison simplement absente du rapport) si Seasons n'est pas installé.
    internal static class SeasonReporting
    {
        private const string SeasonsPluginGuid = "shudnal.Seasons";

        public static bool IsLoaded => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(SeasonsPluginGuid);

        // Nom anglais brut de la saison actuelle ("Spring"/"Summer"/"Fall"/"Winter",
        // voir l'enum Seasons.Seasons.Season) -- à l'appelant de le traduire pour
        // l'affichage (voir FedoServerToolsPlugin.ResolveSeasonName), même principe que
        // pour les biomes. `null` si Seasons n'a pas encore de monde chargé
        // (SeasonState.IsActive) ou en cas d'erreur.
        public static string GetCurrentSeasonKey(ManualLogSource log)
        {
            try
            {
                if (!global::Seasons.SeasonState.IsActive)
                {
                    return null;
                }

                return global::Seasons.Seasons.seasonState.GetCurrentSeason().ToString();
            }
            catch (Exception e)
            {
                log?.LogWarning($"FedoServerTools: failed to resolve current season: {e.Message}");
                return null;
            }
        }
    }
}
