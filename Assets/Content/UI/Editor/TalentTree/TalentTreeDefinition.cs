using System;
using System.Collections.Generic;
using Dragon.UI.TalentTree;
using UnityEngine;

namespace Dragon.UI.TalentTree.Editor
{
    [Serializable]
    public sealed class TalentTreeAuthorNode
    {
        public string key;
        public int id;
        public TalentNodeType type;
        [HideInInspector] // Kept only to migrate existing combined assets.
        public Vector2 position;
        [HideInInspector]
        public Sprite icon;
    }

    [Serializable]
    public sealed class TalentTreeAuthorEdge
    {
        public string fromKey;
        public string toKey;
        [HideInInspector]
        public Vector2 fromHandle;
        [HideInInspector]
        public Vector2 toHandle;
    }

    public sealed class TalentTreeDefinition : ScriptableObject
    {
        [HideInInspector] public float boardSize = 4096f;
        [HideInInspector] public float smallNodeSize = 72f;
        [HideInInspector] public float coreNodeSize = 124f;
        [HideInInspector] public float lineWidth = 5f;
        [HideInInspector]
        public Color lineColor = new Color(0.65f, 0.55f, 0.36f, 1f);
        [HideInInspector]
        public Color smallFillColor = new Color(0.12f, 0.2f, 0.29f, 1f);
        [HideInInspector]
        public Color coreFillColor = new Color(0.25f, 0.18f, 0.24f, 1f);
        [HideInInspector]
        public Color nodeRimColor = new Color(0.71f, 0.59f, 0.37f, 1f);
        [HideInInspector]
        public Sprite smallFrame;
        [HideInInspector]
        public Sprite coreFrame;
        public List<TalentTreeAuthorNode> nodes = new List<TalentTreeAuthorNode>();
        public List<TalentTreeAuthorEdge> edges = new List<TalentTreeAuthorEdge>();

        public TalentTreeAuthorNode FindNode(string key) => nodes.Find(node => node.key == key);

        public int NextId()
        {
            var used = new HashSet<int>();
            foreach (var node in nodes) used.Add(node.id);
            int candidate = 1;
            while (used.Contains(candidate)) candidate++;
            return candidate;
        }
    }
}
