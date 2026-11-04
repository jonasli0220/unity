using System;
using System.IO;
using System.Linq;
using Dragon.UI.TalentTree;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dragon.UI.TalentTree.Editor
{
    internal static class TalentTreeExample
    {
        private const string PathToDefinition = "Assets/Content/UI/Editor/TalentTree/Definitions/ExampleTalentTree.asset";
        private const string PathToLayout = "Assets/Content/UI/Editor/TalentTree/Layouts/ExampleTalentTreeLayout.asset";
        private const string PathToPrefab = "Assets/Content/UI/Prefab/talent_tree/ExampleTalentTree.prefab";
        private const string PathToJson = "Assets/Content/UI/Prefab/talent_tree/ExampleTalentTree.json";
        private const string StressName = "__TalentTreeStress";

        [MenuItem("Tools/UI/Talent Tree/Create Example Tree", false, 2331)]
        public static void CreateExample()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Content/UI/Editor/TalentTree/Definitions"))
                AssetDatabase.CreateFolder("Assets/Content/UI/Editor/TalentTree", "Definitions");
            if (!AssetDatabase.IsValidFolder("Assets/Content/UI/Editor/TalentTree/Layouts"))
                AssetDatabase.CreateFolder("Assets/Content/UI/Editor/TalentTree", "Layouts");
            if (AssetDatabase.LoadAssetAtPath<TalentTreeDefinition>(PathToDefinition) != null)
            {
                RebuildExample();
                return;
            }

            var definition = ScriptableObject.CreateInstance<TalentTreeDefinition>();
            var layout = ScriptableObject.CreateInstance<TalentTreeLayout>();
            layout.definition = definition;
            AddNode(definition, layout, 1, TalentNodeType.Core, Vector2.zero);
            for (int sector = 0; sector < 6; sector++)
            {
                float angle = sector * 60f * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                AddNode(definition, layout, sector + 2, TalentNodeType.Core, direction * 620f);
                AddNode(definition, layout, sector + 8, TalentNodeType.Small, direction * 1220f);
            }
            AddNode(definition, layout, 14, TalentNodeType.Small, new Vector2(710f, 1400f));
            for (int sector = 0; sector < 6; sector++)
            {
                AddEdge(definition, layout, 1, sector + 2);
                AddEdge(definition, layout, sector + 2, sector + 8);
            }
            AddEdge(definition, layout, 8, 14);
            AddEdge(definition, layout, 9, 14);
            var errors = TalentTreeValidator.Validate(definition, layout);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            AssetDatabase.CreateAsset(definition, PathToDefinition);
            AssetDatabase.CreateAsset(layout, PathToLayout);
            AssetDatabase.SaveAssets();
            RebuildExample();
        }

        [MenuItem("Tools/UI/Talent Tree/Rebuild Example Prefab", false, 2334)]
        public static void RebuildExample()
        {
            var definition = AssetDatabase.LoadAssetAtPath<TalentTreeDefinition>(PathToDefinition);
            if (definition == null) throw new InvalidOperationException("Create Example Tree first.");
            var layout = TalentTreeLayoutUtility.Find(definition) ?? TalentTreeLayoutUtility.Create(definition);
            if (!TalentTreeExporter.Export(definition, layout, out string message)) throw new InvalidOperationException(message);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathToPrefab);
            var data = prefab != null ? prefab.GetComponent<TalentTreeData>() : null;
            if (data == null || data.Nodes.Count != 14 || data.Edges.Count != 14)
                throw new InvalidOperationException("Example prefab graph is incomplete.");
            if (!data.TryGetNode(14, out var merge) || !merge.prerequisiteIds.SequenceEqual(new[] { 8, 9 }))
                throw new InvalidOperationException("Merge-node AND prerequisites were exported incorrectly.");
            if (Vector2.Distance(merge.position, layout.FindNode("example-14").position) > 0.01f)
                throw new InvalidOperationException("Example layout position was not merged into the prefab.");
            var missingRenderer = prefab.GetComponentsInChildren<Graphic>(true)
                .FirstOrDefault(graphic => graphic.GetComponent<CanvasRenderer>() == null);
            if (missingRenderer != null) throw new InvalidOperationException("Missing CanvasRenderer: " + missingRenderer.gameObject.name);
            if (!File.Exists(Path.GetFullPath(PathToJson))) throw new InvalidOperationException("Example JSON is missing.");
            Debug.Log("Talent Tree example validated: 14 nodes, 14 edges, merged UI layout, all Graphics have CanvasRenderer. " + message);
        }

        [MenuItem("Tools/UI/Talent Tree/Run Validation Checks", false, 2332)]
        public static void RunValidationChecks()
        {
            var source = AssetDatabase.LoadAssetAtPath<TalentTreeDefinition>(PathToDefinition);
            if (source == null) throw new InvalidOperationException("Create Example Tree first.");
            var layout = TalentTreeLayoutUtility.Find(source) ?? TalentTreeLayoutUtility.Create(source);
            if (TalentTreeValidator.Validate(source, layout).Count != 0) throw new InvalidOperationException("Valid example was rejected.");

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
                var from = cycle.nodes.Find(node => node.id == 14);
                var to = cycle.nodes.Find(node => node.id == 1);
                cycle.edges.Add(new TalentTreeAuthorEdge { fromKey = from.key, toKey = to.key });
                if (!TalentTreeValidator.Validate(cycle).Any(error => error.Contains("存在环")))
                    throw new InvalidOperationException("Cycle was not rejected.");
            }
            finally { UnityEngine.Object.DestroyImmediate(cycle); }

            var incomplete = UnityEngine.Object.Instantiate(layout);
            try
            {
                incomplete.nodes.RemoveAt(0);
                if (!TalentTreeValidator.Validate(source, incomplete).Any(error => error.Contains("缺少 1 个节点布局")))
                    throw new InvalidOperationException("Missing UI layout was not rejected.");
                string before = File.ReadAllText(Path.GetFullPath(PathToJson));
                if (TalentTreeExporter.Export(source, incomplete, out _))
                    throw new InvalidOperationException("Incomplete UI layout was exported.");
                if (File.ReadAllText(Path.GetFullPath(PathToJson)) != before)
                    throw new InvalidOperationException("Failed export changed the generated JSON.");
            }
            finally { UnityEngine.Object.DestroyImmediate(incomplete); }
            Debug.Log("Talent Tree validation checks passed: valid pair, duplicate ID, directed cycle, missing UI layout, unchanged output after failed export.");
        }

        [MenuItem("Tools/UI/Talent Tree/Run 1000 Node Stress Check", false, 2333)]
        public static void RunStressCheck()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Content/UI/Editor/TalentTree/Definitions"))
                AssetDatabase.CreateFolder("Assets/Content/UI/Editor/TalentTree", "Definitions");
            if (!AssetDatabase.IsValidFolder("Assets/Content/UI/Editor/TalentTree/Layouts"))
                AssetDatabase.CreateFolder("Assets/Content/UI/Editor/TalentTree", "Layouts");
            string definitionPath = "Assets/Content/UI/Editor/TalentTree/Definitions/" + StressName + ".asset";
            string layoutPath = "Assets/Content/UI/Editor/TalentTree/Layouts/" + StressName + "Layout.asset";
            string prefabPath = "Assets/Content/UI/Prefab/talent_tree/" + StressName + ".prefab";
            string jsonPath = "Assets/Content/UI/Prefab/talent_tree/" + StressName + ".json";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(definitionPath) != null ||
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(layoutPath) != null ||
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath) != null || File.Exists(Path.GetFullPath(jsonPath)))
                throw new InvalidOperationException("Stress-check paths already exist; remove them manually after inspection.");

            var definition = ScriptableObject.CreateInstance<TalentTreeDefinition>();
            var layout = ScriptableObject.CreateInstance<TalentTreeLayout>();
            layout.definition = definition;
            for (int id = 1; id <= 1000; id++)
            {
                float angle = id * 2.39996323f;
                float radius = 80f + 1800f * Mathf.Sqrt(id / 1000f);
                AddNode(definition, layout, id, id % 50 == 0 ? TalentNodeType.Core : TalentNodeType.Small,
                    new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius);
                if (id > 1) AddEdge(definition, layout, id - 1, id);
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                AssetDatabase.CreateAsset(definition, definitionPath);
                AssetDatabase.CreateAsset(layout, layoutPath);
                if (!TalentTreeExporter.Export(definition, layout, out string message)) throw new InvalidOperationException(message);
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
                AssetDatabase.DeleteAsset(layoutPath);
                AssetDatabase.DeleteAsset(definitionPath);
                AssetDatabase.Refresh();
            }
        }

        private static void AddNode(TalentTreeDefinition definition, TalentTreeLayout layout, int id, TalentNodeType type, Vector2 position)
        {
            string key = "example-" + id;
            definition.nodes.Add(new TalentTreeAuthorNode { key = key, id = id, type = type });
            layout.nodes.Add(new TalentTreeNodeLayout { key = key, position = position });
        }

        private static void AddEdge(TalentTreeDefinition definition, TalentTreeLayout layout, int fromId, int toId)
        {
            var from = definition.nodes.Find(node => node.id == fromId);
            var to = definition.nodes.Find(node => node.id == toId);
            Vector2 delta = layout.FindNode(to.key).position - layout.FindNode(from.key).position;
            definition.edges.Add(new TalentTreeAuthorEdge { fromKey = from.key, toKey = to.key });
            layout.edges.Add(new TalentTreeEdgeLayout { fromKey = from.key, toKey = to.key,
                fromHandle = delta / 3f, toHandle = -delta / 3f });
        }
    }
}
