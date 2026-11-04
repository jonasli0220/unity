using UnityEngine;
using UnityEngine.UI;

namespace Dragon.UI.TalentTree
{
    public sealed class TalentTreeNodeGraphic : MaskableGraphic
    {
        [SerializeField] private Color innerColor = new Color(0.15f, 0.2f, 0.28f, 1f);
        [SerializeField] private Color rimColor = new Color(0.68f, 0.56f, 0.35f, 1f);
        [SerializeField, Range(0.02f, 0.4f)] private float rimFraction = 0.14f;

        public void SetAppearance(Color inner, Color rim)
        {
            innerColor = inner;
            rimColor = rim;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            const int segments = 40;
            float outer = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f;
            float inner = outer * (1f - rimFraction);
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                int index = vh.currentVertCount;
                AddVertex(vh, Vector2.zero, innerColor);
                AddVertex(vh, d0 * inner, innerColor);
                AddVertex(vh, d1 * inner, innerColor);
                AddVertex(vh, d0 * outer, rimColor);
                AddVertex(vh, d1 * outer, rimColor);
                vh.AddTriangle(index, index + 1, index + 2);
                vh.AddTriangle(index + 1, index + 3, index + 4);
                vh.AddTriangle(index + 1, index + 4, index + 2);
            }
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
