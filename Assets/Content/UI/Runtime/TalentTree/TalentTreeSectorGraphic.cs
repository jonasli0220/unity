using UnityEngine;
using UnityEngine.UI;

namespace Dragon.UI.TalentTree
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TalentTreeSectorGraphic : MaskableGraphic
    {
        [SerializeField] private Color evenColor = new Color(0.09f, 0.13f, 0.19f, 0.7f);
        [SerializeField] private Color oddColor = new Color(0.12f, 0.16f, 0.22f, 0.7f);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f;
            const int slicesPerSector = 12;
            for (int sector = 0; sector < 6; sector++)
            {
                Color tint = sector % 2 == 0 ? evenColor : oddColor;
                for (int slice = 0; slice < slicesPerSector; slice++)
                {
                    float a0 = (sector * 60f + slice * 5f) * Mathf.Deg2Rad;
                    float a1 = a0 + 5f * Mathf.Deg2Rad;
                    int index = vh.currentVertCount;
                    AddVertex(vh, Vector2.zero, tint);
                    AddVertex(vh, new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)) * radius, tint);
                    AddVertex(vh, new Vector2(Mathf.Sin(a1), Mathf.Cos(a1)) * radius, tint);
                    vh.AddTriangle(index, index + 1, index + 2);
                }
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
