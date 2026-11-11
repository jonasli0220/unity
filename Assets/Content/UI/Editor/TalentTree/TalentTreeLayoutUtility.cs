using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dragon.UI.TalentTree.Editor
{
    internal static class TalentTreeLayoutUtility
    {
        private const string LayoutFolder = "Assets/Content/UI/Editor/TalentTree/Layouts";

        public static TalentTreeLayout Find(TalentTreeDefinition definition)
        {
            if (definition == null) return null;
            string sourcePath = AssetDatabase.GetAssetPath(definition);
            if (string.IsNullOrEmpty(sourcePath)) return null;
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            var layout = AssetDatabase.LoadAssetAtPath<TalentTreeLayout>(LayoutFolder + "/" + name + "Layout.asset");
            return layout != null && layout.definition == definition ? layout : null;
        }

        public static TalentTreeLayout Create(TalentTreeDefinition definition)
        {
            if (definition == null) throw new InvalidOperationException("请先选择逻辑配置。");
            var errors = TalentTreeValidator.Validate(definition);
            errors.Remove("至少需要一个节点。");
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            string sourcePath = AssetDatabase.GetAssetPath(definition);
            if (string.IsNullOrEmpty(sourcePath)) throw new InvalidOperationException("请先保存逻辑配置。");
            if (!AssetDatabase.IsValidFolder(LayoutFolder))
                AssetDatabase.CreateFolder("Assets/Content/UI/Editor/TalentTree", "Layouts");
            string path = LayoutFolder + "/" + Path.GetFileNameWithoutExtension(sourcePath) + "Layout.asset";
            var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (existing != null)
            {
                if (existing is TalentTreeLayout matching && matching.definition == definition) return matching;
                throw new InvalidOperationException("表现配置路径已被其他资产占用：" + path);
            }

            var layout = ScriptableObject.CreateInstance<TalentTreeLayout>();
            layout.definition = definition;
            bool hasLegacy = HasLegacyPresentation(definition);
            layout.guideMode = definition.nodes.Count == 0 && !hasLegacy
                ? TalentTreeGuideMode.Horizontal1080 : TalentTreeGuideMode.SixSectors;
            if (hasLegacy) CopyLegacyStyle(definition, layout);
            SyncMissingEntries(definition, layout, hasLegacy);
            AssetDatabase.CreateAsset(layout, path);
            AssetDatabase.SaveAssets();
            return layout;
        }

        public static int SyncMissingEntries(TalentTreeDefinition definition, TalentTreeLayout layout, bool copyLegacy = false)
        {
            if (definition == null || layout == null || layout.definition != definition)
                throw new InvalidOperationException("表现配置未关联当前逻辑配置。");
            int added = 0;
            foreach (var node in definition.nodes)
            {
                if (layout.FindNode(node.key) != null) continue;
                layout.nodes.Add(new TalentTreeNodeLayout
                {
                    key = node.key,
                    position = copyLegacy ? node.position : SuggestPosition(node.id),
                    icon = copyLegacy ? node.icon : null
                });
                added++;
            }
            return added + SyncMissingEdges(definition, layout, copyLegacy);
        }

        public static int SyncMissingEdges(TalentTreeDefinition definition, TalentTreeLayout layout, bool copyLegacy = false)
        {
            if (definition == null || layout == null || layout.definition != definition)
                throw new InvalidOperationException("表现配置未关联当前逻辑配置。");
            int added = 0;
            foreach (var edge in definition.edges)
            {
                if (layout.FindEdge(edge.fromKey, edge.toKey) != null) continue;
                var from = layout.FindNode(edge.fromKey);
                var to = layout.FindNode(edge.toKey);
                if (from == null || to == null) continue;
                Vector2 delta = to.position - from.position;
                layout.edges.Add(new TalentTreeEdgeLayout
                {
                    fromKey = edge.fromKey,
                    toKey = edge.toKey,
                    fromHandle = copyLegacy ? edge.fromHandle : delta / 3f,
                    toHandle = copyLegacy ? edge.toHandle : -delta / 3f
                });
                added++;
            }
            return added;
        }

        public static Vector2 SuggestPosition(int id)
        {
            if (id <= 1) return Vector2.zero;
            float angle = (id - 2) * 2.39996323f;
            float radius = 180f + Mathf.Sqrt(id - 2) * 110f;
            return new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius;
        }

        private static bool HasLegacyPresentation(TalentTreeDefinition definition)
        {
            foreach (var node in definition.nodes)
                if (node.position != Vector2.zero || node.icon != null) return true;
            foreach (var edge in definition.edges)
                if (edge.fromHandle != Vector2.zero || edge.toHandle != Vector2.zero) return true;
            return definition.boardSize != 4096f || definition.smallNodeSize != 72f ||
                   definition.coreNodeSize != 124f || definition.lineWidth != 5f ||
                   definition.smallFrame != null || definition.coreFrame != null ||
                   definition.lineColor != new Color(0.65f, 0.55f, 0.36f, 1f) ||
                   definition.smallFillColor != new Color(0.12f, 0.2f, 0.29f, 1f) ||
                   definition.coreFillColor != new Color(0.25f, 0.18f, 0.24f, 1f) ||
                   definition.nodeRimColor != new Color(0.71f, 0.59f, 0.37f, 1f);
        }

        private static void CopyLegacyStyle(TalentTreeDefinition definition, TalentTreeLayout layout)
        {
            layout.boardSize = definition.boardSize;
            layout.smallNodeSize = definition.smallNodeSize;
            layout.coreNodeSize = definition.coreNodeSize;
            layout.lineWidth = definition.lineWidth;
            layout.lineColor = definition.lineColor;
            layout.smallFillColor = definition.smallFillColor;
            layout.coreFillColor = definition.coreFillColor;
            layout.nodeRimColor = definition.nodeRimColor;
            layout.smallFrame = definition.smallFrame;
            layout.coreFrame = definition.coreFrame;
        }
    }
}
