using System;
using System.Collections;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace FedoDeathGif
{
    // Capture un gif du moment de la mort et l'envoie sur Discord. Purement client
    // (chaque joueur capture et envoie sa propre mort).
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FedoDeathGifPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.deathgif";
        public const string PluginName = "FedoDeathGif";
        public const string PluginVersion = "1.0.0";

        public static FedoDeathGifPlugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        // ServerSync, webhook Discord inclus : chaque client poste sa propre mort
        // directement sur Discord (voir CaptureDeathAndSend ci-dessous), donc a de toute
        // façon besoin de l'URL en clair pour fonctionner -- ici c'est tout l'intérêt :
        // que toute la communauté poste au même webhook sans que chacun doive le
        // configurer à la main. Un joueur curieux peut relire cette valeur dans son
        // propre .cfg une fois synchronisée -- accepté comme compromis.
        private readonly ConfigSync _configSync = new ConfigSync(PluginGuid) { DisplayName = PluginName, CurrentVersion = PluginVersion };

        private ConfigEntry<string> _webhookUrl;
        private ConfigEntry<int> _captureFps;
        private ConfigEntry<int> _captureWidth;
        private ConfigEntry<int> _captureHeight;
        private ConfigEntry<float> _bufferSeconds;
        private ConfigEntry<bool> _showGifMessage;
        private ConfigEntry<string> _gifMessageText;
        private ConfigEntry<string> _discordMessageTemplate;
        private ConfigEntry<float> _postDeathDelay;
        private ConfigEntry<bool> _showDeathChatMessage;
        private ConfigEntry<string> _deathChatMessageText;

        private Harmony _harmony;
        private FrameBuffer _frameBuffer;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            _webhookUrl = SyncedConfig(
                "Discord",
                "WebhookUrl",
                "",
                "Discord webhook URL (Server Settings > Integrations > Webhooks), shared with every connected player so everyone's death gets posted to the same channel. Keep it out of public repos/screenshots -- anyone who has it can post in your channel.");

            _captureFps = SyncedConfig(
                "Capture",
                "Fps",
                12,
                new ConfigDescription(
                    "Frames captured per second while you play. Higher = smoother gif but more expensive to capture.",
                    new AcceptableValueRange<int>(1, 30)));

            _captureWidth = SyncedConfig(
                "Capture",
                "Width",
                640,
                new ConfigDescription(
                    "Gif width in pixels. Bigger = sharper but heavier file (Discord webhooks reject uploads above roughly 8 MB).",
                    new AcceptableValueRange<int>(160, 1920)));

            _captureHeight = SyncedConfig(
                "Capture",
                "Height",
                360,
                new ConfigDescription(
                    "Gif height in pixels. Bigger = sharper but heavier file (Discord webhooks reject uploads above roughly 8 MB).",
                    new AcceptableValueRange<int>(90, 1080)));

            _bufferSeconds = SyncedConfig(
                "Capture",
                "BufferSeconds",
                5f,
                new ConfigDescription(
                    "How many seconds are kept before the player's death. Longer = more memory used at all times and a bigger gif to export.",
                    new AcceptableValueRange<float>(1f, 15f)));

            _postDeathDelay = SyncedConfig(
                "Capture",
                "PostDeathDelay",
                1.5f,
                new ConfigDescription(
                    "Delay after death before freezing the gif, to give the death animation time to actually show on screen.",
                    new AcceptableValueRange<float>(0f, 5f)));

            _showGifMessage = SyncedConfig(
                "Message",
                "ShowGifMessage",
                true,
                "Shows an on-screen message when a gif is captured after your death.");

            _gifMessageText = SyncedConfig(
                "Message",
                "GifMessageText",
                "Your exploits have been immortalized.",
                "Text shown on death (same style as the game's own messages, e.g. \"The gods are merciful\").");

            _discordMessageTemplate = SyncedConfig(
                "Discord",
                "MessageTemplate",
                "{player} just died!",
                "Message posted alongside the gif on Discord. {player} is replaced with the dead player's name.");

            _showDeathChatMessage = SyncedConfig(
                "Message",
                "ShowDeathChatMessage",
                true,
                "Makes the player say a line in chat (speech bubble above the character) at the moment of death.");

            _deathChatMessageText = SyncedConfig(
                "Message",
                "DeathChatMessageText",
                "OUCH!",
                "Text said by the player in chat on death.");

            _configSync.IsLocked = true;

            _frameBuffer = gameObject.AddComponent<FrameBuffer>();
            _frameBuffer.Configure(_captureWidth.Value, _captureHeight.Value, _captureFps.Value, _bufferSeconds.Value);

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
        }

        private ConfigEntry<T> SyncedConfig<T>(string section, string key, T value, ConfigDescription description)
        {
            var entry = Config.Bind(section, key, value, description);
            _configSync.AddConfigEntry(entry);
            return entry;
        }

        private ConfigEntry<T> SyncedConfig<T>(string section, string key, T value, string description)
        {
            var entry = Config.Bind(section, key, value, description);
            _configSync.AddConfigEntry(entry);
            return entry;
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        // Appelé à chaque mort du joueur local (voir PlayerDeathPatch.cs) : message, ligne de
        // chat, puis export et envoi du gif sur Discord.
        public void OnLocalPlayerDeath()
        {
            if (_showGifMessage.Value && MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, _gifMessageText.Value);
            }

            if (_showDeathChatMessage.Value && Chat.instance != null)
            {
                Chat.instance.SendText(Talker.Type.Shout, _deathChatMessageText.Value);
            }

            string playerName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "A player";
            StartCoroutine(CaptureDeathAndSend(playerName));
        }

        // La capture continue déjà en tâche de fond après la mort (le joueur local existe toujours) :
        // on attend juste que l'animation de mort ait eu le temps de s'afficher avant de figer le buffer.
        private IEnumerator CaptureDeathAndSend(string playerName)
        {
            yield return new WaitForSeconds(_postDeathDelay.Value);

            if (string.IsNullOrWhiteSpace(_webhookUrl.Value))
            {
                Log.LogWarning("FedoDeathGif: no Discord webhook configured (see fedo.deathgif.cfg), gif not sent.");
                yield break;
            }

            var snapshot = _frameBuffer.Snapshot();
            if (snapshot.Frames.Count == 0)
            {
                yield break;
            }

            int delayCentiseconds = Mathf.Max(1, Mathf.RoundToInt(100f / _captureFps.Value));
            string webhookUrl = _webhookUrl.Value;
            string message = _discordMessageTemplate.Value.Replace("{player}", playerName);
            var log = Log;

            Task.Run(async () =>
            {
                try
                {
                    byte[] gif = GifBuilder.Build(snapshot.Frames, snapshot.Width, snapshot.Height, delayCentiseconds);
                    await DiscordUploader.UploadGifAsync(webhookUrl, gif, "death.gif", message);
                }
                catch (Exception e)
                {
                    log.LogError($"FedoDeathGif: failed to export/send the gif: {e}");
                }
            });
        }
    }
}
