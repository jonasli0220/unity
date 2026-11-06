using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dragon.UI.TalentTree.Editor
{
    [Serializable]
    public sealed class TalentTreeNodeLayout
    {
        public string key;
        public Vector2 position;
        public Sprite icon;
    }

    [Serializable]
    public sealed class TalentTreeEdgeLayout
    {
        public string fromKey;
        public string toKey;
        public Vector2 fromHandle;
        public Vector2 toHandle;
    }

    public sealed class TalentTreeLayout : ScriptableObject
    {
        public TalentTreeDefinition definition;
        [HideInInspector] public float boardSize = 4096f; // Legacy migration value; export sizes itself from content.
        [Min(24f)] public float smallNodeSize = 72f;
        [Min(32f)] public float coreNodeSize = 124f;
        [Min(1f)] public float lineWidth = 5f;
        public Color lineColor = new Color(0.65f, 0.55f, 0.36f, 1f);
        public Color smallFillColor = new Color(0.12f, 0.2f, 0.29f, 1f);
        public Color coreFillColor = new Color(0.25f, 0.18f, 0.24f, 1f);
        public Color nodeRimColor = new Color(0.71f, 0.59f, 0.37f, 1f);
        public Sprite smallFrame;
        public Sprite coreFrame;
        public List<TalentTreeNodeLayout> nodes = new List<TalentTreeNodeLayout>();
        public List<TalentTreeEdgeLayout> edges = new List<TalentTreeEdgeLayout>();

        public TalentTreeNodeLayout FindNode(string key) => nodes.Find(node => node != null && node.key == key);
        public TalentTreeEdgeLayout FindEdge(string fromKey, string toKey) =>
            edges.Find(edge => edge != null && edge.fromKey == fromKey && edge.toKey == toKey);
    }
}
