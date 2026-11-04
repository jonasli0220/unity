using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dragon.UI.TalentTree
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class TalentTreePanZoom : MonoBehaviour, IDragHandler, IScrollHandler
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private float minZoom = 0.3f;
        [SerializeField] private float maxZoom = 2.5f;
        [SerializeField] private float wheelStep = 1.12f;

        public void SetContent(RectTransform target) => content = target;

        public void OnDrag(PointerEventData eventData)
        {
            if (content == null || (eventData.button != PointerEventData.InputButton.Left && eventData.button != PointerEventData.InputButton.Middle)) return;
            Canvas canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null ? Mathf.Max(0.001f, canvas.rootCanvas.scaleFactor) : 1f;
            content.anchoredPosition += eventData.delta / scale;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (content == null || Mathf.Approximately(eventData.scrollDelta.y, 0f)) return;
            var viewport = (RectTransform)transform;
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, eventData.position, camera, out Vector2 pointer)) return;
            float before = content.localScale.x;
            float after = Mathf.Clamp(before * Mathf.Pow(wheelStep, eventData.scrollDelta.y), minZoom, maxZoom);
            if (Mathf.Approximately(before, after)) return;
            content.anchoredPosition = pointer - (pointer - content.anchoredPosition) * (after / before);
            content.localScale = Vector3.one * after;
        }
    }
}
