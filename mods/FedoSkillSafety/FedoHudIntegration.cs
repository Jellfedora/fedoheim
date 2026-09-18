using System;
using System.Collections;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace FedoSkillSafety
{
    // Dépendance douce vers FedoHud, jamais l'inverse -- FedoHud n'a besoin d'aucune
    // connaissance de FedoSkillSafety. Purement par réflexion (aucune référence de projet
    // vers FedoHud.dll) : FedoHud.SkillsOverlay et sa classe imbriquée Row sont
    // `internal`, donc inaccessibles au compilateur depuis cet assembly même avec une
    // référence -- même raisonnement que la garde IsLoaded de Seasons dans
    // FedoServerTools (voir mods/CLAUDE.md) : le CLR ne résout le corps d'une méthode
    // (donc les types qu'il référence) qu'à sa première exécution, jamais à la simple
    // présence de sa signature -- ce fichier reste chargeable même si FedoHud est
    // absent du serveur.
    internal static class FedoHudIntegration
    {
        private const string FedoHudGuid = "fedo.hud";

        private static FieldInfo _rowsField;
        private static FieldInfo _rowTypeField;
        private static FieldInfo _rowLabelField;
        private static bool _reflectionResolved;

        public static void TryPatch(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.ContainsKey(FedoHudGuid))
            {
                return;
            }

            try
            {
                var overlayType = AccessTools.TypeByName("FedoHud.SkillsOverlay");
                var refreshMethod = AccessTools.Method(overlayType, "Refresh");
                if (refreshMethod == null)
                {
                    FedoSkillSafetyPlugin.Log?.LogWarning("FedoSkillSafety: FedoHud detected, but SkillsOverlay.Refresh was not found -- skipping HUD integration.");
                    return;
                }

                harmony.Patch(refreshMethod, postfix: new HarmonyMethod(typeof(FedoHudIntegration), nameof(Postfix)));
                FedoSkillSafetyPlugin.Log?.LogInfo("FedoSkillSafety: FedoHud detected, tier suffix enabled on the pinned skills block.");
            }
            catch (Exception e)
            {
                FedoSkillSafetyPlugin.Log?.LogWarning($"FedoSkillSafety: FedoHud integration failed to hook, skipping: {e.Message}");
            }
        }

        // Appelé juste après que FedoHud a fini d'écrire le texte "Nom niveau (%)" de
        // chaque ligne épinglée -- on ajoute simplement la suite plutôt que de tenter de
        // ré-analyser ce texte (format interne à FedoHud, pas garanti stable).
        private static void Postfix()
        {
            try
            {
                if (Player.m_localPlayer == null)
                {
                    return;
                }

                if (!EnsureReflectionReady())
                {
                    return;
                }

                var rows = _rowsField.GetValue(null) as IEnumerable;
                if (rows == null)
                {
                    return;
                }

                foreach (var row in rows)
                {
                    var type = (Skills.SkillType)_rowTypeField.GetValue(row);
                    var label = _rowLabelField.GetValue(row) as TMP_Text;
                    if (label == null)
                    {
                        continue;
                    }

                    int palier = PalierTracker.GetBestPalier(Player.m_localPlayer, type);
                    if (palier > 0)
                    {
                        // Couleur/format fixes (pas configurables) : sert justement à
                        // distinguer ce plancher du reste de la ligne (niveau/pourcentage).
                        // TMP_Text a le rich text activé par défaut (voir
                        // SkillsOverlay.CreateRow, jamais désactivé), donc la balise est
                        // bien interprétée plutôt qu'affichée telle quelle.
                        label.text += " <color=#4aa3ff>(" + palier + ")</color>";
                    }
                }
            }
            catch (Exception e)
            {
                FedoSkillSafetyPlugin.Log?.LogWarning($"FedoSkillSafety: FedoHud integration postfix failed: {e.Message}");
            }
        }

        private static bool EnsureReflectionReady()
        {
            if (_reflectionResolved)
            {
                return _rowsField != null;
            }

            _reflectionResolved = true;

            var overlayType = AccessTools.TypeByName("FedoHud.SkillsOverlay");
            var rowType = AccessTools.Inner(overlayType, "Row");
            if (overlayType == null || rowType == null)
            {
                return false;
            }

            _rowsField = AccessTools.Field(overlayType, "Rows");
            _rowTypeField = AccessTools.Field(rowType, "Type");
            _rowLabelField = AccessTools.Field(rowType, "Label");

            return _rowsField != null && _rowTypeField != null && _rowLabelField != null;
        }
    }
}
