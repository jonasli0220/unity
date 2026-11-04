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
        private const int CurvesPerGraphic = 256;

        [Serializable] private sealed class ExportDocument
        {
            public int version = 1;
            public string sourceGuid;
            public string name;
            public float boardSize;
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

        public static bool Export(TalentTreeDefinition definition, out string message)
        {
            var errors = TalentTreeValidator.Validate(definition);
            if (errors.Count > 0) { message = string.Join("\n", errors); return false; }
            string sourcePath = AssetDatabase.GetAssetPath(definition);
            if (string.IsNullOrEmpty(sourcePath)) { message = "请先把配置保存为 .asset 文件。"; return false; }
            string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder("Assets/Content/UI/Prefab", "talent_tree");
            string prefabPath = OutputFolder + "/" + name + ".prefab";
            string jsonPath = OutputFolder + "/" + name + ".json";
            if (!CheckExistingOutputs(prefabPath, jsonPath, sourceGuid, out message)) return false;

            var orderedNodes = definition.nodes.OrderBy(node => node.id).ToArray();
            var nodeByKey = orderedNodes.ToDictionary(node => node.key);
            var orderedEdges = definition.edges.OrderBy(edge => nodeByKey[edge.fromKey].id).ThenBy(edge => nodeByKey[edge.toKey].id).ToArray();
            var runtimeNodes = new TalentTreeNodeRecord[orderedNodes.Length];
            var runtimeEdges = new TalentTreeEdgeRecord[orderedEdges.Length];
            var exportNodes = new ExportNode[orderedNodes.Length];
            var exportEdges = new ExportEdge[orderedEdges.Length];

            for (int i = 0; i < orderedNodes.Length; i++)
            {
                var node = orderedNodes[i];
                int[] prerequisites = orderedEdges.Where(edge => edge.toKey == node.key)
                    .Select(edge => nodeByKey[edge.fromKey].id).OrderBy(id => id).ToArray();
                runtimeNodes[i] = new TalentTreeNodeRecord { id = node.id, type = node.type, position = node.position, prerequisiteIds = prerequisites, icon = node.icon };
                exportNodes[i] = new ExportNode { id = node.id, type = node.type.ToString(), position = node.position,
                    iconAssetPath = node.icon != null ? AssetDatabase.GetAssetPath(node.icon) : string.Empty,
                    prerequisiteIds = prerequisites };
            }
            for (int i = 0; i < orderedEdges.Length; i++)
            {
                var edge = orderedEdges[i];
                int fromId = nodeByKey[edge.fromKey].id;
                int toId = nodeByKey[edge.toKey].id;
                runtimeEdges[i] = new TalentTreeEdgeRecord { fromId = fromId, toId = toId, fromHandle = edge.fromHandle, toHandle = edge.toHandle };
                exportEdges[i] = new ExportEdge { fromId = fromId, toId = toId, fromHandle = edge.fromHandle, toHandle = edge.toHandle };
            }
            var document = new ExportDocument { sourceGuid = sourceGuid, name = name, boardSize = definition.boardSize, nodes = exportNodes, edges = exportEdges };
            string json = JsonUtility.ToJson(document, true) + "\n";

            GameObject root = null;
            try
            {
                root = BuildPrefab(name, sourceGuid, definition, orderedNodes, nodeByKey, orderedEdges, runtimeNodes, runtimeEdges);
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
            if (prefab != null)
            {
                var data = prefab.GetComponent<TalentTreeData>();
                if (data == null || data.SourceGuid != sourceGuid)
                {
                    message = "目标 Prefab 已存在，且不属于当前配置：" + prefabPath;
                    return false;
                }
            }
            if (File.Exists(Path.GetFullPath(jsonPath)))
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

        private static GameObject BuildPrefab(string name, string sourceGuid, TalentTreeDefinition definition,
            TalentTreeAuthorNode[] orderedNodes, Dictionary<string, TalentTreeAuthorNode> byKey,
            TalentTreeAuthorEdge[] orderedEdges, TalentTreeNodeRecord[] runtimeNodes, TalentTreeEdgeRecord[] runtimeEdges)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(TalentTreePanZoom), typeof(TalentTreeData));
            var viewport = (RectTransform)root.transform;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var content = NewRect("Content", viewport, definition.boardSize);
            root.GetComponent<TalentTreePanZoom>().SetContent(content);
            var sectors = NewStretchRect("Six Sectors", content);
            sectors.gameObject.AddComponent<CanvasRenderer>();
            sectors.gameObject.AddComponent<TalentTreeSectorGraphic>().raycastTarget = false;
            var edgeLayer = NewStretchRect("Connections", content);
            for (int offset = 0; offset < orderedEdges.Length; offset += CurvesPerGraphic)
            {
                int count = Mathf.Min(CurvesPerGraphic, orderedEdges.Length - offset);
                var curves = new TalentTreeCurve[count];
                for (int i = 0; i < count; i++)
                {
                    var edge = orderedEdges[offset + i];
                    curves[i] = new TalentTreeCurve { start = byKey[edge.fromKey].position, end = byKey[edge.toKey].position,
                        fromHandle = edge.fromHandle, toHandle = edge.toHandle };
                }
                var chunk = NewStretchRect("Curves " + (offset / CurvesPerGraphic + 1), edgeLayer);
                chunk.gameObject.AddComponent<CanvasRenderer>();
                chunk.gameObject.AddComponent<TalentTreeCurveGraphic>().SetCurves(curves, definition.lineWidth, definition.lineColor);
            }
            var nodeLayer = NewStretchRect("Nodes", content);
            for (int i = 0; i < orderedNodes.Length; i++)
            {
                var node = orderedNodes[i];
                float size = node.type == TalentNodeType.Core ? definition.coreNodeSize : definition.smallNodeSize;
                var view = NewRect("Node " + node.id, nodeLayer, size);
                view.anchoredPosition = node.position;
                view.gameObject.AddComponent<CanvasRenderer>();
                view.gameObject.AddComponent<TalentTreeNodeGraphic>().SetAppearance(
                    node.type == TalentNodeType.Core ? definition.coreFillColor : definition.smallFillColor, definition.nodeRimColor);
                Sprite frame = node.type == TalentNodeType.Core ? definition.coreFrame : definition.smallFrame;
                if (frame != null) AddImage("Frame", view, frame, 1f);
                if (node.icon != null) AddImage("Icon", view, node.icon, 0.62f);
                runtimeNodes[i].view = view;
            }
            root.GetComponent<TalentTreeData>().SetGeneratedData(sourceGuid, definition.boardSize, runtimeNodes, runtimeEdges);
            return root;
        }

        private static RectTransform NewRect(string name, RectTransform parent, float size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
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

        private static void AddImage(string name, RectTransform parent, Sprite sprite, float scale)
        {
            var rect = NewRect(name, parent, parent.sizeDelta.x * scale);
            rect.gameObject.AddComponent<CanvasRenderer>();
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }
    }
}
