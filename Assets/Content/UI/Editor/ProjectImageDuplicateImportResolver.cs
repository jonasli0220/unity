using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class ProjectImageDuplicateImportResolver : AssetPostprocessor
{
    private const string UIAssetRoot = "Assets/Content/UI/";
    private const string ResolveSelectedFolderMenuPath =
        "UITools/处理当前UI文件夹同名图片副本";

    private static readonly HashSet<string> SupportedImageExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".psd",
            ".tga",
            ".tif",
            ".tiff"
        };

    private static readonly HashSet<string> PendingAssetPaths =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static bool isDelayCallRegistered;
    private static bool isProcessing;

    [InitializeOnLoadMethod]
    private static void InitProjectImageDuplicateImportResolver()
    {
        EditorApplication.projectWindowItemOnGUI -= HandleProjectWindowExternalImageDrag;
        EditorApplication.projectWindowItemOnGUI += HandleProjectWindowExternalImageDrag;
    }

    private static void HandleProjectWindowExternalImageDrag(string guid, Rect selectionRect)
    {
        Event currentEvent = Event.current;
        if (currentEvent == null
            || (currentEvent.type != EventType.DragUpdated
                && currentEvent.type != EventType.DragPerform)
            || !selectionRect.Contains(currentEvent.mousePosition))
        {
            return;
        }

        List<string> externalImagePaths = new List<string>();
        if (!TryGetExternalImagePaths(externalImagePaths))
        {
            return;
        }

        string targetFolder = ResolveTargetFolderFromProjectItem(guid);
        if (string.IsNullOrEmpty(targetFolder))
        {
            return;
        }

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (currentEvent.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            ImportExternalImagesToProjectFolder(externalImagePaths, targetFolder);
        }

        currentEvent.Use();
    }

    [MenuItem(ResolveSelectedFolderMenuPath)]
    private static void ResolveSelectedFolderDuplicateImages()
    {
        string folderPath = GetSelectedProjectFolderPath();
        if (string.IsNullOrEmpty(folderPath))
        {
            EditorUtility.DisplayDialog(
                "处理同名图片副本",
                "请先在 Project 窗口选中一个 Assets/Content/UI/ 下的文件夹或图片资源。",
                "确定");
            return;
        }

        string[] guids = AssetDatabase.FindAssets(
            "t:Texture",
            new[] { folderPath });

        List<string> candidatePaths = new List<string>();
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guids[i]));
            if (IsSupportedImageAsset(assetPath) && TryFindOriginalAssetPath(assetPath, out _))
            {
                candidatePaths.Add(assetPath);
            }
        }

        if (candidatePaths.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "处理同名图片副本",
                "当前文件夹没有发现可处理的同名序号副本。",
                "确定");
            return;
        }

        ProcessDuplicateImports(candidatePaths.ToArray());
    }

    [MenuItem(ResolveSelectedFolderMenuPath, true)]
    private static bool ValidateResolveSelectedFolderDuplicateImages()
    {
        return !string.IsNullOrEmpty(GetSelectedProjectFolderPath());
    }

    private static bool TryGetExternalImagePaths(List<string> externalImagePaths)
    {
        string[] draggedPaths = DragAndDrop.paths;
        if (draggedPaths == null || draggedPaths.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < draggedPaths.Length; i++)
        {
            string draggedPath = draggedPaths[i];
            if (string.IsNullOrEmpty(draggedPath)
                || !Path.IsPathRooted(draggedPath)
                || !File.Exists(draggedPath)
                || !SupportedImageExtensions.Contains(Path.GetExtension(draggedPath)))
            {
                externalImagePaths.Clear();
                return false;
            }

            externalImagePaths.Add(Path.GetFullPath(draggedPath));
        }

        return externalImagePaths.Count > 0;
    }

    private static string ResolveTargetFolderFromProjectItem(string guid)
    {
        string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guid));
        if (string.IsNullOrEmpty(assetPath))
        {
            return null;
        }

        if (AssetDatabase.IsValidFolder(assetPath))
        {
            return assetPath.StartsWith(UIAssetRoot, StringComparison.OrdinalIgnoreCase)
                ? assetPath
                : null;
        }

        if (!assetPath.StartsWith(UIAssetRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string parentFolder = NormalizeAssetPath(Path.GetDirectoryName(assetPath));
        return AssetDatabase.IsValidFolder(parentFolder) ? parentFolder : null;
    }

    private static void ImportExternalImagesToProjectFolder(
        List<string> externalImagePaths,
        string targetFolder)
    {
        if (externalImagePaths == null
            || externalImagePaths.Count == 0
            || string.IsNullOrEmpty(targetFolder)
            || !AssetDatabase.IsValidFolder(targetFolder)
            || !targetFolder.StartsWith(UIAssetRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        isProcessing = true;
        try
        {
            for (int i = 0; i < externalImagePaths.Count; i++)
            {
                ImportExternalImageToProjectFolder(externalImagePaths[i], targetFolder);
            }
        }
        finally
        {
            isProcessing = false;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void ImportExternalImageToProjectFolder(string externalPath, string targetFolder)
    {
        string fileName = Path.GetFileName(externalPath);
        if (string.IsNullOrEmpty(fileName))
        {
            return;
        }

        string assetPath = NormalizeAssetPath(targetFolder + "/" + fileName);
        string absoluteAssetPath = AssetPathToAbsolutePath(assetPath);
        bool assetExists = AssetDatabase.LoadMainAssetAtPath(assetPath) != null
            || File.Exists(absoluteAssetPath);

        if (assetExists
            && !EditorUtility.DisplayDialog(
                "替换同名图片？",
                "Project 中已经存在同名图片：\n\n" +
                assetPath +
                "\n\n是否用当前拖入的图片替换它？\n确认后会保留已有资源 GUID。",
                "替换已有资源",
                "跳过"))
        {
            return;
        }

        if (!string.Equals(
                Path.GetFullPath(externalPath),
                Path.GetFullPath(absoluteAssetPath),
                StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(externalPath, absoluteAssetPath, assetExists);
        }

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (isProcessing || importedAssets == null || importedAssets.Length == 0)
        {
            return;
        }

        bool hasCandidate = false;
        for (int i = 0; i < importedAssets.Length; i++)
        {
            string assetPath = NormalizeAssetPath(importedAssets[i]);
            if (!IsSupportedImageAsset(assetPath) || !TryFindOriginalAssetPath(assetPath, out _))
            {
                continue;
            }

            PendingAssetPaths.Add(assetPath);
            hasCandidate = true;
        }

        if (!hasCandidate || isDelayCallRegistered)
        {
            return;
        }

        isDelayCallRegistered = true;
        EditorApplication.delayCall += ProcessPendingDuplicateImports;
    }

    private static void ProcessPendingDuplicateImports()
    {
        isDelayCallRegistered = false;
        if (PendingAssetPaths.Count == 0)
        {
            return;
        }

        string[] candidatePaths = new string[PendingAssetPaths.Count];
        PendingAssetPaths.CopyTo(candidatePaths);
        PendingAssetPaths.Clear();

        ProcessDuplicateImports(candidatePaths);
    }

    private static void ProcessDuplicateImports(string[] candidatePaths)
    {
        if (candidatePaths == null || candidatePaths.Length == 0)
        {
            return;
        }

        isProcessing = true;
        try
        {
            for (int i = 0; i < candidatePaths.Length; i++)
            {
                try
                {
                    TryResolveDuplicateImport(candidatePaths[i]);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        "Unable to resolve duplicate Project image import: " +
                        candidatePaths[i] +
                        "\n" +
                        exception);
                }
            }
        }
        finally
        {
            isProcessing = false;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void TryResolveDuplicateImport(string duplicateAssetPath)
    {
        duplicateAssetPath = NormalizeAssetPath(duplicateAssetPath);
        if (!IsSupportedImageAsset(duplicateAssetPath)
            || !TryFindOriginalAssetPath(duplicateAssetPath, out string originalAssetPath)
            || !File.Exists(AssetPathToAbsolutePath(duplicateAssetPath))
            || !File.Exists(AssetPathToAbsolutePath(originalAssetPath)))
        {
            return;
        }

        bool shouldReplace = EditorUtility.DisplayDialog(
            "替换同名图片？",
            "Project 中检测到同名图片导入：\n\n" +
            "已有资源：\n" + originalAssetPath + "\n\n" +
            "刚导入的序号副本：\n" + duplicateAssetPath + "\n\n" +
            "是否用刚导入的图片替换已有资源？\n确认后会保留已有资源 GUID，并删除这个序号副本。",
            "替换已有资源",
            "保留序号副本");

        if (!shouldReplace)
        {
            return;
        }

        string duplicateAbsolutePath = AssetPathToAbsolutePath(duplicateAssetPath);
        string originalAbsolutePath = AssetPathToAbsolutePath(originalAssetPath);

        File.Copy(duplicateAbsolutePath, originalAbsolutePath, true);
        AssetDatabase.ImportAsset(originalAssetPath, ImportAssetOptions.ForceSynchronousImport);

        if (!AssetDatabase.DeleteAsset(duplicateAssetPath))
        {
            Debug.LogWarning("Unable to delete duplicate imported image: " + duplicateAssetPath);
        }
    }

    private static bool TryFindOriginalAssetPath(
        string duplicateAssetPath,
        out string originalAssetPath)
    {
        originalAssetPath = null;

        string directory = Path.GetDirectoryName(duplicateAssetPath);
        string extension = Path.GetExtension(duplicateAssetPath);
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(duplicateAssetPath);
        if (string.IsNullOrEmpty(directory)
            || string.IsNullOrEmpty(extension)
            || string.IsNullOrEmpty(fileNameWithoutExtension))
        {
            return false;
        }

        if (!TryResolveOriginalBaseName(
                directory,
                extension,
                fileNameWithoutExtension,
                out string baseName))
        {
            return false;
        }

        originalAssetPath = NormalizeAssetPath(directory + "/" + baseName + extension);
        return true;
    }

    private static bool TryResolveOriginalBaseName(
        string directory,
        string extension,
        string fileNameWithoutExtension,
        out string baseName)
    {
        baseName = StripUnityDuplicateSuffix(fileNameWithoutExtension);
        if (!string.IsNullOrEmpty(baseName)
            && !string.Equals(baseName, fileNameWithoutExtension, StringComparison.Ordinal)
            && AssetExists(directory, baseName, extension))
        {
            return true;
        }

        return false;
    }

    private static string StripUnityDuplicateSuffix(string fileNameWithoutExtension)
    {
        Match spaceMatch = Regex.Match(fileNameWithoutExtension, @"^(.*) [1-9][0-9]*$");
        if (spaceMatch.Success)
        {
            return spaceMatch.Groups[1].Value;
        }

        Match underscoreMatch = Regex.Match(fileNameWithoutExtension, @"^(.*)_[1-9][0-9]*$");
        if (underscoreMatch.Success)
        {
            return underscoreMatch.Groups[1].Value;
        }

        Match directNumberMatch = Regex.Match(fileNameWithoutExtension, @"^(.*?)[1-9][0-9]*$");
        if (directNumberMatch.Success)
        {
            return directNumberMatch.Groups[1].Value;
        }

        return fileNameWithoutExtension;
    }

    private static bool AssetExists(string directory, string fileNameWithoutExtension, string extension)
    {
        string candidatePath = NormalizeAssetPath(
            directory + "/" + fileNameWithoutExtension + extension);
        return AssetDatabase.LoadMainAssetAtPath(candidatePath) != null
            || File.Exists(AssetPathToAbsolutePath(candidatePath));
    }

    private static bool IsSupportedImageAsset(string assetPath)
    {
        return !string.IsNullOrEmpty(assetPath)
            && assetPath.StartsWith(UIAssetRoot, StringComparison.OrdinalIgnoreCase)
            && SupportedImageExtensions.Contains(Path.GetExtension(assetPath));
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return string.IsNullOrEmpty(assetPath) ? assetPath : assetPath.Replace('\\', '/');
    }

    private static string AssetPathToAbsolutePath(string assetPath)
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
    }

    private static string GetSelectedProjectFolderPath()
    {
        UnityEngine.Object selectedObject = Selection.activeObject;
        if (selectedObject == null)
        {
            return null;
        }

        string assetPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(selectedObject));
        if (string.IsNullOrEmpty(assetPath))
        {
            return null;
        }

        if (AssetDatabase.IsValidFolder(assetPath))
        {
            return assetPath.StartsWith(UIAssetRoot, StringComparison.OrdinalIgnoreCase)
                ? assetPath
                : null;
        }

        if (!IsSupportedImageAsset(assetPath))
        {
            return null;
        }

        return NormalizeAssetPath(Path.GetDirectoryName(assetPath));
    }
}
