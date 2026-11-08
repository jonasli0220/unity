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
        [Serializable] private sealed class ExportSummary
        {
            public int version;
            public Vector2 contentSize;
            public Vector2 contentCenter;
            public ExportNodeSummary[] nodes;
            public ExportEdgeSummary[] edges;
            public ExportTileSummary[] inactiveTiles;
            public string[] generatedAssets;
        }

        [Serializable] private sealed class ExportNodeSummary
        {
            public int id;
            public string type;
            public Vector2 position;
            public float size;
            public int[] prerequisiteIds;
        }

        [Serializable] private sealed class ExportEdgeSummary
        {
            public int fromId;
            public int toId;
            public Vector2 activePosition;
            public Vector2 activeSize;
            public string activeAtlasPath;
            public string activeSpriteName;
            public float activePixelsPerUnit;
        }

        [Serializable] private sealed class ExportTileSummary
        {
            public int x;
            public int y;
            public Vector2 position;
            public Vector2 size;
            public string assetPath;
        }

        private const string PathToDefinition = "Assets/Content/UI/Editor/TalentTree/Definitions/ExampleTalentTree.asset";
        private const string PathToLayout = "Assets/Content/UI/Editor/TalentTree/Layouts/ExampleTalentTreeLayout.asset";
        private const string PathToPrefab = "Assets/Content/UI/Prefab/talent_tree/ExampleTalentTree.prefab";
        private const string PathToJson = "Assets/Content/UI/Prefab/talent_tree/ExampleTalentTree.json";
        private const string StressName = "__TalentTreeStress";
        private const string GraphCheckName = "__TalentTreeGraphEditCheck";

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
            var json = JsonUtility.FromJson<ExportSummary>(File.ReadAllText(Path.GetFullPath(PathToJson)));
            if (prefab == null || json == null || json.nodes == null || json.edges == null ||
                json.nodes.Length != 14 || json.edges.Length != 14)
                throw new InvalidOperationException("Example prefab or JSON graph is incomplete.");
            var merge = json.nodes.First(node => node.id == 14);
            if (!merge.prerequisiteIds.SequenceEqual(new[] { 8, 9 }))
                throw new InvalidOperationException("Merge-node AND prerequisites were exported incorrectly.");
            if (json.nodes.Any(node => Mathf.Abs(node.size -
                (node.type == nameof(TalentNodeType.Core) ? layout.coreNodeSize : layout.smallNodeSize)) > 0.01f))
                throw new InvalidOperationException("Exported node sizes differ from the authoring layout.");
            var bounds = TalentTreeBoundsUtility.CalculateOriginCentered(definition, layout);
            if (Vector2.Distance(merge.position, layout.FindNode("example-14").position) > 0.01f)
                throw new InvalidOperationException("Example layout position was not merged into the prefab.");
            var content = prefab.transform.Find("Content") as RectTransform;
            if (content == null || Vector2.Distance(content.sizeDelta, bounds.Size) > 0.01f ||
                Vector2.Distance(json.contentSize, bounds.Size) > 0.01f ||
                json.contentCenter != Vector2.zero || content.anchoredPosition != Vector2.zero ||
                content.pivot != new Vector2(0.5f, 0.5f))
                throw new InvalidOperationException("Example prefab did not keep the authored origin centered.");
            AssertPatchSafe(prefab, json);
            var missingRenderer = prefab.GetComponentsInChildren<Graphic>(true)
                .FirstOrDefault(graphic => graphic.GetComponent<CanvasRenderer>() == null);
            if (missingRenderer != null) throw new InvalidOperationException("Missing CanvasRenderer: " + missingRenderer.gameObject.name);
            if (json == null || json.version != 6 || Vector2.Distance(json.contentSize, bounds.Size) > 0.01f ||
                json.contentCenter != Vector2.zero || json.nodes == null ||
                Vector2.Distance(json.nodes.First(node => node.id == 14).position, layout.FindNode("example-14").position) > 0.01f)
                throw new InvalidOperationException("Example JSON crop metadata is incomplete.");
            Debug.Log("Talent Tree example validated: 14 nodes, 14 edges, merged UI layout, all Graphics have CanvasRenderer. " + message);
        }

        [MenuItem("Tools/UI/Talent Tree/Run Validation Checks", false, 2332)]
        public static void RunValidationChecks()
        {
            var source = AssetDatabase.LoadAssetAtPath<TalentTreeDefinition>(PathToDefinition);
            if (source == null) throw new InvalidOperationException("Create Example Tree first.");
            var layout = TalentTreeLayoutUtility.Find(source) ?? TalentTreeLayoutUtility.Create(source);
            if (TalentTreeValidator.Validate(source, layout).Count != 0) throw new InvalidOperationException("Valid example was rejected.");

            var translated = UnityEngine.Object.Instantiate(layout);
            try
            {
                Vector2 offset = new Vector2(10000f, -20000f);
                var originalBounds = TalentTreeBoundsUtility.Calculate(source, layout);
                foreach (var node in translated.nodes) node.position += offset;
                if (TalentTreeValidator.Validate(source, translated).Count != 0)
                    throw new InvalidOperationException("Finite nodes outside the old board were rejected.");
                var translatedBounds = TalentTreeBoundsUtility.Calculate(source, translated);
                if (Vector2.Distance(translatedBounds.Center, originalBounds.Center + offset) > 0.05f ||
                    Vector2.Distance(translatedBounds.Size, originalBounds.Size) > 0.05f)
                    throw new InvalidOperationException("Automatic content crop depends on the old board origin.");
            }
            finally { UnityEngine.Object.DestroyImmediate(translated); }

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
                if (!TalentTreeValidator.Validate(source, incomplete).Any(error => error.Contains("缺少 1 个节点位置")))
                    throw new InvalidOperationException("Missing UI layout was not rejected.");
                string before = File.ReadAllText(Path.GetFullPath(PathToJson));
                if (TalentTreeExporter.Export(source, incomplete, out _))
                    throw new InvalidOperationException("Incomplete UI layout was exported.");
                if (File.ReadAllText(Path.GetFullPath(PathToJson)) != before)
                    throw new InvalidOperationException("Failed export changed the generated JSON.");
            }
            finally { UnityEngine.Object.DestroyImmediate(incomplete); }
            Debug.Log("Talent Tree validation checks passed: valid pair, unbounded coordinates, translation-invariant crop, duplicate ID, directed cycle, missing UI layout, unchanged output after failed export.");
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
            string spritesPath = "Assets/Content/UI/Prefab/talent_tree/" + StressName + "_Sprites";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(definitionPath) != null ||
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(layoutPath) != null ||
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath) != null ||
                File.Exists(Path.GetFullPath(jsonPath)) || AssetDatabase.IsValidFolder(spritesPath))
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
                var json = JsonUtility.FromJson<ExportSummary>(File.ReadAllText(Path.GetFullPath(jsonPath)));
                if (prefab == null || json == null || json.nodes.Length != 1000 || json.edges.Length != 999 ||
                    json.version != 6 || prefab.GetComponentsInChildren<UILineRenderer>(true).Length != 0)
                    throw new InvalidOperationException("Stress-check prefab graph or baked sprites are incomplete.");
                AssertPatchSafe(prefab, json);
                timer.Stop();
                Debug.Log("Talent Tree 1000-node stress check passed: 1000 JSON nodes, 999 JSON edges, baked base tiles, " + timer.ElapsedMilliseconds + " ms.");
            }
            finally
            {
                AssetDatabase.DeleteAsset(prefabPath);
                AssetDatabase.DeleteAsset(jsonPath);
                AssetDatabase.DeleteAsset(spritesPath);
                AssetDatabase.DeleteAsset(layoutPath);
                AssetDatabase.DeleteAsset(definitionPath);
                AssetDatabase.Refresh();
            }
        }

        [MenuItem("Tools/UI/Talent Tree/Run Graph Edit Checks", false, 2335)]
        public static void RunGraphEditChecks()
        {
            string definitionPath = "Assets/Content/UI/Editor/TalentTree/Definitions/" + GraphCheckName + ".asset";
            string layoutPath = "Assets/Content/UI/Editor/TalentTree/Layouts/" + GraphCheckName + "Layout.asset";
            string prefabPath = "Assets/Content/UI/Prefab/talent_tree/" + GraphCheckName + ".prefab";
            string jsonPath = "Assets/Content/UI/Prefab/talent_tree/" + GraphCheckName + ".json";
            string spritesPath = "Assets/Content/UI/Prefab/talent_tree/" + GraphCheckName + "_Sprites";
            string resultPath = "Library/Dragon/TalentTree/GraphEditCheck.result";
            foreach (string path in new[] { definitionPath, layoutPath, prefabPath, jsonPath })
                if (File.Exists(Path.GetFullPath(path)))
                    throw new InvalidOperationException("临时测试路径已存在，请先检查：" + path);
            if (AssetDatabase.IsValidFolder(spritesPath))
                throw new InvalidOperationException("临时测试图片目录已存在，请先检查：" + spritesPath);
            if (File.Exists(resultPath)) File.Delete(resultPath);
            var definition = ScriptableObject.CreateInstance<TalentTreeDefinition>();
            var layout = ScriptableObject.CreateInstance<TalentTreeLayout>();
            layout.definition = definition;
            bool definitionSaved = false;
            bool layoutSaved = false;
            try
            {
                AddNode(definition, layout, 1, TalentNodeType.Small, new Vector2(0f, 0f));
                AddNode(definition, layout, 2, TalentNodeType.Small, new Vector2(300f, 100f));
                AddNode(definition, layout, 3, TalentNodeType.Core, new Vector2(600f, 0f));
                AddNode(definition, layout, 4, TalentNodeType.Small, new Vector2(0f, 400f));
                AddNode(definition, layout, 5, TalentNodeType.Small, new Vector2(650f, 350f));
                AddEdge(definition, layout, 1, 2);
                AddEdge(definition, layout, 4, 2);
                AddEdge(definition, layout, 2, 3);
                AddEdge(definition, layout, 2, 5);
                AddEdge(definition, layout, 1, 3); // Existing shortcut must not be duplicated on deletion.
                var original = layout.edges[0];
                original.fromHandle = new Vector2(140f, 210f);
                original.toHandle = new Vector2(-110f, 160f);
                Vector2 p0 = layout.FindNode(original.fromKey).position;
                Vector2 p3 = layout.FindNode(original.toKey).position;
                Vector2 p1 = p0 + original.fromHandle;
                Vector2 p2 = p3 + original.toHandle;
                var inserted = TalentTreeGraphEdit.InsertNode(definition, layout, 0, TalentNodeType.Small);
                if (definition.edges.Count != 6 || definition.edges[0].toKey != inserted.key ||
                    definition.edges[1].fromKey != inserted.key || definition.edges[1].toKey != "example-2")
                    throw new InvalidOperationException("插入节点后的有向连线不正确。");
                for (int step = 0; step <= 20; step++)
                {
                    float t = step / 20f;
                    var newEdge = layout.edges[t <= 0.5f ? 0 : 1];
                    Vector2 from = layout.FindNode(newEdge.fromKey).position;
                    Vector2 to = layout.FindNode(newEdge.toKey).position;
                    float localT = t <= 0.5f ? t * 2f : (t - 0.5f) * 2f;
                    Vector2 actual = CurvePoint(from, from + newEdge.fromHandle, to + newEdge.toHandle, to, localT);
                    if (Vector2.Distance(actual, CurvePoint(p0, p1, p2, p3, t)) > 0.02f)
                        throw new InvalidOperationException("拆线后曲线形状发生变化。");
                }
                if (TalentTreeValidator.Validate(definition, layout).Count != 0)
                    throw new InvalidOperationException("插入节点后的图无效。");
                TalentTreeGraphEdit.DeleteNodes(definition, layout, new System.Collections.Generic.HashSet<string>
                    { inserted.key, "example-2" });
                string[] pairs = { "example-1>example-3", "example-1>example-5",
                    "example-4>example-3", "example-4>example-5" };
                foreach (string pair in pairs)
                    if (!definition.edges.Any(edge => edge.fromKey + ">" + edge.toKey == pair))
                        throw new InvalidOperationException("删除节点后缺少前后补连：" + pair);
                if (definition.edges.Count != 4 || layout.edges.Count != 4 ||
                    layout.nodes.Any(node => node.key == inserted.key || node.key == "example-2") ||
                    TalentTreeValidator.Validate(definition, layout).Count != 0)
                    throw new InvalidOperationException("批量删除后存在重复或遗留数据。");
                var testEdge = definition.edges[0];
                layout.edges.RemoveAll(edge => edge.fromKey == testEdge.fromKey && edge.toKey == testEdge.toKey);
                TalentTreeGraphEdit.EnsureEdgeLayout(definition, layout, testEdge);
                if (TalentTreeValidator.Validate(definition, layout).Count != 0)
                    throw new InvalidOperationException("旧连线自动补全曲线失败。");

                AssetDatabase.CreateAsset(definition, definitionPath);
                definitionSaved = true;
                AssetDatabase.CreateAsset(layout, layoutPath);
                layoutSaved = true;
                if (!TalentTreeExporter.Export(definition, layout, out string message))
                    throw new InvalidOperationException(message);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                var json = JsonUtility.FromJson<ExportSummary>(File.ReadAllText(Path.GetFullPath(jsonPath)));
                if (prefab == null || json == null || json.nodes.Length != 4 || json.edges.Length != 4 ||
                    json.contentSize.x < 256f || json.contentSize.y < 256f ||
                    json.contentCenter != Vector2.zero)
                    throw new InvalidOperationException("临时树导出的 Prefab 或 JSON 数据不正确。");
                AssertPatchSafe(prefab, json);
                foreach (var node in json.nodes)
                {
                    Vector2 authored = layout.FindNode("example-" + node.id).position;
                    if (Vector2.Distance(node.position, authored) > 0.01f)
                        throw new InvalidOperationException("JSON 节点坐标与编辑器不一致：" + node.id);
                }
                if (json.version != 6 ||
                    json.contentCenter != Vector2.zero || json.nodes == null ||
                    json.nodes.Any(node => Vector2.Distance(node.position, layout.FindNode("example-" + node.id).position) > 0.01f))
                    throw new InvalidOperationException("临时树 JSON 画布数据不正确。");
                int styledFromId = json.edges[0].fromId;
                int styledToId = json.edges[0].toId;
                Vector2 originalPosition = json.edges[0].activePosition;
                string originalAtlasGuid = AssetDatabase.AssetPathToGUID(json.edges[0].activeAtlasPath);
                var changedEdge = definition.edges.Find(edge =>
                    definition.FindNode(edge.fromKey).id == styledFromId &&
                    definition.FindNode(edge.toKey).id == styledToId);
                layout.FindNode(changedEdge.fromKey).position += new Vector2(41f, -17f);
                layout.FindEdge(changedEdge.fromKey, changedEdge.toKey).fromHandle += new Vector2(10f, 20f);
                if (!TalentTreeExporter.Export(definition, layout, out string updatedMessage))
                    throw new InvalidOperationException(updatedMessage);
                var updated = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                var updatedJson = JsonUtility.FromJson<ExportSummary>(File.ReadAllText(Path.GetFullPath(jsonPath)));
                var updatedRecord = updatedJson.edges.First(edge => edge.fromId == styledFromId && edge.toId == styledToId);
                if (updated == null || Vector2.Distance(originalPosition, updatedRecord.activePosition) < 0.01f ||
                    (updatedRecord.activeAtlasPath == json.edges[0].activeAtlasPath &&
                     AssetDatabase.AssetPathToGUID(updatedRecord.activeAtlasPath) != originalAtlasGuid))
                    throw new InvalidOperationException("重导出连线检查失败：原位置=" + originalPosition +
                        " 新位置=" + updatedRecord.activePosition +
                        " 原图集 GUID=" + originalAtlasGuid + " 新图集 GUID=" +
                        AssetDatabase.AssetPathToGUID(updatedRecord.activeAtlasPath) +
                        " 原图集=" + json.edges[0].activeAtlasPath +
                        " 新图集=" + updatedRecord.activeAtlasPath);
                AssertPatchSafe(updated, updatedJson);
                Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
                File.WriteAllText(resultPath, "PASS: graph edits, baked sprites, stable atlas GUID, editor-owned geometry, cropped prefab and JSON");
                Debug.Log("Talent Tree graph edit checks passed. " + message);
            }
            finally
            {
                foreach (string path in new[] { prefabPath, jsonPath, layoutPath, definitionPath })
                    if (File.Exists(Path.GetFullPath(path))) AssetDatabase.DeleteAsset(path);
                AssetDatabase.DeleteAsset(spritesPath);
                if (!layoutSaved) UnityEngine.Object.DestroyImmediate(layout);
                if (!definitionSaved) UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        private static Vector2 CurvePoint(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        private static void AssertPatchSafe(GameObject prefab, ExportSummary json)
        {
            if (prefab.GetComponent<ScrollRect>() == null ||
                prefab.GetComponentsInChildren<RectMask2D>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Mask>(true).Length != 0 ||
                prefab.GetComponent<TalentTreeData>() != null ||
                prefab.GetComponentsInChildren<UILineRenderer>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Graphic>(true).Any(graphic => graphic.GetComponent<CanvasRenderer>() == null) ||
                prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(component =>
                    component == null || !(component is Image ||
                        component is ScrollRect)))
                throw new InvalidOperationException("Prefab 含有裁切组件、未随包体发布的脚本或缺少必要 UGUI 组件。");
            if (json.version != 6 || json.inactiveTiles == null || json.generatedAssets == null)
                throw new InvalidOperationException("缺少 Sprite 烘焙清单。");
            if (json.nodes == null || json.nodes.Any(node =>
                node.size <= 0f || float.IsNaN(node.size) || float.IsInfinity(node.size)))
                throw new InvalidOperationException("JSON 节点缺少有效的编辑器显示尺寸。");
            var nodeLayer = prefab.transform.Find("Content/Nodes");
            var edgeLayer = prefab.transform.Find("Content/ActiveEdges");
            var tileLayer = prefab.transform.Find("Content/InactiveTiles");
            if (nodeLayer == null || edgeLayer == null || tileLayer == null ||
                nodeLayer.childCount != 0 || edgeLayer.childCount != 0 ||
                tileLayer.childCount != json.inactiveTiles.Length ||
                prefab.GetComponentsInChildren<RectTransform>(true).Length != json.inactiveTiles.Length + 5)
                throw new InvalidOperationException("Prefab 仍含有逐节点或逐连线空对象。");
            foreach (string path in json.generatedAssets)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || importer.textureType != TextureImporterType.Sprite ||
                    importer.mipmapEnabled ||
                    !importer.GetPlatformTextureSettings("Android").overridden ||
                    !importer.GetPlatformTextureSettings("Standalone").overridden)
                    throw new InvalidOperationException("烘焙图片缺少 Sprite 或平台压缩设置：" + path);
            }
            var spritesByAtlas = new System.Collections.Generic.Dictionary<string,
                System.Collections.Generic.HashSet<string>>();
            foreach (var edge in json.edges)
            {
                if (!spritesByAtlas.TryGetValue(edge.activeAtlasPath, out var spriteNames))
                {
                    spriteNames = new System.Collections.Generic.HashSet<string>(
                        AssetDatabase.LoadAllAssetsAtPath(edge.activeAtlasPath).OfType<Sprite>()
                            .Select(sprite => sprite.name));
                    spritesByAtlas[edge.activeAtlasPath] = spriteNames;
                }
                if (edge.activeSize.x <= 0f || edge.activeSize.y <= 0f ||
                    edge.activePixelsPerUnit <= 0f ||
                    !json.generatedAssets.Contains(edge.activeAtlasPath) ||
                    !spriteNames.Contains(edge.activeSpriteName))
                    throw new InvalidOperationException("激活连线 JSON 坐标或图集子 Sprite 不正确：" + edge.fromId + " to " + edge.toId);
            }
            foreach (var tile in json.inactiveTiles)
            {
                var view = prefab.transform.Find("Content/InactiveTiles/Tile " + tile.x + "," + tile.y)
                    as RectTransform;
                var image = view != null ? view.GetComponent<Image>() : null;
                if (view == null || image == null || image.sprite == null ||
                    AssetDatabase.GetAssetPath(image.sprite) != tile.assetPath ||
                    Vector2.Distance(view.anchoredPosition, tile.position) > 0.01f ||
                    Vector2.Distance(view.sizeDelta, tile.size) > 0.01f)
                    throw new InvalidOperationException("未激活线条底图坐标或 Sprite 不正确：" + tile.assetPath);
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
