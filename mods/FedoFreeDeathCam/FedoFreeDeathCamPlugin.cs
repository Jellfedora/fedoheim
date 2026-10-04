using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace FedoFreeDeathCam
{
    // Laisse la caméra bouger librement pendant l'écran de mort, au lieu de rester
    // bloquée à fixer le cadavre du personnage -- voir FreeDeathCamera.cs.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FedoFreeDeathCamPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.freedeathcam";
        public const string PluginName = "FedoFreeDeathCam";
        public const string PluginVersion = "0.0.1";

        public static FedoFreeDeathCamPlugin Instance { get; private set; }

        // Exposé pour que les classes de patch Harmony (statiques) puissent logguer sans
        // jamais laisser une exception remonter dans le code du jeu qu'elles patchent.
        public static ManualLogSource Log { get; private set; }

        private ConfigEntry<bool> _enableFreeDeathCam;

        public bool EnableFreeDeathCam => _enableFreeDeathCam.Value;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            _enableFreeDeathCam = Config.Bind(
                "FreeDeathCam",
                "EnableFreeDeathCam",
                true,
                "Lets you move the camera freely while dead (same free-fly camera the game already has, normally only reachable via debug mode), instead of it staying locked looking at your character's body. Purely a local visual addition -- no network call involved.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
