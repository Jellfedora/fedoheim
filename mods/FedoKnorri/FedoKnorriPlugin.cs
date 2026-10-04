using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using UnityEngine.Networking;

namespace FedoKnorri
{
    // Jotunn doit être chargé avant ce mod : CompanionPrefabPatch/SummonItemPrefabPatch
    // s'appuient sur son PrefabManager/ItemManager dès Awake() (abonnement à l'événement
    // PrefabManager.OnVanillaPrefabsAvailable). Sans cette dépendance déclarée, BepInEx
    // pourrait charger les plugins dans n'importe quel ordre -- avec elle, un serveur sans
    // Jotunn installé refuse proprement de charger FedoKnorri (message clair dans les logs
    // BepInEx) plutôt que de planter plus loin sur une NullReferenceException.
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FedoKnorriPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.knorri";
        public const string PluginName = "FedoKnorri";
        public const string PluginVersion = "1.0.0";

        public static FedoKnorriPlugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        public ConfigEntry<string> CompanionName;
        public ConfigEntry<float> CompanionScale;
        public ConfigEntry<float> FollowDistance;
        public ConfigEntry<float> RunDistance;
        public ConfigEntry<float> TeleportDistance;
        public ConfigEntry<float> ShamanCompanionScale;
        public ConfigEntry<float> CompanionMaxHealth;
        public ConfigEntry<float> ShamanCompanionMaxHealth;
        public ConfigEntry<float> CompanionAggroChance;
        public ConfigEntry<float> DeathCooldownSeconds;

        public ConfigEntry<float> HealAmount;
        public ConfigEntry<float> HealCooldownSeconds;
        public ConfigEntry<float> HealRange;
        public ConfigEntry<float> HealThrowDelaySeconds;
        public ConfigEntry<float> ShamanHealAmount;
        public ConfigEntry<float> ShamanHealCooldownSeconds;

        public ConfigEntry<float> PickupRange;
        public ConfigEntry<float> PickupIntervalSeconds;
        public ConfigEntry<string> PickupPhrase1;
        public ConfigEntry<string> PickupPhrase2;
        public ConfigEntry<float> PickupChatCooldownSeconds;

        public ConfigEntry<string> RenameHintText;
        public ConfigEntry<string> RenamePromptText;
        public ConfigEntry<string> BehaviorModeHintText;
        public ConfigEntry<string> DefensiveModeLabel;
        public ConfigEntry<string> AggressiveModeLabel;
        public ConfigEntry<string> DefensiveModeMessage;
        public ConfigEntry<string> AggressiveModeMessage;
        public ConfigEntry<float> CombatEngageRange;
        public ConfigEntry<float> CombatAttackCooldownSeconds;

        public ConfigEntry<string> SummonItemSourceItem;
        public ConfigEntry<string> SummonItemName;
        public ConfigEntry<string> SummonItemDescription;
        public ConfigEntry<float> SummonCooldownSeconds;
        public ConfigEntry<float> SummonDistance;
        public ConfigEntry<string> SummonItemOwnerLabel;
        public ConfigEntry<string> SummonItemNotOwnerMessage;
        public ConfigEntry<string> CannotDowngradeMessage;

        public ConfigEntry<string> ShamanSummonItemSourceItem;
        public ConfigEntry<string> ShamanSummonItemName;
        public ConfigEntry<string> ShamanSummonItemDescription;
        public ConfigEntry<string> ShamanRecipeHoneyItem;
        public ConfigEntry<int> ShamanRecipeHoneyAmount;
        public ConfigEntry<string> ShamanRecipeCraftingStation;
        public ConfigEntry<int> ShamanRecipeMinStationLevel;

        private const string CoinPickupSoundFileName = "shiny.mp3";
        public ConfigEntry<float> CoinPickupSoundVolume;
        public ConfigEntry<float> CoinPickupSoundMaxDistance;
        public ConfigEntry<float> CoinPickupSoundCooldownSeconds;
        private AudioClip _coinPickupClip;

        // Pas de cooldown dédié ici : le soin lui-même (HealCooldownSeconds, 10s par défaut)
        // empêche déjà tout spam. Fichier optionnel -- absent par défaut, LoadHealImpactClip
        // se contente d'un avertissement (pas d'erreur) tant qu'il n'a pas été déposé à côté de
        // la DLL, l'impact reste alors muet (juste les particules).
        private const string HealImpactSoundFileName = "healing.mp3";
        public ConfigEntry<float> HealImpactSoundVolume;
        public ConfigEntry<float> HealImpactSoundMaxDistance;
        private AudioClip _healImpactClip;

