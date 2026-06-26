using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class ProjectImageDuplicateImportResolver : AssetPostprocessor
{
    private const string UIAssetRoot = "Assets/Content/UI/";

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

        string baseName = StripUnityDuplicateSuffix(fileNameWithoutExtension);
        if (string.IsNullOrEmpty(baseName)
            || string.Equals(baseName, fileNameWithoutExtension, StringComparison.Ordinal))
        {
            return false;
        }

        string candidatePath = NormalizeAssetPath(directory + "/" + baseName + extension);
        if (AssetDatabase.LoadMainAssetAtPath(candidatePath) == null
            && !File.Exists(AssetPathToAbsolutePath(candidatePath)))
        {
            return false;
        }

        originalAssetPath = candidatePath;
        return true;
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

        return fileNameWithoutExtension;
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
}
