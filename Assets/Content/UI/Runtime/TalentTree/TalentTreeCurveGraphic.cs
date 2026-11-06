using System;
using System.Collections.Generic;
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

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TalentTreeCurveGraphic : MaskableGraphic
    {
        [SerializeField, HideInInspector] private TalentTreeCurve[] curves = Array.Empty<TalentTreeCurve>();
        [SerializeField, Min(0.5f)] private float lineWidth = 5f;
        [SerializeField, Range(8, 64)] private int maxSegments = 24;
        [SerializeField] private bool roundCaps = true;

        public IReadOnlyList<TalentTreeCurve> Curves => curves;
        public float LineWidth => lineWidth;
        public int MaxSegments => maxSegments;
        public bool RoundCaps => roundCaps;

        public void SetCurves(TalentTreeCurve[] data, float width, Color tint)
        {
            curves = data ?? Array.Empty<TalentTreeCurve>();
            lineWidth = Mathf.Max(0.5f, width);
            color = tint;
            raycastTarget = false;
            SetVerticesDirty();
        }

        public void CopyAppearanceFrom(TalentTreeCurveGraphic source)
        {
            if (source == null) return;
            lineWidth = source.lineWidth;
            maxSegments = source.maxSegments;
            roundCaps = source.roundCaps;
            color = source.color;
            material = source.material;
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
                float controlLength = Vector2.Distance(p0, p1) + Vector2.Distance(p1, p2) + Vector2.Distance(p2, p3);
                int steps = Mathf.Clamp(Mathf.CeilToInt(controlLength / 42f), 8, Mathf.Max(8, maxSegments));
                Vector2 previous = p0;
                for (int i = 1; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float u = 1f - t;
                    Vector2 current = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
                    AddSegment(vh, previous, current);
                    previous = current;
                }
                if (roundCaps)
                {
                    AddRoundCap(vh, p0);
                    AddRoundCap(vh, p3);
                }
            }
        }

        private void AddRoundCap(VertexHelper vh, Vector2 center)
        {
            const int sides = 12;
            int index = vh.currentVertCount;
            AddVertex(vh, center);
            for (int i = 0; i <= sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                AddVertex(vh, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (lineWidth * 0.5f));
                if (i > 0) vh.AddTriangle(index, index + i, index + i + 1);
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
