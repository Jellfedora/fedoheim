using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace FedoAchievements
{
    // Lève les deux masquages de l'écran de succès du jeu (Inventaire > Succès) : les
    // succès secrets (nom/description/détail remplacés par "???") et la progression des
    // conditions pas encore remplies (toujours "??? / ???", même sur un succès non
    // secret). Purement de l'affichage local -- aucun déblocage, aucune stat modifiée,
    // rien côté Steam (le statut "caché" de l'overlay Steam est défini chez Steam, hors
    // de portée d'un mod). Voir AchievementPatches.cs.
    //
    // Pas de ServerSync : c'est un choix d'affichage personnel, sans effet sur le monde
    // partagé ni sur les autres joueurs.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FedoAchievementsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.achievements";
        public const string PluginName = "FedoAchievements";
        public const string PluginVersion = "1.0.1";

        // Exposé pour que les classes de patch Harmony (statiques) puissent logguer sans
        // jamais laisser une exception remonter dans le code du jeu qu'elles patchent.
        public static ManualLogSource Log { get; private set; }

        private static ConfigEntry<bool> _revealSecretAchievements;
        private static ConfigEntry<bool> _showProgress;

        public static bool RevealSecretAchievements => _revealSecretAchievements?.Value ?? false;
        public static bool ShowProgress => _showProgress?.Value ?? false;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            _revealSecretAchievements = Config.Bind(
                "Achievements",
                "RevealSecretAchievements",
                true,
                "Shows the real name, description and unlock conditions of secret achievements in the in-game achievements panel, even before they are unlocked. Display only -- nothing gets unlocked. Takes effect the next time the achievements panel is opened.");

            _showProgress = Config.Bind(
                "Achievements",
                "ShowProgress",
                true,
                "Shows the name and current progress (e.g. 3 / 10) of every unlock condition in an achievement's detail panel, instead of '??? / ???' for the ones not completed yet.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
