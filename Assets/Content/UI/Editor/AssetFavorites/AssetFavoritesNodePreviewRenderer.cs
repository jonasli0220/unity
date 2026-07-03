using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

internal static class AssetFavoritesNodePreviewRenderer
{
    private const int PreviewTextureSize = 512;
    private const float Padding = 1.16f;
    private const float CameraDistance = 1000f;

    private static readonly Dictionary<string, Texture2D> PreviewCache = new Dictionary<string, Texture2D>();
    private static readonly HashSet<string> FailedCache = new HashSet<string>();
    private static readonly Vector3[] Corners = new Vector3[4];

    public static Texture2D GetPreview(AssetFavoriteEntry entry, GameObject template)
    {
        if (entry == null || template == null || !AssetFavoritesLibrary.IsNodeTemplate(entry))
        {
            return null;
        }

        string cacheKey = string.IsNullOrEmpty(entry.templateGuid)
            ? AssetFavoritesLibrary.GetEntryId(entry)
            : entry.templateGuid;
        if (string.IsNullOrEmpty(cacheKey))
        {
            return null;
        }

        Texture2D cached;
        if (PreviewCache.TryGetValue(cacheKey, out cached) && cached != null)
        {
            return cached;
        }

        if (FailedCache.Contains(cacheKey))
        {
            return null;
        }

        Texture2D rendered = RenderTemplate(template);
        if (rendered == null)
        {
            FailedCache.Add(cacheKey);
            return null;
        }

        rendered.name = "AssetFavoritesNodePreview_" + template.name;
        rendered.hideFlags = HideFlags.HideAndDontSave;
        PreviewCache[cacheKey] = rendered;
        return rendered;
    }

    public static void ClearCache()
    {
        foreach (Texture2D texture in PreviewCache.Values)
        {
            if (texture != null)
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        PreviewCache.Clear();
        FailedCache.Clear();
    }

    private static Texture2D RenderTemplate(GameObject template)
    {
        PreviewRenderUtility previewUtility = null;
        GameObject canvasRoot = null;

        try
        {
            previewUtility = new PreviewRenderUtility();
            PrepareCamera(previewUtility.camera);

            canvasRoot = BuildPreviewCanvas(template, previewUtility.camera);
            if (canvasRoot == null)
            {
                return null;
            }

            previewUtility.AddSingleGO(canvasRoot);
            Canvas.ForceUpdateCanvases();

            Bounds bounds;
            if (!TryGetRenderableBounds(canvasRoot, out bounds))
            {
                bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
            }

            FrameCamera(previewUtility.camera, bounds);

            Rect previewRect = new Rect(0f, 0f, PreviewTextureSize, PreviewTextureSize);
            previewUtility.BeginPreview(previewRect, GUIStyle.none);
            GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
            previewUtility.camera.Render();
            Texture renderedTexture = previewUtility.EndPreview();

            return CopyTexture(renderedTexture);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Asset Favorites failed to render node template preview: " + exception.Message);
            return null;
        }
        finally
        {
            if (canvasRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }

            if (previewUtility != null)
            {
                previewUtility.Cleanup();
            }
        }
    }

    private static void PrepareCamera(Camera camera)
    {
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.orthographic = true;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = CameraDistance * 2f;
        camera.transform.rotation = Quaternion.identity;
    }

    private static GameObject BuildPreviewCanvas(GameObject template, Camera camera)
    {
        GameObject canvasRoot = new GameObject("AssetFavorites Node Preview Canvas", typeof(RectTransform), typeof(Canvas));
        canvasRoot.hideFlags = HideFlags.HideAndDontSave;

        RectTransform canvasRect = canvasRoot.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(PreviewTextureSize, PreviewTextureSize);
        canvasRect.localScale = Vector3.one;

        Canvas canvas = canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        canvas.pixelPerfect = false;
        canvas.sortingOrder = 0;

        GameObject instance = UnityEngine.Object.Instantiate(template);
        if (instance == null)
        {
            UnityEngine.Object.DestroyImmediate(canvasRoot);
            return null;
        }

        SetHideFlagsRecursive(instance.transform);
        instance.name = template.name;
        instance.SetActive(true);
        instance.transform.SetParent(canvasRoot.transform, false);
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        RectTransform instanceRect = instance.GetComponent<RectTransform>();
        if (instanceRect != null)
        {
            instanceRect.anchoredPosition = Vector2.zero;
            LayoutRebuilder.ForceRebuildLayoutImmediate(instanceRect);
        }

        return canvasRoot;
    }

    private static void SetHideFlagsRecursive(Transform transform)
    {
        if (transform == null)
        {
            return;
        }

        transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
        foreach (Transform child in transform)
        {
            SetHideFlagsRecursive(child);
        }
    }

    private static bool TryGetRenderableBounds(GameObject root, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;
        RectTransform[] rectTransforms = root.GetComponentsInChildren<RectTransform>(false);

        foreach (RectTransform rectTransform in rectTransforms)
        {
            if (rectTransform == null || rectTransform.gameObject == root)
            {
                continue;
            }

            if (!rectTransform.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (rectTransform.rect.width <= 0f && rectTransform.rect.height <= 0f)
            {
                continue;
            }

            rectTransform.GetWorldCorners(Corners);
            for (int i = 0; i < Corners.Length; i++)
            {
                if (!hasBounds)
                {
                    bounds = new Bounds(Corners[i], Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(Corners[i]);
                }
            }
        }

        if (!hasBounds || bounds.size.sqrMagnitude <= 0.01f)
        {
            return false;
        }

        return true;
    }

    private static void FrameCamera(Camera camera, Bounds bounds)
    {
        float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, 1f);
        camera.orthographicSize = maxDimension * 0.5f * Padding;
        camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z - CameraDistance);
    }

    private static Texture2D CopyTexture(Texture source)
    {
        if (source == null)
        {
            return null;
        }

        RenderTexture temporaryRenderTexture = null;
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            RenderTexture sourceRenderTexture = source as RenderTexture;
            if (sourceRenderTexture == null)
            {
                temporaryRenderTexture = RenderTexture.GetTemporary(PreviewTextureSize, PreviewTextureSize, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, temporaryRenderTexture);
                sourceRenderTexture = temporaryRenderTexture;
            }

            RenderTexture.active = sourceRenderTexture;
            Texture2D copy = new Texture2D(PreviewTextureSize, PreviewTextureSize, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0f, 0f, PreviewTextureSize, PreviewTextureSize), 0, 0);
            copy.Apply(false, false);
            return copy;
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (temporaryRenderTexture != null)
            {
                RenderTexture.ReleaseTemporary(temporaryRenderTexture);
            }
        }
    }
}
