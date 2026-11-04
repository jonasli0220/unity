using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dragon.UI.TalentTree
{
    public enum TalentNodeType { Small, Core }

    [Serializable]
    public sealed class TalentTreeNodeRecord
    {
        public int id;
        public TalentNodeType type;
        public Vector2 position;
        public int[] prerequisiteIds = Array.Empty<int>();
        public Sprite icon;
        public RectTransform view;
    }

    [Serializable]
    public sealed class TalentTreeEdgeRecord
    {
        public int fromId;
        public int toId;
        public Vector2 fromHandle;
        public Vector2 toHandle;
    }

    [DisallowMultipleComponent]
    public sealed class TalentTreeData : MonoBehaviour
    {
        [SerializeField] private string sourceGuid;
        [SerializeField] private float boardSize = 4096f;
        [SerializeField] private TalentTreeNodeRecord[] nodes = Array.Empty<TalentTreeNodeRecord>();
        [SerializeField] private TalentTreeEdgeRecord[] edges = Array.Empty<TalentTreeEdgeRecord>();

        public string SourceGuid => sourceGuid;
        public float BoardSize => boardSize;
        public IReadOnlyList<TalentTreeNodeRecord> Nodes => nodes;
        public IReadOnlyList<TalentTreeEdgeRecord> Edges => edges;

        public bool TryGetNode(int id, out TalentTreeNodeRecord node)
        {
            foreach (var candidate in nodes)
            {
                if (candidate.id != id) continue;
                node = candidate;
                return true;
            }
            node = null;
            return false;
        }

        public void SetGeneratedData(string guid, float size, TalentTreeNodeRecord[] nodeData, TalentTreeEdgeRecord[] edgeData)
        {
            sourceGuid = guid;
            boardSize = size;
            nodes = nodeData ?? Array.Empty<TalentTreeNodeRecord>();
            edges = edgeData ?? Array.Empty<TalentTreeEdgeRecord>();
        }
    }
}
