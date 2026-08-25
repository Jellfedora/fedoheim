using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FedoServerTools
{
    // Écran affiché en plein milieu de l'écran pendant que la connexion automatique (voir
    // FejdStartupPatches.cs, section "Connexion automatique" de CLAUDE.md) enchaîne
    // directement sur la connexion sans jamais passer par les panneaux du menu -- ce qui
    // laisse sinon un écran noir sans aucun texte de chargement (le flux vanilla affiche
    // le sien via ces mêmes panneaux, jamais montrés ici), qui peut faire croire à un
    // plantage plutôt qu'à un chargement normal. Logo Fedoheim + texte de chargement,
    // centrés au milieu de l'écran, dans la police déjà empruntée au jeu (voir LoadingLogo.cs
    // pour pourquoi une police custom a été essayée puis abandonnée).
    internal static class LoadingOverlay
    {
        private static GameObject _root;

        // Appelé encore depuis la scène du menu (voir FejdStartupPatches.cs) --
        // DontDestroyOnLoad pour survivre au changement de scène vers la partie.
        // `fejd` sert uniquement à emprunter une police TMP déjà chargée (m_csName,
        // toujours présent à ce stade) -- même technique que ClockOverlay avec le Hud,
        // pour ne pas avoir à référencer un asset de police séparé.
        public static void Show(FejdStartup fejd, string text)
        {
            if (_root != null)
            {
                return;
            }

            try
            {
                _root = new GameObject("FedoheimLoadingOverlay", typeof(RectTransform));
                UnityEngine.Object.DontDestroyOnLoad(_root);

                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                // Au-dessus de tout, y compris un éventuel écran de secours vanilla
                // partiellement affiché si la connexion échoue.
                canvas.sortingOrder = short.MaxValue;
                _root.AddComponent<CanvasScaler>();

                // Conteneur centré au milieu de l'écran -- logo/texte sont positionnés à
                // l'intérieur en partant de son bord haut, ce qui centre le groupe entier
                // autour du centre de l'écran plutôt que d'ancrer chaque élément séparément.
                var content = new GameObject("Content", typeof(RectTransform));
                content.transform.SetParent(_root.transform, worldPositionStays: false);
                var contentRect = content.GetComponent<RectTransform>();
                contentRect.anchorMin = new Vector2(0.5f, 0.5f);
                contentRect.anchorMax = new Vector2(0.5f, 0.5f);
                contentRect.pivot = new Vector2(0.5f, 0.5f);
                contentRect.anchoredPosition = Vector2.zero;
                contentRect.sizeDelta = new Vector2(900f, 260f);

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
                    logoRect.sizeDelta = new Vector2(160f, 160f);

                    var image = logoGo.AddComponent<RawImage>();
                    image.texture = logoTexture;
                    image.raycastTarget = false;
                }

                var textGo = new GameObject("Text", typeof(RectTransform));
                textGo.transform.SetParent(content.transform, worldPositionStays: false);

                var rect = textGo.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -180f);
                // Assez large pour ne jamais forcer de retour à la ligne, quel que soit
                // le texte passé (voir enableWordWrapping ci-dessous, filet de sécurité
                // supplémentaire si jamais un texte plus long était passé un jour).
                rect.sizeDelta = new Vector2(900f, 70f);

                var label = textGo.AddComponent<TextMeshProUGUI>();
                // m_csName (le nom du perso sur l'écran de sélection) utilise la police
                // "normale" du jeu -- plus adaptée ici que m_versionLabel (pixel/rétro
                // marqué, utilisé avant). Repli sur m_versionLabel si m_csName n'est pas
                // encore initialisé pour une raison quelconque.
                TMP_FontAsset font = fejd?.m_csName != null ? fejd.m_csName.font : fejd?.m_versionLabel?.font;
                if (font != null)
                {
                    label.font = font;
                }
                label.fontSize = 40f;
                label.alignment = TextAlignmentOptions.Top;
                label.enableWordWrapping = false;
                label.color = Color.white;
                // Ne doit jamais intercepter un clic, y compris sur l'écran de secours
                // vanilla si la connexion échoue et qu'un panneau redevient interactif.
                label.raycastTarget = false;
                label.text = text;

                // Filet de sécurité : si Hud.Awake ne se déclenche jamais (échec de
                // connexion, ex. cible mal configurée qui ramène sur un écran de menu),
                // l'overlay ne doit jamais rester affiché indéfiniment.
                var killer = _root.AddComponent<SelfDestructAfter>();
                killer.Seconds = 30f;
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: loading overlay creation failed: {e}");
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

        // Le Hud n'apparaît qu'une fois la partie réellement chargée -- signal fiable
        // que l'écran noir est terminé, peu importe le chemin emprunté pour y arriver
        // (host local ou connexion à un serveur dédié).
        [HarmonyPatch(typeof(Hud), "Awake")]
        private static class HudAwakePatch
        {
            private static void Postfix()
            {
                try
                {
                    Hide();
                }
                catch (Exception e)
                {
                    FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: loading overlay cleanup failed: {e}");
                }
            }
        }

        private class SelfDestructAfter : MonoBehaviour
        {
            public float Seconds;

            private void Start()
            {
                Invoke(nameof(Kill), Seconds);
            }

            private void Kill()
            {
                Hide();
            }
        }
    }
}
