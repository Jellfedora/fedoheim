using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace FedoHud
{
    // Regroupe les éléments de HUD custom purement locaux (aucun appel réseau, aucun
    // ServerToken) : l'horloge en jeu (déplaçable, ex-FedoClientTools) et l'ajout d'une
    // ligne "pousse dans Xh" au survol d'une plante/d'un jeune arbre planté au
    // cultivateur (voir GrowthTooltip.cs). Tout ce qui est purement cosmétique/local
    // vit ici plutôt que dans FedoClientTools (auto-connexion/session) ou
    // FedoServerTools (reporting API/admin) -- voir mods/CLAUDE.md.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FedoHudPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.hud";
        public const string PluginName = "FedoHud";
        public const string PluginVersion = "0.0.1";

        public static FedoHudPlugin Instance { get; private set; }

        // Exposé pour que les classes de patch Harmony (statiques) puissent logguer sans
        // jamais laisser une exception remonter dans le code du jeu qu'elles patchent.
        public static ManualLogSource Log { get; private set; }

        // Horloge en jeu -- voir ClockOverlay.cs pour l'affichage à l'écran. Calcul dérivé
        // indépendamment de EnvMan.GetDayFraction() ; FedoServerTools calcule la même
        // chose de son côté pour son propre rapport à l'API/launcher, mais les deux mods
        // ne partagent pas ce calcul -- deux installations distinctes.
        // `_clockRefreshTimer` throttle le rafraîchissement à ~1x/seconde plutôt qu'à
        // chaque frame, la minute affichée ne changeant de toute façon pas plus vite que ça.
        private ConfigEntry<bool> _showClockOverlay;
        private float _clockRefreshTimer;

        // Position de l'horloge à l'écran (voir ClockOverlay.cs, glissée à la souris en
        // maintenant Maj) -- préférence purement locale à cette installation.
        private ConfigEntry<float> _clockPositionX;
        private ConfigEntry<float> _clockPositionY;

        // Ligne ajoutée au survol d'une plante en cours de pousse -- voir GrowthTooltip.cs.
        private ConfigEntry<bool> _showGrowthTooltip;
        private ConfigEntry<string> _growthRemainingPrefix;
        private ConfigEntry<string> _growthReadyText;

        public bool ShowGrowthTooltip => _showGrowthTooltip.Value;
        public string GrowthRemainingPrefix => _growthRemainingPrefix.Value;
        public string GrowthReadyText => _growthReadyText.Value;

        // Icône "!" flottante au-dessus d'une culture prête à récolter -- voir
        // PlantReadyIndicator.cs. Toggle séparé de ShowGrowthTooltip (le texte au survol) :
        // les deux sont indépendants.
        private ConfigEntry<bool> _showGrowthReadyIcon;
        public bool ShowGrowthReadyIcon => _showGrowthReadyIcon.Value;

        // Ligne ajoutée au survol d'une ruche -- voir BeehiveTooltip.cs. Section distincte
        // de [Growth] : une ruche n'est pas une `Plant`, et l'admin/joueur doit pouvoir
        // désactiver l'un sans l'autre.
        private ConfigEntry<bool> _showBeehiveTooltip;
        private ConfigEntry<string> _honeyRemainingPrefix;
        private ConfigEntry<string> _honeyFullText;
        private ConfigEntry<string> _honeyPausedText;

        public bool ShowBeehiveTooltip => _showBeehiveTooltip.Value;
        public string HoneyRemainingPrefix => _honeyRemainingPrefix.Value;
        public string HoneyFullText => _honeyFullText.Value;
        public string HoneyPausedText => _honeyPausedText.Value;

        // Icône "!" flottante au-dessus d'une ruche pleine -- voir BeehiveFullIndicator.cs.
        // Toggle séparé de ShowBeehiveTooltip (le texte au survol) : les deux sont
        // indépendants.
        private ConfigEntry<bool> _showBeehiveFullIcon;
        public bool ShowBeehiveFullIcon => _showBeehiveFullIcon.Value;

        // Ligne ajoutée au survol d'un fermenteur -- voir FermenterTooltip.cs.
        private ConfigEntry<bool> _showFermenterTooltip;
        private ConfigEntry<string> _fermenterRemainingPrefix;
        private ConfigEntry<string> _fermenterReadyText;

        public bool ShowFermenterTooltip => _showFermenterTooltip.Value;
        public string FermenterRemainingPrefix => _fermenterRemainingPrefix.Value;
        public string FermenterReadyText => _fermenterReadyText.Value;

        // Ligne ajoutée au survol d'une fonderie/d'un four à charbon -- voir
        // SmelterTooltip.cs (les deux partagent la même classe `Smelter` côté jeu).
        private ConfigEntry<bool> _showSmelterTooltip;
        private ConfigEntry<string> _smelterRemainingPrefix;
        private ConfigEntry<string> _smelterReadyText;
        private ConfigEntry<string> _smelterPausedText;

        public bool ShowSmelterTooltip => _showSmelterTooltip.Value;
        public string SmelterRemainingPrefix => _smelterRemainingPrefix.Value;
        public string SmelterReadyText => _smelterReadyText.Value;
        public string SmelterPausedText => _smelterPausedText.Value;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            _showClockOverlay = Config.Bind(
                "Time",
                "ShowClockOverlay",
                true,
                "Shows a small in-game clock (HH:MM, following the day/night cycle) at the top-center of the screen. Purely a local HUD addition -- no network call involved.");

            _clockPositionX = Config.Bind(
                "Time",
                "ClockPositionX",
                0f,
                "Horizontal position of the clock overlay, in UI pixels from the top-center of the screen. Saved automatically when you drag the clock (hold Left Shift and drag it with the mouse) -- not meant to be hand-edited, but you can reset it here.");
            _clockPositionY = Config.Bind(
                "Time",
                "ClockPositionY",
                -18f,
                "Vertical position of the clock overlay, in UI pixels from the top-center of the screen (negative = downward). Saved automatically when you drag the clock (hold Left Shift and drag it with the mouse).");

            _showGrowthTooltip = Config.Bind(
                "Growth",
                "ShowGrowthTooltip",
                true,
                "Adds a line to the hover tooltip of a planted crop/sapling showing how long until it's ready to harvest. Purely a local HUD addition -- no network call involved.");

            _growthRemainingPrefix = Config.Bind(
                "Growth",
                "GrowthRemainingPrefix",
                "Grows in",
                "Text shown before the remaining growth time in a plant's hover tooltip, e.g. \"Grows in 3h20m\".");

            _growthReadyText = Config.Bind(
                "Growth",
                "GrowthReadyText",
                "Ready to harvest!",
                "Text shown in a plant's hover tooltip once its growth time has elapsed.");

            _showGrowthReadyIcon = Config.Bind(
                "Growth",
                "ShowGrowthReadyIcon",
                true,
                "Shows a floating \"!\" above a crop once it's ready to harvest (never above a planted tree sapling). Purely a local HUD addition -- no network call involved.");

            _showBeehiveTooltip = Config.Bind(
                "Beehive",
                "ShowBeehiveTooltip",
                true,
                "Adds a line to a beehive's hover tooltip showing how long until it produces one more honey. Purely a local HUD addition -- no network call involved.");

            _honeyRemainingPrefix = Config.Bind(
                "Beehive",
                "HoneyRemainingPrefix",
                "Next honey in",
                "Text shown before the remaining time until the next honey in a beehive's hover tooltip, e.g. \"Next honey in 0h45m\".");

            _honeyFullText = Config.Bind(
                "Beehive",
                "HoneyFullText",
                "Storage full!",
                "Text shown in a beehive's hover tooltip once it holds as much honey as it can -- collect some to resume production.");

            _honeyPausedText = Config.Bind(
                "Beehive",
                "HoneyPausedText",
                "Production paused",
                "Text shown in a beehive's hover tooltip when it can't currently produce honey (wrong biome, or no free space around it) -- no countdown shown in that case, since it wouldn't be accurate.");

            _showBeehiveFullIcon = Config.Bind(
                "Beehive",
                "ShowBeehiveFullIcon",
                true,
                "Shows a floating \"!\" above a beehive once it holds as much honey as it can. Purely a local HUD addition -- no network call involved.");

            _showFermenterTooltip = Config.Bind(
                "Fermenter",
                "ShowFermenterTooltip",
                true,
                "Adds a line to a fermenter's hover tooltip showing how long until it's done fermenting. Purely a local HUD addition -- no network call involved.");

            _fermenterRemainingPrefix = Config.Bind(
                "Fermenter",
                "FermenterRemainingPrefix",
                "Ready in",
                "Text shown before the remaining time in a fermenter's hover tooltip, e.g. \"Ready in 3h20m\".");

            _fermenterReadyText = Config.Bind(
                "Fermenter",
                "FermenterReadyText",
                "Ready!",
                "Text shown in a fermenter's hover tooltip once fermentation is done.");

            _showSmelterTooltip = Config.Bind(
                "Smelter",
                "ShowSmelterTooltip",
                true,
                "Adds a line to a smelter's/charcoal kiln's hover tooltip showing how long until everything queued is done. Purely a local HUD addition -- no network call involved.");

            _smelterRemainingPrefix = Config.Bind(
                "Smelter",
                "SmelterRemainingPrefix",
                "Done in",
                "Text shown before the remaining time in a smelter's/charcoal kiln's hover tooltip, e.g. \"Done in 3h20m\".");

            _smelterReadyText = Config.Bind(
                "Smelter",
                "SmelterReadyText",
                "Done!",
                "Text shown in a smelter's/charcoal kiln's hover tooltip once everything queued is done.");

            _smelterPausedText = Config.Bind(
                "Smelter",
                "SmelterPausedText",
                "Paused",
                "Text shown in a smelter's/charcoal kiln's hover tooltip when it can't currently process its queue (missing fuel, needs a roof...) -- no countdown shown in that case, since it wouldn't be accurate.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        private void Update()
        {
            RefreshClockOverlay();
        }

        // Throttlé à ~1x/seconde -- voir ClockOverlay.cs pour la création/le positionnement
        // de l'élément UI lui-même.
        private void RefreshClockOverlay()
        {
            ClockOverlay.SetVisible(_showClockOverlay.Value);
            if (!_showClockOverlay.Value)
            {
                return;
            }

            _clockRefreshTimer -= Time.deltaTime;
            if (_clockRefreshTimer > 0f)
            {
                return;
            }

            _clockRefreshTimer = 1f;
            ClockOverlay.SetText(GetCurrentGameTime());
        }

        // Lu par ClockOverlay au moment de (re)créer l'élément (voir Hud.Awake) pour le
        // replacer où le joueur l'avait laissé une session précédente.
        public Vector2 SavedClockPosition => new Vector2(_clockPositionX.Value, _clockPositionY.Value);

        // Appelé par ClockOverlay.DragHandler à la fin d'un glissement -- écrit directement
        // dans le .cfg local, pas de round-trip serveur.
        public void SaveClockPosition(Vector2 anchoredPosition)
        {
            _clockPositionX.Value = anchoredPosition.x;
            _clockPositionY.Value = anchoredPosition.y;
        }

        // Horloge en jeu au format HH:MM, dérivée de EnvMan.GetDayFraction() -- déjà la
        // fraction (0..1) du jour en cours utilisée en interne par le jeu pour l'éclairage,
        // donc déjà correctement calée sur le vrai cycle jour/nuit.
        private string GetCurrentGameTime()
        {
            if (EnvMan.instance == null)
            {
                return null;
            }

            try
            {
                float totalHours = EnvMan.instance.GetDayFraction() * 24f;
                totalHours = ((totalHours % 24f) + 24f) % 24f;
                int hour = (int)totalHours;
                int minute = (int)((totalHours - hour) * 60f);
                return $"{hour:D2}:{minute:D2}";
            }
            catch (System.Exception e)
            {
                Log?.LogWarning($"FedoHud: failed to resolve current game time: {e.Message}");
                return null;
            }
        }
    }
}
