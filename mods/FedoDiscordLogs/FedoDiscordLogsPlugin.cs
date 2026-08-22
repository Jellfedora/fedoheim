using System;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace FedoDiscordLogs
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FedoDiscordLogsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.discordlogs";
        public const string PluginName = "FedoDiscordLogs";
        public const string PluginVersion = "1.0.0";

        public static FedoDiscordLogsPlugin Instance { get; private set; }

        // Exposé pour que les classes de patch Harmony (statiques) puissent logguer sans jamais
        // laisser une exception remonter dans le code du jeu qu'elles patchent.
        public static ManualLogSource Log { get; private set; }

        private ConfigEntry<string> _webhookUrl;

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

        private Harmony _harmony;
        private bool _serverStartAnnounced;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            _webhookUrl = Config.Bind(
                "Discord",
                "WebhookUrl",
                "",
                "Discord webhook URL (Server Settings > Integrations > Webhooks). Keep it secret: anyone who has it can post in your channel.");

            _logPlayerConnected = Config.Bind(
                "Events",
                "LogPlayerConnected",
                true,
                "Logs when a player connects. Only fires on the server (or a client hosting the game).");
            _playerConnectedTemplate = Config.Bind(
                "Events",
                "PlayerConnectedTemplate",
                "**{player}** connected.",
                "Message posted when a player connects. {player} is replaced with their name.");

            _logPlayerDisconnected = Config.Bind(
                "Events",
                "LogPlayerDisconnected",
                true,
                "Logs when a player disconnects. Only fires on the server (or a client hosting the game).");
            _playerDisconnectedTemplate = Config.Bind(
                "Events",
                "PlayerDisconnectedTemplate",
                "**{player}** disconnected.",
                "Message posted when a player disconnects. {player} is replaced with their name.");

            _logPlayerDeath = Config.Bind(
                "Events",
                "LogPlayerDeath",
                true,
                "Logs when a player dies. This fires on whichever machine actually simulates that player's character (their own client, or the host if they are the host) -- for every player's death to be logged, every player needs this mod installed with a webhook configured.");
            _playerDeathTemplate = Config.Bind(
                "Events",
                "PlayerDeathTemplate",
                "**{player}** died ({cause}).",
                "Message posted when a player dies. {player} is replaced with their name, {cause} with the cause of death (drowning, fall damage, an attacker's name, etc.).");

            _logServerStarted = Config.Bind(
                "Events",
                "LogServerStarted",
                true,
                "Logs once the server (or a hosting client) finishes starting up.");
            _serverStartedTemplate = Config.Bind(
                "Events",
                "ServerStartedTemplate",
                "Server started (world: **{world}**).",
                "Message posted when the server starts. {world} is replaced with the world name.");

            _logServerStopped = Config.Bind(
                "Events",
                "LogServerStopped",
                true,
                "Logs when the server (or a hosting client) shuts down. Not guaranteed to arrive if the process is force-killed.");
            _serverStoppedTemplate = Config.Bind(
                "Events",
                "ServerStoppedTemplate",
                "Server stopped.",
                "Message posted when the server stops.");

            _logWorldSaved = Config.Bind(
                "Events",
                "LogWorldSaved",
                true,
                "Logs when the world finishes saving. Only fires on the server (or a hosting client).");
            _worldSavedTemplate = Config.Bind(
                "Events",
                "WorldSavedTemplate",
                "World saved.",
                "Message posted when the world finishes saving.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        public void OnPlayerConnected(string playerName)
        {
            Send(_logPlayerConnected, _playerConnectedTemplate, playerName);
        }

        public void OnPlayerDisconnected(string playerName)
        {
            Send(_logPlayerDisconnected, _playerDisconnectedTemplate, playerName);
        }

        public void OnPlayerDied(string playerName, string cause)
        {
            Send(_logPlayerDeath, _playerDeathTemplate, playerName, cause: cause);
        }

        public void OnServerStarted(string worldName)
        {
            if (_serverStartAnnounced)
            {
                return;
            }

            _serverStartAnnounced = true;
            Send(_logServerStarted, _serverStartedTemplate, null, worldName);
        }

        public void OnWorldSaved()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            Send(_logWorldSaved, _worldSavedTemplate, null);
        }

        public void OnServerStopped()
        {
            // Fire-and-forget comme les autres événements : ZNet.OnDestroy peut être appelé
            // pendant de simples transitions de menu (pas seulement un vrai arrêt de serveur),
            // donc on ne doit surtout pas bloquer le thread principal ici.
            Send(_logServerStopped, _serverStoppedTemplate, null);
        }

        private void Send(ConfigEntry<bool> toggle, ConfigEntry<string> template, string playerName, string worldName = null, string cause = null)
        {
            if (!toggle.Value)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_webhookUrl.Value))
            {
                Logger.LogWarning("FedoDiscordLogs: no Discord webhook configured (see fedo.discordlogs.cfg), message not sent.");
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

            string webhookUrl = _webhookUrl.Value;
            var logger = Logger;

            Task.Run(async () =>
            {
                try
                {
                    await DiscordWebhook.PostMessageAsync(webhookUrl, message);
                }
                catch (Exception e)
                {
                    logger.LogError($"FedoDiscordLogs: failed to send message: {e}");
                }
            });
        }
    }
}
