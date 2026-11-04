using System;
using UnityEngine;
using UnityEngine.UI;

namespace Dragon.UI.TalentTree
{
    [Serializable]
    public struct TalentTreeCurve
    {
        public Vector2 start;
        public Vector2 end;
        public Vector2 fromHandle;
        public Vector2 toHandle;
    }

    public sealed class TalentTreeCurveGraphic : MaskableGraphic
    {
        [SerializeField] private TalentTreeCurve[] curves = Array.Empty<TalentTreeCurve>();
        [SerializeField, Min(0.5f)] private float lineWidth = 5f;

        public void SetCurves(TalentTreeCurve[] data, float width, Color tint)
        {
            curves = data ?? Array.Empty<TalentTreeCurve>();
            lineWidth = Mathf.Max(0.5f, width);
            color = tint;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            foreach (var curve in curves)
            {
                var p0 = curve.start;
                var p1 = curve.start + curve.fromHandle;
                var p2 = curve.end + curve.toHandle;
                var p3 = curve.end;
                int steps = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(p0, p3) / 42f), 8, 24);
                Vector2 previous = p0;
                for (int i = 1; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float u = 1f - t;
                    Vector2 current = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
                    AddSegment(vh, previous, current);
                    previous = current;
                }
            }
        }

        private void AddSegment(VertexHelper vh, Vector2 a, Vector2 b)
        {
            Vector2 direction = b - a;
            if (direction.sqrMagnitude < 0.0001f) return;
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (lineWidth * 0.5f);
            int index = vh.currentVertCount;
            AddVertex(vh, a - normal);
            AddVertex(vh, a + normal);
            AddVertex(vh, b + normal);
            AddVertex(vh, b - normal);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }

        private void AddVertex(VertexHelper vh, Vector2 position)
        {
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = position;
            vh.AddVert(vertex);
        }
    }
}
