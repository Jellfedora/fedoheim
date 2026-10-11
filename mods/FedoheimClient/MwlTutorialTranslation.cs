using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;

namespace FedoheimClient
{
    // Traduit les pages d'aide de l'interface des ports du mod tiers warpalicious
    // More_World_Locations_AIO. Ces pages ne passent PAS par son fichier de localisation
    // YAML : ce sont des .md embarqués dans son DLL (`PortTutorial`, champs `label`/`text`,
    // liste statique `PortTutorial.tutorials`, vérifié par décompilation de la 5.2.0),
    // donc impossibles à traduire par config.
    //
    // Fichiers lus : BepInEx/config/FedoheimClient/mwl_tutorials/<label anglais en
    // minuscules>.md (introduction, port, manifest, shipment, delivery, teleport) --
    // première ligne = nouveau titre de l'onglet, le reste = texte de la page. Page sans
    // fichier : laissée telle quelle.
    //
    // Dépendance douce : tout passe par réflexion/AccessTools.TypeByName, rien n'est fait
    // si le mod n'est pas installé. Patché depuis FejdStartup.Awake (tous les plugins sont
    // chargés à ce moment-là, pas forcément au moment de notre propre Awake) et appliqué
    // juste avant que l'interface n'affiche la liste (`PortUI.LoadTutorials`).
    internal static class MwlTutorialTranslation
    {
        private const string TutorialTypeName = "More_World_Locations_AIO.tutorials.PortTutorial";
        private const string PortUiTypeName = "More_World_Locations_AIO.PortUI";

        private static bool _patched;
        private static bool _applied;

        private static string Folder => Path.Combine(Paths.ConfigPath, "FedoheimClient", "mwl_tutorials");

        [HarmonyPatch(typeof(FejdStartup), "Awake")]
        private static class PatchWhenModsLoaded
        {
            private static void Postfix()
            {
                if (_patched)
                {
                    return;
                }

                _patched = true;
                try
                {
                    var portUi = AccessTools.TypeByName(PortUiTypeName);
                    var loadTutorials = portUi == null ? null : AccessTools.Method(portUi, "LoadTutorials");
                    if (loadTutorials == null)
                    {
                        return;
                    }

                    var harmony = new Harmony(FedoheimClientPlugin.PluginGuid + ".mwltutorials");
                    harmony.Patch(loadTutorials, prefix: new HarmonyMethod(typeof(MwlTutorialTranslation), nameof(BeforeLoadTutorials)));
                }
                catch (Exception e)
                {
                    FedoheimClientPlugin.Log?.LogWarning($"FedoheimClient: could not hook More World Locations tutorials: {e.Message}");
                }
            }
        }

        private static void BeforeLoadTutorials()
        {
            if (_applied)
            {
                return;
            }

            _applied = true;
            try
            {
                Apply();
            }
            catch (Exception e)
            {
                FedoheimClientPlugin.Log?.LogWarning($"FedoheimClient: failed to translate More World Locations tutorials: {e.Message}");
            }
        }

        private static void Apply()
        {
            var type = AccessTools.TypeByName(TutorialTypeName);
            if (type == null || !Directory.Exists(Folder))
            {
                return;
            }

            var listField = AccessTools.Field(type, "tutorials");
            var labelField = AccessTools.Field(type, "label");
            var textField = AccessTools.Field(type, "text");
            if (listField == null || labelField == null || textField == null || !(listField.GetValue(null) is IEnumerable list))
            {
                FedoheimClientPlugin.Log?.LogWarning("FedoheimClient: More World Locations tutorial structure changed, translation skipped.");
                return;
            }

            int count = 0;
            foreach (var tutorial in list)
            {
                string label = labelField.GetValue(tutorial) as string;
                if (string.IsNullOrEmpty(label))
                {
                    continue;
                }

                string path = Path.Combine(Folder, label.ToLowerInvariant() + ".md");
                if (!File.Exists(path))
                {
                    continue;
                }

                string content = File.ReadAllText(path).Replace("\r\n", "\n").TrimStart('﻿');
                int newline = content.IndexOf('\n');
                string newLabel = (newline < 0 ? content : content.Substring(0, newline)).Trim();
                string newText = newline < 0 ? "" : content.Substring(newline + 1);

                // `label`/`text` sont readonly côté mod tiers : FieldInfo.SetValue les
                // écrit quand même sur un champ d'instance initonly.
                if (newLabel.Length > 0)
                {
                    labelField.SetValue(tutorial, newLabel);
                }

                textField.SetValue(tutorial, newText);
                count++;
            }

            FedoheimClientPlugin.Log?.LogInfo($"FedoheimClient: translated {count} More World Locations tutorial page(s).");
        }
    }
}
