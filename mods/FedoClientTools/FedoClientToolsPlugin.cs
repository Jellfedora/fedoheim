using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace FedoClientTools
{
    // Moitié client de l'ancien FedoClientTools -- séparé de FedoServerTools (reporting API,
    // commandes admin, anti-usurpation, tout ce qui doit tourner sur le serveur dédié) pour
    // que les joueurs normaux n'installent que ce dont ils ont réellement besoin. Aucun
    // ServerToken ici : ce mod ne parle jamais à l'API avec un jeton authentifié, seulement
    // à des routes publiques (voir DisconnectChoiceOverlay.cs/ServerStatusLine.cs). L'horloge
    // en jeu (déplaçable) a déménagé dans le mod FedoHud, qui regroupe désormais tous les
    // éléments de HUD custom -- voir mods/FedoHud/README.md.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FedoClientToolsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.clienttools";
        public const string PluginName = "FedoClientTools";
        public const string PluginVersion = "1.1.0";

        public static FedoClientToolsPlugin Instance { get; private set; }

        // Exposé pour que les classes de patch Harmony (statiques) puissent logguer sans
        // jamais laisser une exception remonter dans le code du jeu qu'elles patchent.
        public static ManualLogSource Log { get; private set; }

        private ConfigEntry<string> _apiBaseUrl;

        // Exposé pour DisconnectChoiceOverlay.cs/ServerStatusLine.cs -- interroge
        // uniquement des routes publiques de l'API (statut serveur/joueurs en ligne), pas
        // besoin d'un jeton.
        public string ApiBaseUrl => _apiBaseUrl.Value;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            _apiBaseUrl = Config.Bind(
                "Api",
                "ApiBaseUrl",
                "http://127.0.0.1:3000",
                "Base URL of the Fedoheim API, no trailing slash. Only used to check server status when reconnecting after a disconnect -- public routes, no login/token involved.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
