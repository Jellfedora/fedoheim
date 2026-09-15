using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace FedoSignColor
{
    // Ajoute un color picker à côté de la fenêtre de saisie de texte d'un panneau --
    // cliquer une couleur préfixe le texte actuellement tapé d'une balise
    // <color=#rrggbb>, sans avoir à la taper à la main. Voir SignColorPicker.cs.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FedoSignColorPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "fedo.signcolor";
        public const string PluginName = "FedoSignColor";
        public const string PluginVersion = "1.0.0";

        public static FedoSignColorPlugin Instance { get; private set; }

        // Exposé pour que les classes de patch Harmony (statiques) puissent logguer sans
        // jamais laisser une exception remonter dans le code du jeu qu'elles patchent.
        public static ManualLogSource Log { get; private set; }

        private ConfigEntry<bool> _enableColorPicker;
        private ConfigEntry<string> _palette;

        public bool EnableColorPicker => _enableColorPicker.Value;

        // Parsée à chaque accès (lue seulement à l'ouverture de la fenêtre de saisie,
        // jamais par frame) -- une entrée invalide est simplement ignorée (avec un
        // avertissement en log) plutôt que de faire échouer toute la palette.
        public IReadOnlyList<Color> ParsedPalette
        {
            get
            {
                var result = new List<Color>();
                foreach (var part in (_palette.Value ?? "").Split(','))
                {
                    var trimmed = part.Trim().TrimStart('#');
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    if (ColorUtility.TryParseHtmlString("#" + trimmed, out var color))
                    {
                        result.Add(color);
                    }
                    else
                    {
                        Log?.LogWarning($"FedoSignColor: invalid hex color \"{part}\" in Palette, ignored.");
                    }
                }

                return result;
            }
        }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            _enableColorPicker = Config.Bind(
                "SignColor",
                "EnableColorPicker",
                true,
                "Adds a row of color swatches next to a sign's text input window -- click one to wrap the whole text you've typed in a <color=#rrggbb> tag. Purely a local UI addition -- no network call involved.");

            _palette = Config.Bind(
                "SignColor",
                "Palette",
                "ffffff,000000,ff4040,ff9640,ffe140,52d452,4098ff,b060ff",
                "Comma-separated list of hex colors (without '#') shown as clickable swatches, in order.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
