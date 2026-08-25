using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace FedoServerTools
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FedoServerToolsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.servertools";
        public const string PluginName = "FedoServerTools";
        public const string PluginVersion = "1.0.0";

        public static FedoServerToolsPlugin Instance { get; private set; }

        // Exposé pour que les classes de patch Harmony (statiques) puissent logguer sans
        // jamais laisser une exception remonter dans le code du jeu qu'elles patchent.
        public static ManualLogSource Log { get; private set; }

        private ConfigEntry<string> _apiBaseUrl;
        private ConfigEntry<string> _serverToken;

        // Exposés pour CharacterOwnershipPatch.cs (classe statique, pas d'accès direct
        // aux champs privés d'ici) -- mêmes valeurs que celles utilisées par le rapport
        // périodique ci-dessus.
        public string ApiBaseUrl => _apiBaseUrl.Value;
        public string ServerToken => _serverToken.Value;
        private ConfigEntry<float> _syncIntervalSeconds;
        private ConfigEntry<float> _startingGracePeriodSeconds;

        // Nombre d'échecs consécutifs du rapport périodique (API injoignable, mauvais
        // ApiBaseUrl...) -- sert uniquement à espacer les logs, voir Report() ci-dessous.
        private int _consecutiveReportFailures;

        // Idem pour l'avertissement "pas de jeton configuré" -- un seul log tant que
        // ServerToken reste vide, pas un à chaque rapport (SyncIntervalSeconds).
        private bool _missingTokenWarned;

        // Les commandes serveur (ServerCommands.ApplyFromReportResponse) touchent des API
        // Unity/ZNet -- jamais sûr de les exécuter directement depuis la continuation
        // async de Report() ci-dessous, qui tourne sur un thread du pool (Task.Run +
        // ConfigureAwait(false)), pas le thread principal. Mise en file ici, vidée à
        // chaque Update() (même principe que RefreshClockOverlay/CheckDayAndSeasonChange
        // déjà pilotés depuis Update()).
        private static readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();

        public static void RunOnMainThread(Action action) => _mainThreadActions.Enqueue(action);

        private static void DrainMainThreadActions()
        {
            while (_mainThreadActions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Log?.LogWarning($"FedoServerTools: main-thread action failed: {e.Message}");
                }
            }
        }

        // Time.realtimeSinceStartup au moment où ce plugin charge -- avant même que
        // ZNet existe, donc bien avant qu'on sache si cette instance sera serveur ou
        // client. C'est justement pendant cette fenêtre (chargement de BepInEx et de
        // tous les mods, potentiellement long sur un serveur qui en a beaucoup) qu'on
        // veut pouvoir remonter "starting" plutôt que rien du tout.
        private float _bootRealtime;

        private ConfigEntry<string> _biomeMeadows;
        private ConfigEntry<string> _biomeBlackForest;
        private ConfigEntry<string> _biomeSwamp;
        private ConfigEntry<string> _biomeMountain;
        private ConfigEntry<string> _biomePlains;
        private ConfigEntry<string> _biomeAshLands;
        private ConfigEntry<string> _biomeDeepNorth;
        private ConfigEntry<string> _biomeOcean;
        private ConfigEntry<string> _biomeMistlands;

        private ConfigEntry<string> _seasonSpring;
        private ConfigEntry<string> _seasonSummer;
        private ConfigEntry<string> _seasonFall;
        private ConfigEntry<string> _seasonWinter;

        // Horloge en jeu -- voir ClockOverlay.cs pour l'affichage à l'écran (client, sans
        // rapport avec le reporting vers l'API) ; la même valeur formatée est aussi
        // envoyée dans le rapport périodique pour être affichée par le launcher (voir
        // GetCurrentGameTime ci-dessous). `_clockRefreshTimer` throttle le rafraîchissement
        // de l'overlay à ~1x/seconde plutôt qu'à chaque frame (Update), la minute affichée
        // ne changeant de toute façon pas plus vite que ça.
        private ConfigEntry<float> _timeOffsetHours;
        private ConfigEntry<bool> _showClockOverlay;
        private float _clockRefreshTimer;

        // Position de l'horloge à l'écran (voir ClockOverlay.cs, glissée à la souris en
        // maintenant Maj) -- préférence purement locale à cette installation, jamais dans
        // ServerSync : contrairement à ForcePublicPosition, ça n'affecte personne d'autre,
        // chaque joueur doit pouvoir la placer où il veut sans que le serveur en décide.
        private ConfigEntry<float> _clockPositionX;
        private ConfigEntry<float> _clockPositionY;

        private ConfigEntry<bool> _forcePublicPosition;
        private bool ForcePublicPosition => _forcePublicPosition.Value;

        // Intégration Discord (voir DiscordWebhook.cs) -- indépendante du reporting vers
        // l'API Fedoheim ci-dessus : un webhook Discord, pas un jeton d'API. Jamais dans
        // ServerSync, même raison que ServerToken (voir mods/CLAUDE.md) : AddConfigEntry
        // diffuse la valeur à chaque client connecté dès qu'elle change. Contrairement à
        // ServerToken cependant, un admin peut choisir de la renseigner sur toutes les
        // installations (y compris joueur) pour que les morts, qui ne se déclenchent que
        // côté client (voir PlayerDeathAnnouncePatch), soient elles aussi loguées -- voir
        // le README pour la distinction avec ServerToken.
        private ConfigEntry<string> _discordWebhookUrl;

        private ConfigEntry<bool> _logPlayerConnected;
        private ConfigEntry<string> _playerConnectedTemplate;
        private ConfigEntry<bool> _logPlayerDisconnected;
        private ConfigEntry<string> _playerDisconnectedTemplate;
        private ConfigEntry<bool> _logPlayerDeath;
        private ConfigEntry<string> _playerDeathTemplate;
        private ConfigEntry<bool> _logServerStarted;
        private ConfigEntry<string> _serverStartedTemplate;
        private ConfigEntry<bool> _logServerStopped;
        private ConfigEntry<string> _serverStoppedTemplate;
        private ConfigEntry<bool> _logWorldSaved;
        private ConfigEntry<string> _worldSavedTemplate;
        private ConfigEntry<bool> _logNewDay;
        private ConfigEntry<string> _newDayTemplate;
        private ConfigEntry<bool> _logSeasonChanged;
        private ConfigEntry<string> _seasonChangedTemplate;
        private ConfigEntry<bool> _logAdminMessage;
        private ConfigEntry<string> _adminMessageTemplate;

        // État pour détecter un changement (voir CheckDayAndSeasonChange) -- `null`
        // signifie "pas encore observé pour cette session", remis à `null` par
        // OnServerStarted à chaque nouvelle session pour ne jamais annoncer un faux
        // changement au tout premier relevé d'une session qui reprend sur un monde/jour
        // différent de la précédente.
        private int? _lastKnownDay;
        private string _lastKnownSeason;
        private float _dayAndSeasonCheckTimer;

        // Dernier état vivant/mort connu par nom de joueur (voir GetConnectedPlayers) --
        // sert uniquement à détecter la transition vivant->mort d'un rapport à l'autre
        // (PlayerReport.Died), pas un historique. Remis à zéro à chaque nouvelle session
        // comme _lastKnownDay/_lastKnownSeason ci-dessus, pour la même raison : reprendre
        // une session ne doit pas compter comme une mort un joueur déjà mort au moment où
        // ce mod recommence à observer.
        private readonly Dictionary<string, bool> _lastKnownDead = new Dictionary<string, bool>();

        // ZNet.SetServer peut être appelé plus d'une fois par session (transitions de
        // scène) -- l'annonce Discord de démarrage ne doit partir qu'une fois.
        private bool _serverStartAnnounced;

        // ServerSync (voir mods/_shared/ConfigSync.cs) : ForcePublicPosition est
        // volontairement le seul réglage inscrit ici. Jamais ServerToken -- AddConfigEntry
        // diffuse la valeur à chaque client connecté dès qu'elle change, ce qui enverrait
        // le vrai jeton du serveur à tout le monde.
        private readonly ConfigSync _configSync = new ConfigSync(PluginGuid) { DisplayName = PluginName, CurrentVersion = PluginVersion };

        private Harmony _harmony;
        private Coroutine _reportLoop;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            _bootRealtime = Time.realtimeSinceStartup;

            _apiBaseUrl = Config.Bind(
                "Api",
                "ApiBaseUrl",
                "http://127.0.0.1:3000",
                "Base URL of the Fedoheim API, no trailing slash.");

            _serverToken = Config.Bind(
                "Api",
                "ServerToken",
                "",
                "Shared secret for this modpack profile (Profiles page in the launcher, admin only -- 'Regenerate token'). Identifies which profile this server reports as, so no separate slug setting is needed here. Reports are rejected without it. Keep it secret.");

            _syncIntervalSeconds = Config.Bind(
                "Api",
                "SyncIntervalSeconds",
                30f,
                new ConfigDescription(
                    "How often (in seconds) this mod talks to the API -- reporting the connected player list today, and in the future also picking up anything the API needs to tell the game (this mod isn't just a player-list reporter).",
                    new AcceptableValueRange<float>(10f, 300f)));

            _startingGracePeriodSeconds = Config.Bind(
                "Api",
                "StartingGracePeriodSeconds",
                60f,
                new ConfigDescription(
                    "How long (in seconds) after this plugin loads to keep reporting 'starting' instead of 'online' on the launcher's home page, to give a heavily modded server time to fully boot before being shown as ready to join. Increase this if your server has a lot of mods and takes longer than this to start.",
                    new AcceptableValueRange<float>(0f, 600f)));

            // Nom envoyé pour chaque biome, affiché tel quel par le launcher -- éditer ce
            // .cfg pour y mettre sa propre traduction (ex: français), comme pour n'importe
            // quel autre texte affiché au joueur dans les autres mods (voir mods/CLAUDE.md).
            _biomeMeadows = Config.Bind("Biomes", "MeadowsName", "Meadows", "Display name sent for the Meadows biome. Edit to translate (e.g. French).");
            _biomeBlackForest = Config.Bind("Biomes", "BlackForestName", "Black Forest", "Display name sent for the Black Forest biome. Edit to translate (e.g. French).");
            _biomeSwamp = Config.Bind("Biomes", "SwampName", "Swamp", "Display name sent for the Swamp biome. Edit to translate (e.g. French).");
            _biomeMountain = Config.Bind("Biomes", "MountainName", "Mountains", "Display name sent for the Mountain biome. Edit to translate (e.g. French).");
            _biomePlains = Config.Bind("Biomes", "PlainsName", "Plains", "Display name sent for the Plains biome. Edit to translate (e.g. French).");
            _biomeAshLands = Config.Bind("Biomes", "AshLandsName", "Ashlands", "Display name sent for the Ashlands biome. Edit to translate (e.g. French).");
            _biomeDeepNorth = Config.Bind("Biomes", "DeepNorthName", "Deep North", "Display name sent for the Deep North biome. Edit to translate (e.g. French).");
            _biomeOcean = Config.Bind("Biomes", "OceanName", "Ocean", "Display name sent for the Ocean biome. Edit to translate (e.g. French).");
            _biomeMistlands = Config.Bind("Biomes", "MistlandsName", "Mistlands", "Display name sent for the Mistlands biome. Edit to translate (e.g. French).");

            // Rapportée seulement si le mod shudnal/Seasons est aussi présent sur ce
            // serveur (voir SeasonReporting.cs, dépendance douce -- absente sinon,
            // sans erreur). Même principe que les noms de biome ci-dessus : la valeur
            // anglaise brute de Seasons n'est jamais envoyée telle quelle à l'API,
            // éditer ce .cfg pour traduire.
            _seasonSpring = Config.Bind("Seasons", "SpringName", "Spring", "Display name sent for the Spring season (requires the Seasons mod). Edit to translate (e.g. French).");
            _seasonSummer = Config.Bind("Seasons", "SummerName", "Summer", "Display name sent for the Summer season (requires the Seasons mod). Edit to translate (e.g. French).");
            _seasonFall = Config.Bind("Seasons", "FallName", "Fall", "Display name sent for the Fall season (requires the Seasons mod). Edit to translate (e.g. French).");
            _seasonWinter = Config.Bind("Seasons", "WinterName", "Winter", "Display name sent for the Winter season (requires the Seasons mod). Edit to translate (e.g. French).");

            _showClockOverlay = Config.Bind(
                "Time",
                "ShowClockOverlay",
                true,
                "Shows a small in-game clock (HH:MM, following the day/night cycle) at the top-center of the screen. Purely a local HUD addition -- works on any installation with a local player (client or host), no ServerToken needed.");

            _timeOffsetHours = Config.Bind(
                "Time",
                "TimeOffsetHours",
                0f,
                new ConfigDescription(
                    "Shifts the displayed clock (both the HUD overlay and the value sent to the API/launcher) by this many hours, in case it doesn't match what the sky looks like (e.g. it reads midday while it visually looks like dawn) -- purely cosmetic, has no effect on the actual day/night cycle.",
                    new AcceptableValueRange<float>(-12f, 12f)));

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

            _forcePublicPosition = Config.Bind(
                "Players",
                "ForcePublicPosition",
                true,
                "Forces every connected player's 'Public position' setting on for this session (Options > Game), so they show up on each other's map and a biome can be reported for everyone -- their own local setting is left untouched, this only affects what this server sees for as long as they're connected here. Locked: a connecting player can't override this from their own .cfg, only the server admin controls it.");
            _configSync.AddConfigEntry(_forcePublicPosition);
            // Toujours verrouillé, pas une option -- un joueur ne doit jamais pouvoir
            // désactiver ça pour lui-même depuis son propre .cfg local. N'affecte pas
            // l'admin du serveur lui-même (ConfigSync.IsAdmin reste vrai côté serveur,
            // qui fait toujours autorité sur sa propre valeur).
            _configSync.IsLocked = true;

            _discordWebhookUrl = Config.Bind(
                "Discord",
                "WebhookUrl",
                "",
                "Discord webhook URL (Server Settings > Integrations > Webhooks). Keep it secret: anyone who has it can post in your channel. Unlike ServerToken above, this one can safely be filled in on every installation (including players') if you want player deaths -- which only fire on that player's own client -- to also be logged; see the README.");

            _logPlayerConnected = Config.Bind(
                "Discord",
                "LogPlayerConnected",
                true,
                "Logs when a player connects. Only fires on the server (or a client hosting the game).");
            _playerConnectedTemplate = Config.Bind(
                "Discord",
                "PlayerConnectedTemplate",
                "**{player}** connected.",
                "Message posted when a player connects. {player} is replaced with their name.");

            _logPlayerDisconnected = Config.Bind(
                "Discord",
                "LogPlayerDisconnected",
                true,
                "Logs when a player disconnects. Only fires on the server (or a client hosting the game).");
            _playerDisconnectedTemplate = Config.Bind(
                "Discord",
                "PlayerDisconnectedTemplate",
                "**{player}** disconnected.",
                "Message posted when a player disconnects. {player} is replaced with their name.");

            _logPlayerDeath = Config.Bind(
                "Discord",
                "LogPlayerDeath",
                true,
                "Logs when a player dies. This fires on whichever machine actually simulates that player's character (their own client, or the host if they are the host) -- for every player's death to be logged, every player needs a webhook configured here.");
            _playerDeathTemplate = Config.Bind(
                "Discord",
                "PlayerDeathTemplate",
                "**{player}** died ({cause}).",
                "Message posted when a player dies. {player} is replaced with their name, {cause} with the cause of death (drowning, fall damage, an attacker's name, etc.).");

            _logServerStarted = Config.Bind(
                "Discord",
                "LogServerStarted",
                true,
                "Logs once the server (or a hosting client) finishes starting up.");
            _serverStartedTemplate = Config.Bind(
                "Discord",
                "ServerStartedTemplate",
                "Server started (world: **{world}**).",
                "Message posted when the server starts. {world} is replaced with the world name.");

            _logServerStopped = Config.Bind(
                "Discord",
                "LogServerStopped",
                true,
                "Logs when the server (or a hosting client) shuts down. Not guaranteed to arrive if the process is force-killed.");
            _serverStoppedTemplate = Config.Bind(
                "Discord",
                "ServerStoppedTemplate",
                "Server stopped.",
                "Message posted when the server stops.");

            _logWorldSaved = Config.Bind(
                "Discord",
                "LogWorldSaved",
                true,
                "Logs when the world finishes saving. Only fires on the server (or a hosting client).");
            _worldSavedTemplate = Config.Bind(
                "Discord",
                "WorldSavedTemplate",
                "World saved.",
                "Message posted when the world finishes saving.");

            _logNewDay = Config.Bind(
                "Discord",
                "LogNewDay",
                true,
                "Logs when a new in-game day begins (at dawn, following EnvMan's own day counter). Only fires on the server (or a hosting client).");
            _newDayTemplate = Config.Bind(
                "Discord",
                "NewDayTemplate",
                "Day **{day}** has begun.",
                "Message posted when a new in-game day begins. {day} is replaced with the day number.");

            _logSeasonChanged = Config.Bind(
                "Discord",
                "LogSeasonChanged",
                true,
                "Logs when the season changes (requires the Seasons mod -- simply never fires without it). Only fires on the server (or a hosting client).");
            _seasonChangedTemplate = Config.Bind(
                "Discord",
                "SeasonChangedTemplate",
                "The season has changed to **{season}**.",
                "Message posted when the season changes. {season} is replaced with the new season's display name (see [Seasons] above for translating it).");

            _logAdminMessage = Config.Bind(
                "Discord",
                "LogAdminMessage",
                true,
                "Logs an admin message broadcast from the launcher's Admin > Serveur page (also shown on every connected player's screen). Only fires on the server (or a client hosting the game).");
            _adminMessageTemplate = Config.Bind(
                "Discord",
                "AdminMessageTemplate",
                "📢 {message}",
                "Message posted when an admin broadcasts a message from the launcher. {message} is replaced with the broadcast text.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            // Envoyé avant même de savoir si cette instance sera serveur ou client --
            // sans effet sur un client normal (ServerToken y reste vide par convention,
            // voir README, donc ce rapport est simplement sauté). Sur le vrai serveur,
            // c'est le tout premier signal possible : la suite (ZNet, monde) peut encore
            // mettre du temps à charger derrière, d'où "starting" plutôt que "online".
            Report(new List<PlayerReport>(), "starting");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        // Fire-and-forget comme les rapports périodiques : le process a le temps de
        // tourner encore un peu après le début d'une fermeture demandée par le joueur
        // (contrairement à OnServerStopping ci-dessous, juste avant la destruction
        // effective de ZNet, où ce n'est plus vrai -- voir ce commentaire).
        private void OnApplicationQuit()
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                Report(new List<PlayerReport>(), "stopping");
            }
        }

        public void OnServerStarted()
        {
            if (_reportLoop != null)
            {
                return;
            }

            // Repart de zéro à chaque nouvelle session (voir CheckDayAndSeasonChange) --
            // sans ça, reprendre sur un monde différent (jour/saison différents de la
            // session précédente, encore en mémoire ici) déclencherait une fausse
            // annonce de changement dès le premier relevé. Fait une seule fois par
            // session réelle (avant que _reportLoop ne soit posé ci-dessous), pas à
            // chaque rechargement de scène qui rappelle aussi cette méthode.
            _lastKnownDay = null;
            _lastKnownSeason = null;
            _lastKnownDead.Clear();

            _reportLoop = StartCoroutine(ReportLoop());
        }

        // Contrairement à Report() ci-dessous (fire-and-forget, adapté aux rapports
        // périodiques tant que le process continue de tourner), celui-ci est attendu de
        // façon bloquante (voir ReportBlocking) : le process peut se terminer dans les
        // instants qui suivent OnDestroy, ce qui tuerait une tâche encore en vol avant
        // qu'elle n'atteigne l'API -- observé en pratique même sur un arrêt propre du
        // jeu, pas seulement un crash. La péremption du dernier rapport côté API (voir
        // onlinePlayers.ts) reste le filet de sécurité si même ça échoue (vrai crash,
        // coupure réseau...).
        public void OnServerStopping()
        {
            if (_reportLoop != null)
            {
                StopCoroutine(_reportLoop);
                _reportLoop = null;
            }

            ReportBlocking(new List<PlayerReport>(), "stopping");
        }

        // Vérifié à chaque frame (voir Update ci-dessous) plutôt que déclenché une seule
        // fois sur un événement précis (ex: ZNet.SetServer/Game.Start) : rien ne garantit
        // l'ordre d'exécution entre objets Unity différents dans une même frame, donc
        // Minimap.instance pouvait encore être nul au moment où un patch ponctuel
        // s'exécutait -- observé en pratique (case jamais forcée). Ici, dès que Minimap
        // existe (quelle que soit la frame), la frame suivante la force -- et ne fait
        // plus rien une fois que c'est fait (`isOn` déjà true).
        public void ForceOwnPublicPosition()
        {
            if (!ForcePublicPosition || Minimap.instance == null || Minimap.instance.m_publicPosition == null)
            {
                return;
            }

            if (!Minimap.instance.m_publicPosition.isOn)
            {
                Minimap.instance.m_publicPosition.isOn = true;
                Minimap.instance.OnTogglePublicPosition();
            }
        }

        // Reproduit un vrai clic sur la case "Position publique" des options du jeu via
        // l'API publique de Minimap (ForceOwnPublicPosition), pour passer par le chemin
        // normal du jeu (RPC, diffusion aux autres clients...) plutôt que d'espérer qu'un
        // champ forcé côté serveur seul (ForcePublicPositionOnPeers) suffise. Pas de
        // vérification IsServer() : nécessaire aussi pour l'hôte d'une partie
        // solo/hébergée, jamais son propre "pair" côté serveur -- voir cette dernière.
        private void Update()
        {
            DrainMainThreadActions();

            try
            {
                ForceOwnPublicPosition();
            }
            catch (Exception e)
            {
                // Ne devrait arriver que si Minimap est en train d'être détruite pile
                // entre les deux vérifications de null ci-dessus (transition de scène) --
                // rattrapé pour ne jamais spammer la console à chaque frame si ça arrive.
                Log?.LogWarning($"FedoServerTools: ForceOwnPublicPosition failed: {e.Message}");
            }

            RefreshClockOverlay();
            CheckDayAndSeasonChange();
        }

        // Indépendant de RefreshClockOverlay ci-dessous (qui ne tourne pas du tout si
        // ShowClockOverlay est désactivé) -- un jour/une saison qui change doit être
        // annoncé que l'horloge soit affichée ou non. Throttlé à 5s (pas la peine de
        // vérifier à un rythme plus fin pour un événement qui n'arrive qu'une fois toutes
        // les ~30 min de jeu en réel). Serveur/hôte seulement, comme les autres logs de
        // session (voir SendDiscordMessage).
        private void CheckDayAndSeasonChange()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            _dayAndSeasonCheckTimer -= Time.deltaTime;
            if (_dayAndSeasonCheckTimer > 0f)
            {
                return;
            }

            _dayAndSeasonCheckTimer = 5f;

            if (EnvMan.instance != null)
            {
                int currentDay = EnvMan.instance.GetDay();
                if (_lastKnownDay.HasValue && _lastKnownDay.Value != currentDay)
                {
                    AnnounceNewDay(currentDay);
                }

                _lastKnownDay = currentDay;
            }

            // GetCurrentSeasonName() (voir plus bas) renvoie déjà `null` si le mod Seasons
            // n'est pas installé -- dans ce cas `_lastKnownSeason` ne bouge jamais et
            // cette annonce ne se déclenche donc simplement jamais, comme le reste du
            // reporting de saison.
            string currentSeason = GetCurrentSeasonName();
            if (currentSeason != null)
            {
                if (_lastKnownSeason != null && _lastKnownSeason != currentSeason)
                {
                    AnnounceSeasonChanged(currentSeason);
                }

                _lastKnownSeason = currentSeason;
            }
        }

        public void AnnounceNewDay(int day)
        {
            SendDiscordMessage(_logNewDay, DiscordEventKind.NewDay, _newDayTemplate, null, day: day);
        }

        public void AnnounceSeasonChanged(string season)
        {
            SendDiscordMessage(_logSeasonChanged, DiscordEventKind.SeasonChanged, _seasonChangedTemplate, null, season: season);
        }

        // Throttlé à ~1x/seconde (pas la peine de reformater une chaîne à chaque frame
        // pour une minute qui ne change pas plus vite que ça) -- voir ClockOverlay.cs
        // pour la création/le positionnement de l'élément UI lui-même.
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

        // Appelé par ClockOverlay.DragHandler à la fin d'un glissement (voir ClockOverlay.
        // cs) -- écrit directement dans le .cfg local, pas de round-trip serveur.
        public void SaveClockPosition(Vector2 anchoredPosition)
        {
            _clockPositionX.Value = anchoredPosition.x;
            _clockPositionY.Value = anchoredPosition.y;
        }

        private IEnumerator ReportLoop()
        {
            var wait = new WaitForSecondsRealtime(Mathf.Max(1f, _syncIntervalSeconds.Value));
            while (true)
            {
                bool stillStarting = Time.realtimeSinceStartup - _bootRealtime < _startingGracePeriodSeconds.Value;
                Report(GetConnectedPlayers(), stillStarting ? "starting" : "online");
                yield return wait;
            }
        }

        // ZNet.GetPlayerList() est l'API publique du jeu lui-même (celle qui alimente son
        // propre panneau "joueurs"), pas une liste reconstruite à la main depuis des
        // événements de connexion/déconnexion -- elle inclut donc aussi l'hôte en partie
        // solo/hébergée, pas seulement les pairs réseau distants.
        private static List<PlayerReport> GetConnectedPlayers()
        {
            if (ZNet.instance == null)
            {
                return new List<PlayerReport>();
            }

            if (Instance.ForcePublicPosition)
            {
                ForcePublicPositionOnPeers();
            }

            // Player.GetAllPlayers() donne les instances réellement simulées côté serveur
            // (armure calculée depuis leur équipement synchronisé) -- indexées par nom,
            // qui est déjà ce qu'on utilise pour identifier un joueur dans PlayerInfo.
            var playersByName = new Dictionary<string, Player>();
            foreach (var p in Player.GetAllPlayers())
            {
                string name = p.GetPlayerName();
                if (!string.IsNullOrEmpty(name))
                {
                    playersByName[name] = p;
                }
            }

            var result = new List<PlayerReport>();
            foreach (var info in ZNet.instance.GetPlayerList())
            {
                if (string.IsNullOrEmpty(info.m_name))
                {
                    continue;
                }

                playersByName.TryGetValue(info.m_name, out var player);
                result.Add(new PlayerReport(
                    info.m_name,
                    GetBiomeName(info),
                    GetArmor(player),
                    PeerSteamId.Resolve(info.m_name),
                    JustDied(info.m_name, player)));
            }

            return result;
        }

        // Écrit directement `m_publicRefPos` (le champ réel du jeu, public -- voir
        // Minimap.OnTogglePublicPosition côté client) sur chaque pair actuellement
        // connecté, pour de vrai cette fois : contrairement à l'ancienne version qui
        // patchait GetPlayerList() en Harmony pour réécrire sa liste de retour (risque
        // de corrompre une liste partagée avec d'autres systèmes, voir CHANGELOG), on
        // modifie ici un champ précis sur un objet précis, à l'origine de la donnée --
        // ça a un vrai effet en jeu (le joueur apparaît sur la carte des autres), pas
        // seulement sur ce que ce mod rapporte. Rappelé à chaque cycle (pas juste à la
        // connexion) pour absorber tout pair rejoint entre deux appels.
        //
        // `m_characterID.IsNone()` exclut un pair dont le personnage n'a pas encore
        // fini de spawn (ex: vient tout juste de se connecter) -- forcer le flag avant
        // ce moment-là faisait logguer en boucle "Character ID for player (...) was
        // 0:0. Skipping." (un rapport interne du jeu qui tente d'inclure ce pair dans
        // la liste des positions publiques avant qu'il ait une ZDOID valide).
        private static void ForcePublicPositionOnPeers()
        {
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (!peer.m_characterID.IsNone())
                {
                    peer.m_publicRefPos = true;
                }
            }
        }

        private static string GetBiomeName(ZNet.PlayerInfo player)
        {
            if (!player.m_publicPosition || WorldGenerator.instance == null)
            {
                return null;
            }

            try
            {
                // Heightmap.FindBiome en priorité : c'est le biome réellement affiché au
                // joueur (post-lissage des bordures de la zone déjà chargée), mais renvoie
                // silencieusement `None` si cette zone n'est pas chargée en mémoire à cet
                // instant. Secours sur WorldGenerator.GetBiome (calcul procédural brut,
                // celui qui a servi à générer le terrain) dans ce cas -- toujours
                // disponible, mais peut se tromper près d'une côte (une bordure de plage
                // en Forêt Noire peut y être classée Océan avant lissage).
                var biome = Heightmap.FindBiome(player.m_position);
                if (biome == Heightmap.Biome.None)
                {
                    biome = WorldGenerator.instance.GetBiome(player.m_position);
                }

                return biome != Heightmap.Biome.None ? ResolveBiomeName(biome) : null;
            }
            catch (Exception e)
            {
                Log?.LogWarning($"FedoServerTools: failed to resolve biome for {player.m_name}: {e.Message}");
                return null;
            }
        }

        // Détecte la transition vivant->mort d'un rapport à l'autre (voir _lastKnownDead
        // ci-dessus) pour compter chaque mort une seule fois, même si le joueur reste sur
        // son écran de tombe pendant plusieurs cycles de rapport (30s par défaut) avant de
        // respawn. `player == null` (personnage introuvable côté serveur) laisse l'état
        // précédent inchangé plutôt que de le remettre à "vivant" -- un simple souci de
        // résolution ne doit pas effacer une mort déjà détectée ni permettre d'en recompter
        // une au rapport suivant.
        private static bool JustDied(string name, Player player)
        {
            if (player == null)
            {
                return false;
            }

            bool isDeadNow;
            try
            {
                isDeadNow = player.IsDead();
            }
            catch (Exception e)
            {
                Log?.LogWarning($"FedoServerTools: failed to resolve death state for {name}: {e.Message}");
                return false;
            }

            Instance._lastKnownDead.TryGetValue(name, out bool wasDead);
            Instance._lastKnownDead[name] = isDeadNow;
            return isDeadNow && !wasDead;
        }

        private static int? GetArmor(Player player)
        {
            if (player == null)
            {
                return null;
            }

            try
            {
                return Mathf.RoundToInt(player.GetBodyArmor());
            }
            catch (Exception e)
            {
                Log?.LogWarning($"FedoServerTools: failed to resolve armor for {player.GetPlayerName()}: {e.Message}");
                return null;
            }
        }

        // Le texte envoyé vient directement du .cfg (voir Awake) -- le launcher affiche
        // cette valeur telle quelle, il n'y a pas de traduction/mapping côté API ou
        // launcher. `Heightmap.Biome` est un [Flags] mais un point du monde n'appartient
        // jamais qu'à une seule des valeurs ci-dessous (`None` est filtré par l'appelant).
        private static string ResolveBiomeName(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.Meadows: return Instance._biomeMeadows.Value;
                case Heightmap.Biome.BlackForest: return Instance._biomeBlackForest.Value;
                case Heightmap.Biome.Swamp: return Instance._biomeSwamp.Value;
                case Heightmap.Biome.Mountain: return Instance._biomeMountain.Value;
                case Heightmap.Biome.Plains: return Instance._biomePlains.Value;
                case Heightmap.Biome.AshLands: return Instance._biomeAshLands.Value;
                case Heightmap.Biome.DeepNorth: return Instance._biomeDeepNorth.Value;
                case Heightmap.Biome.Ocean: return Instance._biomeOcean.Value;
                case Heightmap.Biome.Mistlands: return Instance._biomeMistlands.Value;
                default: return biome.ToString();
            }
        }

        // Saison actuelle du serveur -- pas une donnée par joueur comme le biome/
        // l'armure, une seule valeur par rapport. `null` si le mod Seasons n'est pas
        // installé sur ce serveur (voir SeasonReporting.IsLoaded, dépendance douce) ou
        // n'a pas encore de monde chargé -- rapporté tel quel, jamais une erreur.
        private static string GetCurrentSeasonName()
        {
            if (!SeasonReporting.IsLoaded)
            {
                return null;
            }

            switch (SeasonReporting.GetCurrentSeasonKey(Log))
            {
                case "Spring": return Instance._seasonSpring.Value;
                case "Summer": return Instance._seasonSummer.Value;
                case "Fall": return Instance._seasonFall.Value;
                case "Winter": return Instance._seasonWinter.Value;
                default: return null;
            }
        }

        // Horloge en jeu au format HH:MM, dérivée de EnvMan.GetDayFraction() -- déjà la
        // fraction (0..1) du jour en cours utilisée en interne par le jeu pour
        // l'éclairage, donc déjà correctement calée sur le vrai cycle jour/nuit (pas de
        // recalcul maison depuis ZNet.GetTimeSeconds() qui ignorerait le décalage du
        // début de journée). `TimeOffsetHours` permet de recaler l'affichage si jamais il
        // ne correspond pas visuellement au ciel (purement cosmétique). Une seule valeur
        // par rapport, comme la saison ci-dessus -- pas une donnée par joueur.
        private static string GetCurrentGameTime()
        {
            if (EnvMan.instance == null)
            {
                return null;
            }

            try
            {
                float totalHours = EnvMan.instance.GetDayFraction() * 24f + Instance._timeOffsetHours.Value;
                totalHours = ((totalHours % 24f) + 24f) % 24f;
                int hour = (int)totalHours;
                int minute = (int)((totalHours - hour) * 60f);
                return $"{hour:D2}:{minute:D2}";
            }
            catch (Exception e)
            {
                Log?.LogWarning($"FedoServerTools: failed to resolve current game time: {e.Message}");
                return null;
            }
        }

        // `status` : "starting" (juste chargé/pas encore prêt), "online" (rapport
        // périodique normal), "stopping" (arrêt en cours) -- voir onlinePlayers.ts pour
        // comment l'API en déduit un "offline" par péremption quand plus rien n'arrive.
        private void Report(List<PlayerReport> players, string status)
        {
            string apiBaseUrl = _apiBaseUrl.Value;
            string serverToken = _serverToken.Value;

            if (string.IsNullOrWhiteSpace(serverToken))
            {
                if (!_missingTokenWarned)
                {
                    Logger.LogWarning("FedoServerTools: no server token configured (see fedo.servertools.cfg), report skipped.");
                    _missingTokenWarned = true;
                }

                return;
            }

            _missingTokenWarned = false;

            var logger = Logger;
            string season = GetCurrentSeasonName();
            string time = GetCurrentGameTime();

            Task.Run(async () =>
            {
                try
                {
                    string responseBody = await OnlinePlayersReporter.ReportAsync(apiBaseUrl, serverToken, players, status, season, time).ConfigureAwait(false);
                    RunOnMainThread(() => ServerCommands.ApplyFromReportResponse(responseBody, logger));
                    if (_consecutiveReportFailures > 0)
                    {
                        logger.LogInfo($"FedoServerTools: online players report recovered after {_consecutiveReportFailures} consecutive failure(s).");
                        _consecutiveReportFailures = 0;
                    }
                }
                catch (Exception e)
                {
                    _consecutiveReportFailures++;
                    // Le rapport tourne toutes les SyncIntervalSeconds (30s par défaut) --
                    // une API injoignable en continu (mauvais ApiBaseUrl, API down...)
                    // spammerait sinon un LogError avec stack complète indéfiniment.
                    // Stack complète seulement au premier échec, puis un rappel bref de
                    // loin en loin tant que ça ne se rétablit pas.
                    if (_consecutiveReportFailures == 1)
                    {
                        logger.LogError($"FedoServerTools: failed to report online players: {e}");
                    }
                    else if (_consecutiveReportFailures % 20 == 0)
                    {
                        logger.LogWarning($"FedoServerTools: still failing to report online players ({_consecutiveReportFailures} consecutive failures, last error: {e.Message}).");
                    }
                }
            });
        }

        // Utilisé uniquement pour le tout dernier rapport (arrêt du serveur) : bloque le
        // thread principal le temps de l'appel (borné par le timeout HTTP côté
        // OnlinePlayersReporter, quelques secondes) plutôt que de lancer une tâche en
        // fire-and-forget qui pourrait ne jamais s'exécuter si le process se termine
        // juste après OnDestroy -- acceptable ici puisque le jeu est de toute façon en
        // train de s'arrêter.
        private void ReportBlocking(List<PlayerReport> players, string status)
        {
            string apiBaseUrl = _apiBaseUrl.Value;
            string serverToken = _serverToken.Value;

            if (string.IsNullOrWhiteSpace(serverToken))
            {
                return;
            }

            try
            {
                OnlinePlayersReporter.ReportAsync(apiBaseUrl, serverToken, players, status, GetCurrentSeasonName(), GetCurrentGameTime())
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception e)
            {
                Logger.LogError($"FedoServerTools: failed to report server stopping: {e}");
            }
        }

        // À partir d'ici : intégration Discord (voir DiscordWebhook.cs), sans rapport
        // avec le reporting vers l'API Fedoheim ci-dessus -- un webhook, pas ServerToken.

        public void AnnouncePlayerConnected(string playerName)
        {
            SendDiscordMessage(_logPlayerConnected, DiscordEventKind.PlayerConnected, _playerConnectedTemplate, playerName);
        }

        public void AnnouncePlayerDisconnected(string playerName)
        {
            SendDiscordMessage(_logPlayerDisconnected, DiscordEventKind.PlayerDisconnected, _playerDisconnectedTemplate, playerName);
        }

        // L'hôte d'une partie solo/hébergée n'a pas de ZNetPeer le représentant (voir
        // PeerSteamId.cs, même limitation déjà rencontrée pour la liaison de compte) --
        // ZNetPeerInfoAnnouncePatch/ZNetDisconnectAnnouncePatch (voir
        // ZNetJoinLeaveAnnouncePatches.cs) ne se déclenchent donc jamais pour lui, et
        // sans ce cas à part, l'hôte ne voyait jamais son propre connect/disconnect
        // annoncé sur Discord (seul "Serveur démarré/arrêté" apparaissait).
        //
        // Appelé depuis Hud.Awake (voir ZNetLifecyclePatches.cs), pas depuis
        // ZNet.SetServer comme AnnounceServerStarted ci-dessous -- testé en jeu : à ce
        // stade précoce, Game.instance.GetPlayerProfile() ne renvoie encore rien
        // d'exploitable (le profil n'est pleinement disponible qu'une fois le
        // personnage effectivement chargé dans la partie), donc l'annonce ne partait
        // jamais. Hud.Awake garantit que ce n'est plus le cas. `_hostConnectAnnounced`
        // (jamais réinitialisé, même principe que `_serverStartAnnounced`) évite de
        // ré-annoncer à chaque rechargement de scène (Hud.Awake se redéclenche à
        // chacun) dans une même session.
        private bool _hostConnectAnnounced;

        public void AnnounceHostConnected()
        {
            if (_hostConnectAnnounced)
            {
                return;
            }

            string hostName = Game.instance?.GetPlayerProfile()?.GetName();
            if (string.IsNullOrEmpty(hostName))
            {
                return;
            }

            _hostConnectAnnounced = true;
            AnnouncePlayerConnected(hostName);
        }

        public void AnnounceHostDisconnected()
        {
            string hostName = Game.instance?.GetPlayerProfile()?.GetName();
            if (!string.IsNullOrEmpty(hostName))
            {
                AnnouncePlayerDisconnected(hostName);
            }
        }

        public void AnnouncePlayerDied(string playerName, string cause)
        {
            SendDiscordMessage(_logPlayerDeath, DiscordEventKind.PlayerDeath, _playerDeathTemplate, playerName, cause: cause);
        }

        public void AnnounceServerStarted(string worldName)
        {
            if (_serverStartAnnounced)
            {
                return;
            }

            _serverStartAnnounced = true;
            SendDiscordMessage(_logServerStarted, DiscordEventKind.ServerStarted, _serverStartedTemplate, null, worldName);
        }

        public void AnnounceWorldSaved()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            SendDiscordMessage(_logWorldSaved, DiscordEventKind.WorldSaved, _worldSavedTemplate, null);
        }

        // Posé par un admin depuis le launcher (Admin > Serveur) -- voir
        // ServerCommands.ApplyBroadcastMessage, qui appelle aussi BroadcastMessage.Send
        // pour l'afficher sur l'écran de chaque joueur, indépendamment de ce log Discord.
        public void AnnounceAdminMessage(string message)
        {
            SendDiscordMessage(_logAdminMessage, DiscordEventKind.AdminMessage, _adminMessageTemplate, null, broadcastMessage: message);
        }

        public void AnnounceServerStopped()
        {
            // Fire-and-forget comme les autres annonces : ZNet.OnDestroy peut être appelé
            // pendant de simples transitions de menu (pas seulement un vrai arrêt de
            // serveur), donc on ne doit surtout pas bloquer le thread principal ici --
            // contrairement à ReportBlocking ci-dessus, qui a une bonne raison de le faire.
            SendDiscordMessage(_logServerStopped, DiscordEventKind.ServerStopped, _serverStoppedTemplate, null);
        }

        // Émoji + titre + couleur (décimal, format embed Discord) par type d'événement --
        // inspiré de mods communautaires équivalents (barre de couleur + petit titre à
        // émoji), sans reprendre leur mise en page exacte. Volontairement fixes (pas de
        // ConfigEntry) : contrairement aux `*Template` ci-dessus (le texte réellement
        // affiché, personnalisable), ce ne sont que des éléments de mise en forme.
        private enum DiscordEventKind
        {
            PlayerConnected,
            PlayerDisconnected,
            PlayerDeath,
            ServerStarted,
            ServerStopped,
            WorldSaved,
            NewDay,
            SeasonChanged,
            AdminMessage,
        }

        private static (string Title, int Color) DescribeEventKind(DiscordEventKind kind)
        {
            switch (kind)
            {
                case DiscordEventKind.PlayerConnected: return ("👋 Player Joined", 0x57F287);
                case DiscordEventKind.PlayerDisconnected: return ("🚪 Player Left", 0xED4245);
                case DiscordEventKind.PlayerDeath: return ("💀 Player Died", 0x992D22);
                case DiscordEventKind.ServerStarted: return ("🟢 Server Started", 0x57F287);
                case DiscordEventKind.ServerStopped: return ("🔴 Server Stopped", 0xED4245);
                case DiscordEventKind.WorldSaved: return ("💾 World Saved", 0x25D3E4);
                case DiscordEventKind.NewDay: return ("🌅 New Day", 0xF1C40F);
                case DiscordEventKind.SeasonChanged: return ("🍂 Season Changed", 0x9B59B6);
                case DiscordEventKind.AdminMessage: return ("Serveur Fedoheim Message", 0xF1C40F);
                default: return ("Fedoheim", 0x25D3E4);
            }
        }

        private void SendDiscordMessage(
            ConfigEntry<bool> toggle,
            DiscordEventKind kind,
            ConfigEntry<string> template,
            string playerName,
            string worldName = null,
            string cause = null,
            int? day = null,
            string season = null,
            string broadcastMessage = null)
        {
            if (!toggle.Value)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_discordWebhookUrl.Value))
            {
                Logger.LogWarning("FedoServerTools: no Discord webhook configured (see fedo.servertools.cfg), message not sent.");
                return;
            }

            string message = template.Value;
            if (playerName != null)
            {
                message = message.Replace("{player}", playerName);
            }
            if (worldName != null)
            {
                message = message.Replace("{world}", worldName);
            }
            if (cause != null)
            {
                message = message.Replace("{cause}", cause);
            }
            if (day.HasValue)
            {
                message = message.Replace("{day}", day.Value.ToString());
            }
            if (season != null)
            {
                message = message.Replace("{season}", season);
            }
            if (broadcastMessage != null)
            {
                message = message.Replace("{message}", broadcastMessage);
            }

            var (title, color) = DescribeEventKind(kind);
            var embed = new DiscordEmbed { Title = title, Description = message, Color = color };
            if (playerName != null)
            {
                embed.Fields.Add(new DiscordEmbedField("Player", playerName));
            }
            if (cause != null)
            {
                embed.Fields.Add(new DiscordEmbedField("Cause", cause));
            }
            if (day.HasValue)
            {
                embed.Fields.Add(new DiscordEmbedField("Day", day.Value.ToString()));
            }
            if (season != null)
            {
                embed.Fields.Add(new DiscordEmbedField("Season", season));
            }

            // Le nom du monde -- passé explicitement pour "Serveur démarré" (voir
            // AnnounceServerStarted, seul événement où ZNet.GetWorldName() peut lever une
            // NullReferenceException juste après SetServer), sinon relu directement :
            // toujours disponible aux autres points d'appel (bien après SetServer).
            string footerWorldName = worldName ?? GetWorldNameSafe();
            embed.FooterText = footerWorldName != null ? $"Fedoheim · {footerWorldName}" : "Fedoheim";

            string webhookUrl = _discordWebhookUrl.Value;
            var logger = Logger;

            Task.Run(async () =>
            {
                try
                {
                    await DiscordWebhook.PostEmbedAsync(webhookUrl, embed);
                }
                catch (Exception e)
                {
                    logger.LogError($"FedoServerTools: failed to send Discord message: {e}");
                }
            });
        }

        private static string GetWorldNameSafe()
        {
            try
            {
                return ZNet.instance != null ? ZNet.instance.GetWorldName() : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
