using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed partial class AssetFavoritesLibrary
{
    private const string ExchangeFormat = "Dragon.AssetFavorites";
    private const int ExchangeVersion = 1;
    private const long MaxExchangeFileBytes = 512L * 1024L * 1024L;
    private const int MaxNodeTemplateBytes = 32 * 1024 * 1024;

    public AssetFavoritesExchangeSummary ExportExchange(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Export path is empty.", "filePath");
        }

        AssetFavoritesExchangeManifest manifest = new AssetFavoritesExchangeManifest
        {
            format = ExchangeFormat,
            version = ExchangeVersion,
            exportedAtUtc = DateTime.UtcNow.ToString("o"),
            folders = folders
                .Where(folder => folder != null && !folder.automatic)
                .Select(folder => new AssetFavoritesExchangeFolder
                {
                    id = folder.id,
                    parentId = folder.parentId,
                    displayName = folder.displayName,
                    order = folder.order
                })
                .ToList()
        };
        AssetFavoritesExchangeSummary summary = new AssetFavoritesExchangeSummary();

        foreach (AssetFavoriteEntry entry in entries.Where(entry => entry != null))
        {
            AssetFavoritesExchangeEntry exported = CreateExchangeEntry(entry);
            if (exported == null)
            {
                summary.skippedInvalid++;
                continue;
            }

            manifest.entries.Add(exported);
            if (IsNodeTemplate(entry))
            {
                summary.exportedNodes++;
            }
            else
            {
                summary.exportedAssets++;
            }
        }

        string directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonUtility.ToJson(manifest, true);
        File.WriteAllText(filePath, json, new UTF8Encoding(false));
        return summary;
    }

    public AssetFavoritesExchangeSummary ImportExchange(string filePath)
    {
        AssetFavoritesExchangeManifest manifest = ReadExchangeManifest(filePath);
        AssetFavoritesExchangeSummary summary = new AssetFavoritesExchangeSummary();
        List<AssetFavoritesImportCandidate> candidates = BuildImportCandidates(manifest, summary);
        if (candidates.Count == 0)
        {
            return summary;
        }

        Dictionary<string, AssetFavoritesExchangeFolder> importedFolders = (manifest.folders ?? new List<AssetFavoritesExchangeFolder>())
            .Where(folder => folder != null && !string.IsNullOrEmpty(folder.id))
            .GroupBy(folder => folder.id)
            .ToDictionary(group => group.Key, group => group.First());
        Dictionary<string, string> folderMap = new Dictionary<string, string>();
        HashSet<string> resolvingFolders = new HashSet<string>();
        bool needsNodeTemplateFolder = candidates.Any(candidate => candidate.entry.kind == (int)AssetFavoriteEntryKind.NodeTemplate);
        if (needsNodeTemplateFolder)
        {
            EnsureNodeTemplatesFolder();
        }

        foreach (AssetFavoritesImportCandidate candidate in candidates)
        {
            string destinationFolderId = ResolveImportedFolderId(
                candidate.entry.folderId,
                candidate.entry,
                importedFolders,
                folderMap,
                resolvingFolders,
                summary);

            if (candidate.entry.kind == (int)AssetFavoriteEntryKind.NodeTemplate)
            {
                if (ImportNodeTemplate(candidate, destinationFolderId))
                {
                    summary.addedNodes++;
                }
                else
                {
                    summary.skippedInvalid++;
                }
            }
            else
            {
                entries.Add(new AssetFavoriteEntry
                {
                    id = Guid.NewGuid().ToString("N"),
                    kind = AssetFavoriteEntryKind.Asset,
                    assetGuid = candidate.entry.assetGuid,
                    folderId = destinationFolderId
                });
                summary.addedAssets++;
            }
        }

        if (summary.addedAssets > 0 || summary.addedNodes > 0 || summary.createdFolders > 0)
        {
            EnsureDefaultFolders();
            Save();
            AssetDatabase.SaveAssets();
        }

        return summary;
    }

    internal static string ComputeTemplateContentHash(string assetPath)
    {
        byte[] bytes;
        return TryReadAssetBytes(assetPath, out bytes) ? ComputeHash(bytes) : string.Empty;
    }

    private AssetFavoritesExchangeEntry CreateExchangeEntry(AssetFavoriteEntry entry)
    {
        AssetFavoritesExchangeEntry exported = new AssetFavoritesExchangeEntry
        {
            kind = (int)entry.kind,
            assetGuid = entry.assetGuid,
            folderId = entry.folderId,
            displayName = entry.displayName,
            sourceScenePath = entry.sourceScenePath,
            sourceHierarchyPath = entry.sourceHierarchyPath,
            sourceGlobalObjectId = entry.sourceGlobalObjectId,
            primaryComponent = entry.primaryComponent,
            rectWidth = entry.rectWidth,
            rectHeight = entry.rectHeight
        };

        if (!IsNodeTemplate(entry))
        {
            return string.IsNullOrEmpty(entry.assetGuid) ? null : exported;
        }

        string templatePath = AssetDatabase.GUIDToAssetPath(entry.templateGuid);
        byte[] templateBytes;
        if (string.IsNullOrEmpty(templatePath)
            || !templatePath.StartsWith(NodeTemplatesFolder + "/", StringComparison.OrdinalIgnoreCase)
            || !TryReadAssetBytes(templatePath, out templateBytes)
            || templateBytes.Length == 0
            || templateBytes.Length > MaxNodeTemplateBytes)
        {
            return null;
        }

        exported.templateFileName = Path.GetFileName(templatePath);
        exported.templateHash = ComputeHash(templateBytes);
        exported.templateData = Convert.ToBase64String(templateBytes);
        return exported;
    }

    private List<AssetFavoritesImportCandidate> BuildImportCandidates(
        AssetFavoritesExchangeManifest manifest,
        AssetFavoritesExchangeSummary summary)
    {
        HashSet<string> assetGuids = new HashSet<string>(
            entries.Where(entry => entry != null && !IsNodeTemplate(entry))
                .Select(entry => entry.assetGuid)
                .Where(guid => !string.IsNullOrEmpty(guid)),
            StringComparer.Ordinal);
        HashSet<string> sourceIdsWithoutTemplateHash = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> templateHashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (AssetFavoriteEntry localNode in entries.Where(IsNodeTemplate))
        {
            string hash = !string.IsNullOrEmpty(localNode.templateContentHash)
                ? localNode.templateContentHash
                : ComputeTemplateContentHash(AssetDatabase.GUIDToAssetPath(localNode.templateGuid));
            if (!string.IsNullOrEmpty(hash))
            {
                templateHashes.Add(hash);
            }
            else if (!string.IsNullOrEmpty(localNode.sourceGlobalObjectId))
            {
                sourceIdsWithoutTemplateHash.Add(localNode.sourceGlobalObjectId);
            }
        }

        List<AssetFavoritesImportCandidate> candidates = new List<AssetFavoritesImportCandidate>();
        foreach (AssetFavoritesExchangeEntry imported in manifest.entries ?? new List<AssetFavoritesExchangeEntry>())
        {
            if (imported == null)
            {
                summary.skippedInvalid++;
                continue;
            }

            if (imported.kind != (int)AssetFavoriteEntryKind.Asset
                && imported.kind != (int)AssetFavoriteEntryKind.NodeTemplate)
            {
                summary.skippedInvalid++;
                continue;
            }

            if (imported.kind != (int)AssetFavoriteEntryKind.NodeTemplate)
            {
                if (string.IsNullOrEmpty(imported.assetGuid)
                    || string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(imported.assetGuid)))
                {
                    summary.skippedMissing++;
                    continue;
                }

                if (!assetGuids.Add(imported.assetGuid))
                {
                    summary.skippedExisting++;
                    continue;
                }

                candidates.Add(new AssetFavoritesImportCandidate { entry = imported });
                continue;
            }

            byte[] templateBytes;
            try
            {
                templateBytes = string.IsNullOrEmpty(imported.templateData)
                    ? null
                    : Convert.FromBase64String(imported.templateData);
            }
            catch (FormatException)
            {
                templateBytes = null;
            }

            if (templateBytes == null || templateBytes.Length == 0 || templateBytes.Length > MaxNodeTemplateBytes)
            {
                summary.skippedInvalid++;
                continue;
            }

            string actualHash = ComputeHash(templateBytes);
            if (!string.IsNullOrEmpty(imported.templateHash)
                && !string.Equals(imported.templateHash, actualHash, StringComparison.Ordinal))
            {
                summary.skippedInvalid++;
                continue;
            }

            bool duplicateTemplate = !string.IsNullOrEmpty(actualHash) && !templateHashes.Add(actualHash);
            bool duplicateFallbackSource = string.IsNullOrEmpty(actualHash)
                                           && !string.IsNullOrEmpty(imported.sourceGlobalObjectId)
                                           && !sourceIdsWithoutTemplateHash.Add(imported.sourceGlobalObjectId);
            if (duplicateTemplate || duplicateFallbackSource)
            {
                summary.skippedExisting++;
                continue;
            }

            candidates.Add(new AssetFavoritesImportCandidate
            {
                entry = imported,
                templateBytes = templateBytes,
                templateHash = actualHash
            });
        }

        return candidates;
    }

    private string ResolveImportedFolderId(
        string sourceFolderId,
        AssetFavoritesExchangeEntry importedEntry,
        Dictionary<string, AssetFavoritesExchangeFolder> importedFolders,
        Dictionary<string, string> folderMap,
        HashSet<string> resolvingFolders,
        AssetFavoritesExchangeSummary summary)
    {
        if (string.IsNullOrEmpty(sourceFolderId))
        {
            return ClassifyImportedEntry(importedEntry);
        }

        string mappedId;
        if (folderMap.TryGetValue(sourceFolderId, out mappedId))
        {
            return mappedId;
        }

        AssetFavoriteFolder existingById = FindFolder(sourceFolderId);
        if (existingById != null)
        {
            folderMap[sourceFolderId] = existingById.id;
            return existingById.id;
        }

        AssetFavoritesExchangeFolder importedFolder;
        if (!importedFolders.TryGetValue(sourceFolderId, out importedFolder))
        {
            return ClassifyImportedEntry(importedEntry);
        }

        if (!resolvingFolders.Add(sourceFolderId))
        {
            return ClassifyImportedEntry(importedEntry);
        }

        string parentId = string.IsNullOrEmpty(importedFolder.parentId)
            ? string.Empty
            : ResolveImportedFolderId(
                importedFolder.parentId,
                importedEntry,
                importedFolders,
                folderMap,
                resolvingFolders,
                summary);
        resolvingFolders.Remove(sourceFolderId);

        string requestedName = string.IsNullOrWhiteSpace(importedFolder.displayName)
            ? "Imported Folder"
            : importedFolder.displayName.Trim();
        AssetFavoriteFolder matchingFolder = folders.FirstOrDefault(folder => folder != null
            && !folder.automatic
            && folder.parentId == parentId
            && string.Equals(folder.displayName, requestedName, StringComparison.OrdinalIgnoreCase));
        if (matchingFolder != null)
        {
            folderMap[sourceFolderId] = matchingFolder.id;
            return matchingFolder.id;
        }

        string createdId = Guid.NewGuid().ToString("N");
        string uniqueName = MakeUniqueFolderName(parentId, requestedName, string.Empty);
        int nextOrder = folders.Where(folder => folder.parentId == parentId)
            .Select(folder => folder.order)
            .DefaultIfEmpty(100)
            .Max() + 10;
        folders.Add(new AssetFavoriteFolder
        {
            id = createdId,
            parentId = parentId,
            displayName = uniqueName,
            automatic = false,
            order = nextOrder
        });
        folderMap[sourceFolderId] = createdId;
        summary.createdFolders++;
        return createdId;
    }

    private string ClassifyImportedEntry(AssetFavoritesExchangeEntry importedEntry)
    {
        if (importedEntry.kind == (int)AssetFavoriteEntryKind.NodeTemplate)
        {
            return AutoNodeFolderId;
        }

        return ClassifyAsset(AssetDatabase.GUIDToAssetPath(importedEntry.assetGuid));
    }

    private bool ImportNodeTemplate(AssetFavoritesImportCandidate candidate, string destinationFolderId)
    {
        string entryId = Guid.NewGuid().ToString("N");
        string displayName = string.IsNullOrWhiteSpace(candidate.entry.displayName)
            ? Path.GetFileNameWithoutExtension(candidate.entry.templateFileName)
            : candidate.entry.displayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = "Imported Node";
        }

        string templatePath = AssetDatabase.GenerateUniqueAssetPath(
            NodeTemplatesFolder + "/" + SanitizeFileName(displayName) + "_imported_" + entryId.Substring(0, 8) + ".prefab");
        string fullPath = AssetPathToFullPath(templatePath);
        try
        {
            File.WriteAllBytes(fullPath, candidate.templateBytes);
            AssetDatabase.ImportAsset(templatePath, ImportAssetOptions.ForceSynchronousImport);
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(templatePath);
            string templateGuid = AssetDatabase.AssetPathToGUID(templatePath);
            if (template == null || string.IsNullOrEmpty(templateGuid))
            {
                AssetDatabase.DeleteAsset(templatePath);
                return false;
            }

            entries.Add(new AssetFavoriteEntry
            {
                id = entryId,
                kind = AssetFavoriteEntryKind.NodeTemplate,
                templateGuid = templateGuid,
                templateContentHash = candidate.templateHash,
                folderId = string.IsNullOrEmpty(destinationFolderId) ? AutoNodeFolderId : destinationFolderId,
                displayName = displayName,
                sourceScenePath = candidate.entry.sourceScenePath,
                sourceHierarchyPath = candidate.entry.sourceHierarchyPath,
                sourceGlobalObjectId = candidate.entry.sourceGlobalObjectId,
                primaryComponent = candidate.entry.primaryComponent,
                rectWidth = candidate.entry.rectWidth,
                rectHeight = candidate.entry.rectHeight
            });
            return true;
        }
        catch (Exception)
        {
            if (AssetDatabase.LoadMainAssetAtPath(templatePath) != null)
            {
                AssetDatabase.DeleteAsset(templatePath);
            }
            else if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            return false;
        }
    }

    private static AssetFavoritesExchangeManifest ReadExchangeManifest(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("Favorites exchange file was not found.", filePath);
        }

        FileInfo fileInfo = new FileInfo(filePath);
        if (fileInfo.Length <= 0 || fileInfo.Length > MaxExchangeFileBytes)
        {
            throw new InvalidDataException("Favorites exchange file size is invalid.");
        }

        AssetFavoritesExchangeManifest manifest;
        try
        {
            manifest = JsonUtility.FromJson<AssetFavoritesExchangeManifest>(File.ReadAllText(filePath, Encoding.UTF8));
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("Favorites exchange file is not valid JSON.", exception);
        }

        if (manifest == null
            || !string.Equals(manifest.format, ExchangeFormat, StringComparison.Ordinal)
            || manifest.version != ExchangeVersion)
        {
            throw new InvalidDataException("Favorites exchange format or version is not supported.");
        }

        return manifest;
    }

    private static bool TryReadAssetBytes(string assetPath, out byte[] bytes)
    {
        bytes = null;
        if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fullPath = AssetPathToFullPath(assetPath);
        if (!File.Exists(fullPath))
        {
            return false;
        }

        bytes = File.ReadAllBytes(fullPath);
        return true;
    }

    private static string AssetPathToFullPath(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string ComputeHash(byte[] bytes)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            return Convert.ToBase64String(sha256.ComputeHash(bytes));
        }
    }

    [Serializable]
    private sealed class AssetFavoritesExchangeManifest
    {
        public string format = string.Empty;
        public int version;
        public string exportedAtUtc = string.Empty;
        public List<AssetFavoritesExchangeFolder> folders = new List<AssetFavoritesExchangeFolder>();
        public List<AssetFavoritesExchangeEntry> entries = new List<AssetFavoritesExchangeEntry>();
    }

    [Serializable]
    private sealed class AssetFavoritesExchangeFolder
    {
        public string id = string.Empty;
        public string parentId = string.Empty;
        public string displayName = string.Empty;
        public int order;
    }

    [Serializable]
    private sealed class AssetFavoritesExchangeEntry
    {
        public int kind;
        public string assetGuid = string.Empty;
        public string folderId = string.Empty;
        public string displayName = string.Empty;
        public string sourceScenePath = string.Empty;
        public string sourceHierarchyPath = string.Empty;
        public string sourceGlobalObjectId = string.Empty;
        public string primaryComponent = string.Empty;
        public int rectWidth;
        public int rectHeight;
        public string templateFileName = string.Empty;
        public string templateHash = string.Empty;
        public string templateData = string.Empty;
    }

    private sealed class AssetFavoritesImportCandidate
    {
        public AssetFavoritesExchangeEntry entry;
        public byte[] templateBytes;
        public string templateHash = string.Empty;
    }
}

public sealed class AssetFavoritesExchangeSummary
{
    public int exportedAssets;
    public int exportedNodes;
    public int addedAssets;
    public int addedNodes;
    public int createdFolders;
    public int skippedExisting;
    public int skippedMissing;
    public int skippedInvalid;
}
