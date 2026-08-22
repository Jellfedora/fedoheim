using System;
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
        public const string PluginVersion = "1.0.0";

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

        // ServerSync (voir mods/_shared/ConfigSync.cs) : tous les réglages ci-dessus
        // affectent ce que voit/vit tout le monde sur ce serveur (le gardien de n'importe
        // quel joueur), donc verrouillés -- un joueur ne doit pas pouvoir les changer
        // localement pour lui-même.
        private readonly ConfigSync _configSync = new ConfigSync(PluginGuid) { DisplayName = PluginName, CurrentVersion = PluginVersion };

        private Harmony _harmony;

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
    }
}
