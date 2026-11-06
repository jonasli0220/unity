using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Dragon.UI.TalentTree
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TalentTreeSectorGraphic : MaskableGraphic
    {
        [SerializeField] private Color evenColor = new Color(0.09f, 0.13f, 0.19f, 0.7f);
        [SerializeField] private Color oddColor = new Color(0.12f, 0.16f, 0.22f, 0.7f);
        [SerializeField] private Vector2 origin;

        public void SetOrigin(Vector2 value)
        {
            origin = value;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            float radius = (rect.size.magnitude + origin.magnitude + 1f) * 3f;
            for (int sector = 0; sector < 6; sector++)
            {
                Color tint = sector % 2 == 0 ? evenColor : oddColor;
                float a0 = sector * 60f * Mathf.Deg2Rad;
                float a1 = (sector + 1) * 60f * Mathf.Deg2Rad;
                var polygon = new List<Vector2>(3)
                {
                    origin,
                    origin + new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)) * radius,
                    origin + new Vector2(Mathf.Sin(a1), Mathf.Cos(a1)) * radius
                };
                for (int side = 0; side < 4 && polygon.Count >= 3; side++)
                    polygon = Clip(polygon, rect, side);
                if (polygon.Count < 3) continue;
                int index = vh.currentVertCount;
                foreach (var point in polygon) AddVertex(vh, point, tint);
                for (int i = 1; i < polygon.Count - 1; i++) vh.AddTriangle(index, index + i, index + i + 1);
            }
        }

        private static List<Vector2> Clip(List<Vector2> polygon, Rect rect, int side)
        {
            var result = new List<Vector2>(polygon.Count + 2);
            Vector2 previous = polygon[polygon.Count - 1];
            bool previousInside = Inside(previous, rect, side);
            foreach (var current in polygon)
            {
                bool currentInside = Inside(current, rect, side);
                if (currentInside != previousInside) result.Add(Intersection(previous, current, rect, side));
                if (currentInside) result.Add(current);
                previous = current;
                previousInside = currentInside;
            }
            return result;
        }

        private static bool Inside(Vector2 point, Rect rect, int side)
        {
            switch (side)
            {
                case 0: return point.x >= rect.xMin;
                case 1: return point.x <= rect.xMax;
                case 2: return point.y >= rect.yMin;
                default: return point.y <= rect.yMax;
            }
        }

        private static Vector2 Intersection(Vector2 from, Vector2 to, Rect rect, int side)
        {
            bool vertical = side < 2;
            float value = side == 0 ? rect.xMin : side == 1 ? rect.xMax : side == 2 ? rect.yMin : rect.yMax;
            float start = vertical ? from.x : from.y;
            float end = vertical ? to.x : to.y;
            float t = Mathf.Clamp01((value - start) / (end - start));
            return from + (to - from) * t;
        }

        private static void AddVertex(VertexHelper vh, Vector2 position, Color tint)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = tint;
            vh.AddVert(vertex);
        }
    }
}
