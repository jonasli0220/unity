using System;
using System.Globalization;
using System.Net;
using System.Xml.Linq;
using UnityEngine;

internal sealed class FigmaPasteSvgRectangle
{
    public Vector2 Size;
    public Color FillColor;
    public float CornerRadius;
}

internal static class FigmaPasteSvgShape
{
    private static readonly string[] OtherVisualElementNames =
    {
        "circle",
        "ellipse",
        "image",
        "line",
        "path",
        "polygon",
        "polyline",
        "text",
        "use"
    };

    public static bool TryParseRectangle(
        FigmaPasteClipboardPayload payload,
        out FigmaPasteSvgRectangle rectangle)
    {
        rectangle = null;
        if (payload == null)
        {
            return false;
        }

        return TryParseRectangle(payload.Svg, out rectangle)
            || TryParseRectangle(payload.Html, out rectangle)
            || TryParseRectangle(payload.Text, out rectangle);
    }

    private static bool TryParseRectangle(
        string source,
        out FigmaPasteSvgRectangle rectangle)
    {
        rectangle = null;
        string svg = ExtractSvgSource(source);
        if (string.IsNullOrEmpty(svg))
        {
            return false;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(svg, LoadOptions.None);
        }
        catch
        {
            return false;
        }

        XElement root = document.Root;
        if (root == null || !IsElementNamed(root, "svg"))
        {
            return false;
        }

        XElement visualRect = null;
        int visualRectCount = 0;
        int otherVisualCount = 0;
        CountVisualElements(root, false, ref visualRect, ref visualRectCount, ref otherVisualCount);
        if (visualRect == null || visualRectCount != 1 || otherVisualCount != 0)
        {
            return false;
        }

        Vector2 rootSize;
        TryReadRootSize(root, out rootSize);

        float width;
        float height;
        if (!TryReadPositiveLength(visualRect, "width", out width))
        {
            width = rootSize.x;
        }

        if (!TryReadPositiveLength(visualRect, "height", out height))
        {
            height = rootSize.y;
        }

        if (width <= 0f || height <= 0f)
        {
            return false;
        }

        Color fillColor;
        if (!TryReadFillColor(visualRect, out fillColor))
        {
            return false;
        }

        rectangle = new FigmaPasteSvgRectangle
        {
            Size = new Vector2(width, height),
            FillColor = fillColor,
            CornerRadius = ReadCornerRadius(visualRect)
        };
        return true;
    }

    private static string ExtractSvgSource(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.Empty;
        }

        string decoded = source.IndexOf("&lt;svg", StringComparison.OrdinalIgnoreCase) >= 0
            ? WebUtility.HtmlDecode(source)
            : source;

        int start = decoded.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        int end = decoded.LastIndexOf("</svg>", StringComparison.OrdinalIgnoreCase);
        if (end < start)
        {
            return string.Empty;
        }

