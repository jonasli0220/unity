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
            public int version = 5;
            public string sourceGuid;
            public string layoutGuid;
            public string name;
            public float boardSize;
            public Vector2 contentSize;
            public Vector2 contentCenter;
            public ExportNode[] nodes;
            public ExportEdge[] edges;
            public ExportTile[] inactiveTiles;
            public string[] generatedAssets;
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
            public Vector2 activePosition;
            public Vector2 activeSize;
            public string activeAtlasPath;
            public string activeSpriteName;
            public float activePixelsPerUnit;
        }

        [Serializable] private sealed class ExportTile
        {
            public int x;
            public int y;
            public Vector2 position;
            public Vector2 size;
            public string assetPath;
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
            if (!CheckExistingOutputs(prefabPath, jsonPath, sourceGuid, out ExportDocument previous,
                out message)) return false;

            TalentTreeContentBounds bounds;
            try { bounds = TalentTreeBoundsUtility.CalculateOriginCentered(definition, layout); }
            catch (Exception exception) { message = "无法计算导出范围：" + exception.Message; return false; }

            var orderedNodes = definition.nodes.OrderBy(node => node.id).ToArray();
            var nodeByKey = orderedNodes.ToDictionary(node => node.key);
            var nodeLayoutsByKey = layout.nodes.ToDictionary(node => node.key);
            var edgeLayoutsByKey = layout.edges.ToDictionary(edge => (edge.fromKey, edge.toKey));
            var orderedEdges = definition.edges.OrderBy(edge => nodeByKey[edge.fromKey].id).ThenBy(edge => nodeByKey[edge.toKey].id).ToArray();
            var exportNodes = new ExportNode[orderedNodes.Length];
            var exportEdges = new ExportEdge[orderedEdges.Length];
            var bakeCurves = new TalentTreeBakeCurve[orderedEdges.Length];
            var prerequisitesByKey = new Dictionary<string, List<int>>();
            foreach (var edge in orderedEdges)
            {
                if (!prerequisitesByKey.TryGetValue(edge.toKey, out var prerequisites))
                    prerequisitesByKey[edge.toKey] = prerequisites = new List<int>();
                prerequisites.Add(nodeByKey[edge.fromKey].id);
            }

            for (int i = 0; i < orderedNodes.Length; i++)
            {
                var node = orderedNodes[i];
                var visual = nodeLayoutsByKey[node.key];
                int[] prerequisites = prerequisitesByKey.TryGetValue(node.key, out var incoming)
                    ? incoming.OrderBy(id => id).ToArray() : Array.Empty<int>();
                exportNodes[i] = new ExportNode { id = node.id, type = node.type.ToString(), position = visual.position,
                    iconAssetPath = visual.icon != null ? AssetDatabase.GetAssetPath(visual.icon) : string.Empty,
                    prerequisiteIds = prerequisites };
            }
            for (int i = 0; i < orderedEdges.Length; i++)
            {
                var edge = orderedEdges[i];
                var visual = edgeLayoutsByKey[(edge.fromKey, edge.toKey)];
                int fromId = nodeByKey[edge.fromKey].id;
                int toId = nodeByKey[edge.toKey].id;
                exportEdges[i] = new ExportEdge { fromId = fromId, toId = toId, fromHandle = visual.fromHandle, toHandle = visual.toHandle };
                bakeCurves[i] = new TalentTreeBakeCurve(fromId, toId,
                    nodeLayoutsByKey[edge.fromKey].position, nodeLayoutsByKey[edge.toKey].position,
                    visual.fromHandle, visual.toHandle);
            }

            GameObject root = null;
            try
            {
                string assetFolder = OutputFolder + "/" + name + "_Sprites";
                var baked = TalentTreeSpriteBaker.Bake(bakeCurves, layout, assetFolder);
                var bakedEdges = baked.edges.ToDictionary(edge => (edge.fromId, edge.toId));
                foreach (var edge in exportEdges)
                {
                    var image = bakedEdges[(edge.fromId, edge.toId)];
                    edge.activePosition = image.position;
                    edge.activeSize = image.size;
                    edge.activeAtlasPath = image.atlasPath;
                    edge.activeSpriteName = image.spriteName;
                    edge.activePixelsPerUnit = image.pixelsPerUnit;
                }
                var document = new ExportDocument { sourceGuid = sourceGuid, layoutGuid = layoutGuid,
                    name = name, boardSize = Mathf.Max(bounds.Size.x, bounds.Size.y),
                    contentSize = bounds.Size, contentCenter = bounds.Center,
                    nodes = exportNodes, edges = exportEdges,
                    inactiveTiles = baked.tiles.Select(tile => new ExportTile { x = tile.x, y = tile.y,
                        position = tile.position, size = tile.size, assetPath = tile.assetPath }).ToArray(),
                    generatedAssets = baked.generatedAssets.ToArray() };
                root = BuildPrefab(name, bounds, orderedNodes, nodeLayoutsByKey, baked);
                var missingRenderer = root.GetComponentsInChildren<Graphic>(true)
                    .FirstOrDefault(graphic => graphic.GetComponent<CanvasRenderer>() == null);
                if (missingRenderer != null)
                    throw new InvalidOperationException("UI 图形缺少 CanvasRenderer：" + missingRenderer.gameObject.name);
                if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null) throw new InvalidOperationException("Prefab 保存失败。");
                File.WriteAllText(Path.GetFullPath(jsonPath), JsonUtility.ToJson(document, true) + "\n",
                    new UTF8Encoding(false));
                AssetDatabase.ImportAsset(jsonPath);
                CleanupObsoleteAssets(previous, document, assetFolder);
                AssetDatabase.SaveAssets();
                message = "已生成 " + prefabPath + "、" + jsonPath + " 和 " +
                    document.inactiveTiles.Length + " 张未激活底图、" + baked.generatedAssets.Count + " 个图片资源。";
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

        private static bool CheckExistingOutputs(string prefabPath, string jsonPath, string sourceGuid,
            out ExportDocument previous, out string message)
        {
            previous = null;
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
                    previous = JsonUtility.FromJson<ExportDocument>(File.ReadAllText(Path.GetFullPath(jsonPath)));
                    if (previous == null || previous.sourceGuid != sourceGuid) throw new InvalidDataException();
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

        private static void CleanupObsoleteAssets(ExportDocument previous, ExportDocument current,
            string assetFolder)
        {
            if (previous == null || previous.generatedAssets == null) return;
            var keep = new HashSet<string>(current.generatedAssets, StringComparer.Ordinal);
            foreach (string oldPath in previous.generatedAssets)
            {
                if (string.IsNullOrEmpty(oldPath) || !oldPath.StartsWith(assetFolder + "/",
                    StringComparison.Ordinal) || keep.Contains(oldPath) ||
                    Path.GetExtension(oldPath) != ".png") continue;
                if (!AssetDatabase.DeleteAsset(oldPath))
                    Debug.LogWarning("天赋树旧图片未能清理：" + oldPath);
            }
        }

        private static GameObject BuildPrefab(string name, TalentTreeContentBounds bounds,
            TalentTreeAuthorNode[] orderedNodes,
            Dictionary<string, TalentTreeNodeLayout> nodeLayoutsByKey,
            TalentTreeSpriteBakeResult baked)
        {
            var positions = orderedNodes.ToDictionary(node => node.key, node => nodeLayoutsByKey[node.key].position);
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
            var tileLayer = NewStretchRect("InactiveTiles", content);
            foreach (var tile in baked.tiles)
            {
                var view = NewRect("Tile " + tile.x + "," + tile.y, tileLayer, tile.size);
                view.anchoredPosition = tile.position;
                view.gameObject.AddComponent<CanvasRenderer>();
                var image = view.gameObject.AddComponent<Image>();
                image.sprite = tile.sprite;
                image.raycastTarget = false;
            }
            var edgeLayer = NewStretchRect("ActiveEdges", content);
            foreach (var edge in baked.edges)
            {
                var view = NewRect("Edge " + edge.fromId + " to " + edge.toId,
                    edgeLayer, edge.size);
                view.anchoredPosition = edge.position;
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
            var nodesByKey = new Dictionary<string, TalentTreeAuthorNode>(definition.nodes.Count);
            foreach (var node in definition.nodes)
                if (node != null && !string.IsNullOrEmpty(node.key)) nodesByKey[node.key] = node;
            var nodeLayoutsByKey = new Dictionary<string, TalentTreeNodeLayout>(layout != null ? layout.nodes.Count : 0);
            var edgeLayoutsByKey = new Dictionary<(string, string), TalentTreeEdgeLayout>(layout != null ? layout.edges.Count : 0);
            if (layout != null)
            {
                foreach (var visual in layout.nodes)
                    if (visual != null && !string.IsNullOrEmpty(visual.key)) nodeLayoutsByKey[visual.key] = visual;
                foreach (var visual in layout.edges)
                    if (visual != null && visual.fromKey != null && visual.toKey != null)
                        edgeLayoutsByKey[(visual.fromKey, visual.toKey)] = visual;
            }

            foreach (var node in definition.nodes)
            {
                Vector2 position = NodePosition(node, layout, nodeLayoutsByKey);
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
                if (edge == null || edge.fromKey == null || edge.toKey == null) continue;
                nodesByKey.TryGetValue(edge.fromKey, out var from);
                nodesByKey.TryGetValue(edge.toKey, out var to);
                if (from == null || to == null) continue;
                Vector2 start = NodePosition(from, layout, nodeLayoutsByKey);
                Vector2 end = NodePosition(to, layout, nodeLayoutsByKey);
                edgeLayoutsByKey.TryGetValue((edge.fromKey, edge.toKey), out var visual);
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

        private static Vector2 NodePosition(TalentTreeAuthorNode node, TalentTreeLayout layout,
            Dictionary<string, TalentTreeNodeLayout> nodeLayoutsByKey)
        {
            TalentTreeNodeLayout visual = null;
            if (node.key != null) nodeLayoutsByKey.TryGetValue(node.key, out visual);
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
