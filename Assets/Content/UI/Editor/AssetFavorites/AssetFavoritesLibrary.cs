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
    public string assetGuid = string.Empty;
    public string folderId = string.Empty;
}

public sealed class AssetFavoritesLibrary : ScriptableObject
{
    public const string AssetPath = "Assets/Content/UI/Library/AssetFavoritesLibrary.asset";

    private const string LibraryFolder = "Assets/Content/UI/Library";

    private static readonly AutoFolderDefinition[] AutoFolders =
    {
        new AutoFolderDefinition("auto-prefabs", "预制体 Prefab", 10),
        new AutoFolderDefinition("auto-textures", "贴图 Texture", 20),
        new AutoFolderDefinition("auto-animations", "动画 Animation", 30),
        new AutoFolderDefinition("auto-materials", "材质 Material", 40),
        new AutoFolderDefinition("auto-audio", "音频 Audio", 50),
        new AutoFolderDefinition("auto-scenes", "场景 Scene", 60),
        new AutoFolderDefinition("auto-scripts", "脚本 Script", 70),
        new AutoFolderDefinition("auto-fonts", "字体 Font", 80),
        new AutoFolderDefinition("auto-other", "其他 Other", 90)
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
            library = CreateInstance<AssetFavoritesLibrary>();
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

        int removedEntries = entries.RemoveAll(entry => folderIds.Contains(entry.folderId));
        folders.RemoveAll(candidate => folderIds.Contains(candidate.id));
        Save();
        return removedEntries;
    }

    public int AddProjectPaths(IEnumerable<string> projectPaths, string destinationFolderId, out int skippedCount)
    {
        EnsureDefaultFolders();
        List<string> expandedPaths = ExpandProjectPaths(projectPaths);
        HashSet<string> existingGuids = new HashSet<string>(entries.Select(entry => entry.assetGuid));
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

    public int MoveEntries(IEnumerable<string> assetGuids, string destinationFolderId)
    {
        HashSet<string> movingGuids = new HashSet<string>(assetGuids ?? Enumerable.Empty<string>());
        int moved = 0;
        foreach (AssetFavoriteEntry entry in entries)
        {
            if (!movingGuids.Contains(entry.assetGuid))
            {
                continue;
            }

            string newFolderId = IsValidDestination(destinationFolderId)
                ? destinationFolderId
                : ClassifyAsset(AssetDatabase.GUIDToAssetPath(entry.assetGuid));
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

    public int RemoveEntries(IEnumerable<string> assetGuids)
    {
        HashSet<string> removing = new HashSet<string>(assetGuids ?? Enumerable.Empty<string>());
        int removed = entries.RemoveAll(entry => removing.Contains(entry.assetGuid));
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

        if (extension == ".prefab") return "auto-prefabs";
        if (type == typeof(Texture2D) || type == typeof(Sprite)) return "auto-textures";
        if (type == typeof(AnimationClip) || type == typeof(RuntimeAnimatorController)
            || extension == ".controller" || extension == ".overridecontroller" || extension == ".mask") return "auto-animations";
        if (type == typeof(Material)) return "auto-materials";
        if (type == typeof(AudioClip)) return "auto-audio";
        if (type == typeof(SceneAsset) || extension == ".unity") return "auto-scenes";
        if (type == typeof(MonoScript)) return "auto-scripts";
        if (type == typeof(Font) || (type != null && type.FullName != null && type.FullName.Contains("TMP_FontAsset"))) return "auto-fonts";
        return "auto-other";
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
