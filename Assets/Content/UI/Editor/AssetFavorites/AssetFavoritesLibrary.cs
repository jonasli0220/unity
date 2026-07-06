using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[Serializable]
public sealed class AssetFavoriteFolder
{
    public string id = string.Empty;
    public string parentId = string.Empty;
    public string displayName = string.Empty;
    public bool automatic;
    public int order;
}

[Serializable]
public sealed class AssetFavoriteEntry
{
    public string id = string.Empty;
    public AssetFavoriteEntryKind kind = AssetFavoriteEntryKind.Asset;
    public string assetGuid = string.Empty;
    public string templateGuid = string.Empty;
    public string templateContentHash = string.Empty;
    public string folderId = string.Empty;
    public string displayName = string.Empty;
    public string sourceScenePath = string.Empty;
    public string sourceHierarchyPath = string.Empty;
    public string sourceGlobalObjectId = string.Empty;
    public string primaryComponent = string.Empty;
    public int rectWidth;
    public int rectHeight;
}

public enum AssetFavoriteEntryKind
{
    Asset = 0,
    NodeTemplate = 1
}

public sealed partial class AssetFavoritesLibrary : ScriptableObject
{
    public const string AssetPath = "Assets/Content/UI/Library/AssetFavoritesLocalLibrary.asset";
    public const string LegacyAssetPath = "Assets/Content/UI/Library/AssetFavoritesLibrary.asset";
    public const string NodeTemplatesFolder = "Assets/Content/UI/Library/AssetFavoritesNodeTemplates";
    public const string AutoNodeFolderId = "auto-nodes";

    private const string LibraryFolder = "Assets/Content/UI/Library";
    private const string AutoPrefabFolderId = "auto-prefabs";
    private const string AutoSpriteFolderId = "auto-textures";
    private const string AutoVfxFolderId = "auto-vfx";
    private const string AutoOtherFolderId = "auto-other";

    private static readonly AutoFolderDefinition[] AutoFolders =
    {
        new AutoFolderDefinition(AutoPrefabFolderId, "预制体 Prefab", 10),
        new AutoFolderDefinition(AutoNodeFolderId, "节点 Node", 15),
        new AutoFolderDefinition(AutoSpriteFolderId, "图片 Sprite", 20),
        new AutoFolderDefinition(AutoVfxFolderId, "特效 VFX", 30),
        new AutoFolderDefinition("auto-animations", "动画 Animation", 40),
        new AutoFolderDefinition("auto-materials", "材质 Material", 50),
        new AutoFolderDefinition(AutoOtherFolderId, "其他 Other", 90)
    };

    private static readonly HashSet<string> ObsoleteAutoFolderIds = new HashSet<string>
    {
        "auto-audio",
        "auto-scenes",
        "auto-scripts",
        "auto-fonts"
    };

    [SerializeField] private List<AssetFavoriteFolder> folders = new List<AssetFavoriteFolder>();
    [SerializeField] private List<AssetFavoriteEntry> entries = new List<AssetFavoriteEntry>();

    public IReadOnlyList<AssetFavoriteFolder> Folders
    {
        get { return folders; }
    }

    public IReadOnlyList<AssetFavoriteEntry> Entries
    {
        get { return entries; }
    }

    public static AssetFavoritesLibrary LoadOrCreate()
    {
        AssetFavoritesLibrary library = AssetDatabase.LoadAssetAtPath<AssetFavoritesLibrary>(AssetPath);
        if (library == null)
        {
            EnsureAssetFolder();
            AssetFavoritesLibrary legacyLibrary = AssetDatabase.LoadAssetAtPath<AssetFavoritesLibrary>(LegacyAssetPath);
            library = legacyLibrary == null
                ? CreateInstance<AssetFavoritesLibrary>()
                : Instantiate(legacyLibrary);
            library.name = "AssetFavoritesLocalLibrary";
            library.hideFlags = HideFlags.None;
            library.EnsureDefaultFolders();
            AssetDatabase.CreateAsset(library, AssetPath);
            AssetDatabase.SaveAssets();
        }
        else if (library.EnsureDefaultFolders())
        {
            library.Save();
        }

        return library;
    }

    public AssetFavoriteFolder FindFolder(string folderId)
    {
        return folders.FirstOrDefault(folder => folder.id == folderId);
    }

