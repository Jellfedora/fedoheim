using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FedoServerTools
{
    // Affiché à la place de la reconnexion automatique quand on revient sur ce menu avec
    // un statut de connexion natif qui n'est pas `None` (voir
    // FejdStartupAutoNavigatePatch.Postfix, ZNet.GetConnectionStatus()) -- typiquement une
    // déconnexion (perte de connexion, kick, arrêt du serveur, serveur injoignable...).
    // Sans cet écran, l'auto-connexion rejouerait la cible configurée en boucle à chaque
    // déconnexion, sans jamais laisser l'occasion de simplement fermer le jeu. Même
    // habillage que LoadingOverlay (logo + texte au centre de l'écran), avec deux boutons
    // -- clones du vrai bouton "Commencer" du jeu (m_csStartButton, voir CreateButton) --
    // à la place du texte de progression.
    internal static class DisconnectChoiceOverlay
    {
        private static GameObject _root;

        // Utilisé par SelectCharacterBackPatch ci-dessous pour bloquer le retour au menu
        // principal (bouton "Retour"/touche Échap) tant que cet écran est affiché --
        // masquer m_characterSelectScreen/m_selectCharacterPanel (voir Show) suffit
        // probablement déjà à neutraliser ce chemin (le gestionnaire vit vraisemblablement
        // sur ces mêmes panneaux), mais ce patch le garantit peu importe le mécanisme
        // d'entrée réel (bouton, Échap, manette).
        internal static bool IsActive => _root != null;

        // `onReconnect` rejoue le même chemin que la toute première connexion (voir
        // FejdStartupAutoNavigatePatch.ProceedToGame) -- "Quitter le jeu" n'a pas besoin
        // de callback, c'est toujours la même action. `slug` vient de SessionFile (écrit
        // par le launcher, voir valheim.rs::write_mod_session) -- sert uniquement à
        // interroger GET /modpacks/:slug/online-players (voir ServerStatusLine.cs).
        // `requireOnlineToReconnect` : uniquement pour une cible "server" (voir
        // FejdStartupPatches.cs) -- désactive le bouton "Connexion" tant que le serveur
        // est confirmé hors ligne, pour ne pas tenter une connexion qui resterait bloquée
        // sans retour avant le timeout vanilla. Sans objet pour une cible "world" (héberger
        // un monde local) : c'est justement ce clic qui va faire exister le serveur.
        public static void Show(FejdStartup fejd, string slug, bool requireOnlineToReconnect, Action onReconnect)
        {
            if (_root != null)
            {
                return;
            }

            try
            {
                // Masque le HUD vanilla de sélection de perso (titre "Sélection de
                // personnage", flèches, nom, barre de boutons du bas) qui resterait
                // sinon visible en dessous de notre overlay -- laisse en revanche
                // intacts la scène 3D (personnage/feu de camp, voir
                // SetupCharacterPreview) et la caméra, qui n'en dépendent pas (champs
                // séparés sur FejdStartup). Les deux champs sont publics, pas besoin de
                // reflection.
                if (fejd != null)
                {
                    fejd.m_characterSelectScreen.SetActive(false);
                    fejd.m_selectCharacterPanel.SetActive(false);
                }

                _root = new GameObject("FedoheimDisconnectOverlay", typeof(RectTransform));
                UnityEngine.Object.DontDestroyOnLoad(_root);

                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = short.MaxValue;

                var scaler = _root.AddComponent<CanvasScaler>();
                // Par défaut un CanvasScaler est en ConstantPixelSize -- nos tailles en
                // pixels (logo, boutons) resteraient alors minuscules sur une grande
                // résolution/fenêtre. Une première version recopiait les réglages du
                // Canvas du menu (m_mainMenu) en espérant qu'il soit en ScaleWithScreenSize
                // -- toujours minuscule en plein écran une fois testé, signe que le menu
                // vanilla est probablement en ConstantPhysicalSize (taille physique fixe,
                // ignore délibérément la résolution) plutôt qu'en ScaleWithScreenSize.
                // On configure donc notre propre mise à l'échelle, indépendante de ce que
                // fait le menu vanilla -- 1920x1080 comme résolution de référence (celle à
                // laquelle logo/boutons ont été dimensionnés ci-dessous), pondérée
                // largeur/hauteur pour rester correct quel que soit le ratio d'écran.
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                // Contrairement à LoadingOverlay (purement décoratif) : nécessaire pour
                // que les boutons ci-dessous reçoivent les clics via l'EventSystem déjà
                // présent dans la scène du menu.
                _root.AddComponent<GraphicRaycaster>();

                var content = new GameObject("Content", typeof(RectTransform));
                content.transform.SetParent(_root.transform, worldPositionStays: false);
                var contentRect = content.GetComponent<RectTransform>();
                contentRect.anchorMin = new Vector2(0.5f, 0.5f);
                contentRect.anchorMax = new Vector2(0.5f, 0.5f);
                contentRect.pivot = new Vector2(0.5f, 0.5f);
                contentRect.anchoredPosition = Vector2.zero;
                contentRect.sizeDelta = new Vector2(1000f, 480f);

                TMP_FontAsset font = fejd?.m_csName != null ? fejd.m_csName.font : fejd?.m_versionLabel?.font;

                Texture2D logoTexture = LoadingLogo.Get();
                if (logoTexture != null)
                {
                    var logoGo = new GameObject("Logo", typeof(RectTransform));
                    logoGo.transform.SetParent(content.transform, worldPositionStays: false);
                    var logoRect = logoGo.GetComponent<RectTransform>();
                    logoRect.anchorMin = new Vector2(0.5f, 1f);
                    logoRect.anchorMax = new Vector2(0.5f, 1f);
                    logoRect.pivot = new Vector2(0.5f, 1f);
                    logoRect.anchoredPosition = new Vector2(0f, 0f);
                    logoRect.sizeDelta = new Vector2(240f, 240f);

                    var image = logoGo.AddComponent<RawImage>();
                    image.texture = logoTexture;
                    image.raycastTarget = false;
                }

                Button reconnectButton = CreateButton(
                    fejd,
                    content.transform,
                    "Connexion",
                    new Vector2(-185f, -340f),
                    () =>
                    {
                        Hide();
                        onReconnect?.Invoke();
                    });

                // Statut du serveur (voir ServerStatusLine.cs) -- uniquement pour une
                // cible "server" (rejoindre un serveur dédié) : pour un monde local
                // hébergé, cet endpoint ne reflète jamais que ce lancement lui-même n'a
                // pas encore démarré/reporté "en ligne" -- l'afficher dirait "hors ligne"
                // à tort alors que la connexion instantanée fonctionne normalement.
                if (requireOnlineToReconnect)
                {
                    var textGo = new GameObject("Text", typeof(RectTransform));
                    textGo.transform.SetParent(content.transform, worldPositionStays: false);
                    var textRect = textGo.GetComponent<RectTransform>();
                    textRect.anchorMin = new Vector2(0.5f, 1f);
                    textRect.anchorMax = new Vector2(0.5f, 1f);
                    textRect.pivot = new Vector2(0.5f, 1f);
                    textRect.anchoredPosition = new Vector2(0f, -260f);
                    textRect.sizeDelta = new Vector2(1000f, 60f);

                    var label = textGo.AddComponent<TextMeshProUGUI>();
                    if (font != null)
                    {
                        label.font = font;
                    }
                    label.fontSize = 30f;
                    label.alignment = TextAlignmentOptions.Top;
                    label.enableWordWrapping = false;
                    label.color = Color.white;
                    label.raycastTarget = false;
                    label.text = "Vérification du serveur...";

                    var runner = _root.AddComponent<CoroutineRunner>();
                    ServerStatusLine.Fetch(runner, FedoServerToolsPlugin.Instance?.ApiBaseUrl, slug, label, online =>
                    {
                        if (reconnectButton != null)
                        {
                            reconnectButton.interactable = online;
                        }
                    });
                }

                CreateButton(
                    fejd,
                    content.transform,
                    "Quitter le jeu",
                    new Vector2(185f, -340f),
                    Application.Quit);
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: disconnect choice overlay creation failed: {e}");
            }
        }

        // Composant vide, uniquement pour porter StartCoroutine (voir ServerStatusLine)
        // -- Canvas/CanvasScaler/GraphicRaycaster ne sont pas des MonoBehaviour utilisables
        // pour ça.
        private class CoroutineRunner : MonoBehaviour
        {
        }

        // Bloque le retour au menu principal (bouton "Retour" du panneau masqué
        // ci-dessus, ou touche Échap qui déclenche vraisemblablement ce même handler)
        // tant que cet écran est affiché -- sans ça, quitter cet écran par ce chemin
        // laisserait le joueur sur le menu principal vanilla plutôt que sur le choix
        // reconnexion/quitter voulu ici.
        [HarmonyPatch(typeof(FejdStartup), "OnSelelectCharacterBack")]
        private static class SelectCharacterBackPatch
        {
            private static bool Prefix()
            {
                return !IsActive;
            }
        }

        public static void Hide()
        {
            if (_root == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(_root);
            _root = null;
        }

        // Clone du vrai bouton "Commencer" du jeu (FejdStartup.m_csStartButton, public,
        // pas de reflection nécessaire) plutôt qu'un bouton fait main -- même habillage
        // (bordure, texture bois) que le reste du menu Valheim, demandé explicitement
        // plutôt qu'un style maison. `Instantiate(source, parent, false)` recopie tout
        // (sprites, Button, texte enfant) ; il suffit ensuite de vider les écouteurs
        // d'origine et de changer le texte affiché.
        private static Button CreateButton(FejdStartup fejd, Transform parent, string label, Vector2 anchoredPosition, Action onClick)
        {
            Button source = fejd?.m_csStartButton;
            if (source == null)
            {
                FedoServerToolsPlugin.Log?.LogWarning("FedoServerTools: FejdStartup.m_csStartButton not found, disconnect overlay button skipped.");
                return null;
            }

            var buttonGo = UnityEngine.Object.Instantiate(source.gameObject, parent, worldPositionStays: false);
            buttonGo.name = "Button";
            // Le bouton d'origine peut être désactivé selon l'état courant de l'écran de
            // sélection de perso (ex. aucun profil sélectionné) -- toujours actif ici,
            // ce clone n'a aucun rapport avec cet état.
            buttonGo.SetActive(true);

            var rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            // Le bouton d'origine ("Commencer", dans la barre du bas) est prévu pour un
            // menu bien plus dense -- explicitement agrandi ici, sinon minuscule au milieu
            // d'un écran par ailleurs vide.
            rect.sizeDelta = new Vector2(340f, 80f);

            var button = buttonGo.GetComponent<Button>();
            button.interactable = true;
            // Remplace complètement l'UnityEvent plutôt qu'un simple RemoveAllListeners :
            // celui-ci ne vide que les écouteurs ajoutés à l'exécution, jamais les
            // écouteurs persistants configurés dans l'éditeur (ex. démarrer la partie) --
            // le clone les reprendrait sinon tel quel en plus du nôtre.
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => onClick?.Invoke());

            var text = buttonGo.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (text != null)
            {
                // N'a d'effet que si l'auto-size TMP est désactivé sur ce texte -- sinon
                // il grossit déjà tout seul avec le bouton agrandi ci-dessus.
                text.fontSize = 30f;
                text.text = label;
            }

            return button;
        }
    }
}