        // ServerSync (voir mods/_shared/ConfigSync.cs) : réglages du compagnon/du charme
        // partagés par tout le monde sur ce serveur, verrouillés pour éviter qu'un joueur
        // les change localement pour lui-même.
        private readonly ConfigSync _configSync = new ConfigSync(PluginGuid) { DisplayName = PluginName, CurrentVersion = PluginVersion };

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            CompanionName = SyncedConfig(
                "Companion",
                "CompanionName",
                "Knorri",
                "Display name shown when hovering over the companion.");

            CompanionScale = SyncedConfig(
                "Companion",
                "CompanionScale",
                0.7f,
                "Uniform scale applied to the companion's model (1 = same size as a vanilla Greyling).");

            FollowDistance = SyncedConfig(
                "Companion",
                "FollowDistance",
                3f,
                "Distance (in meters) the companion tries to keep from its owner while following.");

            RunDistance = SyncedConfig(
                "Companion",
                "RunDistance",
                6f,
                "Distance (in meters) beyond which the companion runs instead of walking to catch up.");

            TeleportDistance = SyncedConfig(
                "Companion",
                "TeleportDistance",
                25f,
                "Distance (in meters) beyond which the companion teleports next to its owner instead of pathing.");

            ShamanCompanionScale = SyncedConfig(
                "Companion",
                "ShamanCompanionScale",
                1f,
                "Uniform scale applied to the evolved (Shaman) companion's model (1 = same size as a vanilla Greydwarf Shaman).");

            CompanionMaxHealth = SyncedConfig(
                "Companion",
                "CompanionMaxHealth",
                50f,
                "Maximum health of the base companion. No longer invulnerable -- it can be damaged and killed by wild creatures (but never by players, see CompanionInvulnerabilityPatch), though it never fights back.");

            ShamanCompanionMaxHealth = SyncedConfig(
                "Companion",
                "ShamanCompanionMaxHealth",
                150f,
                "Maximum health of the evolved (Shaman) companion. Same rules as CompanionMaxHealth.");

            CompanionAggroChance = SyncedConfig(
                "Companion",
                "CompanionAggroChance",
                0.4f,
                "Chance (0-1) that the companion counts as a valid target when a wild creature that doesn't already have a target is looking for one -- the owner is always a 100% valid candidate, so a lower value here makes the companion draw noticeably less aggro without ever making it fully unable to be targeted. Never applies once a creature has already locked onto the companion, so this can't make it lose an ongoing fight's target mid-combat.");

            DeathCooldownSeconds = SyncedConfig(
                "SummonItem",
                "DeathCooldownSeconds",
                120f,
                "Cooldown (in seconds) before either summoning seed can be used again after the companion is killed -- shown on the seed's icon the same way as the normal summon cooldown (darkened with a countdown).");

            HealAmount = SyncedConfig(
                "Healing",
                "HealAmount",
                15f,
                "Health points restored to the owner each time the companion heals them.");

            HealCooldownSeconds = SyncedConfig(
                "Healing",
                "HealCooldownSeconds",
                10f,
                "Minimum delay (in seconds) between two heals from the companion.");

            HealRange = SyncedConfig(
                "Healing",
                "HealRange",
                8f,
                "Distance (in meters) within which the companion can heal its owner.");

            HealThrowDelaySeconds = SyncedConfig(
                "Healing",
                "HealThrowDelaySeconds",
                0.8f,
                "Delay (in seconds) between the companion's throw animation starting and the heal orb actually launching -- tune this by eye in game until the orb leaves right at (or a touch before) the end of the throw motion. A fixed, tunable value rather than something read off the Animator at runtime: reading the Animator's real transition/state timing turned out unreliable across multiple attempts (see CompanionAI.LaunchHealOrbAfterThrow).");

            ShamanHealAmount = SyncedConfig(
                "Healing",
                "ShamanHealAmount",
                40f,
                "Health points restored to the owner each time the evolved (Shaman) companion heals them.");

            ShamanHealCooldownSeconds = SyncedConfig(
                "Healing",
                "ShamanHealCooldownSeconds",
                15f,
                "Minimum delay (in seconds) between two heals from the evolved (Shaman) companion.");

            PickupRange = SyncedConfig(
                "Pickup",
                "PickupRange",
                10f,
                "Distance (in meters) within which the companion notices items lying on the ground and walks over to pick them up for its owner.");

