using System;
using System.Collections;
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
        private ConfigEntry<float> _reportIntervalSeconds;

        private ConfigEntry<string> _biomeMeadows;
        private ConfigEntry<string> _biomeBlackForest;
        private ConfigEntry<string> _biomeSwamp;
        private ConfigEntry<string> _biomeMountain;
        private ConfigEntry<string> _biomePlains;
        private ConfigEntry<string> _biomeAshLands;
        private ConfigEntry<string> _biomeDeepNorth;
        private ConfigEntry<string> _biomeOcean;
        private ConfigEntry<string> _biomeMistlands;

        private ConfigEntry<bool> _forcePublicPosition;

        // Lu par ForcePublicPositionPatch (classe statique) -- voir ce fichier pour le
        // pourquoi (le réglage "Position publique" est côté client, ce mod ne peut pas le
        // changer, seulement neutraliser son effet sur ce que ce serveur voit).
        public bool ForcePublicPosition => _forcePublicPosition.Value;

        private Harmony _harmony;
        private Coroutine _reportLoop;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

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

            _reportIntervalSeconds = Config.Bind(
                "Api",
                "ReportIntervalSeconds",
                30f,
                new ConfigDescription(
                    "How often (in seconds) to report the connected player list to the API.",
                    new AcceptableValueRange<float>(10f, 300f)));

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

            _forcePublicPosition = Config.Bind(
                "Players",
                "ForcePublicPosition",
                true,
                "Treat every player's position as public on this server, regardless of their own 'Public position' setting (Options > Game) -- that setting is stored locally on each player's machine and can't be changed from the server, so this only overrides what this server itself sees. Needed for biome to be reported for every player without asking each of them to enable that setting themselves.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        public void OnServerStarted()
        {
            if (_reportLoop != null)
            {
                return;
            }

            _reportLoop = StartCoroutine(ReportLoop());
        }

        // Fire-and-forget comme les autres événements de fin de vie du serveur (voir
        // FedoDiscordLogs.OnServerStopped) : ZNet.OnDestroy peut survenir pendant de
        // simples transitions de menu, et rien ne garantit que ce dernier rapport parte
        // réellement si le process est tué brutalement -- la péremption du dernier
        // rapport côté API (voir onlinePlayers.ts) rattrape ce cas-là.
        public void OnServerStopping()
        {
            if (_reportLoop != null)
            {
                StopCoroutine(_reportLoop);
                _reportLoop = null;
            }

            Report(new List<PlayerReport>(), online: false);
        }

        private IEnumerator ReportLoop()
        {
            var wait = new WaitForSecondsRealtime(Mathf.Max(1f, _reportIntervalSeconds.Value));
            while (true)
            {
                Report(GetConnectedPlayers(), online: true);
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

            var result = new List<PlayerReport>();
            foreach (var player in ZNet.instance.GetPlayerList())
            {
                if (string.IsNullOrEmpty(player.m_name))
                {
                    continue;
                }

                result.Add(new PlayerReport(player.m_name, GetBiomeName(player)));
            }

            return result;
        }

        // `m_publicPosition` est le même réglage que celui qui décide si un joueur
        // apparaît sur la carte des autres (voir CrossNetworkUserInfo côté jeu) -- on ne
        // calcule/rapporte le biome que si le joueur a lui-même choisi de partager sa
        // position, pour ne jamais contourner ce choix via cette autre voie.
        private static string GetBiomeName(ZNet.PlayerInfo player)
        {
            if (!player.m_publicPosition)
            {
                return null;
            }

            try
            {
                return ResolveBiomeName(Heightmap.FindBiome(player.m_position));
            }
            catch (Exception e)
            {
                Log?.LogWarning($"FedoServerTools: failed to resolve biome for {player.m_name}: {e.Message}");
                return null;
            }
        }

        // Le texte envoyé vient directement du .cfg (voir Awake) -- le launcher affiche
        // cette valeur telle quelle, il n'y a pas de traduction/mapping côté API ou
        // launcher. `Heightmap.Biome` est un [Flags] mais `FindBiome` ne renvoie jamais
        // qu'une seule valeur de la liste ci-dessous (ou `None`, couvert par le défaut).
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

        private void Report(List<PlayerReport> players, bool online)
        {
            string apiBaseUrl = _apiBaseUrl.Value;
            string serverToken = _serverToken.Value;

            if (string.IsNullOrWhiteSpace(serverToken))
            {
                Logger.LogWarning("FedoServerTools: no server token configured (see fedo.servertools.cfg), report skipped.");
                return;
            }

            var logger = Logger;

            Task.Run(async () =>
            {
                try
                {
                    await OnlinePlayersReporter.ReportAsync(apiBaseUrl, serverToken, players, online).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    logger.LogError($"FedoServerTools: failed to report online players: {e}");
                }
            });
        }
    }
}
