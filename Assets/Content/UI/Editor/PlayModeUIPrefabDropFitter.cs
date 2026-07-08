using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class PlayModeUIPrefabDropFitter
{
    private const string UIPrefabFolderMarker = "/Content/UI/Prefab/";
    private const string AutoFitMenuPath =
        "UITools/\u8fd0\u884c\u65f6\u62d6\u5165UI Prefab\u81ea\u52a8\u94fa\u6ee1\u7236\u7ea7";
    private const string ManualFitMenuPath =
        "UITools/\u9009\u4e2dUI\u94fa\u6ee1\u7236\u7ea7";
    private const string AutoFitEnabledEditorPrefKey =
        "SgrProject.UI.PlayModeUIPrefabDropFitter.Enabled";
    private const float ZeroTolerance = 0.001f;

    private static readonly HashSet<int> ProcessedInstanceIds = new HashSet<int>();

    private static bool isSelectionProcessQueued;

    static PlayModeUIPrefabDropFitter()
    {
        if (!EditorPrefs.HasKey(AutoFitEnabledEditorPrefKey))
        {
            EditorPrefs.SetBool(AutoFitEnabledEditorPrefKey, true);
        }

        Selection.selectionChanged -= QueueSelectionProcess;
        Selection.selectionChanged += QueueSelectionProcess;
        EditorApplication.hierarchyChanged -= QueueSelectionProcess;
        EditorApplication.hierarchyChanged += QueueSelectionProcess;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem(AutoFitMenuPath)]
    private static void ToggleAutoFit()
    {
        bool enabled = !IsAutoFitEnabled();
        EditorPrefs.SetBool(AutoFitEnabledEditorPrefKey, enabled);
        Menu.SetChecked(AutoFitMenuPath, enabled);
        if (enabled)
        {
            QueueSelectionProcess();
        }
    }

    [MenuItem(AutoFitMenuPath, true)]
    private static bool ValidateToggleAutoFit()
    {
        Menu.SetChecked(AutoFitMenuPath, IsAutoFitEnabled());
        return true;
    }

    [MenuItem(ManualFitMenuPath)]
    private static void FitSelectedUIToParent()
    {
        foreach (GameObject selectedObject in Selection.gameObjects)
        {
            RectTransform rectTransform = selectedObject != null
                ? selectedObject.transform as RectTransform
                : null;
            if (rectTransform == null || ResolveFitParent(rectTransform) == null)
            {
                continue;
            }

            FitToParent(rectTransform, "Fit UI To Parent");
        }

        SceneView.RepaintAll();
    }

    [MenuItem(ManualFitMenuPath, true)]
    private static bool ValidateFitSelectedUIToParent()
    {
        foreach (GameObject selectedObject in Selection.gameObjects)
        {
            RectTransform rectTransform = selectedObject != null
                ? selectedObject.transform as RectTransform
                : null;
            if (rectTransform != null && ResolveFitParent(rectTransform) != null)
            {
                return true;
            }
        }

        return false;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode ||
            state == PlayModeStateChange.EnteredEditMode)
        {
            ProcessedInstanceIds.Clear();
        }
    }

    private static bool IsAutoFitEnabled()
    {
        return EditorPrefs.GetBool(AutoFitEnabledEditorPrefKey, true);
    }

    private static void QueueSelectionProcess()
    {
        if (isSelectionProcessQueued)
        {
            return;
        }

        isSelectionProcessQueued = true;
        EditorApplication.delayCall += ProcessSelectedDroppedPrefabs;
    }

    private static void ProcessSelectedDroppedPrefabs()
    {
        isSelectionProcessQueued = false;
        if (!EditorApplication.isPlaying || !IsAutoFitEnabled())
        {
            return;
        }

        foreach (GameObject selectedObject in Selection.gameObjects)
        {
            TryAutoFitDroppedPrefab(selectedObject);
        }

        SceneView.RepaintAll();
    }

    private static void TryAutoFitDroppedPrefab(GameObject selectedObject)
    {
        if (selectedObject == null || EditorUtility.IsPersistent(selectedObject))
        {
            return;
        }

        GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(selectedObject);
        if (prefabRoot == null || prefabRoot != selectedObject)
        {
            return;
        }

        int instanceId = prefabRoot.GetInstanceID();
        if (ProcessedInstanceIds.Contains(instanceId))
        {
            return;
        }

        RectTransform rectTransform = prefabRoot.transform as RectTransform;
        if (rectTransform == null ||
            ResolveFitParent(rectTransform) == null ||
            !IsUIPrefabAssetInstance(prefabRoot) ||
            !LooksLikeCollapsedUIPrefabRoot(rectTransform))
        {
            return;
        }

        FitToParent(rectTransform, "Auto Fit Dropped UI Prefab");
        ProcessedInstanceIds.Add(instanceId);
    }

    private static bool IsUIPrefabAssetInstance(GameObject prefabRoot)
    {
        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabRoot);
        if (string.IsNullOrEmpty(prefabPath))
        {
            GameObject prefabSource =
                PrefabUtility.GetCorrespondingObjectFromSource(prefabRoot);
            prefabPath = prefabSource != null
                ? AssetDatabase.GetAssetPath(prefabSource)
                : string.Empty;
        }

        if (string.IsNullOrEmpty(prefabPath))
        {
            return false;
        }

        string normalizedPath = prefabPath.Replace('\\', '/');
        return normalizedPath.IndexOf(
            UIPrefabFolderMarker,
            StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool LooksLikeCollapsedUIPrefabRoot(RectTransform rectTransform)
    {
        if (IsNearlyZero(rectTransform.localScale.x) &&
            IsNearlyZero(rectTransform.localScale.y) &&
            IsNearlyZero(rectTransform.localScale.z))
        {
            return true;
        }

        return IsNearlyZero(rectTransform.sizeDelta.x) &&
               IsNearlyZero(rectTransform.sizeDelta.y) &&
               IsNearlyEqual(rectTransform.anchorMin, rectTransform.anchorMax) &&
               IsNearlyEqual(rectTransform.anchorMin, Vector2.zero);
    }

    private static RectTransform ResolveFitParent(RectTransform rectTransform)
    {
        Transform current = rectTransform != null ? rectTransform.parent : null;
        while (current != null)
        {
            RectTransform parentRect = current as RectTransform;
            if (parentRect != null)
            {
                return parentRect;
            }

            current = current.parent;
        }

        return null;
    }

    private static void FitToParent(RectTransform rectTransform, string undoName)
    {
        Undo.RecordObject(rectTransform, undoName);

        rectTransform.localRotation = Quaternion.identity;
        rectTransform.localScale = Vector3.one;
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        Vector3 anchoredPosition = rectTransform.anchoredPosition3D;
        anchoredPosition.z = 0f;
        rectTransform.anchoredPosition3D = anchoredPosition;

        if (PrefabUtility.IsPartOfPrefabInstance(rectTransform))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(rectTransform);
        }

        EditorUtility.SetDirty(rectTransform);
    }

    private static bool IsNearlyEqual(Vector2 lhs, Vector2 rhs)
    {
        return IsNearlyZero(lhs.x - rhs.x) && IsNearlyZero(lhs.y - rhs.y);
    }

    private static bool IsNearlyZero(float value)
    {
        return Mathf.Abs(value) <= ZeroTolerance;
    }
}
