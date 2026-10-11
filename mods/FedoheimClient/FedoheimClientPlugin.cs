using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace FedoheimClient
{
    // Mod privé à la communauté Fedoheim (jamais publié) : petites fonctionnalités
    // propres à ce serveur, installées à la fois chez tous les joueurs du modpack et sur
    // le serveur dédié -- le même DLL joue le rôle client ou serveur selon
    // ZNet.IsServer(). Aujourd'hui : lancement d'un event de raid vanilla demandé par un
    // dialogue de quête Marketplace (voir QuestEvents.cs), et traduction des pages d'aide
    // des ports de More World Locations (voir MwlTutorialTranslation.cs).
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FedoheimClientPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.fedoheimclient";
        public const string PluginName = "FedoheimClient";
        public const string PluginVersion = "1.0.1";

        public static FedoheimClientPlugin Instance { get; private set; }

        // Exposé pour que les classes de patch Harmony (statiques) puissent logguer sans
        // jamais laisser une exception remonter dans le code du jeu qu'elles patchent.
        public static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            QuestEvents.BindConfig(Config);

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
