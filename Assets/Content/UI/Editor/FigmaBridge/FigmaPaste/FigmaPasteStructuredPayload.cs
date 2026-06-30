using System;
using UnityEngine;

internal static class FigmaPasteStructuredPayload
{
    public const string Marker = "DRAGON_FIGMA_PASTE_JSON_V2";

    private const string LegacyMarker = "DRAGON_FIGMA_PASTE_JSON_V1";
    private const string Schema = "dragon.figmaPaste.v2";
    private const string LegacySchema = "dragon.figmaPaste.v1";

    public static bool LooksLikeStructuredPayload(string source)
    {
        return !string.IsNullOrEmpty(source) &&
               (source.IndexOf(Marker, StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.IndexOf(LegacyMarker, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public static bool TryParse(
        string source,
        out FigmaPasteStructuredPackage package)
    {
        package = null;
        string json = ExtractJson(source);
        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        try
        {
            package = JsonUtility.FromJson<FigmaPasteStructuredPackage>(json);
        }
        catch
        {
            package = null;
            return false;
        }

        return IsValid(package);
    }

    public static bool HasImageNode(FigmaPasteStructuredPackage package)
    {
        if (package == null || package.nodes == null)
        {
            return false;
        }

        return ContainsNode(package.nodes, IsImageNode);
    }

    public static bool IsImageNode(FigmaPasteStructuredNode node)
    {
        return node != null &&
               string.Equals(node.kind, "image", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRectangleNode(FigmaPasteStructuredNode node)
    {
        return node != null &&
               string.Equals(node.kind, "rectangle", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTextNode(FigmaPasteStructuredNode node)
    {
        return node != null &&
               string.Equals(node.kind, "text", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGroupNode(FigmaPasteStructuredNode node)
    {
        return node != null &&
               string.Equals(node.kind, "group", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReferenceNode(FigmaPasteStructuredNode node)
    {
        return node != null &&
               string.Equals(node.kind, "reference", StringComparison.OrdinalIgnoreCase) &&
               node.source != null &&
               (!string.IsNullOrEmpty(node.source.prefabPath) ||
                !string.IsNullOrEmpty(node.source.prefabGuid));
    }

    public static bool IsSupportedNode(FigmaPasteStructuredNode node)
    {
        return IsRectangleNode(node) || IsImageNode(node) || IsTextNode(node) ||
               IsGroupNode(node) || IsReferenceNode(node);
    }

    public static Vector2 GetSelectionSize(FigmaPasteStructuredPackage package)
    {
        if (package == null || package.selection == null)
        {
            return Vector2.zero;
        }

        return new Vector2(
            Mathf.Max(1f, package.selection.width),
            Mathf.Max(1f, package.selection.height));
    }

    public static Vector2 GetNodeSize(FigmaPasteStructuredNode node)
    {
        if (node == null)
        {
            return Vector2.zero;
        }

        float width = Mathf.Max(1f, node.width);
        float height = Mathf.Max(1f, node.height);
        return new Vector2(width, height);
    }

    public static bool TryGetImagePng(
        FigmaPasteStructuredNode node,
        out byte[] pngBytes,
        out int width,
        out int height)
    {
        pngBytes = null;
        width = 0;
        height = 0;

        if (!IsImageNode(node) ||
            node.image == null ||
            string.IsNullOrEmpty(node.image.base64))
        {
            return false;
        }

        byte[] encodedBytes;
        try
        {
            encodedBytes = Convert.FromBase64String(node.image.base64);
        }
        catch
        {
            return false;
        }

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            if (!texture.LoadImage(encodedBytes))
            {
                return false;
            }

            width = texture.width;
            height = texture.height;
            pngBytes = texture.EncodeToPNG();
            return pngBytes != null && pngBytes.Length > 0;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static string ExtractJson(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.Empty;
        }

        int markerIndex = source.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        string matchedMarker = Marker;
        if (markerIndex < 0)
        {
            markerIndex = source.IndexOf(LegacyMarker, StringComparison.OrdinalIgnoreCase);
            matchedMarker = LegacyMarker;
        }

        string candidate = markerIndex >= 0
            ? source.Substring(markerIndex + matchedMarker.Length)
            : source.Trim();

        candidate = candidate.TrimStart();
        if (candidate.StartsWith(":", StringComparison.Ordinal))
        {
            candidate = candidate.Substring(1).TrimStart();
        }

        if (!candidate.StartsWith("{", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return candidate.Trim();
    }

    private static bool IsValid(FigmaPasteStructuredPackage package)
    {
        if (package == null ||
            (!string.Equals(package.schema, Schema, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(package.schema, LegacySchema, StringComparison.OrdinalIgnoreCase)) ||
            package.nodes == null ||
            package.nodes.Length == 0)
        {
            return false;
        }

        return ContainsNode(package.nodes, IsSupportedNode);
    }

    private static bool ContainsNode(
        FigmaPasteStructuredNode[] nodes,
        Func<FigmaPasteStructuredNode, bool> predicate)
    {
        if (nodes == null)
        {
            return false;
        }

        for (int i = 0; i < nodes.Length; i++)
        {
            FigmaPasteStructuredNode node = nodes[i];
            if (node == null)
            {
                continue;
            }

            if (predicate(node) || ContainsNode(node.children, predicate))
            {
                return true;
            }
        }

        return false;
    }
}

[Serializable]
internal sealed class FigmaPasteStructuredPackage
{
    public string schema;
    public int version;
    public string exporter;
    public string exportedAt;
    public FigmaPasteStructuredSelection selection;
    public FigmaPasteStructuredNode[] nodes;
}

[Serializable]
internal sealed class FigmaPasteStructuredSelection
{
    public float x;
    public float y;
    public float width;
    public float height;
}

[Serializable]
internal sealed class FigmaPasteStructuredNode
{
    public string kind;
    public string name;
    public float x;
    public float y;
    public float width;
    public float height;
    public float rotation;
    public float scaleX;
    public float scaleY;
    public float cornerRadius;
    public FigmaPasteStructuredColor fill;
    public FigmaPasteStructuredImage image;
    public string characters;
    public float fontSize;
    public string fontFamily;
    public string fontStyle;
    public string unityFontName;
    public string fontPath;
    public string fontGuid;
    public string textAlignHorizontal;
    public string textAlignVertical;
    public FigmaPasteStructuredSource source;
    public FigmaPasteStructuredNode[] children;
}

[Serializable]
internal sealed class FigmaPasteStructuredSource
{
    public string prefabName;
    public string prefabPath;
    public string prefabGuid;
    public long sourceLocalId;
    public string instanceRootPath;
    public string componentName;
    public string variantProperties;
    public FigmaPasteStructuredNodeState[] nodeStates;
}

[Serializable]
internal sealed class FigmaPasteStructuredNodeState
{
    public string path;
    public bool active;
}

[Serializable]
internal sealed class FigmaPasteStructuredColor
{
    public float r;
    public float g;
    public float b;
    public float a;
}

[Serializable]
internal sealed class FigmaPasteStructuredImage
{
    public string mimeType;
    public string base64;
    public int width;
    public int height;
}
