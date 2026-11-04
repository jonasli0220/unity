using System;
using System.IO;
using System.Linq;
using Dragon.UI.TalentTree;
using UnityEditor;
using UnityEngine;

namespace Dragon.UI.TalentTree.Editor
{
    internal static class TalentTreeExample
    {
        private const string PathToDefinition = "Assets/Content/UI/Editor/TalentTree/Definitions/ExampleTalentTree.asset";
        private const string PathToPrefab = "Assets/Content/UI/Prefab/talent_tree/ExampleTalentTree.prefab";
        private const string PathToJson = "Assets/Content/UI/Prefab/talent_tree/ExampleTalentTree.json";
        private const string StressName = "__TalentTreeStress";

        [MenuItem("Tools/UI/Talent Tree/Create Example Tree", false, 2331)]
        public static void CreateExample()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Content/UI/Editor/TalentTree/Definitions"))
                AssetDatabase.CreateFolder("Assets/Content/UI/Editor/TalentTree", "Definitions");
            if (AssetDatabase.LoadAssetAtPath<TalentTreeDefinition>(PathToDefinition) != null)
            {
                Debug.Log("Talent Tree example already exists: " + PathToDefinition);
                return;
            }
            var definition = ScriptableObject.CreateInstance<TalentTreeDefinition>();
            AddNode(definition, 1, TalentNodeType.Core, Vector2.zero);
            for (int sector = 0; sector < 6; sector++)
            {
                float angle = sector * 60f * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                AddNode(definition, sector + 2, TalentNodeType.Core, direction * 620f);
                AddNode(definition, sector + 8, TalentNodeType.Small, direction * 1220f);
            }
            AddNode(definition, 14, TalentNodeType.Small, new Vector2(710f, 1400f));
            for (int sector = 0; sector < 6; sector++)
            {
                AddEdge(definition, 1, sector + 2);
                AddEdge(definition, sector + 2, sector + 8);
            }
            AddEdge(definition, 8, 14);
            AddEdge(definition, 9, 14);
            var errors = TalentTreeValidator.Validate(definition);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            AssetDatabase.CreateAsset(definition, PathToDefinition);
            AssetDatabase.SaveAssets();
            if (!TalentTreeExporter.Export(definition, out string message)) throw new InvalidOperationException(message);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathToPrefab);
            var data = prefab != null ? prefab.GetComponent<TalentTreeData>() : null;
            if (data == null || data.Nodes.Count != 14 || data.Edges.Count != 14) throw new InvalidOperationException("Example prefab graph is incomplete.");
            if (!data.TryGetNode(14, out var merge) || !merge.prerequisiteIds.SequenceEqual(new[] { 8, 9 }))
                throw new InvalidOperationException("Merge-node AND prerequisites were exported incorrectly.");
            if (!File.Exists(System.IO.Path.GetFullPath(PathToJson))) throw new InvalidOperationException("Example JSON is missing.");
            Debug.Log("Talent Tree example validated: 14 nodes, 14 edges, merge prerequisites [8, 9]. " + message);
        }

        [MenuItem("Tools/UI/Talent Tree/Run Validation Checks", false, 2332)]
        public static void RunValidationChecks()
        {
            var source = AssetDatabase.LoadAssetAtPath<TalentTreeDefinition>(PathToDefinition);
            if (source == null) throw new InvalidOperationException("Create Example Tree first.");
            if (TalentTreeValidator.Validate(source).Count != 0) throw new InvalidOperationException("Valid example was rejected.");

            var duplicate = UnityEngine.Object.Instantiate(source);
            try
            {
                duplicate.nodes[1].id = duplicate.nodes[0].id;
                if (!TalentTreeValidator.Validate(duplicate).Any(error => error.Contains("重复节点 ID")))
                    throw new InvalidOperationException("Duplicate ID was not rejected.");
            }
            finally { UnityEngine.Object.DestroyImmediate(duplicate); }

            var cycle = UnityEngine.Object.Instantiate(source);
            try
            {
                AddEdge(cycle, 14, 1);
                if (!TalentTreeValidator.Validate(cycle).Any(error => error.Contains("存在环")))
                    throw new InvalidOperationException("Cycle was not rejected.");
            }
            finally { UnityEngine.Object.DestroyImmediate(cycle); }
            Debug.Log("Talent Tree validation checks passed: valid graph, duplicate ID, directed cycle.");
        }

        [MenuItem("Tools/UI/Talent Tree/Run 1000 Node Stress Check", false, 2333)]
        public static void RunStressCheck()
        {
            string definitionPath = "Assets/Content/UI/Editor/TalentTree/Definitions/" + StressName + ".asset";
            string prefabPath = "Assets/Content/UI/Prefab/talent_tree/" + StressName + ".prefab";
            string jsonPath = "Assets/Content/UI/Prefab/talent_tree/" + StressName + ".json";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(definitionPath) != null ||
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath) != null || File.Exists(System.IO.Path.GetFullPath(jsonPath)))
                throw new InvalidOperationException("Stress-check paths already exist; remove them manually after inspection.");

            var definition = ScriptableObject.CreateInstance<TalentTreeDefinition>();
            for (int id = 1; id <= 1000; id++)
            {
                float angle = id * 2.39996323f;
                float radius = 80f + 1800f * Mathf.Sqrt(id / 1000f);
                AddNode(definition, id, id % 50 == 0 ? TalentNodeType.Core : TalentNodeType.Small,
                    new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius);
                if (id > 1) AddEdge(definition, id - 1, id);
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                AssetDatabase.CreateAsset(definition, definitionPath);
                if (!TalentTreeExporter.Export(definition, out string message)) throw new InvalidOperationException(message);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                var data = prefab != null ? prefab.GetComponent<TalentTreeData>() : null;
                int chunks = prefab != null ? prefab.GetComponentsInChildren<TalentTreeCurveGraphic>(true).Length : 0;
                if (data == null || data.Nodes.Count != 1000 || data.Edges.Count != 999 || chunks != 4)
                    throw new InvalidOperationException("Stress-check prefab graph or curve chunks are incomplete.");
                timer.Stop();
                Debug.Log("Talent Tree 1000-node stress check passed: 1000 nodes, 999 edges, 4 curve chunks, " + timer.ElapsedMilliseconds + " ms.");
            }
            finally
            {
                AssetDatabase.DeleteAsset(prefabPath);
                AssetDatabase.DeleteAsset(jsonPath);
                AssetDatabase.DeleteAsset(definitionPath);
                AssetDatabase.Refresh();
            }
        }

        private static void AddNode(TalentTreeDefinition definition, int id, TalentNodeType type, Vector2 position)
        {
            definition.nodes.Add(new TalentTreeAuthorNode { key = "example-" + id, id = id, type = type, position = position });
        }

        private static void AddEdge(TalentTreeDefinition definition, int fromId, int toId)
        {
            var from = definition.nodes.Find(node => node.id == fromId);
            var to = definition.nodes.Find(node => node.id == toId);
            Vector2 delta = to.position - from.position;
            definition.edges.Add(new TalentTreeAuthorEdge { fromKey = from.key, toKey = to.key, fromHandle = delta / 3f, toHandle = -delta / 3f });
        }
    }
}