    public List<AssetFavoriteFolder> GetChildFolders(string parentId)
    {
        return folders
            .Where(folder => folder.parentId == parentId)
            .OrderBy(folder => folder.order)
            .ThenBy(folder => folder.displayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string CreateFolder(string parentId, string requestedName)
    {
        if (!string.IsNullOrEmpty(parentId) && FindFolder(parentId) == null)
        {
            parentId = string.Empty;
        }

        string baseName = string.IsNullOrWhiteSpace(requestedName) ? "New Folder" : requestedName.Trim();
        string uniqueName = MakeUniqueFolderName(parentId, baseName, string.Empty);
        int nextOrder = folders.Where(folder => folder.parentId == parentId).Select(folder => folder.order).DefaultIfEmpty(100).Max() + 10;
        AssetFavoriteFolder created = new AssetFavoriteFolder
        {
            id = Guid.NewGuid().ToString("N"),
            parentId = parentId,
            displayName = uniqueName,
            automatic = false,
            order = nextOrder
        };
        folders.Add(created);
        Save();
        return created.id;
    }

    public bool RenameFolder(string folderId, string requestedName)
    {
        AssetFavoriteFolder folder = FindFolder(folderId);
        if (folder == null || folder.automatic || string.IsNullOrWhiteSpace(requestedName))
        {
            return false;
        }

        string uniqueName = MakeUniqueFolderName(folder.parentId, requestedName.Trim(), folder.id);
        if (folder.displayName == uniqueName)
        {
            return false;
        }

        folder.displayName = uniqueName;
        Save();
        return true;
    }

    public int DeleteFolder(string folderId)
    {
        AssetFavoriteFolder folder = FindFolder(folderId);
        if (folder == null || folder.automatic)
        {
            return 0;
        }

        HashSet<string> folderIds = new HashSet<string> { folderId };
        bool foundChild;
        do
        {
            foundChild = false;
            foreach (AssetFavoriteFolder candidate in folders)
            {
                if (!folderIds.Contains(candidate.id) && folderIds.Contains(candidate.parentId))
                {
                    folderIds.Add(candidate.id);
                    foundChild = true;
                }
            }
        }
        while (foundChild);

        List<AssetFavoriteEntry> removingEntries = entries.Where(entry => folderIds.Contains(entry.folderId)).ToList();
        DeleteGeneratedTemplates(removingEntries);
        int removedEntries = entries.RemoveAll(entry => folderIds.Contains(entry.folderId));
        folders.RemoveAll(candidate => folderIds.Contains(candidate.id));
        Save();
        return removedEntries;
    }

    public int AddProjectPaths(IEnumerable<string> projectPaths, string destinationFolderId, out int skippedCount)
    {
        EnsureDefaultFolders();
        List<string> expandedPaths = ExpandProjectPaths(projectPaths);
        HashSet<string> existingGuids = new HashSet<string>(entries
            .Where(entry => entry.kind == AssetFavoriteEntryKind.Asset)
            .Select(entry => entry.assetGuid));
        int addedCount = 0;
        skippedCount = 0;

        foreach (string path in expandedPaths)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid) || !existingGuids.Add(guid))
            {
                skippedCount++;
                continue;
            }

            string folderId = IsValidDestination(destinationFolderId)
                ? destinationFolderId
                : ClassifyAsset(path);
            entries.Add(new AssetFavoriteEntry { assetGuid = guid, folderId = folderId });
            addedCount++;
        }

        if (addedCount > 0)
        {
            Save();
        }

