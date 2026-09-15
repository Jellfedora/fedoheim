using System;
using TMPro;
using UnityEngine;

namespace FedoHud
{
    // Composant générique : un "!" flottant au-dessus d'un objet, visible tant que
    // `IsReady()` renvoie vrai et `IsEnabledInConfig()` (si fourni) aussi -- réutilisé
    // par BeehiveFullIndicator.cs (ruche pleine) et PlantReadyIndicator.cs (culture prête
    // à récolter), une seule implémentation du texte/billboard/throttle pour ne pas
    // diverger entre les deux. Les deux delegates sont assignés juste après
    // `AddComponent<FloatingExclamationIcon>()` par l'appelant.
    internal class FloatingExclamationIcon : MonoBehaviour
    {
        // Revérifier l'état toutes les quelques secondes plutôt qu'à chaque frame : ni la
        // pousse d'une plante ni la production de miel ne changent plus vite que ça.
        private const float CheckIntervalSeconds = 3f;

        public Func<bool> IsReady;
        public Func<bool> IsEnabledInConfig;

        private TMP_Text _text;
        private Camera _mainCamera;
        private float _timer;

        private void Awake()
        {
            var go = new GameObject("FedoHud_ExclamationIcon");
            go.transform.SetParent(transform, worldPositionStays: false);
            // Valeurs approximatives, pas encore ajustées visuellement en jeu (offset au-
            // dessus de l'objet, échelle du texte world-space) -- à retoucher ici si le
            // rendu en jeu ne convient pas.
            go.transform.localPosition = new Vector3(0f, 1.8f, 0f);
            go.transform.localScale = Vector3.one * 0.3f;

            _text = go.AddComponent<TextMeshPro>();
            _text.text = "!";
            _text.fontSize = 10f;
            _text.alignment = TextAlignmentOptions.Center;
            _text.color = Color.yellow;
            var font = HudFont.Resolve();
            if (font != null)
            {
                _text.font = font;
            }
            else
            {
                // Diagnostic temporaire (voir CHANGELOG) : si aucune police TMP n'est
                // trouvée à ce moment, le "!" risque de ne rendre aucun glyphe visible.
                FedoHudPlugin.Log?.LogWarning("FedoHud: no TMP font resolved for exclamation icon -- it may render invisible.");
            }

            _text.gameObject.SetActive(false);
        }

        private void Update()
        {
            bool configEnabled = IsEnabledInConfig == null || IsEnabledInConfig();
            if (!configEnabled)
            {
                if (_text.gameObject.activeSelf)
                {
                    _text.gameObject.SetActive(false);
                }

                return;
            }

            _timer -= Time.deltaTime;
            if (_timer > 0f)
            {
                return;
            }

            _timer = CheckIntervalSeconds;
            bool ready = SafeIsReady();
            if (_text.gameObject.activeSelf != ready)
            {
                _text.gameObject.SetActive(ready);
            }
        }

        private bool SafeIsReady()
        {
            try
            {
                return IsReady != null && IsReady();
            }
            catch (Exception e)
            {
                FedoHudPlugin.Log?.LogWarning($"FedoHud: exclamation icon check failed: {e.Message}");
                return false;
            }
        }

        // Orientation face caméra, seulement pendant que l'icône est visible -- pas la
        // peine sinon.
        private void LateUpdate()
        {
            if (!_text.gameObject.activeSelf)
            {
                return;
            }

            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (_mainCamera != null)
            {
                _text.transform.rotation = _mainCamera.transform.rotation;
            }
        }
    }
}
