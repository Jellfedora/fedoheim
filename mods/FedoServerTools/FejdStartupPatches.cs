using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace FedoServerTools
{
    // Client uniquement (contrairement au reste de ce mod) : saute directement en
    // création de perso (compte pas encore lié, voir CLAUDE.md) ou en connexion (perso
    // déjà lié) dès qu'un profil de modpack a une cible d'auto-connexion configurée --
    // voir AutoConnect.cs/SessionFile.cs. Sans cible configurée (profil non préparé, ex.
    // Production tant que non validé), ces patchs ne font rien : comportement 100%
    // vanilla, kill-switch naturel.
    //
    // Points d'accroche vérifiés par reflection dump contre le vrai assembly_valheim.dll
    // (voir CLAUDE.md, "Notes techniques de modding"). Testé en jeu une première fois :
    // le menu principal (`m_mainMenu`) restait affiché en dessous du panneau de création
    // de perso -- `ShowCharacterSelection()` seule ne le masque jamais, contrairement à
    // `OnStartGame()` (le vrai handler du bouton "Lancer une partie", public, confirmé par
    // désassemblage IL) qui fait `m_mainMenu.SetActive(false)` avant d'appeler
    // `ShowCharacterSelection()`. On appelle donc `OnStartGame()` telle quelle plutôt que
    // de réinvoquer `ShowCharacterSelection()` en reflection, pour rester au plus près du
    // vrai chemin vanilla. Reste à valider en conditions réelles : le timing exact
    // (`m_profiles` est-il déjà peuplé quand `Start()` se termine ?) et l'enchaînement
    // `OnCharacterStart()`/`AutoConnect.Connect` sans état résiduel.
    [HarmonyPatch(typeof(FejdStartup), "Start")]
    internal static class FejdStartupAutoNavigatePatch
    {
        // m_profiles/SetSelectedProfile sont privés sur FejdStartup -- même style de
        // reflection que ZNetJoinLeaveAnnouncePatches.cs.
        private static readonly FieldInfo ProfilesField = AccessTools.Field(typeof(FejdStartup), "m_profiles");
        private static readonly MethodInfo SetSelectedProfileMethod = AccessTools.Method(typeof(FejdStartup), "SetSelectedProfile");
        // m_csNewCharacterName est un GUIFramework.GuiInputField (assembly gui_framework,
        // pas référencée par ce mod) -- récupéré en `object` et sa propriété `text`
        // (héritée de TMPro.TMP_InputField, publique) mise à jour en reflection pure,
        // sans avoir besoin de référencer ce type au moment de la compilation.
        private static readonly FieldInfo NewCharacterNameField = AccessTools.Field(typeof(FejdStartup), "m_csNewCharacterName");
        // Même raisonnement pour m_csNewCharacterCancel (UnityEngine.UI.Button) : masqué
        // en reflection pure plutôt que de référencer UnityEngine.UI dans ce mod.
        private static readonly FieldInfo NewCharacterCancelField = AccessTools.Field(typeof(FejdStartup), "m_csNewCharacterCancel");

        private static void Postfix(FejdStartup __instance)
        {
            try
            {
                var session = SessionFile.LoadNearPlugin();
                // Kill-switch : profil sans cible configurée (ex. Production tant que non
                // validé) -- comportement 100% vanilla, voir CLAUDE.md.
                if (session?.AutoConnect == null)
                {
                    return;
                }

                // Même effet que le bouton vanilla "Lancer une partie" (masque le menu
                // principal, prépare l'écran de sélection de perso) -- voir le
                // commentaire de tête pour pourquoi pas juste ShowCharacterSelection().
                __instance.OnStartGame();

                if (!string.IsNullOrEmpty(session.CharacterName) && PlayerProfile.HaveProfile(session.CharacterName)
                    && SelectExistingProfile(__instance, session.CharacterName))
                {
                    FedoServerToolsPlugin.Log?.LogInfo($"FedoServerTools: found local character '{session.CharacterName}', skipping menus.");
                    // Aucun panneau de menu ne sera plus affiché à partir d'ici jusqu'à
                    // l'entrée en jeu -- sans ça, écran noir sans texte pendant tout le
                    // chargement (voir LoadingOverlay.cs).
                    LoadingOverlay.Show(__instance, "Chargement de Fedoheim");
                    __instance.OnCharacterStart();
                    AutoConnect.Connect(__instance, session.AutoConnect);
                }
                else
                {
                    FedoServerToolsPlugin.Log?.LogInfo("FedoServerTools: no linked/local character yet, jumping to character creation.");
                    __instance.OnCharacterNew();
                    PrefillCharacterName(__instance, ResolvePrefillName(session.DiscordUsername));
                    // "Annuler" ne ramène qu'à l'écran de sélection de perso (jamais au
                    // menu principal, resté masqué), mais laisserait le joueur reprendre
                    // la main sur un flux censé être automatique -- masqué tant que
                    // l'auto-connexion est active.
                    HideCancelButton(__instance);
                }
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: auto-navigate failed: {e}");
            }
        }

        private static bool SelectExistingProfile(FejdStartup instance, string characterName)
        {
            if (ProfilesField == null || SetSelectedProfileMethod == null)
            {
                return false;
            }

            if (!(ProfilesField.GetValue(instance) is List<PlayerProfile> profiles))
            {
                return false;
            }

            var profile = profiles.FirstOrDefault(p => p.GetName() == characterName);
            if (profile == null)
            {
                return false;
            }

            SetSelectedProfileMethod.Invoke(instance, new object[] { profile.GetFilename() });
            return true;
        }

        // "premier arrivé, premier servi" côté local aussi -- si le pseudo Discord
        // correspond déjà à un perso existant sur cette machine (le sien ou celui d'un
        // autre joueur y ayant joué avant), on suffixe par un nombre croissant jusqu'à
        // trouver un nom libre, plutôt que de laisser `PlayerProfile.HaveProfile` bloquer
        // silencieusement la création (voir OnNewCharacterDone, qui refuse déjà les
        // doublons avec `m_newCharacterError`).
        private static string ResolvePrefillName(string discordUsername)
        {
            if (string.IsNullOrWhiteSpace(discordUsername))
            {
                return null;
            }

            if (!PlayerProfile.HaveProfile(discordUsername))
            {
                return discordUsername;
            }

            for (int suffix = 2; suffix < 1000; suffix++)
            {
                string candidate = discordUsername + suffix;
                if (!PlayerProfile.HaveProfile(candidate))
                {
                    return candidate;
                }
            }

            return discordUsername;
        }

        // Verrouillé une fois pré-rempli (`readOnly`, propriété publique héritée de
        // TMPro.TMP_InputField) -- le nom vient du pseudo Discord du compte, pas question
        // de le laisser retaper à la main.
        private static void PrefillCharacterName(FejdStartup instance, string name)
        {
            if (string.IsNullOrEmpty(name) || NewCharacterNameField == null)
            {
                return;
            }

            object inputField = NewCharacterNameField.GetValue(instance);
            if (inputField == null)
            {
                return;
            }

            Type fieldType = inputField.GetType();
            fieldType.GetProperty("text")?.SetValue(inputField, name);
            fieldType.GetProperty("readOnly")?.SetValue(inputField, true);
        }

        private static void HideCancelButton(FejdStartup instance)
        {
            object button = NewCharacterCancelField?.GetValue(instance);
            object gameObject = button?.GetType().GetProperty("gameObject")?.GetValue(button);
            gameObject?.GetType().GetMethod("SetActive")?.Invoke(gameObject, new object[] { false });
        }
    }

    // Une fois le nouveau perso créé (bouton "Terminé" vanilla, jamais patché lui-même),
    // on enchaîne directement sur la connexion configurée au lieu de laisser le joueur
    // sur l'écran de sélection de perso / démarrage.
    [HarmonyPatch(typeof(FejdStartup), "OnNewCharacterDone")]
    internal static class FejdStartupNewCharacterDonePatch
    {
        private static void Postfix(FejdStartup __instance)
        {
            try
            {
                var session = SessionFile.LoadNearPlugin();
                if (session?.AutoConnect == null)
                {
                    return;
                }

                // Testé en jeu : sans cet appel, `m_worlds` n'est jamais peuplé (il ne
                // l'est que par `ShowStartGame()`, appelée depuis `OnCharacterStart()`)
                // et la connexion à un monde local échoue silencieusement ("could not
                // read the local world list"), laissant le joueur sur l'écran de
                // sélection de perso. `OnCharacterStart()` appelle aussi
                // `Game.SetProfile(...)`, nécessaire pour que le perso tout juste créé
                // soit bien celui chargé une fois connecté -- `OnNewCharacterDone` a déjà
                // repeuplé `m_profiles`/`m_profileIndex` via `SetSelectedProfile` juste
                // avant, donc l'appel est sûr ici (mêmes préconditions que dans
                // FejdStartupAutoNavigatePatch, qui l'appelle déjà pour le cas "perso
                // existant lié").
                // Le panneau de création vient de disparaître -- écran noir sans texte
                // jusqu'à l'entrée en jeu à partir d'ici (voir LoadingOverlay.cs).
                LoadingOverlay.Show(__instance, "Chargement de Fedoheim");
                __instance.OnCharacterStart();
                AutoConnect.Connect(__instance, session.AutoConnect);
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: post-creation auto-connect failed: {e}");
            }
        }
    }
}