        return addedCount;
    }

    public int MoveEntries(IEnumerable<string> entryIds, string destinationFolderId)
    {
        HashSet<string> movingIds = new HashSet<string>(entryIds ?? Enumerable.Empty<string>());
        int moved = 0;
        foreach (AssetFavoriteEntry entry in entries)
        {
            if (!movingIds.Contains(GetEntryId(entry)))
            {
                continue;
            }

            string newFolderId = IsValidDestination(destinationFolderId)
                ? destinationFolderId
                : ClassifyEntry(entry);
            if (entry.folderId != newFolderId)
            {
                entry.folderId = newFolderId;
                moved++;
            }
        }

        if (moved > 0)
        {
            Save();
        }

        return moved;
    }

    public int RemoveEntries(IEnumerable<string> entryIds)
    {
        HashSet<string> removing = new HashSet<string>(entryIds ?? Enumerable.Empty<string>());
        List<AssetFavoriteEntry> removingEntries = entries.Where(entry => removing.Contains(GetEntryId(entry))).ToList();
        DeleteGeneratedTemplates(removingEntries);
        int removed = entries.RemoveAll(entry => removing.Contains(GetEntryId(entry)));
        if (removed > 0)
        {
            Save();
        }

        return removed;
    }

    public int CountEntriesRecursive(string folderId)
    {
        if (string.IsNullOrEmpty(folderId))
        {
            return entries.Count;
        }

        HashSet<string> included = new HashSet<string> { folderId };
        bool foundChild;
        do
        {
            foundChild = false;
            foreach (AssetFavoriteFolder folder in folders)
            {
                if (!included.Contains(folder.id) && included.Contains(folder.parentId))
                {
                    included.Add(folder.id);
                    foundChild = true;
                }
            }
        }
        while (foundChild);

        return entries.Count(entry => included.Contains(entry.folderId));
    }

    private bool EnsureDefaultFolders()
    {
        bool changed = false;
        foreach (AutoFolderDefinition definition in AutoFolders)
        {
            AssetFavoriteFolder folder = FindFolder(definition.Id);
            if (folder == null)
            {
                folders.Add(new AssetFavoriteFolder
                {
                    id = definition.Id,
                    parentId = string.Empty,
                    displayName = definition.Name,
                    automatic = true,
                    order = definition.Order
                });
                changed = true;
            }
            else if (!folder.automatic
                     || folder.parentId != string.Empty
                     || folder.displayName != definition.Name
                     || folder.order != definition.Order)
            {
                folder.automatic = true;
                folder.parentId = string.Empty;
                folder.displayName = definition.Name;
                folder.order = definition.Order;
                changed = true;
            }
        }

        foreach (AssetFavoriteEntry entry in entries)
        {
            bool comesFromRemovedCategory = ObsoleteAutoFolderIds.Contains(entry.folderId);
            bool usesVfxMigrationCategory = entry.folderId == AutoPrefabFolderId
                                            || entry.folderId == AutoVfxFolderId
                                            || entry.folderId == AutoOtherFolderId;
            if (!comesFromRemovedCategory && !usesVfxMigrationCategory)
            {
                continue;
            }

            string classifiedFolderId = ClassifyEntry(entry);
            if (comesFromRemovedCategory
                || entry.folderId == AutoVfxFolderId
                || classifiedFolderId == AutoVfxFolderId)
            {
                if (entry.folderId != classifiedFolderId)
                {
                    entry.folderId = classifiedFolderId;
                    changed = true;
                }
            }
        }

        foreach (AssetFavoriteFolder folder in folders)
        {
            if (ObsoleteAutoFolderIds.Contains(folder.parentId))
            {
                folder.parentId = string.Empty;
                changed = true;
            }
        }

        if (folders.RemoveAll(folder => ObsoleteAutoFolderIds.Contains(folder.id)) > 0)
        {
            changed = true;
        }

        return changed;
    }

    private string MakeUniqueFolderName(string parentId, string requestedName, string ignoredFolderId)
    {
        string candidate = requestedName;
        int suffix = 2;
        while (folders.Any(folder => folder.id != ignoredFolderId
                                     && folder.parentId == parentId
                                     && string.Equals(folder.displayName, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = requestedName + " " + suffix;
            suffix++;
        }

        return candidate;
    }

    private bool IsValidDestination(string folderId)
    {
        return !string.IsNullOrEmpty(folderId) && FindFolder(folderId) != null;
    }

    private static List<string> ExpandProjectPaths(IEnumerable<string> projectPaths)
    {
        HashSet<string> results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawPath in projectPaths ?? Enumerable.Empty<string>())
        {
            string path = (rawPath ?? string.Empty).Replace('\\', '/');
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (AssetDatabase.IsValidFolder(path))
            {
                foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { path }))
                {
                    AddAssetPath(AssetDatabase.GUIDToAssetPath(guid), results);
                }
            }
            else
            {
                AddAssetPath(path, results);
            }
        }

        return results.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddAssetPath(string path, HashSet<string> results)
    {
        if (string.IsNullOrEmpty(path)
            || path == AssetPath
            || path == LegacyAssetPath
            || path.StartsWith(NodeTemplatesFolder + "/", StringComparison.OrdinalIgnoreCase)
            || AssetDatabase.IsValidFolder(path)
            || AssetDatabase.LoadMainAssetAtPath(path) == null)
        {
            return;
        }

        results.Add(path);
    }

    private static string ClassifyAsset(string path)
    {
        string extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        Type type = string.IsNullOrEmpty(path) ? null : AssetDatabase.GetMainAssetTypeAtPath(path);

        if (IsVfxAsset(path, type, extension)) return AutoVfxFolderId;
        if (extension == ".prefab") return AutoPrefabFolderId;
        if (type == typeof(Texture2D) || type == typeof(Sprite)) return AutoSpriteFolderId;
        if (type == typeof(AnimationClip) || type == typeof(RuntimeAnimatorController)
            || extension == ".controller" || extension == ".overridecontroller" || extension == ".mask") return "auto-animations";
        if (type == typeof(Material)) return "auto-materials";
        return AutoOtherFolderId;
    }

    private static bool IsVfxAsset(string path, Type type, string extension)
    {
        if (extension == ".vfx" || extension == ".vfxoperator" || extension == ".vfxblock")
        {
            return true;
        }

        if (type != null && string.Equals(type.FullName, "UnityEngine.VFX.VisualEffectAsset", StringComparison.Ordinal))
        {
            return true;
        }

        if (extension != ".prefab")
        {
            return false;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            return false;
        }

        if (HasVfxComponent(prefab, false))
        {
            return true;
        }

        return HasVfxNamingHint(path) && HasVfxComponent(prefab, true);
    }

    private static bool HasVfxComponent(GameObject prefab, bool includeChildren)
    {
        if (includeChildren)
        {
            if (prefab.GetComponentInChildren<ParticleSystem>(true) != null
                || prefab.GetComponentInChildren<TrailRenderer>(true) != null)
            {
                return true;
            }

            return prefab.GetComponentsInChildren<Component>(true)
                .Any(IsVisualEffectComponent);
        }

        if (prefab.GetComponent<ParticleSystem>() != null || prefab.GetComponent<TrailRenderer>() != null)
        {
            return true;
        }

        return prefab.GetComponents<Component>().Any(IsVisualEffectComponent);
    }

    private static bool IsVisualEffectComponent(Component component)
    {
        return component != null
               && string.Equals(
                   component.GetType().FullName,
                   "UnityEngine.VFX.VisualEffect",
                   StringComparison.Ordinal);
    }

    private static bool HasVfxNamingHint(string path)
    {
        string[] tokens = (path ?? string.Empty)
            .Replace('\\', '/')
            .ToLowerInvariant()
            .Split(new[] { '/', '_', '-', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(token => token == "vfx"
                                   || token == "vx"
                                   || token == "fx"
                                   || token == "effect"
                                   || token == "effects"
                                   || token == "particle"
                                   || token == "particles");
    }

    private static void EnsureAssetFolder()
    {
        if (!AssetDatabase.IsValidFolder(LibraryFolder))
        {
            AssetDatabase.CreateFolder("Assets/Content/UI", "Library");
        }
    }

    private void Save()
    {
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
    }

    private struct AutoFolderDefinition
    {
        public readonly string Id;
        public readonly string Name;
        public readonly int Order;

        public AutoFolderDefinition(string id, string name, int order)
        {
            Id = id;
            Name = name;
            Order = order;
        }
    }
}

[InitializeOnLoad]
internal static class AssetFavoritesLocalLibraryBootstrap
{
    static AssetFavoritesLocalLibraryBootstrap()
    {
        EditorApplication.delayCall += EnsureLocalLibrary;
    }

    private static void EnsureLocalLibrary()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += EnsureLocalLibrary;
            return;
        }

        AssetFavoritesLibrary.LoadOrCreate();
    }
}

public static class AssetFavoritesProjectMenu
{
    private const string MenuPath = "Assets/★ 收藏到 Favorites";

    [MenuItem(MenuPath, false, 1900)]
    private static void AddSelectedAssets()
    {
        string[] paths = Selection.objects.Select(AssetDatabase.GetAssetPath).Where(path => !string.IsNullOrEmpty(path)).ToArray();
        AssetFavoritesLibrary library = AssetFavoritesLibrary.LoadOrCreate();
        int skipped;
        int added = library.AddProjectPaths(paths, string.Empty, out skipped);
        AssetFavoritesWindow.OpenAndFocus();
        EditorUtility.DisplayDialog("Asset Favorites", "本次收藏 " + added + " 个资产。\n已收藏或无效资产跳过 " + skipped + " 个。", "知道了");
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateAddSelectedAssets()
    {
        return Selection.objects.Any(item =>
        {
            string path = AssetDatabase.GetAssetPath(item);
            return !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        });
    }
}
