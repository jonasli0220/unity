using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Dragon.UI.TalentTree;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dragon.UI.TalentTree.Editor
{
    internal static class TalentTreeExporter
    {
        private const string OutputFolder = "Assets/Content/UI/Prefab/talent_tree";

        [Serializable] private sealed class ExportDocument
        {
            public int version = 4;
            public string sourceGuid;
            public string layoutGuid;
            public string name;
            public float boardSize;
            public Vector2 contentSize;
            public Vector2 contentCenter;
            public ExportNode[] nodes;
            public ExportEdge[] edges;
        }

        [Serializable] private sealed class ExportNode
        {
            public int id;
            public string type;
            public Vector2 position;
            public string iconAssetPath;
            public int[] prerequisiteIds;
        }

        [Serializable] private sealed class ExportEdge
        {
            public int fromId;
            public int toId;
            public Vector2 fromHandle;
            public Vector2 toHandle;
        }

        [MenuItem("Tools/UI/Talent Tree/Export Selected Tree", false, 2336)]
        private static void ExportSelectedTree()
        {
            var definition = Selection.activeObject as TalentTreeDefinition;
            var selectedLayout = Selection.activeObject as TalentTreeLayout;
            if (selectedLayout != null) definition = selectedLayout.definition;
            if (definition == null) throw new InvalidOperationException("请在 Project 窗口选中天赋树逻辑或表现 .asset，再运行导出。");
            var layout = selectedLayout != null ? selectedLayout : TalentTreeLayoutUtility.Find(definition);
            if (!Export(definition, layout, out string message)) throw new InvalidOperationException(message);
            Debug.Log(message);
        }

        public static bool Export(TalentTreeDefinition definition, TalentTreeLayout layout, out string message)
        {
            var errors = TalentTreeValidator.Validate(definition, layout);
            if (errors.Count > 0) { message = string.Join("\n", errors); return false; }
            string sourcePath = AssetDatabase.GetAssetPath(definition);
            string layoutPath = AssetDatabase.GetAssetPath(layout);
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(layoutPath))
            {
                message = "请先保存逻辑配置和表现配置两个 .asset 文件。";
                return false;
            }
            string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            string layoutGuid = AssetDatabase.AssetPathToGUID(layoutPath);
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder("Assets/Content/UI/Prefab", "talent_tree");
            string prefabPath = OutputFolder + "/" + name + ".prefab";
            string jsonPath = OutputFolder + "/" + name + ".json";
            if (!CheckExistingOutputs(prefabPath, jsonPath, sourceGuid, out message)) return false;

            TalentTreeContentBounds bounds;
            try { bounds = TalentTreeBoundsUtility.CalculateOriginCentered(definition, layout); }
            catch (Exception exception) { message = "无法计算导出范围：" + exception.Message; return false; }

            var orderedNodes = definition.nodes.OrderBy(node => node.id).ToArray();
            var nodeByKey = orderedNodes.ToDictionary(node => node.key);
            var orderedEdges = definition.edges.OrderBy(edge => nodeByKey[edge.fromKey].id).ThenBy(edge => nodeByKey[edge.toKey].id).ToArray();
            var exportNodes = new ExportNode[orderedNodes.Length];
            var exportEdges = new ExportEdge[orderedEdges.Length];

            for (int i = 0; i < orderedNodes.Length; i++)
            {
                var node = orderedNodes[i];
                var visual = layout.FindNode(node.key);
                int[] prerequisites = orderedEdges.Where(edge => edge.toKey == node.key)
                    .Select(edge => nodeByKey[edge.fromKey].id).OrderBy(id => id).ToArray();
                exportNodes[i] = new ExportNode { id = node.id, type = node.type.ToString(), position = visual.position,
                    iconAssetPath = visual.icon != null ? AssetDatabase.GetAssetPath(visual.icon) : string.Empty,
                    prerequisiteIds = prerequisites };
            }
            for (int i = 0; i < orderedEdges.Length; i++)
            {
                var edge = orderedEdges[i];
                var visual = layout.FindEdge(edge.fromKey, edge.toKey);
                int fromId = nodeByKey[edge.fromKey].id;
                int toId = nodeByKey[edge.toKey].id;
                exportEdges[i] = new ExportEdge { fromId = fromId, toId = toId, fromHandle = visual.fromHandle, toHandle = visual.toHandle };
            }
            var document = new ExportDocument { sourceGuid = sourceGuid, layoutGuid = layoutGuid, name = name,
                boardSize = Mathf.Max(bounds.Size.x, bounds.Size.y), contentSize = bounds.Size,
                contentCenter = bounds.Center, nodes = exportNodes, edges = exportEdges };
            string json = JsonUtility.ToJson(document, true) + "\n";

            GameObject root = null;
            try
            {
                var previousPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                root = BuildPrefab(name, layout, bounds, orderedNodes, orderedEdges, previousPrefab);
                var missingRenderer = root.GetComponentsInChildren<Graphic>(true)
                    .FirstOrDefault(graphic => graphic.GetComponent<CanvasRenderer>() == null);
                if (missingRenderer != null)
                    throw new InvalidOperationException("UI 图形缺少 CanvasRenderer：" + missingRenderer.gameObject.name);
                if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null) throw new InvalidOperationException("Prefab 保存失败。");
                File.WriteAllText(Path.GetFullPath(jsonPath), json, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(jsonPath);
                AssetDatabase.SaveAssets();
                message = "已生成 " + prefabPath + " 和 " + jsonPath;
                return true;
            }
            catch (Exception exception)
            {
                message = "生成失败：" + exception.Message;
                Debug.LogException(exception);
                return false;
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static bool CheckExistingOutputs(string prefabPath, string jsonPath, string sourceGuid, out string message)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            bool jsonExists = File.Exists(Path.GetFullPath(jsonPath));
            if (prefab != null)
            {
                var data = prefab.GetComponent<TalentTreeData>();
                if ((data != null && data.SourceGuid != sourceGuid) || (data == null && !jsonExists))
                {
                    message = "目标 Prefab 已存在，且不属于当前配置：" + prefabPath;
                    return false;
                }
            }
            if (jsonExists)
            {
                try
                {
                    var existing = JsonUtility.FromJson<ExportDocument>(File.ReadAllText(Path.GetFullPath(jsonPath)));
                    if (existing == null || existing.sourceGuid != sourceGuid) throw new InvalidDataException();
                }
                catch
                {
                    message = "目标 JSON 已存在，且不属于当前配置：" + jsonPath;
                    return false;
                }
            }
            message = string.Empty;
            return true;
        }

        private static GameObject BuildPrefab(string name, TalentTreeLayout layout, TalentTreeContentBounds bounds,
            TalentTreeAuthorNode[] orderedNodes,
            TalentTreeAuthorEdge[] orderedEdges, GameObject previousPrefab)
        {
            var positions = orderedNodes.ToDictionary(node => node.key, node => layout.FindNode(node.key).position);
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            var viewport = (RectTransform)root.transform;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var content = NewRect("Content", viewport, bounds.Size);
            var scroll = root.GetComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = scroll.vertical = true;
            scroll.inertia = false;
            scroll.movementType = ScrollRect.MovementType.Unrestricted;
            scroll.scrollSensitivity = 0f;
            var edgeLayer = NewStretchRect("Connections", content);
            for (int i = 0; i < orderedEdges.Length; i++)
            {
                var edge = orderedEdges[i];
                var visual = layout.FindEdge(edge.fromKey, edge.toKey);
                int fromId = orderedNodes.First(node => node.key == edge.fromKey).id;
                int toId = orderedNodes.First(node => node.key == edge.toKey).id;
                string edgeName = "Edge " + fromId + " to " + toId;
                var edgeView = NewStretchRect(edgeName, edgeLayer);
                edgeView.gameObject.AddComponent<CanvasRenderer>();
                var graphic = edgeView.gameObject.AddComponent<UILineRenderer>();
                SetCurve(graphic, positions[edge.fromKey], positions[edge.toKey], visual.fromHandle, visual.toHandle,
                    layout.lineWidth, layout.lineColor);
                var previousView = previousPrefab != null ? previousPrefab.transform.Find("Content/Connections/" + edgeName) : null;
                CopyAppearance(graphic, previousView);
                var activeRect = NewStretchRect("Active", edgeView);
                activeRect.gameObject.AddComponent<CanvasRenderer>();
                var activeGraphic = activeRect.gameObject.AddComponent<UILineRenderer>();
                SetCurve(activeGraphic, positions[edge.fromKey], positions[edge.toKey], visual.fromHandle, visual.toHandle,
                    layout.lineWidth * 1.25f, new Color(1f, 0.8f, 0.35f, 1f));
                CopyAppearance(activeGraphic, previousView != null ? previousView.Find("Active") : null);
                activeRect.gameObject.SetActive(false);
            }
            var nodeLayer = NewStretchRect("Nodes", content);
            for (int i = 0; i < orderedNodes.Length; i++)
            {
                var node = orderedNodes[i];
                var view = NewRect("Node " + node.id, nodeLayer, Vector2.zero);
                view.anchoredPosition = positions[node.key];
            }
            return root;
        }

        private static void SetCurve(UILineRenderer graphic, Vector2 start, Vector2 end,
            Vector2 fromHandle, Vector2 toHandle, float width, Color color)
        {
            // This shipped renderer subtracts pivot * 1 from absolute points, so offset by half a unit.
            Vector2 pivotOffset = new Vector2(0.5f, 0.5f);
            graphic.RelativeSize = false;
            graphic.LineList = false;
            graphic.LineCaps = true;
            graphic.BezierMode = UILineRenderer.BezierType.Basic;
            graphic.BezierSegmentsPerCurve = 24;
            graphic.Points = new[] { start + pivotOffset, start + fromHandle + pivotOffset,
                end + toHandle + pivotOffset, end + pivotOffset };
            graphic.LineThickness = width;
            graphic.color = color;
            graphic.raycastTarget = false;
        }

        private static void CopyAppearance(UILineRenderer target, Transform previous)
        {
            if (previous == null) return;
            var shipped = previous.GetComponent<UILineRenderer>();
            if (shipped != null)
            {
                target.color = shipped.color;
                target.material = shipped.material;
                target.LineThickness = shipped.LineThickness;
                target.LineCaps = shipped.LineCaps;
                target.LineJoins = shipped.LineJoins;
                target.BezierSegmentsPerCurve = shipped.BezierSegmentsPerCurve;
                return;
            }
            var legacy = previous.GetComponent<TalentTreeCurveGraphic>();
            if (legacy == null) return;
            target.color = legacy.color;
            target.material = legacy.material;
            target.LineThickness = legacy.LineWidth;
            target.LineCaps = legacy.RoundCaps;
            target.BezierSegmentsPerCurve = legacy.MaxSegments;
        }

        private static RectTransform NewRect(string name, RectTransform parent, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private static RectTransform NewStretchRect(string name, RectTransform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

    }

    internal readonly struct TalentTreeContentBounds
    {
        public readonly Vector2 Center;
        public readonly Vector2 Size;

        public TalentTreeContentBounds(Vector2 center, Vector2 size)
        {
            Center = center;
            Size = size;
        }
    }

    internal static class TalentTreeBoundsUtility
    {
        private const double Padding = 80d;
        private const double MinimumSide = 256d;

        public static TalentTreeContentBounds Calculate(TalentTreeDefinition definition, TalentTreeLayout layout)
        {
            double minX = double.PositiveInfinity;
            double minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;
            float lineWidth = layout != null ? layout.lineWidth : definition.lineWidth;

            foreach (var node in definition.nodes)
            {
                Vector2 position = NodePosition(node, layout);
                float size = node.type == TalentNodeType.Core
                    ? (layout != null ? layout.coreNodeSize : definition.coreNodeSize)
                    : (layout != null ? layout.smallNodeSize : definition.smallNodeSize);
                double radius = size * 0.5d;
                Include(position.x - radius, ref minX, ref maxX);
                Include(position.x + radius, ref minX, ref maxX);
                Include(position.y - radius, ref minY, ref maxY);
                Include(position.y + radius, ref minY, ref maxY);
            }

            foreach (var edge in definition.edges)
            {
                var from = definition.FindNode(edge.fromKey);
                var to = definition.FindNode(edge.toKey);
                if (from == null || to == null) continue;
                Vector2 start = NodePosition(from, layout);
                Vector2 end = NodePosition(to, layout);
                var visual = layout != null ? layout.FindEdge(edge.fromKey, edge.toKey) : null;
                Vector2 fromHandle = visual != null ? visual.fromHandle : (end - start) / 3f;
                Vector2 toHandle = visual != null ? visual.toHandle : (start - end) / 3f;
                ExpandCubic(start.x, start.x + fromHandle.x, end.x + toHandle.x, end.x, ref minX, ref maxX);
                ExpandCubic(start.y, start.y + fromHandle.y, end.y + toHandle.y, end.y, ref minY, ref maxY);
            }

            if (double.IsPositiveInfinity(minX)) return new TalentTreeContentBounds(Vector2.zero, new Vector2(512f, 512f));
            double margin = Padding + lineWidth * 0.5d + 12d;
            double centerX = (minX + maxX) * 0.5d;
            double centerY = (minY + maxY) * 0.5d;
            double width = Math.Max(MinimumSide, maxX - minX + margin * 2d);
            double height = Math.Max(MinimumSide, maxY - minY + margin * 2d);
            if (!IsFiniteFloat(centerX) || !IsFiniteFloat(centerY) || !IsFiniteFloat(width) || !IsFiniteFloat(height))
                throw new InvalidOperationException("节点或曲线坐标过大，无法生成有效的 UGUI 画布。");
            return new TalentTreeContentBounds(new Vector2((float)centerX, (float)centerY),
                new Vector2((float)width, (float)height));
        }

        public static TalentTreeContentBounds CalculateOriginCentered(TalentTreeDefinition definition, TalentTreeLayout layout)
        {
            TalentTreeContentBounds tight = Calculate(definition, layout);
            double halfWidth = Math.Abs((double)tight.Center.x) + tight.Size.x * 0.5d;
            double halfHeight = Math.Abs((double)tight.Center.y) + tight.Size.y * 0.5d;
            double width = Math.Max(MinimumSide, halfWidth * 2d);
            double height = Math.Max(MinimumSide, halfHeight * 2d);
            if (!IsFiniteFloat(width) || !IsFiniteFloat(height))
                throw new InvalidOperationException("节点或曲线坐标过大，无法生成有效的 UGUI 画布。");
            return new TalentTreeContentBounds(Vector2.zero, new Vector2((float)width, (float)height));
        }

        private static Vector2 NodePosition(TalentTreeAuthorNode node, TalentTreeLayout layout)
        {
            var visual = layout != null ? layout.FindNode(node.key) : null;
            if (visual != null) return visual.position;
            if (layout == null && (node.position != Vector2.zero || node.id == 1)) return node.position;
            return TalentTreeLayoutUtility.SuggestPosition(node.id);
        }

        private static void Include(double value, ref double minimum, ref double maximum)
        {
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        private static void ExpandCubic(double p0, double p1, double p2, double p3,
            ref double minimum, ref double maximum)
        {
            Include(p0, ref minimum, ref maximum);
            Include(p3, ref minimum, ref maximum);
            double a = -p0 + 3d * p1 - 3d * p2 + p3;
            double b = 3d * p0 - 6d * p1 + 3d * p2;
            double c = -3d * p0 + 3d * p1;
            if (Math.Abs(a) < 1e-9d)
            {
                if (Math.Abs(b) >= 1e-9d) IncludeCubicAt(-c / (2d * b), p0, p1, p2, p3, ref minimum, ref maximum);
                return;
            }
            double discriminant = b * b - 3d * a * c;
            if (discriminant < 0d) return;
            double root = Math.Sqrt(discriminant);
            IncludeCubicAt((-b + root) / (3d * a), p0, p1, p2, p3, ref minimum, ref maximum);
            IncludeCubicAt((-b - root) / (3d * a), p0, p1, p2, p3, ref minimum, ref maximum);
        }

        private static void IncludeCubicAt(double t, double p0, double p1, double p2, double p3,
            ref double minimum, ref double maximum)
        {
            if (t <= 0d || t >= 1d) return;
            double u = 1d - t;
            double value = u * u * u * p0 + 3d * u * u * t * p1 + 3d * u * t * t * p2 + t * t * t * p3;
            Include(value, ref minimum, ref maximum);
        }

        private static bool IsFiniteFloat(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= -float.MaxValue && value <= float.MaxValue;
    }
}
