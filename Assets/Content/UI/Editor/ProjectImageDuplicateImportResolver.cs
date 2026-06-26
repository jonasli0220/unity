using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
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

    private class DuplicateImportResolution
    {
        public string DuplicateAssetPath;
        public string OriginalAssetPath;
    }

    private class PendingExternalProjectDrop
    {
        public List<string> ExternalImagePaths;
        public string TargetFolder;
        public HashSet<string> ExistingTargetAssetPaths;
        public double Time;
    }

    private const double PendingExternalProjectDropSeconds = 30d;

    private static bool isDelayCallRegistered;
    private static bool isProcessing;
    private static PendingExternalProjectDrop pendingExternalProjectDrop;

    [InitializeOnLoadMethod]
    private static void InitProjectImageDuplicateImportResolver()
    {
        EditorApplication.projectWindowItemOnGUI -= HandleProjectWindowExternalImageDrag;
        EditorApplication.projectWindowItemOnGUI += HandleProjectWindowExternalImageDrag;
        EditorApplication.update -= TrackProjectWindowExternalImageDrag;
        EditorApplication.update += TrackProjectWindowExternalImageDrag;
    }

    private static void TrackProjectWindowExternalImageDrag()
    {
        List<string> externalImagePaths = new List<string>();
        if (!TryGetExternalImagePaths(externalImagePaths)
            || !IsMouseOverProjectWindow())
        {
            return;
        }

        string targetFolder = GetActiveProjectFolderPath();
        if (string.IsNullOrEmpty(targetFolder)
            || !targetFolder.StartsWith(UIAssetRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        pendingExternalProjectDrop = CreatePendingExternalProjectDrop(
            externalImagePaths,
            targetFolder);
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

        pendingExternalProjectDrop = CreatePendingExternalProjectDrop(
            externalImagePaths,
            targetFolder);

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
            if (IsSupportedImageAsset(assetPath))
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

    private static PendingExternalProjectDrop CreatePendingExternalProjectDrop(
        List<string> externalImagePaths,
        string targetFolder)
    {
        PendingExternalProjectDrop pendingDrop = new PendingExternalProjectDrop
        {
            ExternalImagePaths = new List<string>(externalImagePaths),
            TargetFolder = NormalizeAssetPath(targetFolder),
            ExistingTargetAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Time = EditorApplication.timeSinceStartup
        };

        for (int i = 0; i < externalImagePaths.Count; i++)
        {
            string fileName = Path.GetFileName(externalImagePaths[i]);
            if (string.IsNullOrEmpty(fileName))
            {
                continue;
            }

            string targetAssetPath = NormalizeAssetPath(targetFolder + "/" + fileName);
            if (AssetDatabase.LoadMainAssetAtPath(targetAssetPath) != null
                || File.Exists(AssetPathToAbsolutePath(targetAssetPath)))
            {
                pendingDrop.ExistingTargetAssetPaths.Add(targetAssetPath);
            }
        }

        return pendingDrop;
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
            if (!IsSupportedImageAsset(assetPath))
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
            List<DuplicateImportResolution> resolutions =
                ResolveDuplicateImports(candidatePaths);
            for (int i = 0; i < candidatePaths.Length; i++)
            {
                string candidatePath = NormalizeAssetPath(candidatePaths[i]);
                DuplicateImportResolution resolution = FindResolution(
                    resolutions,
                    candidatePath);
                if (resolution == null)
                {
                    continue;
                }

                try
                {
                    TryResolveDuplicateImport(
                        resolution.DuplicateAssetPath,
                        resolution.OriginalAssetPath);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        "Unable to resolve duplicate Project image import: " +
                        candidatePath +
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

    private static List<DuplicateImportResolution> ResolveDuplicateImports(
        string[] candidatePaths)
    {
        List<DuplicateImportResolution> resolutions = new List<DuplicateImportResolution>();
        HashSet<string> resolvedDuplicatePaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddExactExternalDropResolutions(
            candidatePaths,
            resolvedDuplicatePaths,
            resolutions);

        for (int i = 0; i < candidatePaths.Length; i++)
        {
            string duplicateAssetPath = NormalizeAssetPath(candidatePaths[i]);
            if (resolvedDuplicatePaths.Contains(duplicateAssetPath))
            {
                continue;
            }

            if (TryFindOriginalAssetPath(
                    duplicateAssetPath,
                    out string originalAssetPath))
            {
                resolutions.Add(
                    new DuplicateImportResolution
                    {
                        DuplicateAssetPath = duplicateAssetPath,
                        OriginalAssetPath = originalAssetPath
                    });
                resolvedDuplicatePaths.Add(duplicateAssetPath);
            }
        }

        return resolutions;
    }

    private static void AddExactExternalDropResolutions(
        string[] candidatePaths,
        HashSet<string> resolvedDuplicatePaths,
        List<DuplicateImportResolution> resolutions)
    {
        PendingExternalProjectDrop pendingDrop = pendingExternalProjectDrop;
        if (pendingDrop == null
            || EditorApplication.timeSinceStartup - pendingDrop.Time >
                PendingExternalProjectDropSeconds)
        {
            return;
        }

        string targetFolder = NormalizeAssetPath(pendingDrop.TargetFolder);
        for (int i = 0; i < pendingDrop.ExternalImagePaths.Count; i++)
        {
            string externalPath = pendingDrop.ExternalImagePaths[i];
            string fileName = Path.GetFileName(externalPath);
            if (string.IsNullOrEmpty(fileName))
            {
                continue;
            }

            string originalAssetPath = NormalizeAssetPath(targetFolder + "/" + fileName);
            if (!pendingDrop.ExistingTargetAssetPaths.Contains(originalAssetPath))
            {
                continue;
            }

            string duplicateAssetPath = FindImportedDuplicateForExternalFile(
                externalPath,
                originalAssetPath,
                candidatePaths,
                resolvedDuplicatePaths);
            if (string.IsNullOrEmpty(duplicateAssetPath))
            {
                continue;
            }

            resolutions.Add(
                new DuplicateImportResolution
                {
                    DuplicateAssetPath = duplicateAssetPath,
                    OriginalAssetPath = originalAssetPath
                });
            resolvedDuplicatePaths.Add(duplicateAssetPath);
        }
    }

    private static string FindImportedDuplicateForExternalFile(
        string externalPath,
        string originalAssetPath,
        string[] candidatePaths,
        HashSet<string> resolvedDuplicatePaths)
    {
        string externalHash = ComputeFileHash(externalPath);
        if (string.IsNullOrEmpty(externalHash))
        {
            return null;
        }

        string originalFolder = NormalizeAssetPath(Path.GetDirectoryName(originalAssetPath));
        for (int i = 0; i < candidatePaths.Length; i++)
        {
            string candidatePath = NormalizeAssetPath(candidatePaths[i]);
            if (resolvedDuplicatePaths.Contains(candidatePath)
                || string.Equals(candidatePath, originalAssetPath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    NormalizeAssetPath(Path.GetDirectoryName(candidatePath)),
                    originalFolder,
                    StringComparison.OrdinalIgnoreCase)
                || !IsSupportedImageAsset(candidatePath))
            {
                continue;
            }

            string candidateHash = ComputeFileHash(AssetPathToAbsolutePath(candidatePath));
            if (string.Equals(candidateHash, externalHash, StringComparison.OrdinalIgnoreCase))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private static DuplicateImportResolution FindResolution(
        List<DuplicateImportResolution> resolutions,
        string duplicateAssetPath)
    {
        for (int i = 0; i < resolutions.Count; i++)
        {
            if (string.Equals(
                    resolutions[i].DuplicateAssetPath,
                    duplicateAssetPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return resolutions[i];
            }
        }

        return null;
    }

    private static void TryResolveDuplicateImport(
        string duplicateAssetPath,
        string originalAssetPath)
    {
        duplicateAssetPath = NormalizeAssetPath(duplicateAssetPath);
        originalAssetPath = NormalizeAssetPath(originalAssetPath);
        if (!IsSupportedImageAsset(duplicateAssetPath)
            || !IsSupportedImageAsset(originalAssetPath)
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

    private static string AbsolutePathToAssetPath(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
        {
            return null;
        }

        string projectRoot = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath));
        string fullPath = Path.GetFullPath(absolutePath);
        if (!fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string relativePath = fullPath.Substring(projectRoot.Length).TrimStart(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return NormalizeAssetPath(relativePath);
    }

    private static string ComputeFileHash(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        using (SHA256 sha256 = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
        {
            byte[] hashBytes = sha256.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", string.Empty);
        }
    }

    private static bool IsMouseOverProjectWindow()
    {
        EditorWindow mouseOverWindow = EditorWindow.mouseOverWindow;
        if (mouseOverWindow == null)
        {
            return false;
        }

        Type projectBrowserType = Type.GetType("UnityEditor.ProjectBrowser,UnityEditor");
        return projectBrowserType != null && projectBrowserType.IsInstanceOfType(mouseOverWindow);
    }

    private static string GetActiveProjectFolderPath()
    {
        Type projectBrowserType = Type.GetType("UnityEditor.ProjectBrowser,UnityEditor");
        if (projectBrowserType == null)
        {
            return GetSelectedProjectFolderPath();
        }

        object projectBrowser = null;
        FieldInfo lastInteractedBrowserField = projectBrowserType.GetField(
            "s_LastInteractedProjectBrowser",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (lastInteractedBrowserField != null)
        {
            projectBrowser = lastInteractedBrowserField.GetValue(null);
        }

        if (projectBrowser == null && EditorWindow.mouseOverWindow != null
            && projectBrowserType.IsInstanceOfType(EditorWindow.mouseOverWindow))
        {
            projectBrowser = EditorWindow.mouseOverWindow;
        }

        if (projectBrowser != null)
        {
            MethodInfo getActiveFolderPathMethod = projectBrowserType.GetMethod(
                "GetActiveFolderPath",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (getActiveFolderPathMethod != null)
            {
                string activeFolderPath = NormalizeAssetPath(
                    getActiveFolderPathMethod.Invoke(projectBrowser, null) as string);
                if (!string.IsNullOrEmpty(activeFolderPath)
                    && AssetDatabase.IsValidFolder(activeFolderPath))
                {
                    return activeFolderPath;
                }
            }
        }

        return GetSelectedProjectFolderPath();
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
