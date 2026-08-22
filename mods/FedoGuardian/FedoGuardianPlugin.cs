using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace FedoGuardian
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FedoGuardianPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.guardian";
        public const string PluginName = "FedoGuardian";
        public const string PluginVersion = "1.0.0";

        public static FedoGuardianPlugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        // Conteneur racine désactivé en permanence, destiné à recevoir tous les gabarits de
        // prefabs clonés (garde, baguette) : tant qu'un objet reste enfant d'un parent inactif,
        // Unity ne déclenche jamais Awake/OnEnable/Start dessus, quel que soit son propre
        // "activeSelf". Sans ça, cloner un prefab actif (comme Player) exécute ses scripts pour de
        // vrai le temps d'une frame -- ZNetView y crée une vraie ZDO à l'origine du monde (0,0,0),
        // ce qui peut perturber des systèmes globaux (vécu : le joueur "attiré" vers cet endroit).
        public static Transform TemplateRoot { get; private set; }

        public ConfigEntry<float> DetectionRange;
        public ConfigEntry<string> GuardianNameTemplate;
        public ConfigEntry<string> HoverHintText;

        public ConfigEntry<string> SummonWandSourceItem;
        public ConfigEntry<string> SummonWandName;
        public ConfigEntry<float> SummonWandCooldownSeconds;
        public ConfigEntry<float> SummonDistance;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            var root = new GameObject("FedoGuardian_TemplateRoot");
            Object.DontDestroyOnLoad(root);
            root.SetActive(false);
            TemplateRoot = root.transform;

            DetectionRange = Config.Bind(
                "Guardian",
                "DetectionRange",
                15f,
                "Distance (in meters) within which the guardian will notice and engage hostile creatures.");

            GuardianNameTemplate = Config.Bind(
                "Guardian",
                "GuardianNameTemplate",
                "Guardian",
                "Display name shown when hovering over the guardian.");

            HoverHintText = Config.Bind(
                "Guardian",
                "HoverHintText",
                "[Use] Equip currently worn gear\n[Alt+Use] Take back guardian's equipment",
                "Hint shown under the guardian's name when hovering over it.");

            SummonWandSourceItem = Config.Bind(
                "SummonWand",
                "SummonWandSourceItem",
                "Club",
                "Name of the vanilla item prefab used as a visual/base for the summoning wand (placeholder until a custom model is made).");

            SummonWandName = Config.Bind(
                "SummonWand",
                "SummonWandName",
                "Enslavement Wand",
                "Display name of the summoning wand item.");

            SummonWandCooldownSeconds = Config.Bind(
                "SummonWand",
                "SummonWandCooldownSeconds",
                1.5f,
                "Minimum delay (in seconds) between two guardian summons with the wand.");

            SummonDistance = Config.Bind(
                "SummonWand",
                "SummonDistance",
                2f,
                "Distance (in meters) in front of the player at which the guardian is summoned.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