            PickupIntervalSeconds = SyncedConfig(
                "Pickup",
                "PickupIntervalSeconds",
                0.3f,
                "How often (in seconds) the companion scans nearby ground for items to pick up.");

            PickupPhrase1 = SyncedConfig(
                "Pickup",
                "PickupPhrase1",
                "Ooh, shiny!",
                "First line the companion may say (picked at random) when it spots an item to fetch.");

            PickupPhrase2 = SyncedConfig(
                "Pickup",
                "PickupPhrase2",
                "Look, something shiny!",
                "Second line the companion may say (picked at random) when it spots an item to fetch.");

            PickupChatCooldownSeconds = SyncedConfig(
                "Pickup",
                "PickupChatCooldownSeconds",
                20f,
                "Minimum delay (in seconds) between two pickup lines said by the companion, so it doesn't comment on every single item.");

            RenameHintText = SyncedConfig(
                "Companion",
                "RenameHintText",
                "[Shift+E] Rename",
                "Hover hint shown under the companion's name, telling players how to rename it.");

            RenamePromptText = SyncedConfig(
                "Companion",
                "RenamePromptText",
                "Rename companion",
                "Title shown at the top of the rename text box opened with Shift+E.");

            BehaviorModeHintText = SyncedConfig(
                "Companion",
                "BehaviorModeHintText",
                "[E] Toggle Combat Mode",
                "Hover hint telling players how to switch the companion's behavior mode (Defensive/Aggressive, see CompanionBehaviorMode).");

            DefensiveModeLabel = SyncedConfig(
                "Companion",
                "DefensiveModeLabel",
                "Defensive",
                "Label shown next to the companion's name (in its hover tooltip) while in Defensive mode.");

            AggressiveModeLabel = SyncedConfig(
                "Companion",
                "AggressiveModeLabel",
                "Aggressive",
                "Label shown next to the companion's name (in its hover tooltip) while in Aggressive mode.");

            DefensiveModeMessage = SyncedConfig(
                "Companion",
                "DefensiveModeMessage",
                "Playing it safe now.",
                "Line the companion says (in its speech bubble) when switched to Defensive mode.");

            AggressiveModeMessage = SyncedConfig(
                "Companion",
                "AggressiveModeMessage",
                "Ready to fight!",
                "Line the companion says (in its speech bubble) when switched to Aggressive mode.");

            CombatEngageRange = SyncedConfig(
                "Companion",
                "CombatEngageRange",
                12f,
                "Distance (in meters) within which the companion looks for a wild creature currently targeting the owner or itself -- used both by Defensive mode (to evade a creature targeting it) and Aggressive mode (to fight one targeting either the owner or itself). Never picks a fight with something that isn't already targeting the owner or the companion, so it won't drag the companion into unrelated fights just by walking through a Black Forest.");

            CombatAttackCooldownSeconds = SyncedConfig(
                "Companion",
                "CombatAttackCooldownSeconds",
                2f,
                "Minimum delay (in seconds) between two attacks from the companion in Aggressive mode.");

            SummonItemSourceItem = SyncedConfig(
                "SummonItem",
                "SummonItemSourceItem",
                "TrophyGreydwarf",
                "Name of the vanilla item prefab used as a base for the summoning item -- still governs its in-world/in-hand 3D model (placeholder until a custom one is made), but no longer its inventory icon, which is always the custom one bundled with this mod (knorri_seed.jpeg, see SummonItemPrefabPatch). Greylings don't drop their own trophy in vanilla, hence the adult Greydwarf's.");

            SummonItemName = SyncedConfig(
                "SummonItem",
                "SummonItemName",
                "Knorri Seed",
                "Display name of the summoning item.");

            SummonItemDescription = SyncedConfig(
                "SummonItem",
                "SummonItemDescription",
                "A strange seed that summons a tame Greyling companion when used.",
                "Description shown in the tooltip of the summoning item.");

            SummonCooldownSeconds = SyncedConfig(
                "SummonItem",
                "SummonCooldownSeconds",
                3f,
                "Minimum delay (in seconds) between two companion summons/store-aways with the charm. Shown visually as a darkened icon with a countdown on the charm in the inventory (see SummonItemCooldownOverlayPatch).");

            SummonDistance = SyncedConfig(
                "SummonItem",
                "SummonDistance",
                2f,
                "Distance (in meters) in front of the player at which the companion is summoned.");

