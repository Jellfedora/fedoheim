using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FedoAchievements
{
    // `Achievement.m_isSecret` n'est lu que par l'UI (vérifié par décompilation de tout
    // assembly_valheim : InventoryGui.UpdateAchievementsList pour la tuile de la grille,
    // AchievementsGui.OnOpenAchievementDetails pour le panneau de détail -- jamais par
    // la logique de déblocage ni la synchro plateforme). On le force donc à false le
    // temps de ces deux méthodes seulement, puis on restaure la valeur d'origine : les
    // assets Achievement (ScriptableObject partagés) ne restent jamais modifiés en dehors
    // de ces deux appels, et désactiver l'option en cours de partie reprend effet à la
    // prochaine ouverture du panneau.
    internal static class SecretOverride
    {
        public static List<Achievement> Reveal(IEnumerable<Achievement> achievements)
        {
            var revealed = new List<Achievement>();
            if (!FedoAchievementsPlugin.RevealSecretAchievements)
            {
                return revealed;
            }

            foreach (var achievement in achievements)
            {
                if (achievement != null && achievement.m_isSecret)
                {
                    achievement.m_isSecret = false;
                    revealed.Add(achievement);
                }
            }

            return revealed;
        }

        public static void Restore(List<Achievement> revealed)
        {
            if (revealed == null)
            {
                return;
            }

            foreach (var achievement in revealed)
            {
                if (achievement != null)
                {
                    achievement.m_isSecret = true;
                }
            }
        }

        public static IEnumerable<Achievement> AllAchievements()
        {
            var lists = Achievements.m_instance?.m_achievementLists;
            if (lists == null)
            {
                yield break;
            }

            foreach (var list in lists)
            {
                if (list?.m_achievements == null)
                {
                    continue;
                }

                foreach (var achievement in list.m_achievements)
                {
                    yield return achievement;
                }
            }
        }
    }

    // Grille du panneau Succès : nom/description réels au lieu de "Succès secret", et
    // tuile cliquable (le jeu y détruit le Button d'un secret non débloqué).
    [HarmonyPatch(typeof(InventoryGui), "UpdateAchievementsList")]
    internal static class UpdateAchievementsListPatch
    {
        private static void Prefix(out List<Achievement> __state)
        {
            __state = null;
            try
            {
                __state = SecretOverride.Reveal(SecretOverride.AllAchievements());
            }
            catch (Exception e)
            {
                FedoAchievementsPlugin.Log?.LogError($"FedoAchievements: failed to reveal secret achievements: {e}");
            }
        }

        // Finalizer plutôt que Postfix : restaure même si la méthode d'origine lève.
        private static void Finalizer(List<Achievement> __state)
        {
            SecretOverride.Restore(__state);
        }
    }

    // Panneau de détail : sans ça, un secret non débloqué n'affiche qu'une ligne
    // "??? / ???" au lieu de ses vraies conditions. Le test m_isSecret est fait de façon
    // synchrone en tête de méthode (les coroutines lancées ensuite ne le relisent pas),
    // donc restaurer dès la fin de l'appel suffit.
    [HarmonyPatch(typeof(AchievementsGui), nameof(AchievementsGui.OnOpenAchievementDetails))]
    internal static class OnOpenAchievementDetailsPatch
    {
        private static void Prefix(Achievement achievement, out List<Achievement> __state)
        {
            __state = null;
            try
            {
                __state = SecretOverride.Reveal(new[] { achievement });
            }
            catch (Exception e)
            {
                FedoAchievementsPlugin.Log?.LogError($"FedoAchievements: failed to reveal achievement details: {e}");
            }
        }

        private static void Finalizer(List<Achievement> __state)
        {
            SecretOverride.Restore(__state);
        }
    }

    // CreateStatRow (privée) remplace le nom ET la progression de toute condition pas
    // encore remplie par "???" / "??? / ???". Elle instancie la ligne comme dernier enfant
    // de m_achievementDetailsListRoot puis la remplit, de façon synchrone -- on réécrit
    // donc ce dernier enfant en Postfix avec le même formatage que le cas "rempli" du
    // jeu, en gardant la couleur grise d'origine pour distinguer les deux.
    [HarmonyPatch(typeof(AchievementsGui), "CreateStatRow")]
    internal static class CreateStatRowPatch
    {
        private static readonly AccessTools.FieldRef<AchievementsGui, RectTransform> ListRoot =
            AccessTools.FieldRefAccess<AchievementsGui, RectTransform>("m_achievementDetailsListRoot");

        private static void Postfix(AchievementsGui __instance, string statKey, float currentAmount, float totalAmount)
        {
            if (!FedoAchievementsPlugin.ShowProgress || currentAmount >= totalAmount)
            {
                return;
            }

            try
            {
                var root = ListRoot(__instance);
                if (root == null || root.childCount == 0)
                {
                    return;
                }

                var row = root.GetChild(root.childCount - 1).GetComponent<AchievementDetailUnlockCondition>();
                if (row == null)
                {
                    return;
                }

                // Même transformation que le jeu : les stats PlayerStatType ont leur clé de
                // traduction sous "$stat_<nom>", les autres (ennemis, objets...) sont déjà
                // des clés "$..." directement localisables.
                var key = statKey;
                if (Enum.TryParse<PlayerStatType>(key, out _))
                {
                    key = "$stat_" + key;
                }

                var name = Localization.instance.Localize(key);
                row.StatName.text = string.IsNullOrEmpty(name) ? key : name;
                row.Progress.text = $"{currentAmount} / {totalAmount}";
            }
            catch (Exception e)
            {
                FedoAchievementsPlugin.Log?.LogError($"FedoAchievements: failed to show condition progress: {e}");
            }
        }
    }
}
