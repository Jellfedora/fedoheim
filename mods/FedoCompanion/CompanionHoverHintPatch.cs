using System;
using HarmonyLib;

namespace FedoCompanion
{
    // Character.GetHoverName()/GetHoverText() ne sont pas overridables ici (le compagnon garde
    // le Character d'origine du Greyling cloné, pas une sous-classe -- voir CompanionPrefabPatch).
    // Un simple Postfix qui ajoutait juste l'indice de renommage à GetHoverText s'est révélé
    // insuffisant : le nom affiché au-dessus du compagnon (piloté par GetHoverName, dont
    // GetHoverText dépend probablement en interne) ne se mettait pas à jour après un renommage
    // (Maj+E, voir CompanionInteract) -- signe que l'implémentation vanilla met en cache/dérive
    // le nom autrement qu'en relisant m_character.m_name à chaque appel. On remplace donc les
    // deux méthodes en Prefix (return false = la vraie méthode ne s'exécute jamais) pour forcer
    // le nom réellement à jour, sans dépendre de ce que fait l'implémentation d'origine.
    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
    internal static class CompanionHoverNamePatch
    {
        // Patch sur une méthode vanilla appelée à chaque survol de n'importe quel personnage du
        // jeu : une exception non rattrapée ici casserait le survol pour tout le monde, pas
        // seulement le compagnon. On protège large par précaution.
        private static bool Prefix(Character __instance, ref string __result)
        {
            try
            {
                if (__instance == null || __instance.GetComponent<CompanionAI>() == null)
                {
                    return true;
                }

                __result = __instance.m_name;
                return false;
            }
            catch (Exception e)
            {
                FedoCompanionPlugin.Log?.LogError($"FedoCompanion: CompanionHoverNamePatch a levé une exception : {e}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
    internal static class CompanionHoverHintPatch
    {
        private static bool Prefix(Character __instance, ref string __result)
        {
            try
            {
                if (__instance == null || __instance.GetComponent<CompanionAI>() == null)
                {
                    return true;
                }

                __result = __instance.m_name + "\n" + FedoCompanionPlugin.Instance.RenameHintText.Value;
                return false;
            }
            catch (Exception e)
            {
                FedoCompanionPlugin.Log?.LogError($"FedoCompanion: CompanionHoverHintPatch a levé une exception : {e}");
                return true;
            }
        }
    }
}
