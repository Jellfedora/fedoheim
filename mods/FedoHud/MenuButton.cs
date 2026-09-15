using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FedoHud
{
    // Ajoute une entrée "FedoHud" dans le menu pause (Échap), à côté de "Paramètres" --
    // ouvre le panneau d'options (voir FedoHudSettingsPanel.cs). Cloné à partir du vrai
    // bouton "Paramètres" du jeu (Menu.m_settingsButton, public) pour garder le même
    // style visuel plutôt qu'un bouton fait main (`Instantiate` + on vide les écouteurs
    // d'origine + on change le texte). `Menu` n'a pas de méthode `Awake`
    // déclarée (vérifié par réflexion contre assembly_valheim.dll) -- `Start()` (privée)
    // est le point d'accroche le plus tôt disponible, même principe que `Hud.Awake`
    // utilisé ailleurs dans ce mod pour l'horloge.
    //
    // Positionnement/apparence exacts (le bouton cloné s'insère-t-il proprement dans la
    // liste existante, `menuEntriesParent` a-t-il un layout automatique...) pas
    // vérifiables sans lancer le jeu -- à ajuster ici si besoin une fois observé en jeu.
    internal static class MenuButton
    {
        [HarmonyPatch(typeof(Menu), "Start")]
        private static class MenuStartPatch
        {
            private static void Postfix(Menu __instance)
            {
                try
                {
                    Create(__instance);
                }
                catch (Exception e)
                {
                    FedoHudPlugin.Log?.LogError($"FedoHud: menu button creation failed: {e}");
                }
            }
        }

        // Ferme aussi notre panneau s'il était resté ouvert quand le menu pause se
        // referme (ex. le joueur clique "Reprendre" ou rappuie sur Échap) -- sinon il
        // resterait affiché par-dessus le jeu une fois le menu fermé.
        [HarmonyPatch(typeof(Menu), nameof(Menu.Hide))]
        private static class MenuHidePatch
        {
            private static void Postfix()
            {
                FedoHudSettingsPanel.Hide();
            }
        }

        private static void Create(Menu menu)
        {
            if (menu.menuEntriesParent == null || menu.m_settingsButton == null)
            {
                return;
            }

            // Un rechargement recrée le Menu (donc rappelle Start) -- si un enfant du
            // même nom existe déjà sous menuEntriesParent, c'est un résidu d'une
            // instance précédente déjà nettoyée par Unity avec elle ; sinon on le
            // recrée simplement (même garde que ClockOverlay.Create).
            if (menu.menuEntriesParent.Find("FedoHud_MenuButton") != null)
            {
                return;
            }

            var buttonGo = UnityEngine.Object.Instantiate(menu.m_settingsButton.gameObject, menu.menuEntriesParent, worldPositionStays: false);
            buttonGo.name = "FedoHud_MenuButton";
            buttonGo.SetActive(true);

            // Juste avant "Quitter" plutôt qu'à la toute fin de la liste -- se glisse à
            // l'index actuel de m_quitButton, ce qui le repousse (lui et tout ce qui le
            // suit) d'un cran.
            if (menu.m_quitButton != null)
            {
                buttonGo.transform.SetSiblingIndex(menu.m_quitButton.transform.GetSiblingIndex());
            }
            else
            {
                buttonGo.transform.SetAsLastSibling();
            }

            var button = buttonGo.GetComponent<Button>();
            // Remplace complètement l'UnityEvent plutôt qu'un simple RemoveAllListeners :
            // celui-ci ne vide que les écouteurs ajoutés à l'exécution, jamais les
            // écouteurs persistants configurés dans l'éditeur (ex. ouvrir le vrai
            // panneau Paramètres) -- le clone les reprendrait sinon tel quel en plus du
            // nôtre.
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => FedoHudSettingsPanel.Toggle(menu));

            var text = buttonGo.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (text != null)
            {
                text.text = "FedoHud";
            }
        }
    }
}
