using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FedoHud
{
    // Glisser-déposer générique, réutilisé par tous les blocs déplaçables de ce mod (une
    // seule implémentation pour ne pas diverger) : cliquer n'importe où sur le bloc et
    // glisser le déplace directement, sans touche à maintenir.
    //
    // Ajoute elle-même un `Image` invisible en plein cadre si l'appelant n'en a pas déjà
    // un sur ce même GameObject -- nécessaire pour capter le clic de façon fiable quel
    // que soit le contenu réel du bloc (texte/icônes en enfants, pas forcément un
    // Graphic sur la racine). Ce Graphic reste raycastable en permanence : le bloc
    // bloque donc les clics de jeu sous lui en permanence, pas seulement pendant un
    // glissement -- même compromis que la plupart des HUD de jeux.
    internal class DraggableAnchor : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action<Vector2> OnDragEnd;

        private RectTransform _rect;
        private Vector2 _dragStartAnchoredPos;
        private Vector2 _dragStartPointerPos;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();

            var graphic = GetComponent<Graphic>();
            if (graphic == null)
            {
                graphic = gameObject.AddComponent<Image>();
                graphic.color = Color.clear;
            }

            graphic.raycastTarget = true;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragStartAnchoredPos = _rect.anchoredPosition;
            _dragStartPointerPos = eventData.position;
        }

        public void OnDrag(PointerEventData eventData)
        {
            _rect.anchoredPosition = _dragStartAnchoredPos + (eventData.position - _dragStartPointerPos);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            OnDragEnd?.Invoke(_rect.anchoredPosition);
        }
    }
}
