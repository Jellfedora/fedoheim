using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using UnityEngine.Networking;

namespace FedoGoldRabbit
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FedoGoldRabbitPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.goldrabbit";
        public const string PluginName = "FedoGoldRabbit";
        public const string PluginVersion = "1.0.0";

        // Prefab dédié (clone du Hare vanilla), enregistré dans ZNetScene et ajouté à la table de
        // spawn du monde entier (cf. GoldRabbitSpawnPatch) -- il ne dépend donc pas des zones où
        // les Hare normaux apparaissent naturellement. Peut aussi être spawné pour les tests via
        // la console ("spawn Fedo_GoldRabbit") ou un mod comme Easy Spawner.
        public const string GoldRabbitPrefabName = "Fedo_GoldRabbit";

        // ZNetView.GetPrefabName() est privée -- on identifie donc le prefab d'une ZDO via son
        // hash stable (ZDO.GetPrefab()), calculé une seule fois ici.
        public static readonly int GoldRabbitPrefabHash = StringExtensionMethods.GetStableHashCode(GoldRabbitPrefabName);

        // Mémorise dans la ZDO qu'on a déjà affiché le message/son d'apparition, pour ne pas le
        // répéter à chaque rechargement de zone (déconnexion/reconnexion, ...) du même individu.
        public const string ZdoAnnounced = "FedoGoldRabbit_Announced";

        private static readonly FieldInfo ZsfxAudioClipsField = AccessTools.Field(typeof(ZSFX), "m_audioClips");

        public static FedoGoldRabbitPlugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        public ConfigEntry<string> GoldenName;
        public ConfigEntry<int> SpawnMaxPerZone;
        public ConfigEntry<float> SpawnIntervalSeconds;
        public ConfigEntry<float> SpawnChancePercent;
        public ConfigEntry<float> SpawnRadiusMin;
        public ConfigEntry<float> SpawnRadiusMax;
        public ConfigEntry<string> CoinPrefabName;
        public ConfigEntry<float> CoinDropIntervalMin;
        public ConfigEntry<float> CoinDropIntervalMax;
        public ConfigEntry<int> CoinDropAmountMin;
        public ConfigEntry<int> CoinDropAmountMax;
        public ConfigEntry<int> DeathCoinAmountMin;
        public ConfigEntry<int> DeathCoinAmountMax;
        public ConfigEntry<float> LifetimeSeconds;
        public ConfigEntry<string> FleeShoutText;
        public ConfigEntry<float> FleeShoutCooldown;
        public ConfigEntry<string> DespawnShoutText;
        public ConfigEntry<bool> ShowSpawnMessage;
        public ConfigEntry<string> SpawnMessageText;
        public ConfigEntry<bool> ShowGoldenAura;
        public ConfigEntry<float> SpawnSoundVolume;
        public ConfigEntry<float> SpawnSoundMaxDistance;
        public ConfigEntry<bool> TintFurGolden;

        // ServerSync (voir mods/_shared/ConfigSync.cs) : tous ces réglages affectent le
        // monde partagé (spawn, loot, difficulté), verrouillés pour que tout le monde
        // joue avec les mêmes valeurs, pas celles que chacun aurait dans son .cfg local.
        private readonly ConfigSync _configSync = new ConfigSync(PluginGuid) { DisplayName = PluginName, CurrentVersion = PluginVersion };

        private const string CustomSpawnSoundFileName = "rabbit_spawn.mp3";

        private Harmony _harmony;
        private AudioClip _cachedCoinClip;
        private bool _coinClipResolved;
        private AudioClip _customSpawnClip;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            GoldenName = SyncedConfig(
                "GoldRabbit",
                "GoldenName",
                "Gold Rabbit",
                "Display name given to a Gold Rabbit.");

            SpawnMaxPerZone = SyncedConfig(
                "GoldRabbit",
                "SpawnMaxPerZone",
                1,
                "Maximum number of Gold Rabbits allowed at once in a given zone (a zone is roughly a 64x64m area of the world).");

            SpawnIntervalSeconds = SyncedConfig(
                "GoldRabbit",
                "SpawnIntervalSeconds",
                4000f,
                "How often (in seconds) the game rolls a chance to spawn a Gold Rabbit in a zone. Higher = rarer.");

            SpawnChancePercent = SyncedConfig(
                "GoldRabbit",
                "SpawnChancePercent",
                3f,
                "Chance (0-100) of actually spawning a Gold Rabbit each time the spawn interval rolls, in any biome.");

            SpawnRadiusMin = SyncedConfig(
                "GoldRabbit",
                "SpawnRadiusMin",
                5f,
                "Minimum distance (in meters) from a player at which a Gold Rabbit can spawn.");

            SpawnRadiusMax = SyncedConfig(
                "GoldRabbit",
                "SpawnRadiusMax",
                15f,
                "Maximum distance (in meters) from a player at which a Gold Rabbit can spawn -- kept short so you actually have a chance to notice and catch it.");

            CoinPrefabName = SyncedConfig(
                "GoldRabbit",
                "CoinPrefabName",
                "Coins",
                "Name of the item prefab dropped as currency (must be a valid ZNetScene prefab name).");

            CoinDropIntervalMin = SyncedConfig(
                "GoldRabbit",
                "CoinDropIntervalMin",
                2f,
                "Minimum delay (in seconds) between two coin drops while the Gold Rabbit is alive.");

            CoinDropIntervalMax = SyncedConfig(
                "GoldRabbit",
                "CoinDropIntervalMax",
                3f,
                "Maximum delay (in seconds) between two coin drops while the Gold Rabbit is alive.");

            CoinDropAmountMin = SyncedConfig(
                "GoldRabbit",
                "CoinDropAmountMin",
                1,
                "Minimum amount of coins dropped on each periodic drop while the Gold Rabbit is alive.");

            CoinDropAmountMax = SyncedConfig(
                "GoldRabbit",
                "CoinDropAmountMax",
                3,
                "Maximum amount of coins dropped on each periodic drop while the Gold Rabbit is alive.");

            DeathCoinAmountMin = SyncedConfig(
                "GoldRabbit",
                "DeathCoinAmountMin",
                75,
                "Minimum amount of coins dropped when the Gold Rabbit is killed (replaces its normal meat/pelt loot entirely).");

            DeathCoinAmountMax = SyncedConfig(
                "GoldRabbit",
                "DeathCoinAmountMax",
                200,
                "Maximum amount of coins dropped when the Gold Rabbit is killed (replaces its normal meat/pelt loot entirely).");

            LifetimeSeconds = SyncedConfig(
                "GoldRabbit",
                "LifetimeSeconds",
                30f,
                "Time (in seconds) a Gold Rabbit stays in the world before despawning in a puff of smoke (like any dying creature) if it hasn't been killed yet. No loot is dropped when this happens.");

            FleeShoutText = SyncedConfig(
                "GoldRabbit",
                "FleeShoutText",
                "I'm late, I'm late, for a very important date!",
                "Speech bubble text shown above the Gold Rabbit when it notices a player and starts fleeing.");

            FleeShoutCooldown = SyncedConfig(
                "GoldRabbit",
                "FleeShoutCooldown",
                20f,
                "Minimum delay (in seconds) between two flee shouts from the same Gold Rabbit.");

            DespawnShoutText = SyncedConfig(
                "GoldRabbit",
                "DespawnShoutText",
                "Ah, found my burrow!",
                "Speech bubble text shown right before the Gold Rabbit despawns without loot, if nobody caught it in time.");

            ShowSpawnMessage = SyncedConfig(
                "GoldRabbit",
                "ShowSpawnMessage",
                true,
                "Shows an on-screen message (with the vanilla notification sound) when a Gold Rabbit spawns nearby.");

            SpawnMessageText = SyncedConfig(
                "GoldRabbit",
                "SpawnMessageText",
                "A Gold Rabbit is bolting nearby!",
                "On-screen message shown when a Gold Rabbit spawns.");

            ShowGoldenAura = SyncedConfig(
                "GoldRabbit",
                "ShowGoldenAura",
                true,
                "Adds a small golden sparkle aura around the Gold Rabbit so it stands out from a normal Hare.");

            SpawnSoundVolume = SyncedConfig(
                "GoldRabbit",
                "SpawnSoundVolume",
                2f,
                "Volume of the custom spawn sound (rabbit_spawn.mp3). Still a normal 3D positional sound that fades with distance (important on multiplayer servers, so distant players don't hear it) -- this only boosts how loud it is up close. 1 = normal, higher = louder.");

            SpawnSoundMaxDistance = SyncedConfig(
                "GoldRabbit",
                "SpawnSoundMaxDistance",
                30f,
                "Maximum distance (in meters) at which the spawn sound can be heard at all. Keep this low on multiplayer servers so players far away don't hear it.");

            TintFurGolden = SyncedConfig(
                "GoldRabbit",
                "TintFurGolden",
                true,
                "Tints the Gold Rabbit's fur gold instead of the normal Hare color.");

            _configSync.IsLocked = true;

            _harmony = new Harmony(PluginGuid);
            try
            {
                _harmony.PatchAll();
            }
            catch (Exception e)
            {
                Log.LogError($"FedoGoldRabbit: PatchAll threw, some patches may not be applied: {e}");
            }

            var znetScenePatches = string.Join(", ", Harmony.GetAllPatchedMethods()
                .Where(m => m.DeclaringType == typeof(ZNetScene))
                .Select(m => m.Name));
            Log.LogInfo($"FedoGoldRabbit: patched ZNetScene methods -> [{znetScenePatches}]");

            StartCoroutine(LoadCustomSpawnClip());
        }

        private ConfigEntry<T> SyncedConfig<T>(string section, string key, T value, string description)
        {
            var entry = Config.Bind(section, key, value, description);
            _configSync.AddConfigEntry(entry);
            return entry;
        }

        // Le mp3 est déployé à côté de la DLL par le .csproj (CopyToPlugins) -- on le charge de
        // façon asynchrone via UnityWebRequest, seule API disponible pour décoder un fichier audio
        // compressé à l'exécution sans passer par un AssetBundle Unity.
        private IEnumerator LoadCustomSpawnClip()
        {
            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string path = Path.Combine(dllDir ?? "", CustomSpawnSoundFileName);
            if (!File.Exists(path))
            {
                Log.LogWarning($"FedoGoldRabbit: '{CustomSpawnSoundFileName}' not found next to the plugin DLL, spawn will use the vanilla message chime only.");
                yield break;
            }

            using var request = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.MPEG);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Log.LogWarning($"FedoGoldRabbit: failed to load '{CustomSpawnSoundFileName}': {request.error}");
                yield break;
            }

            _customSpawnClip = DownloadHandlerAudioClip.GetContent(request);
            Log.LogInfo($"FedoGoldRabbit: loaded custom spawn sound '{CustomSpawnSoundFileName}'.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        public ItemDrop.ItemData GetCoinItemTemplate()
        {
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(CoinPrefabName.Value) : null;
            var itemDrop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (itemDrop == null)
            {
                Log.LogError($"FedoGoldRabbit: unknown coin prefab '{CoinPrefabName.Value}', Gold Rabbits won't drop any coins.");
                return null;
            }

            // m_dropPrefab n'est renseigné qu'au runtime par ItemDrop.Awake() (qui ne s'exécute
            // jamais sur ce template "Coins", resté inactif dans ZNetScene) -- sans ça,
            // ItemDrop.DropItem tente d'instancier un prefab null et plante.
            if (itemDrop.m_itemData.m_dropPrefab == null)
            {
                itemDrop.m_itemData.m_dropPrefab = prefab;
            }

            return itemDrop.m_itemData;
        }

        public GameObject GetCoinPrefab()
        {
            return ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(CoinPrefabName.Value) : null;
        }

        public void AnnounceGoldenSpawn(Vector3 position)
        {
            if (ShowSpawnMessage.Value && MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, SpawnMessageText.Value);
            }

            if (_customSpawnClip != null)
            {
                PlaySpatialSound(_customSpawnClip, position, SpawnSoundVolume.Value, SpawnSoundMaxDistance.Value);
            }
        }

        // Toujours un son 3D positionnel (spatialBlend = 1) qui s'atténue avec la distance -- sur
        // un serveur multijoueur, un joueur loin du lièvre (voire dans une autre zone chargée) ne
        // doit pas l'entendre. AudioSource.PlayClipAtPoint utilise une courbe logarithmique qui
        // chute très vite ; ici volume et portée (maxDistance, rolloff linéaire) sont réglables
        // pour rester audible à proximité sans pour autant devenir un son "global".
        private void PlaySpatialSound(AudioClip clip, Vector3 position, float volume, float maxDistance)
        {
            var soundObj = new GameObject("FedoGoldRabbit_SpatialSound");
            soundObj.transform.position = position;
            var source = soundObj.AddComponent<AudioSource>();
            source.clip = clip;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.maxDistance = maxDistance;
            source.volume = volume;
            source.Play();
            UnityEngine.Object.Destroy(soundObj, clip.length + 0.1f);
        }

        // Récupère (et met en cache) le son déjà utilisé par la pièce elle-même, pour éviter
        // d'avoir à embarquer un fichier audio custom : on rejoue simplement le clip du prefab
        // Coins à chaque piastre lâchée.
        public void PlayCoinSound(Vector3 position)
        {
            var clip = GetCoinClip();
            if (clip != null)
            {
                AudioSource.PlayClipAtPoint(clip, position, 1f);
            }
        }

        private AudioClip GetCoinClip()
        {
            if (_coinClipResolved)
            {
                return _cachedCoinClip;
            }

            _coinClipResolved = true;

            try
            {
                var prefab = GetCoinPrefab();
                var zsfx = prefab != null ? prefab.GetComponentInChildren<ZSFX>() : null;
                if (zsfx != null && ZsfxAudioClipsField?.GetValue(zsfx) is AudioClip[] clips && clips.Length > 0)
                {
                    _cachedCoinClip = clips[UnityEngine.Random.Range(0, clips.Length)];
                }
                else
                {
                    var audioSource = prefab != null ? prefab.GetComponentInChildren<AudioSource>() : null;
                    _cachedCoinClip = audioSource != null ? audioSource.clip : null;
                }
            }
            catch (Exception e)
            {
                Log.LogWarning($"FedoGoldRabbit: could not resolve a coin sound from prefab '{CoinPrefabName.Value}': {e}");
            }

            if (_cachedCoinClip == null)
            {
                Log.LogWarning($"FedoGoldRabbit: no audio clip found on prefab '{CoinPrefabName.Value}', coin drops will be silent.");
            }

            return _cachedCoinClip;
        }
    }
}
