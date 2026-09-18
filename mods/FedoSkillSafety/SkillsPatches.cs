using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FedoSkillSafety
{
    // Skills.m_player et Skills.m_skillData sont privés -- vérifiés par reflection dump
    // contre le vrai assembly_valheim.dll (voir mods/CLAUDE.md) avant d'écrire ces
    // patches, même principe que les autres accès à des champs privés de ce repo
    // (AccessTools.Field, jamais deviné).
    internal static class SkillsReflection
    {
        public static readonly FieldInfo PlayerField = AccessTools.Field(typeof(Skills), "m_player");
        public static readonly FieldInfo SkillDataField = AccessTools.Field(typeof(Skills), "m_skillData");

        public static Player GetOwner(Skills skills)
        {
            return PlayerField?.GetValue(skills) as Player;
        }

        public static Dictionary<Skills.SkillType, Skills.Skill> GetSkillData(Skills skills)
        {
            return SkillDataField?.GetValue(skills) as Dictionary<Skills.SkillType, Skills.Skill>;
        }
    }

    // Chaque gain d'XP (pas seulement un passage de niveau) appelle RaiseSkill -- on ne
    // fait ici qu'une comparaison entière bon marché, donc pas besoin de throttle comme
    // le fait FedoHud pour son propre rafraîchissement d'affichage.
    [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
    internal static class RaiseSkillPatch
    {
        private static void Postfix(Skills __instance, Skills.SkillType skillType)
        {
            try
            {
                if (skillType == Skills.SkillType.None)
                {
                    return;
                }

                var player = SkillsReflection.GetOwner(__instance);
                var skillData = SkillsReflection.GetSkillData(__instance);
                if (player == null || skillData == null || !skillData.TryGetValue(skillType, out var skill))
                {
                    return;
                }

                // Skills.GetSkillLevel(type) applique aussi ModifySkillLevel (buffs
                // temporaires, ex. un effet de statut qui augmente une compétence pour
                // sa durée) -- utiliser m_level brut ici est nécessaire pour qu'un
                // palier ne devienne jamais permanent grâce à un bonus temporaire.
                int level = (int)skill.m_level;
                int palierSize = Mathf.Max(1, FedoSkillSafetyPlugin.Instance.PalierSize.Value);
                int? newPalier = PalierTracker.CheckAndRecord(player, skillType, level, palierSize);
                if (newPalier.HasValue)
                {
                    FedoSkillSafetyPlugin.Instance.AnnouncePalierUnlocked(player, skillType, newPalier.Value);
                }
            }
            catch (Exception e)
            {
                FedoSkillSafetyPlugin.Log?.LogError($"FedoSkillSafety: RaiseSkill tracking failed: {e}");
            }
        }
    }

    // S'exécute après que le jeu a déjà réduit chaque compétence (voir
    // Skills.OnDeath -> LowerAllSkills) -- on ne fait ici que remonter ce qui est
    // retombé sous le plancher de chaque compétence, jamais recalculer nous-mêmes la
    // perte : quel que soit le pourcentage utilisé par le monde (réglage "Death
    // penalty"), ce patch reste valable sans le connaître.
    [HarmonyPatch(typeof(Skills), nameof(Skills.LowerAllSkills))]
    internal static class LowerAllSkillsPatch
    {
        private static void Postfix(Skills __instance)
        {
            try
            {
                var player = SkillsReflection.GetOwner(__instance);
                var skillData = SkillsReflection.GetSkillData(__instance);
                if (player == null || skillData == null)
                {
                    return;
                }

                foreach (var skill in skillData.Values)
                {
                    var type = skill.m_info.m_skill;
                    if (type == Skills.SkillType.None)
                    {
                        continue;
                    }

                    int floor = PalierTracker.GetBestPalier(player, type);
                    if (skill.m_level < floor)
                    {
                        skill.m_level = floor;
                    }
                }
            }
            catch (Exception e)
            {
                FedoSkillSafetyPlugin.Log?.LogError($"FedoSkillSafety: death skill floor clamp failed: {e}");
            }
        }
    }
}
