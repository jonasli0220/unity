using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Dragon.UI.TalentTree.Editor
{
    internal static class TalentTreeValidator
    {
        public static List<string> Validate(TalentTreeDefinition definition)
        {
            var errors = new List<string>();
            if (definition == null) { errors.Add("请选择天赋树配置资产。"); return errors; }
            if (definition.nodes.Count == 0) errors.Add("至少需要一个节点。");
            if (definition.boardSize < 1024f) errors.Add("画布尺寸必须不小于 1024。");
            var keys = new HashSet<string>();
            var ids = new HashSet<int>();
            var byKey = new Dictionary<string, TalentTreeAuthorNode>();
            foreach (var node in definition.nodes)
            {
                if (string.IsNullOrEmpty(node.key) || !keys.Add(node.key)) errors.Add("存在重复或缺失的内部节点键。");
                else byKey.Add(node.key, node);
                if (node.id <= 0) errors.Add("节点 ID 必须为正整数。");
                else if (!ids.Add(node.id)) errors.Add("重复节点 ID：" + node.id);
                float radius = definition.boardSize * 0.5f - (node.type == TalentNodeType.Core ? definition.coreNodeSize : definition.smallNodeSize) * 0.5f;
                if (node.position.magnitude > radius + 0.01f) errors.Add("节点 " + node.id + " 超出了六区画布。");
            }

            var edgePairs = new HashSet<string>();
            var outgoing = new Dictionary<string, List<string>>();
            foreach (var edge in definition.edges)
            {
                if (!byKey.ContainsKey(edge.fromKey) || !byKey.ContainsKey(edge.toKey)) { errors.Add("连线引用了不存在的节点。"); continue; }
                if (edge.fromKey == edge.toKey) { errors.Add("节点不能连接自己。"); continue; }
                string pair = edge.fromKey + ">" + edge.toKey;
                if (!edgePairs.Add(pair)) errors.Add("重复连线：" + byKey[edge.fromKey].id + "→" + byKey[edge.toKey].id);
                if (!outgoing.TryGetValue(edge.fromKey, out var targets)) outgoing[edge.fromKey] = targets = new List<string>();
                targets.Add(edge.toKey);
            }

            var states = new Dictionary<string, int>();
            foreach (var key in byKey.Keys)
            {
                if (HasCycle(key, outgoing, states)) { errors.Add("前置关系存在环，无法确定解锁顺序。"); break; }
            }
            return errors.Distinct().ToList();
        }

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
