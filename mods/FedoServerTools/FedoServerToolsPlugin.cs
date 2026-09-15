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
        // chaque Update().
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

        // Décalage utilisé pour l'heure envoyée dans le rapport périodique (voir
        // GetCurrentGameTime ci-dessous) -- indépendant de l'horloge à l'écran, qui vit
        // désormais dans FedoClientTools avec son propre réglage du même nom.
        private ConfigEntry<float> _timeOffsetHours;

        // Dernier état vivant/mort connu par nom de joueur (voir GetConnectedPlayers) --
        // sert uniquement à détecter la transition vivant->mort d'un rapport à l'autre
        // (PlayerReport.Died), pas un historique. Remis à zéro à chaque nouvelle session
        // pour la même raison : reprendre une session ne doit pas compter comme une mort
        // un joueur déjà mort au moment où ce mod recommence à observer.
        private readonly Dictionary<string, bool> _lastKnownDead = new Dictionary<string, bool>();

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

            _timeOffsetHours = Config.Bind(
                "Time",
                "TimeOffsetHours",
                0f,
                new ConfigDescription(
                    "Shifts the in-game clock value sent to the API/launcher by this many hours, in case it doesn't match what the sky looks like -- purely cosmetic, has no effect on the actual day/night cycle. Independent of FedoClientTools' own clock overlay setting of the same name.",
                    new AcceptableValueRange<float>(-12f, 12f)));

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

            // Repart de zéro à chaque nouvelle session -- sans ça, reprendre sur un
            // monde différent (joueurs déjà morts la session précédente, encore en
            // mémoire ici) fausserait la détection de transition vivant->mort du tout
            // premier relevé.
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

        private void Update()
        {
            DrainMainThreadActions();
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

        // Horloge en jeu au format HH:MM, envoyée dans le rapport périodique (voir Report/
        // ReportBlocking ci-dessous) -- dérivée de EnvMan.GetDayFraction() -- déjà la
        // fraction (0..1) du jour en cours utilisée en interne par le jeu pour
        // l'éclairage, donc déjà correctement calée sur le vrai cycle jour/nuit.
        // `TimeOffsetHours` permet de recaler l'affichage si jamais il ne correspond pas
        // visuellement au ciel (purement cosmétique). FedoClientTools calcule la même
        // chose de son côté pour son propre affichage local, indépendamment.
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
    }
}
