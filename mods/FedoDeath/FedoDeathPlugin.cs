using System;
using System.Collections;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace FedoDeath
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FedoDeathPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.death";
        public const string PluginName = "FedoDeath";
        public const string PluginVersion = "2.0.0";

        // Clés stockées directement dans la ZDO du gardien : elle est sauvegardée et synchronisée
        // par le jeu, contrairement à un composant/état en mémoire, qui ne survivrait pas à un
        // rechargement de zone, une déconnexion ou un redémarrage du serveur.
        public const string ZdoIsGuardian = "FedoDeath_Guardian";
        public const string ZdoOwnerName = "FedoDeath_OwnerName";
        public const string ZdoOwnerUID = "FedoDeath_OwnerUID";
        public const string ZdoLoot = "FedoDeath_Loot";

        public static FedoDeathPlugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        public ConfigEntry<string> CreaturePrefab;
        public ConfigEntry<string> GuardianNameTemplate;
        public ConfigEntry<float> ActivationRange;
        public ConfigEntry<bool> ShowMessages;
        public ConfigEntry<string> SpawnMessageText;
        public ConfigEntry<string> DefeatMessageText;

        // Le webhook Discord est un secret -- jamais enregistré via ServerSync (voir
        // mods/_shared/ConfigSync.cs / mods/CLAUDE.md) : AddConfigEntry diffuse la valeur en clair
        // à tous les clients connectés dès qu'elle change, l'inverse de ce qu'on veut ici. Idem
        // pour les réglages de capture, purement locaux au client qui vient de mourir.
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

        // ServerSync (voir mods/_shared/ConfigSync.cs) : tous les réglages du gardien ci-dessus
        // affectent ce que voit/vit tout le monde sur ce serveur (le gardien de n'importe
        // quel joueur), donc verrouillés -- un joueur ne doit pas pouvoir les changer
        // localement pour lui-même.
        private readonly ConfigSync _configSync = new ConfigSync(PluginGuid) { DisplayName = PluginName, CurrentVersion = PluginVersion };

        private Harmony _harmony;
        private FrameBuffer _frameBuffer;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            CreaturePrefab = SyncedConfig(
                "Guardian",
                "CreaturePrefab",
                "Skeleton",
                "Name of the creature prefab spawned to guard your grave (must be a valid ZNetScene prefab name, e.g. Skeleton, Wraith, Troll...).");

            GuardianNameTemplate = SyncedConfig(
                "Guardian",
                "GuardianNameTemplate",
                "Dead {player}",
                "Display name given to the guardian creature. {player} is replaced with the name of the player who died.");

            ActivationRange = SyncedConfig(
                "Guardian",
                "ActivationRange",
                20f,
                "Distance (in meters) a player must come within before the guardian wakes up and starts hunting. Until then, it stays completely still and won't engage anything.");

            ShowMessages = SyncedConfig(
                "Guardian",
                "ShowMessages",
                true,
                "Shows an on-screen message when the guardian spawns and when it is defeated.");

            SpawnMessageText = SyncedConfig(
                "Guardian",
                "SpawnMessageText",
                "A guardian rises to protect your grave!",
                "On-screen message shown when the guardian spawns.");

            DefeatMessageText = SyncedConfig(
                "Guardian",
                "DefeatMessageText",
                "The guardian has fallen. Your grave is safe.",
                "On-screen message shown when the guardian is defeated and the grave appears.");

            _configSync.IsLocked = true;

            _webhookUrl = Config.Bind(
                "Discord",
                "WebhookUrl",
                "",
                "Discord webhook URL (Server Settings > Integrations > Webhooks). Keep it secret: anyone who has it can post in your channel.");

            _captureFps = Config.Bind(
                "Capture",
                "Fps",
                12,
                new ConfigDescription(
                    "Frames captured per second while you play. Higher = smoother gif but more expensive to capture.",
                    new AcceptableValueRange<int>(1, 30)));

            _captureWidth = Config.Bind(
                "Capture",
                "Width",
                640,
                new ConfigDescription(
                    "Gif width in pixels. Bigger = sharper but heavier file (Discord webhooks reject uploads above roughly 8 MB).",
                    new AcceptableValueRange<int>(160, 1920)));

            _captureHeight = Config.Bind(
                "Capture",
                "Height",
                360,
                new ConfigDescription(
                    "Gif height in pixels. Bigger = sharper but heavier file (Discord webhooks reject uploads above roughly 8 MB).",
                    new AcceptableValueRange<int>(90, 1080)));

            _bufferSeconds = Config.Bind(
                "Capture",
                "BufferSeconds",
                5f,
                new ConfigDescription(
                    "How many seconds are kept before the player's death. Longer = more memory used at all times and a bigger gif to export.",
                    new AcceptableValueRange<float>(1f, 15f)));

            _postDeathDelay = Config.Bind(
                "Capture",
                "PostDeathDelay",
                1.5f,
                new ConfigDescription(
                    "Delay after death before freezing the gif, to give the death animation time to actually show on screen.",
                    new AcceptableValueRange<float>(0f, 5f)));

            _showGifMessage = Config.Bind(
                "Message",
                "ShowGifMessage",
                true,
                "Shows an on-screen message when a gif is captured after your death.");

            _gifMessageText = Config.Bind(
                "Message",
                "GifMessageText",
                "Your exploits have been immortalized.",
                "Text shown on death (same style as the game's own messages, e.g. \"The gods are merciful\").");

            _discordMessageTemplate = Config.Bind(
                "Discord",
                "MessageTemplate",
                "{player} just died!",
                "Message posted alongside the gif on Discord. {player} is replaced with the dead player's name.");

            _showDeathChatMessage = Config.Bind(
                "Message",
                "ShowDeathChatMessage",
                true,
                "Makes the player say a line in chat (speech bubble above the character) at the moment of death.");

            _deathChatMessageText = Config.Bind(
                "Message",
                "DeathChatMessageText",
                "OUCH!",
                "Text said by the player in chat on death.");

            _frameBuffer = gameObject.AddComponent<FrameBuffer>();
            _frameBuffer.Configure(_captureWidth.Value, _captureHeight.Value, _captureFps.Value, _bufferSeconds.Value);

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
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

        // Appelé une seule fois, au moment de la mort du joueur : crée le gardien et écrit son
        // état (butin, propriétaire) dans sa ZDO.
        public void SpawnGuardian(Vector3 position, Quaternion rotation, Inventory pendingInventory, string ownerName, long ownerUID, GameObject fallbackTombstonePrefab)
        {
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(CreaturePrefab.Value) : null;
            if (prefab == null)
            {
                Log.LogError($"FedoDeath: unknown creature prefab '{CreaturePrefab.Value}', dropping the tombstone normally instead.");
                CreateTombstoneNow(position, rotation, pendingInventory, ownerName, ownerUID, fallbackTombstonePrefab);
                return;
            }

            var guardian = UnityEngine.Object.Instantiate(prefab, position, rotation);
            var zdo = guardian.GetComponent<ZNetView>()?.GetZDO();
            if (zdo == null)
            {
                Log.LogError("FedoDeath: guardian has no ZDO, dropping the tombstone normally instead.");
                UnityEngine.Object.Destroy(guardian);
                CreateTombstoneNow(position, rotation, pendingInventory, ownerName, ownerUID, fallbackTombstonePrefab);
                return;
            }

            // Garantit que c'est bien nous (le joueur qui vient de mourir) qui possédons cette
            // ZDO -- sinon le patch sur OnDeath (qui exige IsOwner()) ne se déclencherait jamais
            // et la tombe ne réapparaîtrait pas.
            guardian.GetComponent<ZNetView>()?.ClaimOwnership();

            // Un monstre sauvage classique n'est en général pas marqué "persistant" (il est
            // éphémère/re-spawnable) : sans ça, ni lui ni ses données custom ne survivraient à
            // une vraie déconnexion/reconnexion (sauvegarde + rechargement complet du monde).
            zdo.Persistent = true;

            var lootPkg = new ZPackage();
            pendingInventory.Save(lootPkg);

            zdo.Set(ZdoIsGuardian, true);
            zdo.Set(ZdoOwnerName, ownerName);
            zdo.Set(ZdoOwnerUID, ownerUID);
            zdo.Set(ZdoLoot, lootPkg.GetArray());

            // Applique tout de suite l'apparence/comportement et le composant d'activation --
            // le patch sur Character.Awake s'exécute pendant Instantiate(), donc AVANT que les
            // champs ZDO ci-dessus soient écrits : il ne fera que reproduire tout ceci à chaque
            // futur rechargement de cette même ZDO, mais ne peut pas s'en charger la toute
            // première fois.
            GuardianStatePatch.ApplyGuardianState(guardian.GetComponent<Character>(), ownerName);
            if (guardian.GetComponent<GraveGuardianActivator>() == null)
            {
                guardian.AddComponent<GraveGuardianActivator>();
            }

            if (ShowMessages.Value && MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, SpawnMessageText.Value);
            }
        }

        // Appelé à la mort du gardien, potentiellement après un ou plusieurs rechargements
        // (déconnexion/reconnexion, redémarrage serveur) -- tout est relu depuis la ZDO.
        public void OnGuardianDefeated(ZDO zdo, Vector3 position, Quaternion rotation)
        {
            try
            {
                string ownerName = zdo.GetString(ZdoOwnerName, "");
                long ownerUID = zdo.GetLong(ZdoOwnerUID, 0);
                byte[] lootBytes = zdo.GetByteArray(ZdoLoot, null);
                if (lootBytes == null)
                {
                    return;
                }

                var tombstonePrefab = Player.m_localPlayer != null ? Player.m_localPlayer.m_tombstone : null;
                if (tombstonePrefab == null)
                {
                    Log.LogError("FedoDeath: no local player available to fetch the tombstone prefab, loot is lost.");
                    return;
                }

                var tombstoneObj = UnityEngine.Object.Instantiate(tombstonePrefab, position, rotation);
                tombstoneObj.GetComponent<TombStone>()?.Setup(ownerName, ownerUID);

                var lootPkg = new ZPackage();
                lootPkg.Load(lootBytes);
                tombstoneObj.GetComponent<Container>()?.GetInventory()?.Load(lootPkg);

                if (ShowMessages.Value && MessageHud.instance != null)
                {
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, DefeatMessageText.Value);
                }
            }
            catch (Exception e)
            {
                Log.LogError($"FedoDeath: failed to spawn the tombstone after the guardian died: {e}");
            }
        }

        private void CreateTombstoneNow(Vector3 position, Quaternion rotation, Inventory pendingInventory, string ownerName, long ownerUID, GameObject tombstonePrefab)
        {
            if (tombstonePrefab == null || pendingInventory == null)
            {
                return;
            }

            var tombstoneObj = UnityEngine.Object.Instantiate(tombstonePrefab, position, rotation);
            tombstoneObj.GetComponent<TombStone>()?.Setup(ownerName, ownerUID);
            tombstoneObj.GetComponent<Container>()?.GetInventory()?.MoveAll(pendingInventory);
        }

        // Appelé à chaque mort du joueur local (indépendamment du gardien ci-dessus) : message,
        // ligne de chat, puis export et envoi du gif sur Discord.
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
                Log.LogWarning("FedoDeath: no Discord webhook configured (see fedo.death.cfg), gif not sent.");
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
                    log.LogError($"FedoDeath: failed to export/send the gif: {e}");
                }
            });
        }
    }
}