            SummonItemOwnerLabel = SyncedConfig(
                "SummonItem",
                "SummonItemOwnerLabel",
                "Belongs to: {0}",
                "Line appended to the summoning item's tooltip once it has bound to an owner (see SummonItemOwnershipPatch). {0} is replaced with the owner's name.");

            SummonItemNotOwnerMessage = SyncedConfig(
                "SummonItem",
                "SummonItemNotOwnerMessage",
                "This seed doesn't answer to you.",
                "Message shown to a player who tries to use a summoning item that's already bound to someone else.");

            CannotDowngradeMessage = SyncedConfig(
                "SummonItem",
                "CannotDowngradeMessage",
                "Your companion has already evolved beyond this seed.",
                "Message shown when using the base seed while an evolved (Shaman) companion is already summoned -- the existing companion is left untouched, it never downgrades.");

            ShamanSummonItemSourceItem = SyncedConfig(
                "ShamanSummonItem",
                "ShamanSummonItemSourceItem",
                "AncientSeed",
                "Name of the vanilla item prefab used as a base for the evolved summoning item -- still governs its in-world/in-hand 3D model (placeholder until a custom one is made), but not its inventory icon (see SummonItemSourceItem for the equivalent on the base seed).");

            ShamanSummonItemName = SyncedConfig(
                "ShamanSummonItem",
                "ShamanSummonItemName",
                "Shaman Seed",
                "Display name of the evolved summoning item, crafted from the base seed plus honey (see ShamanRecipeHoneyItem/ShamanRecipeHoneyAmount).");

            ShamanSummonItemDescription = SyncedConfig(
                "ShamanSummonItem",
                "ShamanSummonItemDescription",
                "A seed steeped in shamanic magic. Summons an evolved Greydwarf Shaman companion when used.",
                "Description shown in the tooltip of the evolved summoning item.");

            ShamanRecipeHoneyItem = SyncedConfig(
                "ShamanSummonItem",
                "ShamanRecipeHoneyItem",
                "Honey",
                "Name of the vanilla item prefab required (alongside the base seed) to craft the evolved summoning item.");

            ShamanRecipeHoneyAmount = SyncedConfig(
                "ShamanSummonItem",
                "ShamanRecipeHoneyAmount",
                5,
                "How many of ShamanRecipeHoneyItem are required to craft the evolved summoning item.");

            ShamanRecipeCraftingStation = SyncedConfig(
                "ShamanSummonItem",
                "ShamanRecipeCraftingStation",
                "piece_workbench",
                "Name of the vanilla crafting station piece required to craft the evolved summoning item.");

            ShamanRecipeMinStationLevel = SyncedConfig(
                "ShamanSummonItem",
                "ShamanRecipeMinStationLevel",
                1,
                "Minimum upgrade level of ShamanRecipeCraftingStation required to craft the evolved summoning item (1 = base station, no upgrades needed).");

            CoinPickupSoundVolume = SyncedConfig(
                "Pickup",
                "CoinPickupSoundVolume",
                1.5f,
                $"Volume of the coin pickup sound ({CoinPickupSoundFileName}), played when the companion picks up Coins specifically. Still a normal 3D positional sound that fades with distance -- this only boosts how loud it is up close. 1 = normal, higher = louder.");

            CoinPickupSoundMaxDistance = SyncedConfig(
                "Pickup",
                "CoinPickupSoundMaxDistance",
                20f,
                "Maximum distance (in meters) at which the coin pickup sound can be heard at all.");

            CoinPickupSoundCooldownSeconds = SyncedConfig(
                "Pickup",
                "CoinPickupSoundCooldownSeconds",
                20f,
                "Minimum delay (in seconds) between two coin pickup sounds, so picking up several coin stacks in a row doesn't spam the chime.");

            HealImpactSoundVolume = SyncedConfig(
                "Healing",
                "HealImpactSoundVolume",
                1.5f,
                $"Volume of the heal impact sound ({HealImpactSoundFileName}, optional -- drop it next to the DLL to enable it). Still a normal 3D positional sound that fades with distance -- this only boosts how loud it is up close. 1 = normal, higher = louder.");

            HealImpactSoundMaxDistance = SyncedConfig(
                "Healing",
                "HealImpactSoundMaxDistance",
                20f,
                "Maximum distance (in meters) at which the heal impact sound can be heard at all.");

            _configSync.IsLocked = true;

