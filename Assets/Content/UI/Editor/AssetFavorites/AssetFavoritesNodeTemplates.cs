using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class AssetFavoritesLibrary
{
    public AssetFavoriteEntry FindEntryById(string entryId)
    {
        if (string.IsNullOrEmpty(entryId))
        {
            return null;
        }

        return entries.FirstOrDefault(entry => string.Equals(GetEntryId(entry), entryId, StringComparison.Ordinal));
    }

    public static string GetEntryId(AssetFavoriteEntry entry)
    {
        if (entry == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(entry.id))
        {
            return entry.id;
        }

        if (entry.kind == AssetFavoriteEntryKind.NodeTemplate && !string.IsNullOrEmpty(entry.templateGuid))
        {
            return entry.templateGuid;
        }

        return entry.assetGuid ?? string.Empty;
    }

    public static bool IsNodeTemplate(AssetFavoriteEntry entry)
    {
        return entry != null && entry.kind == AssetFavoriteEntryKind.NodeTemplate;
    }

    public static bool CanFavoriteGameObject(GameObject gameObject)
    {
        return gameObject != null && !EditorUtility.IsPersistent(gameObject);
    }

    public int AddGameObjectTemplates(IEnumerable<GameObject> gameObjects, string destinationFolderId, out int skippedCount)
    {
        EnsureDefaultFolders();
        EnsureNodeTemplatesFolder();

        List<GameObject> normalizedObjects = NormalizeGameObjects(gameObjects);
        HashSet<string> existingSourceIds = new HashSet<string>(entries
            .Where(IsNodeTemplate)
            .Select(entry => entry.sourceGlobalObjectId)
            .Where(id => !string.IsNullOrEmpty(id)));

        int addedCount = 0;
        skippedCount = 0;

        foreach (GameObject gameObject in normalizedObjects)
        {
            if (!CanFavoriteGameObject(gameObject))
            {
                skippedCount++;
                continue;
            }

            string sourceId = GetGlobalObjectId(gameObject);
            if (!string.IsNullOrEmpty(sourceId) && existingSourceIds.Contains(sourceId))
            {
                skippedCount++;
                continue;
            }

            string entryId = Guid.NewGuid().ToString("N");
            string templatePath = AssetDatabase.GenerateUniqueAssetPath(NodeTemplatesFolder + "/" + SanitizeFileName(gameObject.name) + "_" + entryId.Substring(0, 8) + ".prefab");
            if (!SaveNodeTemplatePrefab(gameObject, templatePath))
            {
                skippedCount++;
                continue;
            }

            string templateGuid = AssetDatabase.AssetPathToGUID(templatePath);
            if (string.IsNullOrEmpty(templateGuid))
            {
                AssetDatabase.DeleteAsset(templatePath);
                skippedCount++;
                continue;
            }

            RectTransform rectTransform = gameObject.GetComponent<RectTransform>();
            entries.Add(new AssetFavoriteEntry
            {
                id = entryId,
                kind = AssetFavoriteEntryKind.NodeTemplate,
                templateGuid = templateGuid,
                folderId = IsValidDestination(destinationFolderId) ? destinationFolderId : AutoNodeFolderId,
                displayName = gameObject.name,
                sourceScenePath = GetSourcePath(gameObject),
                sourceHierarchyPath = BuildHierarchyPath(gameObject.transform),
                sourceGlobalObjectId = sourceId,
                primaryComponent = GetPrimaryComponentName(gameObject),
                rectWidth = rectTransform == null ? 0 : Mathf.RoundToInt(rectTransform.rect.width),
                rectHeight = rectTransform == null ? 0 : Mathf.RoundToInt(rectTransform.rect.height)
            });

            if (!string.IsNullOrEmpty(sourceId))
            {
                existingSourceIds.Add(sourceId);
            }
            addedCount++;
        }

        if (addedCount > 0)
        {
            Save();
            AssetDatabase.SaveAssets();
        }

        return addedCount;
    }

    public int InstantiateNodeTemplates(IEnumerable<string> entryIds, Transform parent, out List<GameObject> createdObjects)
    {
        return InstantiateNodeTemplates(entryIds, parent, -1, out createdObjects);
    }

    public int InstantiateNodeTemplates(
        IEnumerable<string> entryIds,
        Transform parent,
        int siblingIndex,
        out List<GameObject> createdObjects)
    {
        return InstantiateNodeTemplates(entryIds, parent, siblingIndex, default(Scene), out createdObjects);
    }

    public int InstantiateNodeTemplates(
        IEnumerable<string> entryIds,
        Transform parent,
        int siblingIndex,
        Scene destinationScene,
        out List<GameObject> createdObjects)
    {
        createdObjects = new List<GameObject>();
        int nextSiblingIndex = siblingIndex;
        foreach (string entryId in (entryIds ?? Enumerable.Empty<string>()).Distinct())
        {
            AssetFavoriteEntry entry = FindEntryById(entryId);
            if (!IsNodeTemplate(entry))
            {
                continue;
            }

            GameObject template = LoadNodeTemplateAsset(entry);
            if (template == null)
            {
                continue;
            }

            GameObject instance = parent == null
                ? PrefabUtility.InstantiatePrefab(template) as GameObject
                : PrefabUtility.InstantiatePrefab(template, parent) as GameObject;
            if (instance == null)
            {
                instance = parent == null
                    ? UnityEngine.Object.Instantiate(template)
                    : UnityEngine.Object.Instantiate(template, parent);
            }

            if (instance == null)
            {
                continue;
            }

            if (parent == null
                && destinationScene.IsValid()
                && destinationScene.isLoaded
                && instance.scene != destinationScene)
            {
                SceneManager.MoveGameObjectToScene(instance, destinationScene);
            }

            instance.name = string.IsNullOrEmpty(entry.displayName) ? template.name : entry.displayName;
            Undo.RegisterCreatedObjectUndo(instance, "Place Favorite Node");
            if (PrefabUtility.IsPartOfPrefabInstance(instance))
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            if (nextSiblingIndex >= 0)
            {
                instance.transform.SetSiblingIndex(nextSiblingIndex);
                nextSiblingIndex++;
            }

            createdObjects.Add(instance);
        }

        if (createdObjects.Count > 0)
        {
            Selection.objects = createdObjects.Cast<UnityEngine.Object>().ToArray();
        }

        return createdObjects.Count;
    }

    public GameObject LoadNodeTemplateAsset(AssetFavoriteEntry entry)
    {
        if (!IsNodeTemplate(entry) || string.IsNullOrEmpty(entry.templateGuid))
        {
            return null;
        }

        string path = AssetDatabase.GUIDToAssetPath(entry.templateGuid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    public UnityEngine.Object LoadFavoriteObject(AssetFavoriteEntry entry)
    {
        if (IsNodeTemplate(entry))
        {
            return LoadNodeTemplateAsset(entry);
        }

        string path = AssetDatabase.GUIDToAssetPath(entry == null ? string.Empty : entry.assetGuid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadMainAssetAtPath(path);
    }

    public UnityEngine.Object FindSourceObject(AssetFavoriteEntry entry)
    {
        if (!IsNodeTemplate(entry) || string.IsNullOrEmpty(entry.sourceGlobalObjectId))
        {
            return null;
        }

        GlobalObjectId globalObjectId;
        if (!GlobalObjectId.TryParse(entry.sourceGlobalObjectId, out globalObjectId))
        {
            return null;
        }

        return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalObjectId);
    }

    private string ClassifyEntry(AssetFavoriteEntry entry)
    {
        if (IsNodeTemplate(entry))
        {
            return AutoNodeFolderId;
        }

        return ClassifyAsset(AssetDatabase.GUIDToAssetPath(entry == null ? string.Empty : entry.assetGuid));
    }

    private void DeleteGeneratedTemplates(IEnumerable<AssetFavoriteEntry> removingEntries)
    {
        foreach (AssetFavoriteEntry entry in removingEntries ?? Enumerable.Empty<AssetFavoriteEntry>())
        {
            if (!IsNodeTemplate(entry) || string.IsNullOrEmpty(entry.templateGuid))
            {
                continue;
            }

            string path = AssetDatabase.GUIDToAssetPath(entry.templateGuid);
            if (string.IsNullOrEmpty(path) || !path.StartsWith(NodeTemplatesFolder + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AssetDatabase.DeleteAsset(path);
        }
    }

    private static void EnsureNodeTemplatesFolder()
    {
        EnsureAssetFolder();
        if (!AssetDatabase.IsValidFolder(NodeTemplatesFolder))
        {
            AssetDatabase.CreateFolder(LibraryFolder, "AssetFavoritesNodeTemplates");
        }
    }

    private static bool SaveNodeTemplatePrefab(GameObject source, string templatePath)
    {
        GameObject clone = null;
        try
        {
            clone = UnityEngine.Object.Instantiate(source);
            clone.name = source.name;

            GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(clone);
            if (prefabRoot != null)
            {
                PrefabUtility.UnpackPrefabInstance(prefabRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            return PrefabUtility.SaveAsPrefabAsset(clone, templatePath) != null;
        }
        finally
        {
            if (clone != null)
            {
                UnityEngine.Object.DestroyImmediate(clone);
            }
        }
    }

    private static List<GameObject> NormalizeGameObjects(IEnumerable<GameObject> gameObjects)
    {
        List<GameObject> source = (gameObjects ?? Enumerable.Empty<GameObject>())
            .Where(CanFavoriteGameObject)
            .Distinct()
            .ToList();
        HashSet<GameObject> selected = new HashSet<GameObject>(source);
        return source
            .Where(gameObject => !HasSelectedAncestor(gameObject.transform, selected))
            .OrderBy(gameObject => BuildHierarchyPath(gameObject.transform), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool HasSelectedAncestor(Transform transform, HashSet<GameObject> selected)
    {
        Transform parent = transform == null ? null : transform.parent;
        while (parent != null)
        {
            if (selected.Contains(parent.gameObject))
            {
                return true;
            }

            parent = parent.parent;
        }

        return false;
    }

    private static string GetGlobalObjectId(UnityEngine.Object target)
    {
        try
        {
            return GlobalObjectId.GetGlobalObjectIdSlow(target).ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetSourcePath(GameObject gameObject)
    {
        if (gameObject == null)
        {
            return string.Empty;
        }

        string scenePath = gameObject.scene.IsValid() ? gameObject.scene.path : string.Empty;
        if (!string.IsNullOrEmpty(scenePath))
        {
            return scenePath;
        }

        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
        return prefabPath ?? string.Empty;
    }

    private static string BuildHierarchyPath(Transform transform)
    {
        if (transform == null)
        {
            return string.Empty;
        }

        Stack<string> names = new Stack<string>();
        Transform cursor = transform;
        while (cursor != null)
        {
            names.Push(cursor.name);
            cursor = cursor.parent;
        }

        return string.Join("/", names.ToArray());
    }

    private static string GetPrimaryComponentName(GameObject gameObject)
    {
        if (gameObject == null)
        {
            return "GameObject";
        }

        foreach (Component component in gameObject.GetComponents<Component>())
        {
            if (component == null)
            {
                continue;
            }

            Type type = component.GetType();
            if (type == typeof(Transform)
                || type == typeof(RectTransform)
                || type.Name == "CanvasRenderer")
            {
                continue;
            }

            return ObjectNames.NicifyVariableName(type.Name);
        }

        return "GameObject";
    }

    private static string SanitizeFileName(string rawName)
    {
        string name = string.IsNullOrWhiteSpace(rawName) ? "NodeTemplate" : rawName.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "NodeTemplate" : name;
    }
}

public static class AssetFavoritesGameObjectMenu
{
    private const string MenuPath = "GameObject/★ 收藏为节点模板";

    [MenuItem(MenuPath, false, 49)]
    private static void AddSelectedGameObjects()
    {
        GameObject[] gameObjects = Selection.gameObjects
            .Where(AssetFavoritesLibrary.CanFavoriteGameObject)
            .ToArray();
        AssetFavoritesLibrary library = AssetFavoritesLibrary.LoadOrCreate();
        int skipped;
        int added = library.AddGameObjectTemplates(gameObjects, string.Empty, out skipped);
        AssetFavoritesWindow.OpenAndFocus();
        EditorUtility.DisplayDialog("Asset Favorites", "本次收藏 " + added + " 个节点模板。\n已收藏或无效节点跳过 " + skipped + " 个。", "知道了");
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateAddSelectedGameObjects()
    {
        return Selection.gameObjects.Any(AssetFavoritesLibrary.CanFavoriteGameObject);
    }
}

[InitializeOnLoad]
internal static class AssetFavoritesHierarchyDropHandler
{
    static AssetFavoritesHierarchyDropHandler()
    {
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyWindowItemGUI;
    }

    private static void OnHierarchyWindowItemGUI(int instanceId, Rect selectionRect)
    {
        Event current = Event.current;
        if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
        {
            return;
        }

        string[] nodeTemplateEntryIds = DragAndDrop.GetGenericData(AssetFavoritesWindow.NodeTemplateDragKey) as string[];
        if (nodeTemplateEntryIds == null || nodeTemplateEntryIds.Length == 0)
        {
            return;
        }

        Rect rowRect = selectionRect;
        rowRect.x = 0f;
        rowRect.width = EditorGUIUtility.currentViewWidth;
        if (!rowRect.Contains(current.mousePosition))
        {
            return;
        }

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (current.type == EventType.DragPerform)
        {
            GameObject target = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            AssetFavoritesLibrary library = AssetFavoritesLibrary.LoadOrCreate();
            List<GameObject> createdObjects;
            library.InstantiateNodeTemplates(nodeTemplateEntryIds, target == null ? null : target.transform, out createdObjects);
            DragAndDrop.AcceptDrag();
            DragAndDrop.SetGenericData(AssetFavoritesWindow.NodeTemplateDragKey, null);
            DragAndDrop.SetGenericData(AssetFavoritesWindow.EntryDragKey, null);
            DragAndDrop.SetGenericData(AssetFavoritesWindow.SceneSiblingTargetDragKey, null);
        }

        current.Use();
    }
}

[InitializeOnLoad]
internal static class AssetFavoritesSceneDropHandler
{
    static AssetFavoritesSceneDropHandler()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        Event current = Event.current;
        if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
        {
            return;
        }

        string[] nodeTemplateEntryIds = DragAndDrop.GetGenericData(AssetFavoritesWindow.NodeTemplateDragKey) as string[];
        if (nodeTemplateEntryIds == null || nodeTemplateEntryIds.Length == 0)
        {
            return;
        }

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (current.type == EventType.DragPerform)
        {
            GameObject siblingTarget = GetSiblingTarget();
            Transform parent = siblingTarget == null ? null : siblingTarget.transform.parent;
            int siblingIndex = siblingTarget == null ? -1 : siblingTarget.transform.GetSiblingIndex() + 1;

            AssetFavoritesLibrary library = AssetFavoritesLibrary.LoadOrCreate();
            List<GameObject> createdObjects;
            library.InstantiateNodeTemplates(
                nodeTemplateEntryIds,
                parent,
                siblingIndex,
                siblingTarget == null ? default(Scene) : siblingTarget.scene,
                out createdObjects);

            DragAndDrop.AcceptDrag();
            DragAndDrop.SetGenericData(AssetFavoritesWindow.NodeTemplateDragKey, null);
            DragAndDrop.SetGenericData(AssetFavoritesWindow.EntryDragKey, null);
            DragAndDrop.SetGenericData(AssetFavoritesWindow.SceneSiblingTargetDragKey, null);
            EditorApplication.RepaintHierarchyWindow();
            sceneView.Repaint();
        }

        current.Use();
    }

    private static GameObject GetSiblingTarget()
    {
        object instanceIdData = DragAndDrop.GetGenericData(AssetFavoritesWindow.SceneSiblingTargetDragKey);
        int instanceId = instanceIdData is int ? (int)instanceIdData : 0;
        GameObject target = instanceId == 0
            ? null
            : EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        if (target == null
            || EditorUtility.IsPersistent(target)
            || !target.scene.IsValid()
            || !target.scene.isLoaded)
        {
            return null;
        }

        return target;
    }
}
