using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace FedoSkillSafety
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FedoSkillSafetyPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.skillsafety";
        public const string PluginName = "FedoSkillSafety";
        public const string PluginVersion = "1.0.0";

        public static FedoSkillSafetyPlugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        public ConfigEntry<int> PalierSize;

        public ConfigEntry<bool> ShowPalierUnlockedMessage;
        public ConfigEntry<string> PalierUnlockedMessageText;

        // ServerSync (voir mods/_shared/ConfigSync.cs) : PalierSize change l'équilibrage
        // (combien de niveaux sont réellement protégés d'une mort), donc verrouillé comme
        // les autres réglages de gameplay partagé de ce repo -- un joueur ne doit pas
        // pouvoir s'accorder des paliers plus larges pour lui-même. Ne dépend d'aucun
        // serveur pour fonctionner : sans connexion à un serveur qui a aussi ce mod (ou en
        // solo), le client applique simplement sa propre valeur locale -- ServerSync
        // ne fait que permettre à un admin de l'imposer quand il y a un serveur.
        private readonly ConfigSync _configSync = new ConfigSync(PluginGuid) { DisplayName = PluginName, CurrentVersion = PluginVersion };

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            PalierSize = SyncedConfig(
                "Skills",
                "PalierSize",
                5,
                "Number of skill levels between two permanent floors. Once a skill's level reaches a multiple of this value, it can never drop below that level again, even after death.");

            ShowPalierUnlockedMessage = Config.Bind(
                "Skills",
                "ShowPalierUnlockedMessage",
                true,
                "Shows an on-screen message the first time a skill reaches a new tier.");

            PalierUnlockedMessageText = Config.Bind(
                "Skills",
                "PalierUnlockedMessageText",
                "Tier unlocked: {skill} level {level}!",
                "On-screen message shown the first time a skill reaches a new tier. {skill} is replaced with the skill's name, {level} with the tier level reached.");

            _configSync.IsLocked = true;

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            FedoHudIntegration.TryPatch(_harmony);
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

        // Appelé par RaiseSkillPatch dès qu'un nouveau palier vient d'être franchi.
        // Centré, en jaune -- même convention que FedoDeath/FedoGuardian (voir
        // MessageHud.instance.ShowMessage). Local uniquement (pas de RPC, contrairement
        // à BroadcastMessage.cs) : ça ne concerne que le joueur qui vient de progresser.
        public void AnnouncePalierUnlocked(Player player, Skills.SkillType type, int palier)
        {
            if (!ShowPalierUnlockedMessage.Value || player != Player.m_localPlayer || MessageHud.instance == null)
            {
                return;
            }

            string skillName = LocalizeSkillName(type);
            string text = PalierUnlockedMessageText.Value
                .Replace("{skill}", skillName)
                .Replace("{level}", palier.ToString());

            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"<color=yellow>{text}</color>");
        }

        // Même formule que le panneau de compétences natif du jeu (clé "$skill_xxx"
        // traduite via Localization.instance, voir FedoHud/SkillLocalization.cs pour le
        // même besoin résolu côté HUD) -- dupliqué ici plutôt que référencé depuis FedoHud
        // pour ne pas dépendre de sa présence : ce message doit s'afficher même sans
        // FedoHud installé.
        private static string LocalizeSkillName(Skills.SkillType type)
        {
            string key = "$skill_" + type.ToString().ToLowerInvariant();
            try
            {
                if (Localization.instance != null)
                {
                    return Localization.instance.Localize(key);
                }
            }
            catch (Exception e)
            {
                Log?.LogWarning($"FedoSkillSafety: failed to localize \"{key}\": {e.Message}");
            }

            return key;
        }
    }
}
