using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

using AdaptationLocking = UnityEngine.UI.AdaptationLocking;
using CanvasScaler = UnityEngine.UI.CanvasScaler;

[InitializeOnLoad]
public static class UIPcAdaptationPreviewController
{
    public const float PreviewScaleFactor = 0.8f;
    private const string ToggleMenuPath = "Tools/UI/PC Adaptation Preview/Toggle PC 0.8";

    private static readonly List<CanvasScalerSnapshot> ActiveSnapshots = new List<CanvasScalerSnapshot>();
    private static readonly MethodInfo GetRemovedComponentsMethod =
        typeof(PrefabUtility).GetMethod("GetRemovedComponents", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject) }, null);
    private static double lastWarningTime;
    private static double nextMaintainTime;
    private static double transientStatusUntil;
    private static string transientStatusText;

    public static event Action PreviewStateChanged;

    static UIPcAdaptationPreviewController()
    {
        AssemblyReloadEvents.beforeAssemblyReload += EndPreview;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.quitting += EndPreview;
    }

    public static bool IsPreviewing
    {
        get { return ActiveSnapshots.Count > 0; }
    }

    public static string TransientStatusText
    {
        get
        {
            if (EditorApplication.timeSinceStartup <= transientStatusUntil)
            {
                return transientStatusText;
            }

            return null;
        }
    }

    public static bool HasPreviewTargets()
    {
        return CollectPreviewTargets().Count > 0 || CollectTemporaryScalerTargets().Count > 0;
    }

    public static bool BeginPreview()
    {
        if (IsPreviewing)
        {
            return true;
        }

        bool cleanedPreviewArtifacts = CleanOrphanPreviewArtifacts();

        List<CanvasScaler> targets = CollectPreviewTargets();
        List<Canvas> temporaryScalerTargets = null;
        if (targets.Count == 0)
        {
            temporaryScalerTargets = CollectTemporaryScalerTargets();
            if (temporaryScalerTargets.Count == 0 && cleanedPreviewArtifacts)
            {
                RefreshSceneViews();
                targets = CollectPreviewTargets();
                if (targets.Count == 0)
                {
                    temporaryScalerTargets = CollectTemporaryScalerTargets();
                }
            }

            if (targets.Count == 0 && temporaryScalerTargets.Count == 0)
            {
                WarnNoTargets();
                return false;
            }
        }

        try
        {
            ActiveSnapshots.Clear();

            for (int i = 0; i < targets.Count; i++)
            {
                CanvasScaler scaler = targets[i];
                CanvasScalerSnapshot snapshot = new CanvasScalerSnapshot
                {
                    scaler = scaler,
                    uiScaleMode = scaler.uiScaleMode,
                    scaleFactor = scaler.scaleFactor
                };

                CaptureLockingSnapshots(scaler, snapshot.lockings);
                CaptureBackgroundSnapshots(scaler, snapshot.backgrounds);
                ActiveSnapshots.Add(snapshot);
            }

            if (temporaryScalerTargets != null)
            {
                for (int i = 0; i < temporaryScalerTargets.Count; i++)
                {
                    Canvas canvas = temporaryScalerTargets[i];
                    if (canvas == null)
                    {
                        continue;
                    }

                    CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                    if (scaler == null)
                    {
                        scaler = canvas.gameObject.AddComponent<CanvasScaler>();
                        scaler.hideFlags = HideFlags.DontSave;
                    }

                    CanvasScalerSnapshot snapshot = new CanvasScalerSnapshot
                    {
                        scaler = scaler,
                        uiScaleMode = scaler.uiScaleMode,
                        scaleFactor = scaler.scaleFactor,
                        createdByPreview = true
                    };

                    CaptureLockingSnapshots(scaler, snapshot.lockings);
                    CaptureBackgroundSnapshots(scaler, snapshot.backgrounds);
                    ActiveSnapshots.Add(snapshot);
                }
            }

            for (int i = 0; i < ActiveSnapshots.Count; i++)
            {
                CanvasScalerSnapshot snapshot = ActiveSnapshots[i];
                if (snapshot.scaler != null)
                {
                    snapshot.scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    snapshot.scaler.scaleFactor = PreviewScaleFactor;
                    Debug.Log("PC adaptation preview target: " + DescribeSnapshot(snapshot));
                }
            }

            for (int i = 0; i < ActiveSnapshots.Count; i++)
            {
                ApplyLockingPreview(ActiveSnapshots[i]);
                ApplyBackgroundPreview(ActiveSnapshots[i]);
            }

            SetTransientStatus(null, 0d);
            Debug.Log($"PC adaptation preview is ON. CanvasScaler count: {ActiveSnapshots.Count}. Temporary CanvasScaler count: {CountTemporaryScalers()}. Click PC ON again to restore.");
            if (!NotifySelectionAncestorLocking())
            {
                NotifySceneView("PC preview ON");
            }

            RefreshSceneViews();
            PreviewStateChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("PC adaptation preview failed: " + ex);
            EndPreview();
            return false;
        }
    }

    public static void EndPreview()
    {
        if (!IsPreviewing)
        {
            return;
        }

        try
        {
            for (int i = 0; i < ActiveSnapshots.Count; i++)
            {
                CanvasScalerSnapshot snapshot = ActiveSnapshots[i];
                if (snapshot.scaler != null && !snapshot.createdByPreview)
                {
                    snapshot.scaler.uiScaleMode = snapshot.uiScaleMode;
                    snapshot.scaler.scaleFactor = snapshot.scaleFactor;
                }
            }

            for (int i = 0; i < ActiveSnapshots.Count; i++)
            {
                CanvasScalerSnapshot snapshot = ActiveSnapshots[i];
                for (int j = 0; j < snapshot.lockings.Count; j++)
                {
                    RestoreLocking(snapshot.lockings[j]);
                }

                for (int j = 0; j < snapshot.backgrounds.Count; j++)
                {
                    RestoreBackground(snapshot.backgrounds[j]);
                }
            }

            for (int i = 0; i < ActiveSnapshots.Count; i++)
            {
                CanvasScalerSnapshot snapshot = ActiveSnapshots[i];
                if (snapshot.createdByPreview && snapshot.scaler != null)
                {
                    DestroyTemporaryScaler(snapshot.scaler);
                }
            }
        }
        finally
        {
            ActiveSnapshots.Clear();
            SetTransientStatus("PC OFF", 1.2d);
            NotifySceneView("PC preview OFF");
            RefreshSceneViews();
            PreviewStateChanged?.Invoke();
        }
    }

    [MenuItem(ToggleMenuPath, false, 2308)]
    public static void TogglePreview()
    {
        if (IsPreviewing)
        {
            EndPreview();
            return;
        }

        BeginPreview();
    }

    [MenuItem(ToggleMenuPath, true)]
    private static bool ValidateTogglePreview()
    {
        Menu.SetChecked(ToggleMenuPath, IsPreviewing);
        return true;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        EndPreview();
    }

    private static void OnEditorUpdate()
    {
        if (!IsPreviewing || EditorApplication.timeSinceStartup < nextMaintainTime)
        {
            return;
        }

        nextMaintainTime = EditorApplication.timeSinceStartup + 0.1d;
        for (int i = 0; i < ActiveSnapshots.Count; i++)
        {
            CanvasScalerSnapshot snapshot = ActiveSnapshots[i];
            if (snapshot.scaler == null)
            {
                continue;
            }

            snapshot.scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            snapshot.scaler.scaleFactor = PreviewScaleFactor;
            ApplyLockingPreview(snapshot);
            ApplyBackgroundPreview(snapshot);
        }

    }

    private static List<CanvasScaler> CollectPreviewTargets()
    {
        List<CanvasScaler> result = new List<CanvasScaler>();
        HashSet<int> seen = new HashSet<int>();
        PrefabStage currentPrefabStage = PrefabStageUtility.GetCurrentPrefabStage();

        if (currentPrefabStage != null && currentPrefabStage.prefabContentsRoot != null)
        {
            CanvasScaler stageScaler = FindPrefabStageEnvironmentScaler(currentPrefabStage);
            if (stageScaler != null)
            {
                AddTarget(result, seen, stageScaler);
            }

            return result;
        }

        GameObject[] selectedObjects = Selection.gameObjects;
        for (int i = 0; i < selectedObjects.Length; i++)
        {
            GameObject selectedObject = selectedObjects[i];
            if (selectedObject == null)
            {
                continue;
            }

            CanvasScaler selectedScaler = FindCanvasScalerInParents(selectedObject.transform);
            if (selectedScaler == null || !IsSceneObject(selectedScaler))
            {
                continue;
            }

            AddTarget(result, seen, selectedScaler);
        }

        if (result.Count > 0)
        {
            return result;
        }

        CanvasScaler[] allScalers = Resources.FindObjectsOfTypeAll<CanvasScaler>();
        for (int i = 0; i < allScalers.Length; i++)
        {
            CanvasScaler scaler = allScalers[i];
            if (!IsSceneObject(scaler))
            {
                continue;
            }

            if (currentPrefabStage != null && scaler.gameObject.scene != currentPrefabStage.scene)
            {
                continue;
            }

            AddTarget(result, seen, scaler);
        }

        return result;
    }

    private static List<Canvas> CollectTemporaryScalerTargets()
    {
        List<Canvas> result = new List<Canvas>();
        HashSet<int> seen = new HashSet<int>();
        PrefabStage currentPrefabStage = PrefabStageUtility.GetCurrentPrefabStage();

        if (currentPrefabStage != null && currentPrefabStage.prefabContentsRoot != null)
        {
            Canvas environmentCanvas = FindPrefabStageEnvironmentCanvas(currentPrefabStage);
            if (environmentCanvas != null)
            {
                AddTemporaryScalerTarget(result, seen, environmentCanvas);
            }
            else
            {
                AddTemporaryScalerTarget(result, seen, FindPrefabStagePrefabCanvas(currentPrefabStage));
            }

            return result;
        }

        GameObject[] selectedObjects = Selection.gameObjects;
        for (int i = 0; i < selectedObjects.Length; i++)
        {
            GameObject selectedObject = selectedObjects[i];
            if (selectedObject == null)
            {
                continue;
            }

            AddTemporaryScalerTarget(result, seen, FindCanvasInParents(selectedObject.transform));
        }

        if (result.Count > 0)
        {
            return result;
        }

        Canvas[] allCanvases = Resources.FindObjectsOfTypeAll<Canvas>();
        for (int i = 0; i < allCanvases.Length; i++)
        {
            Canvas canvas = allCanvases[i];
            if (!CanAddTemporaryScaler(canvas))
            {
                continue;
            }

            if (currentPrefabStage != null && canvas.gameObject.scene != currentPrefabStage.scene)
            {
                continue;
            }

            AddTemporaryScalerTarget(result, seen, canvas);
        }

        return result;
    }

    private static void AddTemporaryScalerTarget(List<Canvas> targets, HashSet<int> seen, Canvas canvas)
    {
        Canvas scalerCanvas = GetRootCanvas(canvas);
        if (!CanAddTemporaryScaler(scalerCanvas))
        {
            return;
        }

        int id = scalerCanvas.GetInstanceID();
        if (seen.Contains(id))
        {
            return;
        }

        seen.Add(id);
        targets.Add(scalerCanvas);
    }

    private static bool CanAddTemporaryScaler(Canvas canvas)
    {
        return canvas != null
            && canvas.isActiveAndEnabled
            && canvas.GetComponent<CanvasScaler>() == null
            && IsSceneObject(canvas);
    }

    private static Canvas GetRootCanvas(Canvas canvas)
    {
        if (canvas == null)
        {
            return null;
        }

        Canvas rootCanvas = null;
        Transform current = canvas.transform;
        while (current != null)
        {
            Canvas parentCanvas = current.GetComponent<Canvas>();
            if (parentCanvas != null)
            {
                rootCanvas = parentCanvas;
            }

            current = current.parent;
        }

        return rootCanvas != null ? rootCanvas : canvas;
    }

    private static CanvasScaler GetRootCanvasScaler(Canvas canvas)
    {
        Canvas rootCanvas = GetRootCanvas(canvas);
        return rootCanvas != null ? rootCanvas.GetComponent<CanvasScaler>() : null;
    }

    private static CanvasScaler GetRootCanvasScaler(CanvasScaler scaler)
    {
        if (scaler == null)
        {
            return null;
        }

        Canvas canvas = scaler.GetComponent<Canvas>() ?? scaler.GetComponentInParent<Canvas>(true);
        return GetRootCanvasScaler(canvas);
    }

    private static Canvas FindPrefabStagePrefabCanvas(PrefabStage prefabStage)
    {
        if (prefabStage == null || prefabStage.prefabContentsRoot == null)
        {
            return null;
        }

        GameObject prefabRoot = prefabStage.prefabContentsRoot;
        Canvas rootCanvas = prefabRoot.GetComponent<Canvas>();
        if (CanAddTemporaryScaler(rootCanvas))
        {
            return rootCanvas;
        }

        Transform namedRoot = prefabRoot.transform.Find("root");
        if (namedRoot != null)
        {
            Canvas namedRootCanvas = namedRoot.GetComponent<Canvas>();
            if (CanAddTemporaryScaler(namedRootCanvas))
            {
                return namedRootCanvas;
            }
        }

        return null;
    }

    private static CanvasScaler FindPrefabStageEnvironmentScaler(PrefabStage prefabStage)
    {
        Transform prefabRoot = prefabStage.prefabContentsRoot.transform;
        CanvasScaler parentScaler = FindCanvasScalerInParents(prefabRoot);
        parentScaler = GetRootCanvasScaler(parentScaler);
        if (IsPrefabStageEnvironmentObject(prefabStage, parentScaler)
            && !IsTransformUnder(parentScaler.transform, prefabRoot))
        {
            return parentScaler;
        }

        CanvasScaler fallback = null;
        CanvasScaler[] allScalers = Resources.FindObjectsOfTypeAll<CanvasScaler>();
        for (int i = 0; i < allScalers.Length; i++)
        {
            CanvasScaler scaler = allScalers[i];
            if (!CanPreviewScaler(scaler)
                || !IsPrefabStageEnvironmentObject(prefabStage, scaler)
                || IsTransformUnder(scaler.transform, prefabRoot))
            {
                continue;
            }

            Canvas canvas = scaler.GetComponent<Canvas>() ?? scaler.GetComponentInParent<Canvas>(true);
            CanvasScaler rootScaler = GetRootCanvasScaler(canvas);
            if (rootScaler == null || !CanPreviewScaler(rootScaler))
            {
                continue;
            }

            if (IsEnvironmentCanvas(canvas))
            {
                return rootScaler;
            }

            if (fallback == null)
            {
                fallback = rootScaler;
            }
        }

        return fallback;
    }

    private static Canvas FindPrefabStageEnvironmentCanvas(PrefabStage prefabStage)
    {
        Transform prefabRoot = prefabStage.prefabContentsRoot.transform;
        Canvas parentCanvas = FindCanvasInParents(prefabRoot);
        if (IsPrefabStageEnvironmentObject(prefabStage, parentCanvas)
            && !IsTransformUnder(parentCanvas.transform, prefabRoot))
        {
            return parentCanvas;
        }

        Canvas fallback = null;
        Canvas[] allCanvases = Resources.FindObjectsOfTypeAll<Canvas>();
        for (int i = 0; i < allCanvases.Length; i++)
        {
            Canvas canvas = allCanvases[i];
            Canvas scalerCanvas = GetRootCanvas(canvas);
            if (!CanAddTemporaryScaler(scalerCanvas)
                || !IsPrefabStageEnvironmentObject(prefabStage, canvas)
                || IsTransformUnder(canvas.transform, prefabRoot))
            {
                continue;
            }

            if (IsEnvironmentCanvas(canvas))
            {
                return canvas;
            }

            if (fallback == null)
            {
                fallback = canvas;
            }
        }

        return fallback;
    }

    private static bool IsPrefabStageEnvironmentObject(PrefabStage prefabStage, Component component)
    {
        return component != null && IsPrefabStageEnvironmentObject(prefabStage, component.gameObject);
    }

    private static bool IsPrefabStageEnvironmentObject(PrefabStage prefabStage, GameObject gameObject)
    {
        if (prefabStage == null || prefabStage.prefabContentsRoot == null || gameObject == null)
        {
            return false;
        }

        if (gameObject.scene == prefabStage.scene)
        {
            return true;
        }

        if (gameObject.name.IndexOf("Environment", StringComparison.OrdinalIgnoreCase) >= 0
            || gameObject.name.IndexOf("环境", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return StageUtility.GetStageHandle(gameObject) == StageUtility.GetCurrentStageHandle();
    }

    private static bool IsEnvironmentCanvas(Canvas canvas)
    {
        if (canvas == null)
        {
            return false;
        }

        return canvas.name.IndexOf("Environment", StringComparison.OrdinalIgnoreCase) >= 0
            || canvas.name.IndexOf("环境", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsTransformUnder(Transform child, Transform parent)
    {
        Transform current = child;
        while (current != null)
        {
            if (current == parent)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static void AddTarget(List<CanvasScaler> targets, HashSet<int> seen, CanvasScaler scaler)
    {
        scaler = GetRootCanvasScaler(scaler);
        if (!CanPreviewScaler(scaler))
        {
            return;
        }

        int id = scaler.GetInstanceID();
        if (seen.Contains(id))
        {
            return;
        }

        seen.Add(id);
        targets.Add(scaler);
    }

    private static bool CanPreviewScaler(CanvasScaler scaler)
    {
        return scaler != null
            && scaler.isActiveAndEnabled
            && IsRootCanvasScaler(scaler)
            && IsSceneObject(scaler);
    }

    private static bool IsRootCanvasScaler(CanvasScaler scaler)
    {
        if (scaler == null)
        {
            return false;
        }

        Canvas canvas = scaler.GetComponent<Canvas>() ?? scaler.GetComponentInParent<Canvas>(true);
        return canvas != null && GetRootCanvas(canvas) == canvas;
    }

    private static bool IsSceneObject(Component component)
    {
        if (component == null || EditorUtility.IsPersistent(component))
        {
            return false;
        }

        Scene scene = component.gameObject.scene;
        return scene.IsValid() && scene.isLoaded;
    }

    private static CanvasScaler FindCanvasScalerInParents(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            CanvasScaler scaler = current.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                return scaler;
            }

            current = current.parent;
        }

        return null;
    }

    private static Canvas FindCanvasInParents(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            Canvas canvas = current.GetComponent<Canvas>();
            if (canvas != null)
            {
                return canvas;
            }

            current = current.parent;
        }

        return null;
    }

    private static void CaptureLockingSnapshots(CanvasScaler scaler, List<LockingSnapshot> snapshots)
    {
        AdaptationLocking[] lockings = scaler.GetComponentsInChildren<AdaptationLocking>(true);
        for (int i = 0; i < lockings.Length; i++)
        {
            AdaptationLocking locking = lockings[i];
            if (locking == null || !locking.enabled)
            {
                continue;
            }

            if (IsRemovedPrefabOverride(locking))
            {
                Debug.Log("PC adaptation preview skipped removed AdaptationLocking: " + GetTransformPath(locking.transform));
                continue;
            }

            if (FindCanvasScalerInParents(locking.transform) != scaler)
            {
                continue;
            }

            snapshots.Add(new LockingSnapshot
            {
                locking = locking,
                localScale = locking.transform.localScale,
                lastZoom = locking._last_zoom,
                lastWidth = locking._last_width,
                lastHeight = locking._last_height,
                lastScaleFactor = locking._last_scaleFactor,
                canvasScaleMode = locking._CanvasScaleMode,
                cachedCanvasScaler = locking._canvasScaler
            });
        }
    }

    private static void CaptureBackgroundSnapshots(CanvasScaler scaler, List<BackgroundSnapshot> snapshots)
    {
        AutomaticBackground[] backgrounds = scaler.GetComponentsInChildren<AutomaticBackground>(true);
        for (int i = 0; i < backgrounds.Length; i++)
        {
            AutomaticBackground background = backgrounds[i];
            if (background == null
                || !background.enabled
                || FindCanvasScalerInParents(background.transform) != scaler)
            {
                continue;
            }

            snapshots.Add(new BackgroundSnapshot
            {
                background = background,
                localScale = background.transform.localScale,
                baseScreen = background._BaseScreen,
                lastWidth = background._last_width,
                lastHeight = background._last_height,
                lastSize = background._last_size,
                lastCanvasScalerSize = background._last_canvasscaler_size,
                lastZoom = background._last_zoom,
                canvasScaleMode = background._CanvasScaleMode,
                screenSize = background._ScreenSize,
                cachedRect = background._rect,
                cachedCanvasScaler = background._canvasScaler
            });
        }
    }

    private static bool CleanOrphanPreviewArtifacts()
    {
        bool cleaned = false;
        CanvasScaler[] scalers = Resources.FindObjectsOfTypeAll<CanvasScaler>();
        for (int i = 0; i < scalers.Length; i++)
        {
            CanvasScaler scaler = scalers[i];
            if (scaler == null
                || (scaler.hideFlags & HideFlags.DontSave) == HideFlags.None
                || !IsSceneObject(scaler))
            {
                continue;
            }

            Debug.Log("PC adaptation preview removed orphan temporary CanvasScaler: " + GetTransformPath(scaler.transform));
            UnityEngine.Object.DestroyImmediate(scaler);
            cleaned = true;
        }

        return cleaned;
    }

    private static string DescribeSnapshot(CanvasScalerSnapshot snapshot)
    {
        if (snapshot == null || snapshot.scaler == null)
        {
            return "<missing>";
        }

        Canvas scalerCanvas = snapshot.scaler.GetComponent<Canvas>();
        Canvas rootCanvas = GetRootCanvas(scalerCanvas);
        string scalerPath = GetTransformPath(snapshot.scaler.transform);
        string rootCanvasPath = rootCanvas != null ? GetTransformPath(rootCanvas.transform) : "<no root canvas>";
        string mode = snapshot.createdByPreview ? "temporary" : "existing";
        return $"{mode}, scaler={scalerPath}, rootCanvas={rootCanvasPath}";
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
        {
            return "<null>";
        }

        List<string> names = new List<string>();
        Transform current = transform;
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names.ToArray());
    }

    private static void ApplyLockingPreview(CanvasScalerSnapshot snapshot)
    {
        for (int i = 0; i < snapshot.lockings.Count; i++)
        {
            AdaptationLocking locking = snapshot.lockings[i].locking;
            if (locking == null || !locking.enabled || IsRemovedPrefabOverride(locking))
            {
                continue;
            }

            locking._canvasScaler = snapshot.scaler;
            locking.SetLocking();
        }
    }

    private static void ApplyBackgroundPreview(CanvasScalerSnapshot snapshot)
    {
        for (int i = 0; i < snapshot.backgrounds.Count; i++)
        {
            AutomaticBackground background = snapshot.backgrounds[i].background;
            if (background == null || !background.enabled)
            {
                continue;
            }

            background._canvasScaler = snapshot.scaler;
            background.SetAutoSize();
        }
    }

    private static void RestoreLocking(LockingSnapshot snapshot)
    {
        AdaptationLocking locking = snapshot.locking;
        if (locking == null)
        {
            return;
        }

        locking.transform.localScale = snapshot.localScale;
        locking._last_zoom = snapshot.lastZoom;
        locking._last_width = snapshot.lastWidth;
        locking._last_height = snapshot.lastHeight;
        locking._last_scaleFactor = snapshot.lastScaleFactor;
        locking._CanvasScaleMode = snapshot.canvasScaleMode;
        locking._canvasScaler = snapshot.cachedCanvasScaler;
    }

    private static void RestoreBackground(BackgroundSnapshot snapshot)
    {
        AutomaticBackground background = snapshot.background;
        if (background == null)
        {
            return;
        }

        background.transform.localScale = snapshot.localScale;
        background._BaseScreen = snapshot.baseScreen;
        background._last_width = snapshot.lastWidth;
        background._last_height = snapshot.lastHeight;
        background._last_size = snapshot.lastSize;
        background._last_canvasscaler_size = snapshot.lastCanvasScalerSize;
        background._last_zoom = snapshot.lastZoom;
        background._CanvasScaleMode = snapshot.canvasScaleMode;
        background._ScreenSize = snapshot.screenSize;
        background._rect = snapshot.cachedRect;
        background._canvasScaler = snapshot.cachedCanvasScaler;
    }

    private static bool IsRemovedPrefabOverride(Component component)
    {
        if (component == null || GetRemovedComponentsMethod == null)
        {
            return false;
        }

        Component directSourceComponent = PrefabUtility.GetCorrespondingObjectFromSource(component) as Component;
        Component originalSourceComponent = PrefabUtility.GetCorrespondingObjectFromOriginalSource(component) as Component;
        if (directSourceComponent == null && originalSourceComponent == null)
        {
            return false;
        }

        GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(component.gameObject);
        while (instanceRoot != null)
        {
            if (HasRemovedSourceComponent(instanceRoot, directSourceComponent)
                || HasRemovedSourceComponent(instanceRoot, originalSourceComponent))
            {
                return true;
            }

            Transform parent = instanceRoot.transform.parent;
            instanceRoot = parent != null
                ? PrefabUtility.GetNearestPrefabInstanceRoot(parent.gameObject)
                : null;
        }

        return false;
    }

    private static bool HasRemovedSourceComponent(GameObject prefabInstanceRoot, Component sourceComponent)
    {
        if (prefabInstanceRoot == null || sourceComponent == null)
        {
            return false;
        }

        object removedComponents = GetRemovedComponentsMethod.Invoke(null, new object[] { prefabInstanceRoot });
        if (!(removedComponents is System.Collections.IEnumerable enumerable))
        {
            return false;
        }

        foreach (object removedComponent in enumerable)
        {
            Component assetComponent = GetRemovedAssetComponent(removedComponent);
            if (assetComponent == sourceComponent)
            {
                return true;
            }
        }

        return false;
    }

    private static Component GetRemovedAssetComponent(object removedComponent)
    {
        if (removedComponent == null)
        {
            return null;
        }

        Type type = removedComponent.GetType();
        const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        PropertyInfo property = type.GetProperty("assetComponent", bindingFlags);
        if (property != null)
        {
            return property.GetValue(removedComponent, null) as Component;
        }

        FieldInfo field = type.GetField("assetComponent", bindingFlags);
        return field != null ? field.GetValue(removedComponent) as Component : null;
    }

    private static int CountTemporaryScalers()
    {
        int count = 0;
        for (int i = 0; i < ActiveSnapshots.Count; i++)
        {
            if (ActiveSnapshots[i].createdByPreview)
            {
                count++;
            }
        }

        return count;
    }

    private static void DestroyTemporaryScaler(CanvasScaler scaler)
    {
        if (scaler == null)
        {
            return;
        }

        if (EditorApplication.isPlaying)
        {
            UnityEngine.Object.Destroy(scaler);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(scaler);
        }
    }

    private static void RefreshSceneViews()
    {
        Canvas.ForceUpdateCanvases();
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }

    private static void WarnNoTargets()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now - lastWarningTime < 1d)
        {
            return;
        }

        lastWarningTime = now;
        SetTransientStatus("NO UI", 1.8d);
        Debug.LogWarning("PC adaptation preview found no active scene CanvasScaler or Canvas. Select a UI node or open a prefab/stage with a Canvas.");
        NotifySceneView("No Canvas found");
        PreviewStateChanged?.Invoke();
    }

    private static void SetTransientStatus(string text, double seconds)
    {
        transientStatusText = text;
        transientStatusUntil = string.IsNullOrEmpty(text) ? 0d : EditorApplication.timeSinceStartup + seconds;
    }

    private static void NotifySceneView(string message)
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.ShowNotification(new GUIContent(message));
        }
    }

    private static bool NotifySelectionAncestorLocking()
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        for (int i = 0; i < selectedObjects.Length; i++)
        {
            GameObject selectedObject = selectedObjects[i];
            if (selectedObject == null)
            {
                continue;
            }

            AdaptationLocking selfLocking = selectedObject.GetComponent<AdaptationLocking>();
            if (IsEffectiveLocking(selfLocking))
            {
                continue;
            }

            Transform current = selectedObject.transform.parent;
            while (current != null)
            {
                AdaptationLocking ancestorLocking = current.GetComponent<AdaptationLocking>();
                if (IsEffectiveLocking(ancestorLocking))
                {
                    string message = "父级锁定: " + current.name;
                    NotifySceneView(message);
                    Debug.Log("PC adaptation preview selection is affected by ancestor AdaptationLocking: "
                        + GetTransformPath(selectedObject.transform)
                        + " <- "
                        + GetTransformPath(current));
                    return true;
                }

                current = current.parent;
            }
        }

        return false;
    }

    private static bool IsEffectiveLocking(AdaptationLocking locking)
    {
        return locking != null
            && locking.isActiveAndEnabled
            && !IsRemovedPrefabOverride(locking);
    }

    private sealed class CanvasScalerSnapshot
    {
        public CanvasScaler scaler;
        public CanvasScaler.ScaleMode uiScaleMode;
        public float scaleFactor;
        public bool createdByPreview;
        public List<LockingSnapshot> lockings = new List<LockingSnapshot>();
        public List<BackgroundSnapshot> backgrounds = new List<BackgroundSnapshot>();
    }

    private struct LockingSnapshot
    {
        public AdaptationLocking locking;
        public Vector3 localScale;
        public Vector2 lastZoom;
        public int lastWidth;
        public int lastHeight;
        public float lastScaleFactor;
        public CanvasScaler.ScaleMode canvasScaleMode;
        public CanvasScaler cachedCanvasScaler;
    }

    private struct BackgroundSnapshot
    {
        public AutomaticBackground background;
        public Vector3 localScale;
        public Vector2 baseScreen;
        public int lastWidth;
        public int lastHeight;
        public Vector2 lastSize;
        public float lastCanvasScalerSize;
        public Vector2 lastZoom;
        public CanvasScaler.ScaleMode canvasScaleMode;
        public Vector2 screenSize;
        public RectTransform cachedRect;
        public CanvasScaler cachedCanvasScaler;
    }
}