            // Abonnement aux événements Jotunn qui construiront les prefabs custom dès que
            // ZNetScene/ObjectDB seront prêts (voir CompanionPrefabPatch.cs/
            // SummonItemPrefabPatch.cs) -- rien de tout ça ne peut se faire ici, ZNetScene
            // n'existe pas encore à ce stade du chargement. Ordre important : les deux Init()
            // du palier de base doivent s'abonner AVANT ceux du chaman -- PrefabManager.
            // OnVanillaPrefabsAvailable est un delegate multicast, invoqué dans l'ordre
            // d'abonnement, et ShamanSummonItemPrefabPatch.RegisterRecipe a besoin que
            // SummonItemPrefabPatch.PrefabName (l'ingrédient de la recette) soit déjà résolvable
            // au moment où elle s'exécute, sur la même invocation de l'événement.
            CompanionPrefabPatch.Init();
            SummonItemPrefabPatch.Init();
            ShamanCompanionPrefabPatch.Init();
            ShamanSummonItemPrefabPatch.Init();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            StartCoroutine(LoadCoinPickupClip());
            StartCoroutine(LoadHealImpactClip());
        }

        private ConfigEntry<T> SyncedConfig<T>(string section, string key, T value, string description)
        {
            var entry = Config.Bind(section, key, value, description);
            _configSync.AddConfigEntry(entry);
            return entry;
        }

        // Le mp3 est déployé à côté de la DLL par le .csproj (CopyToPlugins) -- chargé de façon
        // asynchrone via UnityWebRequest, seule API disponible pour décoder un fichier audio
        // compressé à l'exécution sans passer par un AssetBundle Unity. Même technique que
        // FedoGoldRabbit.LoadCustomSpawnClip.
        private IEnumerator LoadCoinPickupClip()
        {
            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string path = Path.Combine(dllDir ?? "", CoinPickupSoundFileName);
            if (!File.Exists(path))
            {
                Log.LogWarning($"FedoKnorri: '{CoinPickupSoundFileName}' not found next to the plugin DLL, coin pickups will be silent.");
                yield break;
            }

            using var request = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.MPEG);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Log.LogWarning($"FedoKnorri: failed to load '{CoinPickupSoundFileName}': {request.error}");
                yield break;
            }

            _coinPickupClip = DownloadHandlerAudioClip.GetContent(request);
            Log.LogInfo($"FedoKnorri: loaded coin pickup sound '{CoinPickupSoundFileName}'.");
        }

        public void PlayCoinPickupSound(Vector3 position)
        {
            if (_coinPickupClip == null)
            {
                return;
            }

            PlaySpatialSound(_coinPickupClip, position, CoinPickupSoundVolume.Value, CoinPickupSoundMaxDistance.Value);
        }

        // healing.mp3 est optionnel (contrairement à shiny.mp3, requis dès la sortie) -- silence si
        // absent, LoadHealImpactClip se contente d'un avertissement, pas d'une erreur.
        private IEnumerator LoadHealImpactClip()
        {
            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string path = Path.Combine(dllDir ?? "", HealImpactSoundFileName);
            if (!File.Exists(path))
            {
                Log.LogWarning($"FedoKnorri: '{HealImpactSoundFileName}' not found next to the plugin DLL, heal impacts will be silent (particles only).");
                yield break;
            }

            using var request = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.MPEG);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Log.LogWarning($"FedoKnorri: failed to load '{HealImpactSoundFileName}': {request.error}");
                yield break;
            }

            _healImpactClip = DownloadHandlerAudioClip.GetContent(request);
            Log.LogInfo($"FedoKnorri: loaded heal impact sound '{HealImpactSoundFileName}'.");
        }

        public void PlayHealImpactSound(Vector3 position)
        {
            if (_healImpactClip == null)
            {
                return;
            }

            PlaySpatialSound(_healImpactClip, position, HealImpactSoundVolume.Value, HealImpactSoundMaxDistance.Value);
        }

        // Toujours un son 3D positionnel (spatialBlend = 1) qui s'atténue avec la distance -- sur
        // un serveur multijoueur, un joueur loin du compagnon ne doit pas l'entendre. Même
        // technique que FedoGoldRabbit.PlaySpatialSound.
        private void PlaySpatialSound(AudioClip clip, Vector3 position, float volume, float maxDistance)
        {
            var soundObj = new GameObject("FedoKnorri_SpatialSound");
            soundObj.transform.position = position;
            var source = soundObj.AddComponent<AudioSource>();
            source.clip = clip;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.maxDistance = maxDistance;
            source.volume = volume;
            source.Play();
            Object.Destroy(soundObj, clip.length + 0.1f);
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
