using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace FedoSkillSafety
{
    // Panneau de compétences natif du jeu (Tab). Structure de chaque ligne décompilée
    // depuis SkillsDialog.Setup (ilspycmd contre assembly_valheim.dll, voir
    // mods/CLAUDE.md) : chaque élément de SkillsDialog.m_elements a un enfant nommé
    // "leveltext" (TMP_Text) qui ne contient QUE le niveau brut (ex. "10"), contrairement
    // au bloc de FedoHud qui combine nom+niveau+% dans un seul TMP_Text (voir
    // FedoHudIntegration.cs pour cette autre intégration, sur un mod tiers celle-là).
    // SkillsDialog est du vanilla public, pas une dépendance douce : référencé
    // normalement comme le reste d'assembly_valheim, patché via l'attribut habituel.
    [HarmonyPatch(typeof(SkillsDialog), nameof(SkillsDialog.Setup))]
    internal static class SkillsDialogPatch
    {
        private static readonly FieldInfo ElementsField = AccessTools.Field(typeof(SkillsDialog), "m_elements");

        // Setup(player) reconstruit skillList et parcourt m_elements dans le même
        // ordre (voir le corps décompilé) -- on peut donc rappeler GetSkillList() nous-
        // mêmes après coup et zipper par index avec m_elements plutôt que de dupliquer
        // la moindre logique de layout.
        private static void Postfix(SkillsDialog __instance, Player player)
        {
            try
            {
                if (player == null)
                {
                    return;
                }

                var elements = ElementsField?.GetValue(__instance) as List<GameObject>;
                if (elements == null)
                {
                    return;
                }

                var skillList = player.GetSkills().GetSkillList();
                for (int i = 0; i < skillList.Count && i < elements.Count; i++)
                {
                    var type = skillList[i].m_info.m_skill;
                    int palier = PalierTracker.GetBestPalier(player, type);
                    if (palier <= 0)
                    {
                        continue;
                    }

                    var levelText = Utils.FindChild(elements[i].transform, "leveltext")?.GetComponent<TMP_Text>();
                    if (levelText != null)
                    {
                        levelText.text += " <color=#4aa3ff>(" + palier + ")</color>";
                    }
                }
            }
            catch (Exception e)
            {
                FedoSkillSafetyPlugin.Log?.LogError($"FedoSkillSafety: SkillsDialog tier display failed: {e}");
            }
        }
    }
}