        return decoded.Substring(start, end + "</svg>".Length - start);
    }

    private static void CountVisualElements(
        XElement element,
        bool ignored,
        ref XElement visualRect,
        ref int visualRectCount,
        ref int otherVisualCount)
    {
        string localName = element.Name.LocalName;
        bool childIgnored = ignored || IsIgnoredContainer(localName);

        if (!childIgnored)
        {
            if (IsElementNamed(element, "rect") && HasPositiveRectSize(element))
            {
                visualRect = element;
                visualRectCount++;
            }
            else if (IsOtherVisualElement(localName) && IsElementDisplayed(element))
            {
                otherVisualCount++;
            }
        }

        foreach (XElement child in element.Elements())
        {
            CountVisualElements(child, childIgnored, ref visualRect, ref visualRectCount, ref otherVisualCount);
        }
    }

    private static bool IsIgnoredContainer(string localName)
    {
        return string.Equals(localName, "clipPath", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "defs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "desc", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "mask", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "metadata", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "style", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "title", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOtherVisualElement(string localName)
    {
        for (int i = 0; i < OtherVisualElementNames.Length; i++)
        {
            if (string.Equals(localName, OtherVisualElementNames[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPositiveRectSize(XElement rect)
    {
        float width;
        float height;
        return IsElementDisplayed(rect)
            && TryReadPositiveLength(rect, "width", out width)
            && TryReadPositiveLength(rect, "height", out height);
    }

    private static bool IsElementDisplayed(XElement element)
    {
        string display = ReadInheritedPresentationValue(element, "display");
        if (string.Equals(display, "none", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string visibility = ReadInheritedPresentationValue(element, "visibility");
        return !string.Equals(visibility, "hidden", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadFillColor(XElement rect, out Color color)
    {
        color = Color.white;
        string fill = ReadInheritedPresentationValue(rect, "fill");
        if (string.IsNullOrEmpty(fill) ||
            string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryParseSvgColor(fill, out color))
        {
            return false;
        }

        color.a *= ReadInheritedOpacity(rect, "fill-opacity");
        color.a *= ReadInheritedOpacity(rect, "opacity");
        return color.a > 0f;
    }

    private static float ReadInheritedOpacity(XElement element, string name)
    {
        string value = ReadInheritedPresentationValue(element, name);
        float opacity;
        if (!TryParseFloat(value, out opacity))
        {
            return 1f;
        }

        return Mathf.Clamp01(opacity);
    }

    private static float ReadCornerRadius(XElement rect)
    {
        float rx;
        if (TryReadPositiveLength(rect, "rx", out rx))
        {
            return rx;
        }

        float ry;
        return TryReadPositiveLength(rect, "ry", out ry) ? ry : 0f;
    }

    private static string ReadInheritedPresentationValue(XElement element, string name)
    {
        for (XElement current = element; current != null; current = current.Parent)
        {
            string value = ReadPresentationValue(current, name);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string ReadPresentationValue(XElement element, string name)
    {
        XAttribute attribute = element.Attribute(name);
        if (attribute != null && !string.IsNullOrEmpty(attribute.Value))
        {
            return attribute.Value.Trim();
        }

        string style = ReadAttribute(element, "style");
        if (string.IsNullOrEmpty(style))
        {
            return string.Empty;
        }

        string[] parts = style.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            int colon = parts[i].IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            string key = parts[i].Substring(0, colon).Trim();
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return parts[i].Substring(colon + 1).Trim();
        }

        return string.Empty;
    }

    private static bool TryReadPositiveLength(XElement element, string name, out float value)
    {
        value = 0f;
        string raw = ReadAttribute(element, name);
        return TryParseSvgLength(raw, out value) && value > 0f;
    }

    private static bool TryReadRootSize(XElement root, out Vector2 size)
    {
        size = Vector2.zero;
        float width;
        float height;
        if (TryReadPositiveLength(root, "width", out width) &&
            TryReadPositiveLength(root, "height", out height))
        {
            size = new Vector2(width, height);
            return true;
        }

        string viewBox = ReadAttribute(root, "viewBox");
        if (string.IsNullOrEmpty(viewBox))
        {
            return false;
        }

        string[] parts = viewBox.Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            return false;
        }

        if (!TryParseFloat(parts[2], out width) || !TryParseFloat(parts[3], out height))
        {
            return false;
        }

        size = new Vector2(Mathf.Abs(width), Mathf.Abs(height));
        return size.x > 0f && size.y > 0f;
    }

    private static string ReadAttribute(XElement element, string name)
    {
        XAttribute attribute = element.Attribute(name);
        return attribute != null ? attribute.Value.Trim() : string.Empty;
    }

    private static bool TryParseSvgLength(string value, out float result)
    {
        result = 0f;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (trimmed.EndsWith("%", StringComparison.Ordinal))
        {
            return false;
        }

        int length = 0;
        while (length < trimmed.Length && IsSvgNumberChar(trimmed[length]))
        {
            length++;
        }

        if (length == 0)
        {
            return false;
        }

        return TryParseFloat(trimmed.Substring(0, length), out result);
    }

    private static bool IsSvgNumberChar(char value)
    {
        return char.IsDigit(value)
            || value == '+'
            || value == '-'
            || value == '.'
            || value == 'e'
            || value == 'E';
    }

    private static bool TryParseSvgColor(string value, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (ColorUtility.TryParseHtmlString(trimmed, out color))
        {
            return true;
        }

        return TryParseRgbColor(trimmed, out color);
    }

    private static bool TryParseRgbColor(string value, out Color color)
    {
        color = Color.white;
        bool isRgba = value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase);
        bool isRgb = value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase);
        if (!isRgb && !isRgba)
        {
            return false;
        }

        int start = value.IndexOf('(');
        int end = value.LastIndexOf(')');
        if (start < 0 || end <= start)
        {
            return false;
        }

        string body = value.Substring(start + 1, end - start - 1);
        string[] parts = body.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return false;
        }

        float r;
        float g;
        float b;
        if (!TryParseRgbChannel(parts[0], out r) ||
            !TryParseRgbChannel(parts[1], out g) ||
            !TryParseRgbChannel(parts[2], out b))
        {
            return false;
        }

        float a = 1f;
        if (parts.Length >= 4 && !TryParseAlphaChannel(parts[3], out a))
        {
            return false;
        }

        color = new Color(r, g, b, a);
        return true;
    }

    private static bool TryParseRgbChannel(string value, out float channel)
    {
        channel = 0f;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (trimmed.EndsWith("%", StringComparison.Ordinal))
        {
            float percent;
            if (!TryParseFloat(trimmed.Substring(0, trimmed.Length - 1), out percent))
            {
                return false;
            }

            channel = Mathf.Clamp01(percent / 100f);
            return true;
        }

        float raw;
        if (!TryParseFloat(trimmed, out raw))
        {
            return false;
        }

        channel = Mathf.Clamp01(raw / 255f);
        return true;
    }

    private static bool TryParseAlphaChannel(string value, out float alpha)
    {
        alpha = 1f;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (trimmed.EndsWith("%", StringComparison.Ordinal))
        {
            float percent;
            if (!TryParseFloat(trimmed.Substring(0, trimmed.Length - 1), out percent))
            {
                return false;
            }

            alpha = Mathf.Clamp01(percent / 100f);
            return true;
        }

        float raw;
        if (!TryParseFloat(trimmed, out raw))
        {
            return false;
        }

        alpha = Mathf.Clamp01(raw);
        return true;
    }

    private static bool TryParseFloat(string value, out float result)
    {
        return float.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static bool IsElementNamed(XElement element, string localName)
    {
        return string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);
    }
}
