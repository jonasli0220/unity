using System;
using System.Collections.Generic;
using Dragon.UI.TalentTree;
using UnityEngine;

namespace Dragon.UI.TalentTree.Editor
{
    // Mutates the two source assets together; the caller owns the Undo group and dirty flags.
    internal static class TalentTreeGraphEdit
    {
        public static TalentTreeEdgeLayout EnsureEdgeLayout(TalentTreeDefinition definition, TalentTreeLayout layout,
            TalentTreeAuthorEdge edge)
        {
            var visual = layout.FindEdge(edge.fromKey, edge.toKey);
            if (visual != null) return visual;
            Vector2 delta = Position(definition, layout, edge.toKey) - Position(definition, layout, edge.fromKey);
            visual = new TalentTreeEdgeLayout
            {
                fromKey = edge.fromKey, toKey = edge.toKey,
                fromHandle = delta / 3f, toHandle = -delta / 3f
            };
            layout.edges.Add(visual);
            return visual;
        }

        public static TalentTreeAuthorNode InsertNode(TalentTreeDefinition definition, TalentTreeLayout layout,
            int edgeIndex, TalentNodeType type)
        {
            if (definition == null || layout == null || edgeIndex < 0 || edgeIndex >= definition.edges.Count)
                throw new InvalidOperationException("请先选择有表现配置的连线。");
            var old = definition.edges[edgeIndex];
            Vector2 p0 = Position(definition, layout, old.fromKey);
            Vector2 p3 = Position(definition, layout, old.toKey);
            var visual = layout.FindEdge(old.fromKey, old.toKey);
            Vector2 fromHandle = visual != null ? visual.fromHandle : (p3 - p0) / 3f;
            Vector2 toHandle = visual != null ? visual.toHandle : (p0 - p3) / 3f;
            Vector2 p1 = p0 + fromHandle;
            Vector2 p2 = p3 + toHandle;
            Vector2 a = (p0 + p1) * 0.5f;
            Vector2 b = (p1 + p2) * 0.5f;
            Vector2 c = (p2 + p3) * 0.5f;
            Vector2 d = (a + b) * 0.5f;
            Vector2 e = (b + c) * 0.5f;
            Vector2 middle = (d + e) * 0.5f;

            var node = new TalentTreeAuthorNode
            {
                key = Guid.NewGuid().ToString("N"), id = definition.NextId(), type = type
            };
            var first = new TalentTreeAuthorEdge { fromKey = old.fromKey, toKey = node.key };
            var second = new TalentTreeAuthorEdge { fromKey = node.key, toKey = old.toKey };
            definition.nodes.Add(node);
            definition.edges[edgeIndex] = first;
            definition.edges.Insert(edgeIndex + 1, second);
            layout.nodes.Add(new TalentTreeNodeLayout { key = node.key, position = middle });
            int visualIndex = layout.edges.FindIndex(item => item != null &&
                item.fromKey == old.fromKey && item.toKey == old.toKey);
            if (visualIndex >= 0) layout.edges.RemoveAt(visualIndex);
            else visualIndex = layout.edges.Count;
            layout.edges.Insert(visualIndex, new TalentTreeEdgeLayout
            {
                fromKey = first.fromKey, toKey = first.toKey,
                fromHandle = a - p0, toHandle = d - middle
            });
            layout.edges.Insert(visualIndex + 1, new TalentTreeEdgeLayout
            {
                fromKey = second.fromKey, toKey = second.toKey,
                fromHandle = e - middle, toHandle = c - p3
            });
            return node;
        }

        public static int DeleteNodes(TalentTreeDefinition definition, TalentTreeLayout layout,
            HashSet<string> removedKeys)
        {
            if (definition == null || removedKeys == null || removedKeys.Count == 0) return 0;
            var outgoing = new Dictionary<string, List<TalentTreeAuthorEdge>>();
            foreach (var edge in definition.edges)
            {
                if (!outgoing.TryGetValue(edge.fromKey, out var list))
                    outgoing.Add(edge.fromKey, list = new List<TalentTreeAuthorEdge>());
                list.Add(edge);
            }
            var existing = new HashSet<string>();
            foreach (var edge in definition.edges)
                if (!removedKeys.Contains(edge.fromKey) && !removedKeys.Contains(edge.toKey))
                    existing.Add(Pair(edge.fromKey, edge.toKey));
            var bridges = new List<TalentTreeAuthorEdge>();
            var bridgeStyles = new List<TalentTreeEdgeLayout>();
            foreach (var start in definition.edges)
            {
                if (removedKeys.Contains(start.fromKey) || !removedKeys.Contains(start.toKey)) continue;
                var pending = new Stack<(string key, TalentTreeAuthorEdge last)>();
                pending.Push((start.toKey, start));
                var visited = new HashSet<string>();
                while (pending.Count > 0)
                {
                    var current = pending.Pop();
                    if (!visited.Add(current.key) || !outgoing.TryGetValue(current.key, out var next)) continue;
                    foreach (var edge in next)
                    {
                        if (removedKeys.Contains(edge.toKey))
                        {
                            pending.Push((edge.toKey, edge));
                            continue;
                        }
                        if (start.fromKey == edge.toKey || !existing.Add(Pair(start.fromKey, edge.toKey))) continue;
                        var bridge = new TalentTreeAuthorEdge { fromKey = start.fromKey, toKey = edge.toKey };
                        bridges.Add(bridge);
                        if (layout == null) continue;
                        Vector2 startDelta = Position(definition, layout, start.toKey) -
                                             Position(definition, layout, start.fromKey);
                        Vector2 endDelta = Position(definition, layout, edge.fromKey) -
                                           Position(definition, layout, edge.toKey);
                        var firstStyle = layout.FindEdge(start.fromKey, start.toKey);
                        var lastStyle = layout.FindEdge(edge.fromKey, edge.toKey);
                        bridgeStyles.Add(new TalentTreeEdgeLayout
                        {
                            fromKey = bridge.fromKey, toKey = bridge.toKey,
                            fromHandle = firstStyle != null ? firstStyle.fromHandle : startDelta / 3f,
                            toHandle = lastStyle != null ? lastStyle.toHandle : endDelta / 3f
                        });
                    }
                }
            }
            int removed = definition.nodes.RemoveAll(node => removedKeys.Contains(node.key));
            definition.edges.RemoveAll(edge => removedKeys.Contains(edge.fromKey) || removedKeys.Contains(edge.toKey));
            definition.edges.AddRange(bridges);
            if (layout != null)
            {
                layout.nodes.RemoveAll(node => removedKeys.Contains(node.key));
                layout.edges.RemoveAll(edge => removedKeys.Contains(edge.fromKey) || removedKeys.Contains(edge.toKey));
                layout.edges.AddRange(bridgeStyles);
            }
            return removed;
        }

        public static void DeleteEdge(TalentTreeDefinition definition, TalentTreeLayout layout, int edgeIndex)
        {
            var edge = definition.edges[edgeIndex];
            definition.edges.RemoveAt(edgeIndex);
            if (layout != null)
                layout.edges.RemoveAll(item => item.fromKey == edge.fromKey && item.toKey == edge.toKey);
        }

        private static string Pair(string from, string to) => from.Length + ":" + from + to;

        private static Vector2 Position(TalentTreeDefinition definition, TalentTreeLayout layout, string key)
        {
            var visual = layout.FindNode(key);
            if (visual != null) return visual.position;
            var node = definition.FindNode(key);
            if (node == null) throw new InvalidOperationException("连线引用了不存在的节点。");
            return TalentTreeLayoutUtility.SuggestPosition(node.id);
        }
    }
}
