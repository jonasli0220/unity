using System;
using System.Collections.Generic;
using UnityEngine;

internal static class AssetFavoritesNodePreviewRenderer
{
    private const int PreviewTextureSize = 512;
    private const string NoVisiblePreviewError = "No visible UI graphics found.";

    private static readonly Dictionary<string, Texture2D> PreviewCache = new Dictionary<string, Texture2D>();
    private static readonly HashSet<string> FailedCache = new HashSet<string>();

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
        rendered.wrapMode = TextureWrapMode.Clamp;
        rendered.filterMode = FilterMode.Bilinear;
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
        string error;
        Texture2D texture;
        using (PrefabHistoryLogic.SuppressRecording())
        {
            texture = UIPrefabPreviewGenerator.Generate(template, PreviewTextureSize, out error);
        }

        if (texture == null && !string.Equals(error, NoVisiblePreviewError, StringComparison.Ordinal))
        {
            Debug.LogWarning("[Asset Favorites] Could not render node template preview for "
                + template.name + ": " + (string.IsNullOrEmpty(error) ? "Preview generation failed." : error));
        }

        return texture;
    }

}
