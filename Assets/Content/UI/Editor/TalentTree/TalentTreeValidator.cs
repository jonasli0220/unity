using System;
using System.Collections.Generic;
using System.Linq;
using Dragon.UI.TalentTree;
using UnityEngine;

namespace Dragon.UI.TalentTree.Editor
{
    internal static class TalentTreeValidator
    {
        public static List<string> Validate(TalentTreeDefinition definition)
        {
            var errors = new List<string>();
            if (definition == null) { errors.Add("请选择策划逻辑配置。"); return errors; }
            if (definition.nodes == null || definition.edges == null)
            {
                errors.Add("逻辑配置缺少节点或连线列表。");
                return errors;
            }
            if (definition.nodes.Count == 0) errors.Add("至少需要一个节点。");
            var keys = new HashSet<string>();
            var ids = new HashSet<int>();
            var byKey = new Dictionary<string, TalentTreeAuthorNode>();
            foreach (var node in definition.nodes)
            {
                if (node == null) { errors.Add("逻辑配置包含空节点。"); continue; }
                if (string.IsNullOrEmpty(node.key) || !keys.Add(node.key)) errors.Add("存在重复或缺失的内部节点键。");
                else byKey.Add(node.key, node);
                if (node.id <= 0) errors.Add("节点 ID 必须为正整数。");
                else if (!ids.Add(node.id)) errors.Add("重复节点 ID：" + node.id);
                if (node.type != TalentNodeType.Small && node.type != TalentNodeType.Core)
                    errors.Add("节点 " + node.id + " 的类型无效。");
            }

            var edgePairs = new HashSet<string>();
            var outgoing = new Dictionary<string, List<string>>();
            foreach (var edge in definition.edges)
            {
                if (edge == null || string.IsNullOrEmpty(edge.fromKey) || string.IsNullOrEmpty(edge.toKey) ||
                    !byKey.ContainsKey(edge.fromKey) || !byKey.ContainsKey(edge.toKey))
                {
                    errors.Add("连线引用了不存在的节点。");
                    continue;
                }
                if (edge.fromKey == edge.toKey) { errors.Add("节点不能连接自己。"); continue; }
                string pair = EdgeKey(edge.fromKey, edge.toKey);
                if (!edgePairs.Add(pair)) errors.Add("重复连线：" + byKey[edge.fromKey].id + "→" + byKey[edge.toKey].id);
                if (!outgoing.TryGetValue(edge.fromKey, out var targets)) outgoing[edge.fromKey] = targets = new List<string>();
                targets.Add(edge.toKey);
            }

            var states = new Dictionary<string, int>();
            foreach (var key in byKey.Keys)
                if (HasCycle(key, outgoing, states)) { errors.Add("前置关系存在环，无法确定解锁顺序。"); break; }
            return errors.Distinct().ToList();
        }

        public static List<string> Validate(TalentTreeDefinition definition, TalentTreeLayout layout)
        {
            var errors = Validate(definition);
            if (errors.Count > 0) return errors;
            if (layout == null) { errors.Add("缺少 UI 表现配置，请先创建表现配置。"); return errors; }
            if (layout.definition != definition)
            {
                errors.Add("表现配置关联了另一份逻辑配置，请重新选择匹配的资产。");
                return errors;
            }
            if (layout.nodes == null || layout.edges == null)
            {
                errors.Add("表现配置缺少节点或连线布局列表。");
                return errors;
            }
            if (!IsFinite(layout.smallNodeSize) || !IsFinite(layout.coreNodeSize) || !IsFinite(layout.lineWidth) ||
                layout.smallNodeSize < 24f || layout.coreNodeSize < 32f || layout.lineWidth < 1f)
                errors.Add("节点尺寸或线宽无效或低于允许值。");

            var byKey = new Dictionary<string, TalentTreeNodeLayout>();
            foreach (var node in layout.nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.key) || byKey.ContainsKey(node.key))
                {
                    errors.Add("表现配置存在重复或缺失的节点键。");
                    continue;
                }
                byKey.Add(node.key, node);
            }
            var missingNodes = new List<int>();
            foreach (var node in definition.nodes)
            {
                if (!byKey.TryGetValue(node.key, out var visual)) { missingNodes.Add(node.id); continue; }
                if (!IsFinite(visual.position)) errors.Add("节点 " + node.id + " 的坐标无效。");
            }
            if (missingNodes.Count > 0)
                errors.Add("表现配置缺少 " + missingNodes.Count + " 个节点布局（例如 ID " + missingNodes[0] + "），请点击“同步新增节点/连线”。");

            var edgeKeys = new HashSet<string>();
            foreach (var edge in layout.edges)
            {
                if (edge == null || string.IsNullOrEmpty(edge.fromKey) || string.IsNullOrEmpty(edge.toKey) ||
                    !edgeKeys.Add(EdgeKey(edge.fromKey, edge.toKey)))
                    errors.Add("表现配置存在重复或缺失的连线键。");
                else if (!IsFinite(edge.fromHandle) || !IsFinite(edge.toHandle))
                    errors.Add("连线 " + edge.fromKey + "→" + edge.toKey + " 的贝塞尔控制点无效。");
            }
            int missingEdges = definition.edges.Count(edge => !edgeKeys.Contains(EdgeKey(edge.fromKey, edge.toKey)));
            if (missingEdges > 0)
                errors.Add("表现配置缺少 " + missingEdges + " 条连线样式，请点击“同步新增节点/连线”。");
            return errors.Distinct().ToList();
        }

        private static string EdgeKey(string fromKey, string toKey) => fromKey.Length + ":" + fromKey + toKey;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector2 value) =>
            IsFinite(value.x) && IsFinite(value.y);

        private static bool HasCycle(string key, Dictionary<string, List<string>> outgoing, Dictionary<string, int> states)
        {
            if (states.TryGetValue(key, out int state)) return state == 1;
            states[key] = 1;
            if (outgoing.TryGetValue(key, out var targets))
                foreach (var target in targets)
                    if (HasCycle(target, outgoing, states)) return true;
            states[key] = 2;
            return false;
        }
    }
}
