using TMPro;
using UnityEngine;

namespace FedoKnorri
{
    // Bulle de dialogue maison, indépendante de Talker/RPC_Say -- Talker.Say() plante en
    // profondeur dans du code vanilla (Character.GetHeadPoint(), NullReferenceException sur
    // m_head, un champ privé jamais configuré pour un Greyling, une créature qui ne parle pas
    // nativement) au moment où le message revient par RPC (observé en jeu : le message
    // n'apparaissait jamais, l'exception étant simplement rattrapée par CompanionAI.UpdateAI et
    // le tick abandonné). Cette bulle reste entièrement locale (TextMeshPro 3D world-space, pas
    // de RPC/réseau) : suffisant pour de la simple saveur, sans dépendre d'un système vanilla
    // pensé pour des PNJ explicitement configurés pour parler (ex: Haldor).
    public class FloatingSpeechBubble : MonoBehaviour
    {
        private const float LifetimeSeconds = 3f;
        private const float RiseSpeed = 0.3f;
        private const float HeightOffset = 2.2f;
        private const float FontSize = 3f;

        private float _timer;
        private Transform _cameraTransform;

        public static void Show(Transform anchor, string text)
        {
            if (anchor == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            var bubbleObj = new GameObject("FedoKnorri_Bubble");
            bubbleObj.transform.SetParent(anchor, worldPositionStays: false);
            bubbleObj.transform.localPosition = Vector3.up * HeightOffset;

            var label = bubbleObj.AddComponent<TextMeshPro>();

            // Sans police assignée explicitement, un TextMeshPro créé à l'exécution (pas depuis
            // une scène/un prefab déjà configuré dans l'éditeur Unity) n'affiche rien -- même
            // piège vécu et déjà documenté dans FedoServerTools.LoadingOverlay pour un
            // TextMeshProUGUI. Empruntée au Hud du jeu plutôt qu'embarquer un asset de police.
            TMP_FontAsset font = Hud.instance != null ? Hud.instance.m_hoverName?.font : null;
            if (font != null)
            {
                label.font = font;
            }

            label.text = text;
            label.fontSize = FontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;

            bubbleObj.AddComponent<FloatingSpeechBubble>();
        }

        private void Awake()
        {
            _cameraTransform = Camera.main != null ? Camera.main.transform : null;
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= LifetimeSeconds)
            {
                Destroy(gameObject);
                return;
            }

            transform.localPosition += Vector3.up * (RiseSpeed * Time.deltaTime);

            if (_cameraTransform != null)
            {
                transform.rotation = _cameraTransform.rotation;
            }
        }
    }
}
