using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace FedoHud
{
    // Une case à cocher du panneau en jeu (voir FedoHudSettingsPanel.cs) : un libellé
    // affiché + la ConfigEntry qu'elle bascule directement.
    public readonly struct ToggleOption
    {
        public readonly string Label;
        public readonly ConfigEntry<bool> Entry;

        public ToggleOption(string label, ConfigEntry<bool> entry)
        {
            Label = label;
            Entry = entry;
        }
    }

    // Regroupe les éléments de HUD custom purement locaux (aucun appel réseau) : l'horloge
    // en jeu (déplaçable) et l'ajout d'une ligne "pousse dans Xh" au survol d'une
    // plante/d'un jeune arbre planté au cultivateur (voir GrowthTooltip.cs), et le reste
    // des tooltips/panneaux de ce mod.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FedoHudPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.hud";
        public const string PluginName = "FedoHud";
        public const string PluginVersion = "1.0.0";

        public static FedoHudPlugin Instance { get; private set; }

        // Exposé pour que les classes de patch Harmony (statiques) puissent logguer sans
        // jamais laisser une exception remonter dans le code du jeu qu'elles patchent.
        public static ManualLogSource Log { get; private set; }

        // Horloge en jeu -- voir ClockOverlay.cs pour l'affichage à l'écran. Calcul dérivé
        // indépendamment de EnvMan.GetDayFraction().
        // `_clockRefreshTimer` throttle le rafraîchissement à ~1x/seconde plutôt qu'à
        // chaque frame, la minute affichée ne changeant de toute façon pas plus vite que ça.
        private ConfigEntry<bool> _showClockOverlay;
        private float _clockRefreshTimer;

        // Position de l'horloge à l'écran (voir ClockOverlay.cs/DraggableAnchor.cs,
        // cliquer-glisser directement) -- préférence purement locale à cette
        // installation.
        private ConfigEntry<float> _clockPositionX;
        private ConfigEntry<float> _clockPositionY;

        // Ligne ajoutée au survol d'une plante en cours de pousse -- voir GrowthTooltip.cs.
        private ConfigEntry<bool> _showGrowthTooltip;
        private ConfigEntry<string> _growthRemainingPrefix;
        private ConfigEntry<string> _growthReadyText;

        public bool ShowGrowthTooltip => _showGrowthTooltip.Value;
        public string GrowthRemainingPrefix => _growthRemainingPrefix.Value;
        public string GrowthReadyText => _growthReadyText.Value;

        // Icône "!" flottante au-dessus d'une culture prête à récolter -- voir
        // PlantReadyIndicator.cs. Toggle séparé de ShowGrowthTooltip (le texte au survol) :
        // les deux sont indépendants.
        private ConfigEntry<bool> _showGrowthReadyIcon;
        public bool ShowGrowthReadyIcon => _showGrowthReadyIcon.Value;

        // Ligne ajoutée au survol d'une ruche -- voir BeehiveTooltip.cs. Section distincte
        // de [Growth] : une ruche n'est pas une `Plant`, et l'admin/joueur doit pouvoir
        // désactiver l'un sans l'autre.
        private ConfigEntry<bool> _showBeehiveTooltip;
        private ConfigEntry<string> _honeyRemainingPrefix;
        private ConfigEntry<string> _honeyFullText;
        private ConfigEntry<string> _honeyPausedText;

        public bool ShowBeehiveTooltip => _showBeehiveTooltip.Value;
        public string HoneyRemainingPrefix => _honeyRemainingPrefix.Value;
        public string HoneyFullText => _honeyFullText.Value;
        public string HoneyPausedText => _honeyPausedText.Value;

        // Icône "!" flottante au-dessus d'une ruche pleine -- voir BeehiveFullIndicator.cs.
        // Toggle séparé de ShowBeehiveTooltip (le texte au survol) : les deux sont
        // indépendants.
        private ConfigEntry<bool> _showBeehiveFullIcon;
        public bool ShowBeehiveFullIcon => _showBeehiveFullIcon.Value;

        // Ligne ajoutée au survol d'un fermenteur -- voir FermenterTooltip.cs.
        private ConfigEntry<bool> _showFermenterTooltip;
        private ConfigEntry<string> _fermenterRemainingPrefix;
        private ConfigEntry<string> _fermenterReadyText;

        public bool ShowFermenterTooltip => _showFermenterTooltip.Value;
        public string FermenterRemainingPrefix => _fermenterRemainingPrefix.Value;
        public string FermenterReadyText => _fermenterReadyText.Value;

        // Ligne ajoutée au survol d'une fonderie/d'un four à charbon -- voir
        // SmelterTooltip.cs (les deux partagent la même classe `Smelter` côté jeu).
        private ConfigEntry<bool> _showSmelterTooltip;
        private ConfigEntry<string> _smelterRemainingPrefix;
        private ConfigEntry<string> _smelterReadyText;
        private ConfigEntry<string> _smelterPausedText;

        public bool ShowSmelterTooltip => _showSmelterTooltip.Value;
        public string SmelterRemainingPrefix => _smelterRemainingPrefix.Value;
        public string SmelterReadyText => _smelterReadyText.Value;
        public string SmelterPausedText => _smelterPausedText.Value;

        // Ligne ajoutée au survol d'une ressource sauvage déjà cueillie -- voir
        // PickableTooltip.cs.
        private ConfigEntry<bool> _showPickableTooltip;
        private ConfigEntry<string> _pickableRemainingPrefix;

        public bool ShowPickableTooltip => _showPickableTooltip.Value;
        public string PickableRemainingPrefix => _pickableRemainingPrefix.Value;

        // Lignes ajoutées au survol d'une broche de cuisson -- voir
        // CookingStationTooltip.cs.
        private ConfigEntry<bool> _showCookingTooltip;
        private ConfigEntry<string> _cookingRemainingPrefix;
        private ConfigEntry<string> _cookingBurnPrefix;

        public bool ShowCookingTooltip => _showCookingTooltip.Value;
        public string CookingRemainingPrefix => _cookingRemainingPrefix.Value;
        public string CookingBurnPrefix => _cookingBurnPrefix.Value;

        // Lignes ajoutées au survol d'un animal apprivoisé -- voir TameableTooltip.cs.
        private ConfigEntry<bool> _showTameableTooltip;
        private ConfigEntry<string> _hungryInPrefix;
        private ConfigEntry<string> _loveProgressPrefix;
        private ConfigEntry<string> _pregnantRemainingPrefix;
        private ConfigEntry<string> _pregnantReadyText;
        private ConfigEntry<bool> _showGrowUpTooltip;
        private ConfigEntry<string> _growUpRemainingPrefix;

        public bool ShowTameableTooltip => _showTameableTooltip.Value;
        public string HungryInPrefix => _hungryInPrefix.Value;
        public string LoveProgressPrefix => _loveProgressPrefix.Value;
        public string PregnantRemainingPrefix => _pregnantRemainingPrefix.Value;
        public string PregnantReadyText => _pregnantReadyText.Value;
        public bool ShowGrowUpTooltip => _showGrowUpTooltip.Value;
        public string GrowUpRemainingPrefix => _growUpRemainingPrefix.Value;

        // Icône d'épingle par case du marteau (voir RecipeTracker.cs) + panneau
        // d'ingrédients déplaçable pour les pièces épinglées (voir
        // RecipeTrackerOverlay.cs) -- deux interrupteurs séparés, comme pour les autres
        // icônes/blocs de ce mod.
        private ConfigEntry<bool> _showRecipeTrackerIcon;
        private ConfigEntry<bool> _showRecipeTracker;
        private ConfigEntry<float> _recipeTrackerPositionX;
        private ConfigEntry<float> _recipeTrackerPositionY;
        private float _recipeTrackerRefreshTimer;

        public bool ShowRecipeTrackerIcon => _showRecipeTrackerIcon.Value;
        public bool ShowRecipeTracker => _showRecipeTracker.Value;
        public Vector2 SavedRecipeTrackerPosition => new Vector2(_recipeTrackerPositionX.Value, _recipeTrackerPositionY.Value);
        public Vector2 DefaultRecipeTrackerPosition => new Vector2((float)_recipeTrackerPositionX.DefaultValue, (float)_recipeTrackerPositionY.DefaultValue);

        public void SaveRecipeTrackerPosition(Vector2 anchoredPosition)
        {
            _recipeTrackerPositionX.Value = anchoredPosition.x;
            _recipeTrackerPositionY.Value = anchoredPosition.y;
        }

        // Grossit + anime le chiffre de dégâts natif du jeu quand c'est le joueur local
        // qui vient de frapper -- voir PlayerDamageTextBoost.cs.
        private ConfigEntry<bool> _showPlayerDamageTextBoost;
        private ConfigEntry<float> _playerDamageTextSizeMultiplier;

        public bool ShowPlayerDamageTextBoost => _showPlayerDamageTextBoost.Value;
        public float PlayerDamageTextSizeMultiplier => _playerDamageTextSizeMultiplier.Value;

        // Bloc niveau + barre de progression pour une liste de compétences configurable
        // -- voir SkillsOverlay.cs. `_skillsRefreshTimer` throttle comme pour l'horloge.
        private ConfigEntry<bool> _showSkillsOverlay;
        private ConfigEntry<string> _skillsList;
        private ConfigEntry<float> _skillsPositionX;
        private ConfigEntry<float> _skillsPositionY;
        private float _skillsRefreshTimer;

        // Parsée depuis `SkillsList` ("WoodCutting,Farming,...") à chaque accès -- lue
        // seulement à la (re)création du bloc (voir SkillsOverlay.Create), jamais par
        // frame. Un nom invalide est simplement ignoré (avec un avertissement en log)
        // plutôt que de faire échouer toute la liste.
        public IReadOnlyList<Skills.SkillType> ParsedSkillTypes
        {
            get
            {
                var result = new List<Skills.SkillType>();
                foreach (var part in (_skillsList.Value ?? "").Split(','))
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    if (Enum.TryParse(trimmed, ignoreCase: true, out Skills.SkillType type))
                    {
                        result.Add(type);
                    }
                    else
                    {
                        Log?.LogWarning($"FedoHud: unknown skill name \"{trimmed}\" in SkillsList, ignored.");
                    }
                }

                return result;
            }
        }

        // Consommé par FedoHudSettingsPanel.cs pour construire le sélecteur de
        // compétences (une case par valeur de l'enum, `None`/`All` exclus -- ce ne sont
        // pas de vraies compétences).
        public bool IsSkillSelected(Skills.SkillType type)
        {
            foreach (var t in ParsedSkillTypes)
            {
                if (t == type)
                {
                    return true;
                }
            }

            return false;
        }

        public void ToggleSkill(Skills.SkillType type)
        {
            var current = new List<Skills.SkillType>(ParsedSkillTypes);
            if (!current.Remove(type))
            {
                current.Add(type);
            }

            _skillsList.Value = string.Join(",", current);
        }

        public Vector2 SavedSkillsPosition => new Vector2(_skillsPositionX.Value, _skillsPositionY.Value);
        public Vector2 DefaultSkillsPosition => new Vector2((float)_skillsPositionX.DefaultValue, (float)_skillsPositionY.DefaultValue);

        public void SaveSkillsPosition(Vector2 anchoredPosition)
        {
            _skillsPositionX.Value = anchoredPosition.x;
            _skillsPositionY.Value = anchoredPosition.y;
        }

        // Bloc commun morts/poids/armure -- voir PlayerStatsOverlay.cs. Un interrupteur
        // par indicateur (chacun peut être caché individuellement dans le bloc), mais
        // une seule position pour les trois -- déplacer l'un déplace tout le bloc.
        // Le compteur de morts compte depuis la toute première création du personnage
        // (Valheim tient déjà lui-même ce total, PlayerStatType.Deaths, pas seulement
        // depuis l'installation de ce mod).
        private ConfigEntry<bool> _showDeathCounter;
        private ConfigEntry<bool> _showWeightOverlay;
        private ConfigEntry<bool> _showArmorOverlay;
        private ConfigEntry<float> _playerStatsPositionX;
        private ConfigEntry<float> _playerStatsPositionY;
        private float _playerStatsRefreshTimer;

        public bool ShowDeathCounter => _showDeathCounter.Value;
        public bool ShowWeightOverlay => _showWeightOverlay.Value;
        public bool ShowArmorOverlay => _showArmorOverlay.Value;
        public Vector2 SavedPlayerStatsPosition => new Vector2(_playerStatsPositionX.Value, _playerStatsPositionY.Value);
        public Vector2 DefaultPlayerStatsPosition => new Vector2((float)_playerStatsPositionX.DefaultValue, (float)_playerStatsPositionY.DefaultValue);

        public void SavePlayerStatsPosition(Vector2 anchoredPosition)
        {
            _playerStatsPositionX.Value = anchoredPosition.x;
            _playerStatsPositionY.Value = anchoredPosition.y;
        }

        // Libellés du panneau en jeu (voir FedoHudSettingsPanel.cs) -- textes affichés
        // au joueur, donc passés par Config.Bind comme le reste de ce mod pour rester
        // traduisibles en éditant fedo.hud.cfg (valeurs par défaut en anglais).
        private ConfigEntry<string> _settingsPanelOptionsLabel;
        private ConfigEntry<string> _settingsPanelSkillsLabel;
        private ConfigEntry<string> _settingsPanelCloseLabel;
        private ConfigEntry<string> _settingsPanelResetPositionsLabel;
        private ConfigEntry<string> _toggleLabelClock;
        private ConfigEntry<string> _toggleLabelGrowthTooltip;
        private ConfigEntry<string> _toggleLabelGrowthReadyIcon;
        private ConfigEntry<string> _toggleLabelBeehiveTooltip;
        private ConfigEntry<string> _toggleLabelBeehiveFullIcon;
        private ConfigEntry<string> _toggleLabelFermenterTooltip;
        private ConfigEntry<string> _toggleLabelSmelterTooltip;
        private ConfigEntry<string> _toggleLabelPickableTooltip;
        private ConfigEntry<string> _toggleLabelCookingTooltip;
        private ConfigEntry<string> _toggleLabelTameableTooltip;
        private ConfigEntry<string> _toggleLabelSkillsOverlay;
        private ConfigEntry<string> _toggleLabelDeathCounter;
        private ConfigEntry<string> _toggleLabelRecipeTrackerIcon;
        private ConfigEntry<string> _toggleLabelRecipeTracker;
        private ConfigEntry<string> _toggleLabelPlayerDamageTextBoost;
        private ConfigEntry<string> _toggleLabelWeightOverlay;
        private ConfigEntry<string> _toggleLabelArmorOverlay;

        public string SettingsPanelOptionsLabel => _settingsPanelOptionsLabel.Value;
        public string SettingsPanelSkillsLabel => _settingsPanelSkillsLabel.Value;
        public string SettingsPanelCloseLabel => _settingsPanelCloseLabel.Value;
        public string SettingsPanelResetPositionsLabel => _settingsPanelResetPositionsLabel.Value;

        // Remet chaque bloc déplaçable de ce mod à sa position par défaut (celle du tout
        // premier lancement) -- lit `DefaultValue` de chaque ConfigEntry plutôt que de
        // dupliquer les valeurs numériques ici, pour ne jamais désynchroniser les deux si
        // un défaut change un jour. Répercute aussi le changement sur les blocs déjà à
        // l'écran (voir chaque XxxOverlay.ResetPosition) -- sans ça, il faudrait
        // recharger la zone/redémarrer pour voir l'effet.
        public void ResetOverlayPositions()
        {
            _clockPositionX.Value = (float)_clockPositionX.DefaultValue;
            _clockPositionY.Value = (float)_clockPositionY.DefaultValue;

            ClockOverlay.ResetPosition(SavedClockPosition);

            // Celles-là recalculent leur Y sous la minimap plutôt que de reprendre un
            // nombre fixe (voir HudLayout.ResolveTopRightPosition/
            // ResolveCenteredBelowMinimapPosition), et sauvegardent elles-mêmes le
            // résultat (voir chaque ResetPosition) -- rien à réinitialiser ici avant de
            // les appeler.
            SkillsOverlay.ResetPosition();
            PlayerStatsOverlay.ResetPosition();
            RecipeTrackerOverlay.ResetPosition();
        }

        // Consommé par FedoHudSettingsPanel.cs pour générer une ligne par réglage on/off
        // sans dupliquer la liste ailleurs -- une seule liste à tenir à jour à chaque
        // nouveau toggle ajouté à ce mod. Reconstruite à chaque accès (pas de cache) :
        // seulement lue à l'ouverture du panneau, jamais par frame.
        public IReadOnlyList<ToggleOption> ToggleOptions => new[]
        {
            new ToggleOption(_toggleLabelClock.Value, _showClockOverlay),
            new ToggleOption(_toggleLabelGrowthTooltip.Value, _showGrowthTooltip),
            new ToggleOption(_toggleLabelGrowthReadyIcon.Value, _showGrowthReadyIcon),
            new ToggleOption(_toggleLabelBeehiveTooltip.Value, _showBeehiveTooltip),
            new ToggleOption(_toggleLabelBeehiveFullIcon.Value, _showBeehiveFullIcon),
            new ToggleOption(_toggleLabelFermenterTooltip.Value, _showFermenterTooltip),
            new ToggleOption(_toggleLabelSmelterTooltip.Value, _showSmelterTooltip),
            new ToggleOption(_toggleLabelPickableTooltip.Value, _showPickableTooltip),
            new ToggleOption(_toggleLabelCookingTooltip.Value, _showCookingTooltip),
            new ToggleOption(_toggleLabelTameableTooltip.Value, _showTameableTooltip),
            new ToggleOption(_toggleLabelSkillsOverlay.Value, _showSkillsOverlay),
            new ToggleOption(_toggleLabelDeathCounter.Value, _showDeathCounter),
            new ToggleOption(_toggleLabelRecipeTrackerIcon.Value, _showRecipeTrackerIcon),
            new ToggleOption(_toggleLabelRecipeTracker.Value, _showRecipeTracker),
            new ToggleOption(_toggleLabelPlayerDamageTextBoost.Value, _showPlayerDamageTextBoost),
            new ToggleOption(_toggleLabelWeightOverlay.Value, _showWeightOverlay),
            new ToggleOption(_toggleLabelArmorOverlay.Value, _showArmorOverlay),
        };

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            _showClockOverlay = Config.Bind(
                "Time",
                "ShowClockOverlay",
                true,
                "Shows a small in-game clock (HH:MM, following the day/night cycle) at the top-center of the screen. Purely a local HUD addition -- no network call involved.");

            _clockPositionX = Config.Bind(
                "Time",
                "ClockPositionX",
                0f,
                "Horizontal position of the clock overlay, in UI pixels from the top-center of the screen. Saved automatically when you drag the clock (click and drag it with the mouse) -- not meant to be hand-edited, but you can reset it here.");
            _clockPositionY = Config.Bind(
                "Time",
                "ClockPositionY",
                -18f,
                "Vertical position of the clock overlay, in UI pixels from the top-center of the screen (negative = downward). Saved automatically when you drag the clock (click and drag it with the mouse).");

            _showGrowthTooltip = Config.Bind(
                "Growth",
                "ShowGrowthTooltip",
                true,
                "Adds a line to the hover tooltip of a planted crop/sapling showing how long until it's ready to harvest. Purely a local HUD addition -- no network call involved.");

            _growthRemainingPrefix = Config.Bind(
                "Growth",
                "GrowthRemainingPrefix",
                "Grows in",
                "Text shown before the remaining growth time in a plant's hover tooltip, e.g. \"Grows in 3h20m\".");

            _growthReadyText = Config.Bind(
                "Growth",
                "GrowthReadyText",
                "Ready to harvest!",
                "Text shown in a plant's hover tooltip once its growth time has elapsed.");

            _showGrowthReadyIcon = Config.Bind(
                "Growth",
                "ShowGrowthReadyIcon",
                true,
                "Shows a floating \"!\" above a crop once it's ready to harvest (never above a planted tree sapling). Purely a local HUD addition -- no network call involved.");

            _showBeehiveTooltip = Config.Bind(
                "Beehive",
                "ShowBeehiveTooltip",
                true,
                "Adds a line to a beehive's hover tooltip showing how long until it produces one more honey. Purely a local HUD addition -- no network call involved.");

            _honeyRemainingPrefix = Config.Bind(
                "Beehive",
                "HoneyRemainingPrefix",
                "Next honey in",
                "Text shown before the remaining time until the next honey in a beehive's hover tooltip, e.g. \"Next honey in 0h45m\".");

            _honeyFullText = Config.Bind(
                "Beehive",
                "HoneyFullText",
                "Storage full!",
                "Text shown in a beehive's hover tooltip once it holds as much honey as it can -- collect some to resume production.");

            _honeyPausedText = Config.Bind(
                "Beehive",
                "HoneyPausedText",
                "Production paused",
                "Text shown in a beehive's hover tooltip when it can't currently produce honey (wrong biome, or no free space around it) -- no countdown shown in that case, since it wouldn't be accurate.");

            _showBeehiveFullIcon = Config.Bind(
                "Beehive",
                "ShowBeehiveFullIcon",
                true,
                "Shows a floating \"!\" above a beehive once it holds as much honey as it can. Purely a local HUD addition -- no network call involved.");

            _showFermenterTooltip = Config.Bind(
                "Fermenter",
                "ShowFermenterTooltip",
                true,
                "Adds a line to a fermenter's hover tooltip showing how long until it's done fermenting. Purely a local HUD addition -- no network call involved.");

            _fermenterRemainingPrefix = Config.Bind(
                "Fermenter",
                "FermenterRemainingPrefix",
                "Ready in",
                "Text shown before the remaining time in a fermenter's hover tooltip, e.g. \"Ready in 3h20m\".");

            _fermenterReadyText = Config.Bind(
                "Fermenter",
                "FermenterReadyText",
                "Ready!",
                "Text shown in a fermenter's hover tooltip once fermentation is done.");

            _showSmelterTooltip = Config.Bind(
                "Smelter",
                "ShowSmelterTooltip",
                true,
                "Adds a line to a smelter's/charcoal kiln's hover tooltip showing how long until everything queued is done. Purely a local HUD addition -- no network call involved.");

            _smelterRemainingPrefix = Config.Bind(
                "Smelter",
                "SmelterRemainingPrefix",
                "Done in",
                "Text shown before the remaining time in a smelter's/charcoal kiln's hover tooltip, e.g. \"Done in 3h20m\".");

            _smelterReadyText = Config.Bind(
                "Smelter",
                "SmelterReadyText",
                "Done!",
                "Text shown in a smelter's/charcoal kiln's hover tooltip once everything queued is done.");

            _smelterPausedText = Config.Bind(
                "Smelter",
                "SmelterPausedText",
                "Paused",
                "Text shown in a smelter's/charcoal kiln's hover tooltip when it can't currently process its queue (missing fuel, needs a roof...) -- no countdown shown in that case, since it wouldn't be accurate.");

            _showPickableTooltip = Config.Bind(
                "Pickable",
                "ShowPickableTooltip",
                true,
                "Adds a line to an already-picked wild resource's (mushrooms, berries, thistle...) hover tooltip showing how long until it respawns. Purely a local HUD addition -- no network call involved.");

            _pickableRemainingPrefix = Config.Bind(
                "Pickable",
                "PickableRemainingPrefix",
                "Respawns in",
                "Text shown before the remaining time in a picked resource's hover tooltip, e.g. \"Respawns in 0h45m\".");

            _showCookingTooltip = Config.Bind(
                "Cooking",
                "ShowCookingTooltip",
                true,
                "Adds a line per occupied slot on a cooking station's hover tooltip showing how long until it's cooked (or until it burns, once cooked). Purely a local HUD addition -- no network call involved.");

            _cookingRemainingPrefix = Config.Bind(
                "Cooking",
                "CookingRemainingPrefix",
                "Ready in",
                "Text shown before the remaining cooking time on a cooking station's hover tooltip, e.g. \"Ready in 0h5m\".");

            _cookingBurnPrefix = Config.Bind(
                "Cooking",
                "CookingBurnPrefix",
                "Burns in",
                "Text shown before the remaining time until an already-cooked item burns on a cooking station's hover tooltip.");

            _showTameableTooltip = Config.Bind(
                "Tameable",
                "ShowTameableTooltip",
                true,
                "Adds lines to a tamed animal's hover tooltip showing how long until it's hungry, and its breeding progress. Purely a local HUD addition -- no network call involved.");

            _hungryInPrefix = Config.Bind(
                "Tameable",
                "HungryInPrefix",
                "Hungry in",
                "Text shown before the remaining time until a tamed animal gets hungry, e.g. \"Hungry in 0h45m\". Not shown once it's already hungry (the game already says so).");

            _loveProgressPrefix = Config.Bind(
                "Tameable",
                "LoveProgressPrefix",
                "Love",
                "Text shown before a tamed animal's breeding progress, e.g. \"Love 2/4\". Breeding depends on a periodic random chance while well-fed, so no reliable countdown is possible outside of an active pregnancy.");

            _pregnantRemainingPrefix = Config.Bind(
                "Tameable",
                "PregnantRemainingPrefix",
                "Birth in",
                "Text shown before the remaining time until a pregnant tamed animal gives birth, e.g. \"Birth in 0h20m\".");

            _pregnantReadyText = Config.Bind(
                "Tameable",
                "PregnantReadyText",
                "Giving birth!",
                "Text shown once a pregnant tamed animal's gestation time has elapsed.");

            _showGrowUpTooltip = Config.Bind(
                "Tameable",
                "ShowGrowUpTooltip",
                true,
                "Adds a line to a baby animal's hover tooltip showing how long until it's an adult (tamed or not). Purely a local HUD addition -- no network call involved.");

            _growUpRemainingPrefix = Config.Bind(
                "Tameable",
                "GrowUpRemainingPrefix",
                "Adult in",
                "Text shown before the remaining time until a baby animal is an adult, e.g. \"Adult in 0h45m\".");

            _showSkillsOverlay = Config.Bind(
                "Skills",
                "ShowSkillsOverlay",
                true,
                "Shows a small block with your current level and a progress bar for each skill listed in SkillsList. Purely a local HUD addition -- no network call involved.");

            _skillsList = Config.Bind(
                "Skills",
                "SkillsList",
                "",
                "Comma-separated list of skills to show, e.g. \"WoodCutting,Farming,Cooking\" -- empty by default (shows nothing until you pick some, either by hand-editing this list or via the in-game FedoHud panel, see the pause menu). An unknown name is ignored (see the log). Valid names: Swords, Knives, Clubs, Polearms, Spears, Blocking, Axes, Bows, ElementalMagic, BloodMagic, Unarmed, Pickaxes, WoodCutting, Crossbows, Jump, Sneak, Run, Swim, Fishing, Cooking, Farming, Crafting, Dodge, Ride.");

            _skillsPositionX = Config.Bind(
                "Skills",
                "SkillsPositionX",
                -20f,
                "Horizontal position of the skills block, in UI pixels from the top-right of the screen (negative = leftward). Saved automatically when you drag it (click and drag it with the mouse) -- not meant to be hand-edited, but you can reset it here.");
            _skillsPositionY = Config.Bind(
                "Skills",
                "SkillsPositionY",
                -210f,
                "Vertical position of the skills block, in UI pixels from the top-right of the screen (negative = downward). Saved automatically when you drag it (click and drag it with the mouse).");

            _showDeathCounter = Config.Bind(
                "PlayerStats",
                "ShowDeathCounter",
                true,
                "Shows a small counter of how many times this character has died, since it was first created (not just since installing this mod), next to the skull icon. Purely a local HUD addition -- no network call involved.");

            _showWeightOverlay = Config.Bind(
                "PlayerStats",
                "ShowWeightOverlay",
                true,
                "Shows your current carry weight and maximum capacity next to the weight icon, turning red when you're over the limit. Purely a local HUD addition -- no network call involved.");

            _showArmorOverlay = Config.Bind(
                "PlayerStats",
                "ShowArmorOverlay",
                true,
                "Shows your total armor value next to the armor icon (the same value shown in your inventory screen). Purely a local HUD addition -- no network call involved.");

            _playerStatsPositionX = Config.Bind(
                "PlayerStats",
                "PlayerStatsPositionX",
                0f,
                "Horizontal position of the death/weight/armor block, in UI pixels from the top-right of the screen. Saved automatically when you drag it (click and drag it with the mouse) -- not meant to be hand-edited, but you can reset it here.");
            _playerStatsPositionY = Config.Bind(
                "PlayerStats",
                "PlayerStatsPositionY",
                -170f,
                "Vertical position of the death/weight/armor block, in UI pixels from the top-right of the screen (negative = downward). Saved automatically when you drag it (click and drag it with the mouse) -- starts below the game's own minimap by default.");

            _showRecipeTrackerIcon = Config.Bind(
                "RecipeTracker",
                "ShowRecipeTrackerIcon",
                true,
                "Adds a small pin icon to each piece in the hammer's build menu -- click it to add/remove that piece from the ingredients panel below. Independent from the game's own \"Favorites\" tab. Purely a local HUD addition -- no network call involved.");

            _showRecipeTracker = Config.Bind(
                "RecipeTracker",
                "ShowRecipeTracker",
                true,
                "Shows the draggable panel listing ingredients (and how many you already have) for every piece pinned with the icon above.");

            _recipeTrackerPositionX = Config.Bind(
                "RecipeTracker",
                "RecipeTrackerPositionX",
                -20f,
                "Horizontal position of the recipe ingredients panel, in UI pixels from the top-right of the screen (negative = leftward). Saved automatically when you drag it (click and drag it with the mouse) -- not meant to be hand-edited, but you can reset it here.");
            _recipeTrackerPositionY = Config.Bind(
                "RecipeTracker",
                "RecipeTrackerPositionY",
                -450f,
                "Vertical position of the recipe ingredients panel, in UI pixels from the top-right of the screen (negative = downward). Saved automatically when you drag it (click and drag it with the mouse).");

            _settingsPanelOptionsLabel = Config.Bind(
                "SettingsPanel",
                "OptionsSectionLabel",
                "Options",
                "Section title above the on/off toggles in the in-game FedoHud panel (pause menu).");

            _settingsPanelSkillsLabel = Config.Bind(
                "SettingsPanel",
                "SkillsSectionLabel",
                "Skills to show",
                "Section title above the skill picker grid in the in-game FedoHud panel (pause menu).");

            _settingsPanelCloseLabel = Config.Bind(
                "SettingsPanel",
                "CloseButtonLabel",
                "Close",
                "Label of the button that closes the in-game FedoHud panel (pause menu).");

            _settingsPanelResetPositionsLabel = Config.Bind(
                "SettingsPanel",
                "ResetPositionsButtonLabel",
                "Reset positions",
                "Label of the button that resets every draggable block (clock, skills, death counter, recipe panel, weight) back to its default screen position.");

            _toggleLabelClock = Config.Bind(
                "SettingsPanel",
                "ToggleLabelClock",
                "Clock",
                "Label of the clock's on/off toggle in the in-game FedoHud panel.");

            _toggleLabelGrowthTooltip = Config.Bind(
                "SettingsPanel",
                "ToggleLabelGrowthTooltip",
                "Crop hover hint",
                "Label of the crop growth hover hint's on/off toggle in the in-game FedoHud panel.");

            _toggleLabelGrowthReadyIcon = Config.Bind(
                "SettingsPanel",
                "ToggleLabelGrowthReadyIcon",
                "Crop ready icon",
                "Label of the crop ready icon's on/off toggle in the in-game FedoHud panel.");

            _toggleLabelBeehiveTooltip = Config.Bind(
                "SettingsPanel",
                "ToggleLabelBeehiveTooltip",
                "Beehive hover hint",
                "Label of the beehive hover hint's on/off toggle in the in-game FedoHud panel.");

            _toggleLabelBeehiveFullIcon = Config.Bind(
                "SettingsPanel",
                "ToggleLabelBeehiveFullIcon",
                "Beehive full icon",
                "Label of the beehive full icon's on/off toggle in the in-game FedoHud panel.");

            _toggleLabelFermenterTooltip = Config.Bind(
                "SettingsPanel",
                "ToggleLabelFermenterTooltip",
                "Fermenter hover hint",
                "Label of the fermenter hover hint's on/off toggle in the in-game FedoHud panel.");

            _toggleLabelSmelterTooltip = Config.Bind(
                "SettingsPanel",
                "ToggleLabelSmelterTooltip",
                "Smelter/kiln hover hint",
                "Label of the smelter/kiln hover hint's on/off toggle in the in-game FedoHud panel.");

            _toggleLabelPickableTooltip = Config.Bind(
                "SettingsPanel",
                "ToggleLabelPickableTooltip",
                "Wild resource hover hint",
                "Label of the wild resource respawn hover hint's on/off toggle in the in-game FedoHud panel.");

            _toggleLabelCookingTooltip = Config.Bind(
                "SettingsPanel",
                "ToggleLabelCookingTooltip",
                "Cooking station hover hint",
                "Label of the cooking station hover hint's on/off toggle in the in-game FedoHud panel.");

            _toggleLabelTameableTooltip = Config.Bind(
                "SettingsPanel",
                "ToggleLabelTameableTooltip",
                "Tamed animal hover hint",
                "Label of the tamed animal hover hint's on/off toggle in the in-game FedoHud panel.");

            _toggleLabelSkillsOverlay = Config.Bind(
                "SettingsPanel",
                "ToggleLabelSkillsOverlay",
                "Skills block",
                "Label of the skills block's on/off toggle in the in-game FedoHud panel.");

            _toggleLabelDeathCounter = Config.Bind(
                "SettingsPanel",
                "ToggleLabelDeathCounter",
                "Death counter",
                "Label of the death counter's on/off toggle in the in-game FedoHud panel.");

            _toggleLabelRecipeTrackerIcon = Config.Bind(
                "SettingsPanel",
                "ToggleLabelRecipeTrackerIcon",
                "Recipe pin icons",
                "Label of the recipe pin icons' on/off toggle in the in-game FedoHud panel.");

            _toggleLabelRecipeTracker = Config.Bind(
                "SettingsPanel",
                "ToggleLabelRecipeTracker",
                "Recipe ingredients panel",
                "Label of the recipe ingredients panel's on/off toggle in the in-game FedoHud panel.");

            _toggleLabelPlayerDamageTextBoost = Config.Bind(
                "SettingsPanel",
                "ToggleLabelPlayerDamageTextBoost",
                "Bigger damage numbers",
                "Label of the bigger-damage-numbers on/off toggle in the in-game FedoHud panel.");

            _toggleLabelWeightOverlay = Config.Bind(
                "SettingsPanel",
                "ToggleLabelWeightOverlay",
                "Carry weight",
                "Label of the carry weight display's on/off toggle in the in-game FedoHud panel.");

            _toggleLabelArmorOverlay = Config.Bind(
                "SettingsPanel",
                "ToggleLabelArmorOverlay",
                "Armor value",
                "Label of the armor value display's on/off toggle in the in-game FedoHud panel.");

            _showPlayerDamageTextBoost = Config.Bind(
                "PlayerDamage",
                "ShowPlayerDamageTextBoost",
                true,
                "Makes the floating damage number bigger (and adds a little pop effect) whenever it's YOU landing the hit -- damage you take, or damage dealt by someone/something else, is left untouched. Purely a local visual addition -- no network call involved.");

            _playerDamageTextSizeMultiplier = Config.Bind(
                "PlayerDamage",
                "PlayerDamageTextSizeMultiplier",
                1.6f,
                "How much bigger your own damage numbers get compared to the game's normal size, e.g. 1.6 = 60% bigger.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        private void Update()
        {
            RefreshClockOverlay();
            RefreshSkillsOverlay();
            RefreshPlayerStats();
            RefreshRecipeTracker();
        }

        // Même throttle qu'RefreshClockOverlay -- voir PlayerStatsOverlay.cs pour
        // l'affichage lui-même (morts/poids/armure, un seul bloc déplaçable).
        private void RefreshPlayerStats()
        {
            _playerStatsRefreshTimer -= Time.deltaTime;
            if (_playerStatsRefreshTimer > 0f)
            {
                return;
            }

            _playerStatsRefreshTimer = 1f;
            PlayerStatsOverlay.Refresh(_showDeathCounter.Value, _showWeightOverlay.Value, _showArmorOverlay.Value);
        }

        // Même throttle qu'RefreshClockOverlay -- voir RecipeTrackerOverlay.cs pour
        // l'affichage lui-même.
        private void RefreshRecipeTracker()
        {
            RecipeTrackerOverlay.SetVisible(_showRecipeTracker.Value);
            if (!_showRecipeTracker.Value)
            {
                return;
            }

            _recipeTrackerRefreshTimer -= Time.deltaTime;
            if (_recipeTrackerRefreshTimer > 0f)
            {
                return;
            }

            _recipeTrackerRefreshTimer = 1f;
            RecipeTrackerOverlay.Refresh();
        }

        // Même throttle qu'RefreshClockOverlay -- voir SkillsOverlay.cs pour l'affichage
        // lui-même.
        private void RefreshSkillsOverlay()
        {
            SkillsOverlay.SetVisible(_showSkillsOverlay.Value);
            if (!_showSkillsOverlay.Value)
            {
                return;
            }

            _skillsRefreshTimer -= Time.deltaTime;
            if (_skillsRefreshTimer > 0f)
            {
                return;
            }

            _skillsRefreshTimer = 1f;
            SkillsOverlay.Refresh();
        }

        // Throttlé à ~1x/seconde -- voir ClockOverlay.cs pour la création/le positionnement
        // de l'élément UI lui-même.
        private void RefreshClockOverlay()
        {
            ClockOverlay.SetVisible(_showClockOverlay.Value);
            if (!_showClockOverlay.Value)
            {
                return;
            }

            _clockRefreshTimer -= Time.deltaTime;
            if (_clockRefreshTimer > 0f)
            {
                return;
            }

            _clockRefreshTimer = 1f;
            ClockOverlay.SetText(GetCurrentGameTime());
        }

        // Lu par ClockOverlay au moment de (re)créer l'élément (voir Hud.Awake) pour le
        // replacer où le joueur l'avait laissé une session précédente.
        public Vector2 SavedClockPosition => new Vector2(_clockPositionX.Value, _clockPositionY.Value);

        // Appelé par ClockOverlay.DragHandler à la fin d'un glissement -- écrit directement
        // dans le .cfg local, pas de round-trip serveur.
        public void SaveClockPosition(Vector2 anchoredPosition)
        {
            _clockPositionX.Value = anchoredPosition.x;
            _clockPositionY.Value = anchoredPosition.y;
        }

        // Horloge en jeu au format HH:MM, dérivée de EnvMan.GetDayFraction() -- déjà la
        // fraction (0..1) du jour en cours utilisée en interne par le jeu pour l'éclairage,
        // donc déjà correctement calée sur le vrai cycle jour/nuit.
        private string GetCurrentGameTime()
        {
            if (EnvMan.instance == null)
            {
                return null;
            }

            try
            {
                float totalHours = EnvMan.instance.GetDayFraction() * 24f;
                totalHours = ((totalHours % 24f) + 24f) % 24f;
                int hour = (int)totalHours;
                int minute = (int)((totalHours - hour) * 60f);
                return $"{hour:D2}:{minute:D2}";
            }
            catch (System.Exception e)
            {
                Log?.LogWarning($"FedoHud: failed to resolve current game time: {e.Message}");
                return null;
            }
        }
    }
}
