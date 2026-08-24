using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FedoServerTools
{
    // Petit texte "Chargement de Fedoheim..." affiché en haut de l'écran pendant que la
    // connexion automatique (voir FejdStartupPatches.cs, section "Connexion automatique"
    // de CLAUDE.md) enchaîne directement sur la connexion sans jamais passer par les
    // panneaux du menu -- ce qui laisse sinon un écran noir sans aucun texte de
    // chargement (le flux vanilla affiche le sien via ces mêmes panneaux, jamais montrés
    // ici), qui peut faire croire à un plantage plutôt qu'à un chargement normal.
    internal static class LoadingOverlay
    {
        private static GameObject _root;

        // Appelé encore depuis la scène du menu (voir FejdStartupPatches.cs) --
        // DontDestroyOnLoad pour survivre au changement de scène vers la partie.
        // `fejd` sert uniquement à emprunter une police TMP déjà chargée (m_versionLabel,
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

                var textGo = new GameObject("Text", typeof(RectTransform));
                textGo.transform.SetParent(_root.transform, worldPositionStays: false);

                var rect = textGo.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -24f);
                rect.sizeDelta = new Vector2(700f, 60f);

                var label = textGo.AddComponent<TextMeshProUGUI>();
                if (fejd?.m_versionLabel != null)
                {
                    label.font = fejd.m_versionLabel.font;
                }
                label.fontSize = 28f;
                label.alignment = TextAlignmentOptions.Top;
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
