using System;
using System.Collections;
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
        private ConfigEntry<float> _syncIntervalSeconds;
        private ConfigEntry<float> _startingGracePeriodSeconds;

        // Nombre d'échecs consécutifs du rapport périodique (API injoignable, mauvais
        // ApiBaseUrl...) -- sert uniquement à espacer les logs, voir Report() ci-dessous.
        private int _consecutiveReportFailures;

        // Idem pour l'avertissement "pas de jeton configuré" -- un seul log tant que
        // ServerToken reste vide, pas un à chaque rapport (SyncIntervalSeconds).
        private bool _missingTokenWarned;

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
                result.Add(new PlayerReport(info.m_name, GetBiomeName(info), GetArmor(player)));
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

            Task.Run(async () =>
            {
                try
                {
                    await OnlinePlayersReporter.ReportAsync(apiBaseUrl, serverToken, players, status, season).ConfigureAwait(false);
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
                OnlinePlayersReporter.ReportAsync(apiBaseUrl, serverToken, players, status, GetCurrentSeasonName()).GetAwaiter().GetResult();
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
            SendDiscordMessage(_logPlayerConnected, _playerConnectedTemplate, playerName);
        }

        public void AnnouncePlayerDisconnected(string playerName)
        {
            SendDiscordMessage(_logPlayerDisconnected, _playerDisconnectedTemplate, playerName);
        }

        public void AnnouncePlayerDied(string playerName, string cause)
        {
            SendDiscordMessage(_logPlayerDeath, _playerDeathTemplate, playerName, cause: cause);
        }

        public void AnnounceServerStarted(string worldName)
        {
            if (_serverStartAnnounced)
            {
                return;
            }

            _serverStartAnnounced = true;
            SendDiscordMessage(_logServerStarted, _serverStartedTemplate, null, worldName);
        }

        public void AnnounceWorldSaved()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            SendDiscordMessage(_logWorldSaved, _worldSavedTemplate, null);
        }

        public void AnnounceServerStopped()
        {
            // Fire-and-forget comme les autres annonces : ZNet.OnDestroy peut être appelé
            // pendant de simples transitions de menu (pas seulement un vrai arrêt de
            // serveur), donc on ne doit surtout pas bloquer le thread principal ici --
            // contrairement à ReportBlocking ci-dessus, qui a une bonne raison de le faire.
            SendDiscordMessage(_logServerStopped, _serverStoppedTemplate, null);
        }

        private void SendDiscordMessage(ConfigEntry<bool> toggle, ConfigEntry<string> template, string playerName, string worldName = null, string cause = null)
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

            string webhookUrl = _discordWebhookUrl.Value;
            var logger = Logger;

            Task.Run(async () =>
            {
                try
                {
                    await DiscordWebhook.PostMessageAsync(webhookUrl, message);
                }
                catch (Exception e)
                {
                    logger.LogError($"FedoServerTools: failed to send Discord message: {e}");
                }
            });
        }
    }
}
