using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FedoSignColor
{
    // Zone cliquable/glissable générique, réutilisée par le carré saturation/luminosité
    // et la bande de teinte du picker -- rapporte un point normalisé (0..1 sur chaque
    // axe, origine en bas-gauche du rect) à chaque clic/glissement.
    internal class NormalizedDragArea : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public Action<Vector2> OnChanged;

        private RectTransform _rect;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Report(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            Report(eventData);
        }

        private void Report(PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, eventData.position, eventData.pressEventCamera, out var localPoint))
            {
                return;
            }

            var rect = _rect.rect;
            float x = Mathf.Clamp01((localPoint.x - rect.xMin) / rect.width);
            float y = Mathf.Clamp01((localPoint.y - rect.yMin) / rect.height);
            OnChanged?.Invoke(new Vector2(x, y));
        }
    }
}