[EditorToolbarElement(id, typeof(SceneView))]
public class UIPcAdaptationPreviewButton : Button
{
    public const string id = "DragonUI/PCAdaptationPreviewButton";
    private bool ignoreNextClicked;

    public UIPcAdaptationPreviewButton()
    {
        text = "PC适配预览";
        tooltip = "点击切换 PC 适配预览。预览时 CanvasScaler.scaleFactor = 0.8，再次点击恢复。";
        clicked += OnClicked;
        RegisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);

        AddToClassList("unity-editor-toolbar-button");
        style.width = StyleKeyword.Auto;
        style.minWidth = 62;
        style.paddingLeft = 10;
        style.paddingRight = 10;
        style.unityFontStyleAndWeight = FontStyle.Bold;
        style.unityTextAlign = TextAnchor.MiddleCenter;

        RegisterCallback<AttachToPanelEvent>(OnAttach);
        RegisterCallback<DetachFromPanelEvent>(OnDetach);
    }

    private void OnAttach(AttachToPanelEvent evt)
    {
        RefreshState();
        Selection.selectionChanged += RefreshState;
        EditorApplication.hierarchyChanged += RefreshState;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        UIPcAdaptationPreviewController.PreviewStateChanged += RefreshState;
    }

    private void OnDetach(DetachFromPanelEvent evt)
    {
        ignoreNextClicked = false;
        Selection.selectionChanged -= RefreshState;
        EditorApplication.hierarchyChanged -= RefreshState;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        UIPcAdaptationPreviewController.PreviewStateChanged -= RefreshState;
    }

    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        RefreshState();
    }

    private void OnMouseDown(MouseDownEvent evt)
    {
        if (evt.button != 0)
        {
            return;
        }

        ignoreNextClicked = true;
        TogglePreview();
        evt.StopImmediatePropagation();
    }

    private void OnClicked()
    {
        if (ignoreNextClicked)
        {
            ignoreNextClicked = false;
            return;
        }

        TogglePreview();
    }

    private void TogglePreview()
    {
        UIPcAdaptationPreviewController.TogglePreview();
        RefreshState();
    }

    private void RefreshState()
    {
        SetEnabled(true);
        text = "PC适配预览";
    }
}
