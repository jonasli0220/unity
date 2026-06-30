using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

internal sealed class FigmaPasteClipboardPayload
{
    public string CapturedAt;
    public string Text;
    public string Html;
    public string Svg;
    public string ImageSourceFormat;
    public byte[] ImagePngBytes;
    public int ImageWidth;
    public int ImageHeight;
    public bool LooksLikeFigmaContent;
    public readonly List<FigmaPasteClipboardFormatInfo> Formats =
        new List<FigmaPasteClipboardFormatInfo>();
    public readonly List<string> FileDrops = new List<string>();

    public bool HasText
    {
        get { return !string.IsNullOrEmpty(Text); }
    }

    public bool HasImage
    {
        get { return ImagePngBytes != null && ImagePngBytes.Length > 0; }
    }
}

internal sealed class FigmaPasteClipboardFormatInfo
{
    public string Name;
    public string DataType;
    public string Preview;
}

internal static class FigmaPasteClipboard
{
    private const int MaxTextPreviewLength = 700;
    private const string ProbeRootRelative = "Library/Dragon/FigmaPaste";

#if UNITY_EDITOR_WIN
    private const uint CF_TEXT = 1;
    private const uint CF_DIB = 8;
    private const uint CF_UNICODETEXT = 13;
    private const uint CF_HDROP = 15;
    private const uint CF_DIBV5 = 17;
    private const uint DragQueryFileCount = 0xFFFFFFFF;
    private const uint BI_RGB = 0;
    private const uint BI_BITFIELDS = 3;

    private static readonly string[] EncodedImageFormats =
    {
        "PNG",
        "image/png",
        "Portable Network Graphics",
        "JFIF",
        "JPEG",
        "image/jpeg"
    };

    private static readonly string[] HtmlFormats =
    {
        "HTML Format",
        "text/html"
    };

    private static readonly string[] SvgFormats =
    {
        "image/svg+xml",
        "SVG",
        "text/svg"
    };
#endif

    public static bool TryRead(out FigmaPasteClipboardPayload payload, out string error)
    {
        payload = null;
        error = string.Empty;

        try
        {
#if UNITY_EDITOR_WIN
            payload = ReadWindowsClipboard();
#else
            payload = new FigmaPasteClipboardPayload
            {
                CapturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Text = EditorGUIUtility.systemCopyBuffer
            };
#endif
            if (payload != null)
            {
                payload.LooksLikeFigmaContent = LooksLikeFigmaPayload(payload);
            }

            return payload != null;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string WriteInspectionReport()
    {
        FigmaPasteClipboardPayload payload;
        string error;
        bool ok = TryRead(out payload, out error);

        string probeRoot = GetProbeRoot();
        Directory.CreateDirectory(probeRoot);

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string basePath = Path.Combine(probeRoot, "clipboard_" + stamp);
        string imagePath = string.Empty;

        if (ok && payload != null && payload.HasImage)
        {
            imagePath = basePath + ".png";
            File.WriteAllBytes(imagePath, payload.ImagePngBytes);
        }

        string reportPath = basePath + ".txt";
        File.WriteAllText(reportPath, BuildReport(ok, payload, error, imagePath), Encoding.UTF8);
        return reportPath;
    }

    private static string BuildReport(
        bool ok,
        FigmaPasteClipboardPayload payload,
        string error,
        string imagePath)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Figma Paste Clipboard Inspection");
        builder.AppendLine("Captured At: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("Status: " + (ok ? "OK" : "Failed"));
        if (!ok)
        {
            builder.AppendLine("Error: " + error);
            return builder.ToString();
        }

        if (payload == null)
        {
            builder.AppendLine("No clipboard payload.");
            return builder.ToString();
        }

        builder.AppendLine("Has Image: " + payload.HasImage);
        builder.AppendLine("Looks Like Figma Content: " + payload.LooksLikeFigmaContent);
        if (payload.HasImage)
        {
            builder.AppendLine("Image Source Format: " + payload.ImageSourceFormat);
            builder.AppendLine("Image Size: " + payload.ImageWidth + " x " + payload.ImageHeight);
            builder.AppendLine("Image Probe: " + imagePath);
        }

        builder.AppendLine("Has Text: " + payload.HasText);
        if (payload.HasText)
        {
            builder.AppendLine("Text Preview:");
            builder.AppendLine(TrimPreview(payload.Text));
        }

        if (!string.IsNullOrEmpty(payload.Html))
        {
            builder.AppendLine("HTML Preview:");
            builder.AppendLine(TrimPreview(payload.Html));
        }

        if (!string.IsNullOrEmpty(payload.Svg))
        {
            builder.AppendLine("SVG Preview:");
            builder.AppendLine(TrimPreview(payload.Svg));
        }

        if (payload.FileDrops.Count > 0)
        {
            builder.AppendLine("File Drops:");
            for (int i = 0; i < payload.FileDrops.Count; i++)
            {
                builder.AppendLine("- " + payload.FileDrops[i]);
            }
        }

        builder.AppendLine("Formats:");
        for (int i = 0; i < payload.Formats.Count; i++)
        {
            FigmaPasteClipboardFormatInfo format = payload.Formats[i];
            builder.AppendLine("- " + format.Name + " | " + format.DataType);
            if (!string.IsNullOrEmpty(format.Preview))
            {
                builder.AppendLine("  " + format.Preview);
            }
        }

        return builder.ToString();
    }

    private static string GetProbeRoot()
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        return Path.Combine(projectRoot, ProbeRootRelative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string TrimPreview(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string compact = value.Replace("\r", "\\r").Replace("\n", "\\n");
        if (compact.Length <= MaxTextPreviewLength)
        {
            return compact;
        }

        return compact.Substring(0, MaxTextPreviewLength) + "...";
    }

    private static bool LooksLikeFigmaPayload(FigmaPasteClipboardPayload payload)
    {
        if (payload == null)
        {
            return false;
        }

        for (int i = 0; i < payload.Formats.Count; i++)
        {
            string name = payload.Formats[i].Name;
            if (ContainsIgnoreCase(name, "figma"))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(payload.Svg) &&
                (EqualsIgnoreCase(name, "image/svg+xml") ||
                 EqualsIgnoreCase(name, "SVG") ||
                 EqualsIgnoreCase(name, "text/svg")))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(payload.Html) &&
                (EqualsIgnoreCase(name, "HTML Format") ||
                 EqualsIgnoreCase(name, "text/html")) &&
                ContainsIgnoreCase(payload.Html, "<svg"))
            {
                return true;
            }
        }

        return FigmaPasteStructuredPayload.LooksLikeStructuredPayload(payload.Text)
            || ContainsIgnoreCase(payload.Html, "figma")
            || ContainsIgnoreCase(payload.Svg, "figma")
            || ContainsIgnoreCase(payload.Text, "figma")
            || ContainsIgnoreCase(payload.Svg, "<svg")
            || ContainsIgnoreCase(payload.Text, "<svg");
    }

    private static bool ContainsIgnoreCase(string value, string fragment)
    {
        return !string.IsNullOrEmpty(value) &&
               value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool EqualsIgnoreCase(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

#if UNITY_EDITOR_WIN
    private static FigmaPasteClipboardPayload ReadWindowsClipboard()
    {
        if (!TryOpenClipboard())
        {
            throw new InvalidOperationException(
                "OpenClipboard failed. Win32 error: " + Marshal.GetLastWin32Error());
        }

        try
        {
            List<uint> formatIds = EnumerateClipboardFormats();
            FigmaPasteClipboardPayload payload = new FigmaPasteClipboardPayload
            {
                CapturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            for (int i = 0; i < formatIds.Count; i++)
            {
                string formatName = GetFormatName(formatIds[i]);
                payload.Formats.Add(ReadFormatInfo(formatIds[i], formatName));
            }

            payload.Text = ReadClipboardUnicodeText();
            if (string.IsNullOrEmpty(payload.Text))
            {
                payload.Text = ReadClipboardAnsiText();
            }

            payload.Html = ReadFirstRegisteredString(formatIds, HtmlFormats);
            payload.Svg = ReadFirstRegisteredString(formatIds, SvgFormats);

            payload.FileDrops.AddRange(ReadFileDropPaths());
            ReadImage(formatIds, payload);
            ReadEmbeddedSvgImage(payload);
            return payload;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static FigmaPasteClipboardFormatInfo ReadFormatInfo(
        uint formatId,
        string formatName)
    {
        FigmaPasteClipboardFormatInfo info = new FigmaPasteClipboardFormatInfo
        {
            Name = formatName,
            DataType = string.Empty,
            Preview = string.Empty
        };

        try
        {
            if (IsTextFormat(formatId, formatName))
            {
                info.DataType = "string";
                info.Preview = TrimPreview(ReadStringForFormat(formatId));
                return info;
            }

            if (formatId == CF_HDROP)
            {
                info.DataType = "file-drop";
                List<string> paths = ReadFileDropPaths();
                info.Preview = paths.Count + " file(s)";
                if (paths.Count > 0)
                {
                    info.Preview += ": " + string.Join(" | ", paths.ToArray());
                }
                return info;
            }

            int dataSize = GetClipboardDataSize(formatId);
            if (formatId == CF_DIB || formatId == CF_DIBV5)
            {
                info.DataType = "DIB";
            }
            else
            {
                info.DataType = "HGLOBAL";
            }

            if (dataSize >= 0)
            {
                info.Preview = dataSize + " bytes";
            }
        }
        catch (Exception ex)
        {
            info.DataType = "unreadable";
            info.Preview = ex.GetType().Name + ": " + ex.Message;
        }

        return info;
    }

    private static bool TryOpenClipboard()
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return false;
    }

    private static List<uint> EnumerateClipboardFormats()
    {
        List<uint> formats = new List<uint>();
        uint format = 0;
        while (true)
        {
            format = EnumClipboardFormats(format);
            if (format == 0)
            {
                break;
            }

            formats.Add(format);
        }

        return formats;
    }

    private static string GetFormatName(uint formatId)
    {
        switch (formatId)
        {
            case CF_TEXT:
                return "CF_TEXT";
            case CF_DIB:
                return "CF_DIB";
            case CF_UNICODETEXT:
                return "CF_UNICODETEXT";
            case CF_HDROP:
                return "CF_HDROP";
            case CF_DIBV5:
                return "CF_DIBV5";
        }

        StringBuilder builder = new StringBuilder(256);
        int length = GetClipboardFormatName(formatId, builder, builder.Capacity);
        if (length > 0)
        {
            return builder.ToString();
        }

        return "Format " + formatId;
    }

    private static void ReadImage(
        List<uint> formatIds,
        FigmaPasteClipboardPayload payload)
    {
        if (TryReadEncodedImage(formatIds, payload))
        {
            return;
        }

        if (TryReadDibImage(CF_DIBV5, "CF_DIBV5", payload))
        {
            return;
        }

        TryReadDibImage(CF_DIB, "CF_DIB", payload);
    }

    private static void ReadEmbeddedSvgImage(FigmaPasteClipboardPayload payload)
    {
        if (payload == null || payload.HasImage)
        {
            return;
        }

        FigmaPasteSvgEmbeddedImage embeddedImage;
        if (!FigmaPasteSvgShape.TryParseEmbeddedImage(payload, out embeddedImage) ||
            embeddedImage == null ||
            embeddedImage.EncodedBytes == null ||
            embeddedImage.EncodedBytes.Length == 0)
        {
            return;
        }

        byte[] pngBytes;
        int width;
        int height;
        if (!TryConvertEncodedImageToPng(
            embeddedImage.EncodedBytes,
            out pngBytes,
            out width,
            out height))
        {
            return;
        }

        payload.ImagePngBytes = pngBytes;
        payload.ImageWidth = width > 0 ? width : Mathf.RoundToInt(embeddedImage.Size.x);
        payload.ImageHeight = height > 0 ? height : Mathf.RoundToInt(embeddedImage.Size.y);
        payload.ImageSourceFormat = "SVG embedded " + embeddedImage.MimeType;
    }

    private static bool TryReadEncodedImage(
        List<uint> formatIds,
        FigmaPasteClipboardPayload payload)
    {
        for (int i = 0; i < EncodedImageFormats.Length; i++)
        {
            uint formatId = FindFormatIdByName(formatIds, EncodedImageFormats[i]);
            if (formatId == 0)
            {
                continue;
            }

            try
            {
                byte[] sourceBytes = ReadClipboardBytes(formatId);
                if (sourceBytes == null || sourceBytes.Length == 0)
                {
                    continue;
                }

                byte[] pngBytes;
                int width;
                int height;
                if (!TryConvertEncodedImageToPng(
                    sourceBytes,
                    out pngBytes,
                    out width,
                    out height))
                {
                    continue;
                }

                payload.ImagePngBytes = pngBytes;
                payload.ImageWidth = width;
                payload.ImageHeight = height;
                payload.ImageSourceFormat = GetFormatName(formatId);
                return true;
            }
            catch
            {
                // Try the next available image-like format.
            }
        }

        return false;
    }

    private static bool TryReadDibImage(
        uint formatId,
        string sourceFormat,
        FigmaPasteClipboardPayload payload)
    {
        try
        {
            byte[] dibBytes = ReadClipboardBytes(formatId);
            if (dibBytes == null || dibBytes.Length == 0)
            {
                return false;
            }

            byte[] pngBytes;
            int width;
            int height;
            if (!TryConvertDibToPng(dibBytes, out pngBytes, out width, out height))
            {
                return false;
            }

            payload.ImagePngBytes = pngBytes;
            payload.ImageWidth = width;
            payload.ImageHeight = height;
            payload.ImageSourceFormat = sourceFormat;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static uint FindFormatIdByName(List<uint> formatIds, string expectedName)
    {
        for (int i = 0; i < formatIds.Count; i++)
        {
            if (string.Equals(
                GetFormatName(formatIds[i]),
                expectedName,
                StringComparison.OrdinalIgnoreCase))
            {
                return formatIds[i];
            }
        }

        return 0;
    }

    private static bool IsTextFormat(uint formatId, string formatName)
    {
        if (formatId == CF_TEXT || formatId == CF_UNICODETEXT)
        {
            return true;
        }

        return ContainsFormatName(HtmlFormats, formatName)
            || ContainsFormatName(SvgFormats, formatName);
    }

    private static bool ContainsFormatName(string[] names, string formatName)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], formatName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadFirstRegisteredString(List<uint> formatIds, string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            uint formatId = FindFormatIdByName(formatIds, names[i]);
            if (formatId == 0)
            {
                continue;
            }

            string value = ReadRegisteredText(formatId);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string ReadStringForFormat(uint formatId)
    {
        if (formatId == CF_UNICODETEXT)
        {
            return ReadClipboardUnicodeText();
        }

        if (formatId == CF_TEXT)
        {
            return ReadClipboardAnsiText();
        }

        return ReadRegisteredText(formatId);
    }

    private static string ReadClipboardUnicodeText()
    {
        byte[] bytes = ReadClipboardBytes(CF_UNICODETEXT);
        if (bytes == null || bytes.Length < 2)
        {
            return string.Empty;
        }

        int length = FindUnicodeStringByteLength(bytes);
        if (length <= 0)
        {
            return string.Empty;
        }

        return Encoding.Unicode.GetString(bytes, 0, length).TrimEnd('\0');
    }

    private static string ReadClipboardAnsiText()
    {
        byte[] bytes = ReadClipboardBytes(CF_TEXT);
        if (bytes == null || bytes.Length == 0)
        {
            return string.Empty;
        }

        int length = FindAnsiStringByteLength(bytes);
        if (length <= 0)
        {
            return string.Empty;
        }

        return Encoding.Default.GetString(bytes, 0, length).TrimEnd('\0');
    }

    private static string ReadRegisteredText(uint formatId)
    {
        byte[] bytes = ReadClipboardBytes(formatId);
        if (bytes == null || bytes.Length == 0)
        {
            return string.Empty;
        }

        string unicodeValue;
        if (TryDecodeRegisteredUnicodeText(bytes, out unicodeValue))
        {
            return unicodeValue;
        }

        int length = FindAnsiStringByteLength(bytes);
        if (length <= 0)
        {
            return string.Empty;
        }

        try
        {
            return new UTF8Encoding(false, true)
                .GetString(bytes, 0, length)
                .TrimEnd('\0');
        }
        catch
        {
            return Encoding.Default.GetString(bytes, 0, length).TrimEnd('\0');
        }
    }

    private static bool TryDecodeRegisteredUnicodeText(byte[] bytes, out string value)
    {
        value = string.Empty;
        if (bytes == null || bytes.Length < 4)
        {
            return false;
        }

        if (bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            value = DecodeUnicodeText(bytes, 2, Encoding.Unicode);
            return LooksLikeUsefulText(value);
        }

        if (bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            value = DecodeUnicodeText(bytes, 2, Encoding.BigEndianUnicode);
            return LooksLikeUsefulText(value);
        }

        int sampleLength = Mathf.Min(bytes.Length, 200);
        int evenNonZero = 0;
        int oddZero = 0;
        int evenZero = 0;
        int oddNonZero = 0;
        for (int i = 0; i + 1 < sampleLength; i += 2)
        {
            if (bytes[i] == 0)
            {
                evenZero++;
            }
            else
            {
                evenNonZero++;
            }

            if (bytes[i + 1] == 0)
            {
                oddZero++;
            }
            else
            {
                oddNonZero++;
            }
        }

        if (evenNonZero > 0 && oddZero >= Mathf.Max(2, evenNonZero / 2))
        {
            value = DecodeUnicodeText(bytes, 0, Encoding.Unicode);
            return LooksLikeUsefulText(value);
        }

        if (oddNonZero > 0 && evenZero >= Mathf.Max(2, oddNonZero / 2))
        {
            value = DecodeUnicodeText(bytes, 0, Encoding.BigEndianUnicode);
            return LooksLikeUsefulText(value);
        }

        return false;
    }

    private static string DecodeUnicodeText(
        byte[] bytes,
        int startIndex,
        Encoding encoding)
    {
        int byteLength = bytes.Length - startIndex;
        byteLength -= byteLength % 2;
        if (byteLength <= 0)
        {
            return string.Empty;
        }

        return encoding.GetString(bytes, startIndex, byteLength)
            .TrimEnd('\0')
            .TrimStart('\uFEFF');
    }

    private static bool LooksLikeUsefulText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        int controlCount = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
            {
                controlCount++;
            }
        }

        return controlCount <= Mathf.Max(2, value.Length / 20);
    }

    private static int FindUnicodeStringByteLength(byte[] bytes)
    {
        int evenLength = bytes.Length - bytes.Length % 2;
        for (int i = 0; i + 1 < evenLength; i += 2)
        {
            if (bytes[i] == 0 && bytes[i + 1] == 0)
            {
                return i;
            }
        }

        return evenLength;
    }

    private static int FindAnsiStringByteLength(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 0)
            {
                return i;
            }
        }

        return bytes.Length;
    }

    private static List<string> ReadFileDropPaths()
    {
        List<string> paths = new List<string>();
        IntPtr handle = GetClipboardData(CF_HDROP);
        if (handle == IntPtr.Zero)
        {
            return paths;
        }

        uint count = DragQueryFile(handle, DragQueryFileCount, null, 0);
        for (uint i = 0; i < count; i++)
        {
            uint length = DragQueryFile(handle, i, null, 0);
            if (length == 0)
            {
                continue;
            }

            StringBuilder builder = new StringBuilder((int)length + 1);
            DragQueryFile(handle, i, builder, (uint)builder.Capacity);
            paths.Add(builder.ToString());
        }

        return paths;
    }

    private static int GetClipboardDataSize(uint formatId)
    {
        IntPtr handle = GetClipboardData(formatId);
        if (handle == IntPtr.Zero)
        {
            return -1;
        }

        ulong size = GlobalSize(handle).ToUInt64();
        if (size > int.MaxValue)
        {
            return -1;
        }

        return (int)size;
    }

    private static byte[] ReadClipboardBytes(uint formatId)
    {
        IntPtr handle = GetClipboardData(formatId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        ulong rawSize = GlobalSize(handle).ToUInt64();
        if (rawSize == 0 || rawSize > int.MaxValue)
        {
            return null;
        }

        IntPtr pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            byte[] bytes = new byte[(int)rawSize];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static bool TryConvertEncodedImageToPng(
        byte[] sourceBytes,
        out byte[] pngBytes,
        out int width,
        out int height)
    {
        if (TryLoadImageBytes(sourceBytes, out pngBytes, out width, out height))
        {
            return true;
        }

        byte[] trimmed = TrimTrailingZeros(sourceBytes);
        return trimmed.Length != sourceBytes.Length
            && TryLoadImageBytes(trimmed, out pngBytes, out width, out height);
    }

    private static bool TryLoadImageBytes(
        byte[] sourceBytes,
        out byte[] pngBytes,
        out int width,
        out int height)
    {
        pngBytes = null;
        width = 0;
        height = 0;

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            if (!texture.LoadImage(sourceBytes))
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

    private static byte[] TrimTrailingZeros(byte[] bytes)
    {
        int length = bytes.Length;
        while (length > 0 && bytes[length - 1] == 0)
        {
            length--;
        }

        if (length == bytes.Length)
        {
            return bytes;
        }

        byte[] trimmed = new byte[length];
        Buffer.BlockCopy(bytes, 0, trimmed, 0, length);
        return trimmed;
    }

    private static bool TryConvertDibToPng(
        byte[] dibBytes,
        out byte[] pngBytes,
        out int width,
        out int height)
    {
        pngBytes = null;
        width = 0;
        height = 0;

        if (dibBytes.Length < 40)
        {
            return false;
        }

        int headerSize = BitConverter.ToInt32(dibBytes, 0);
        if (headerSize < 40 || headerSize > dibBytes.Length)
        {
            return false;
        }

        width = BitConverter.ToInt32(dibBytes, 4);
        int rawHeight = BitConverter.ToInt32(dibBytes, 8);
        if (width <= 0 || rawHeight == 0)
        {
            return false;
        }

        height = Math.Abs(rawHeight);
        ushort bitCount = BitConverter.ToUInt16(dibBytes, 14);
        uint compression = BitConverter.ToUInt32(dibBytes, 16);
        uint colorsUsed = headerSize >= 36 ? BitConverter.ToUInt32(dibBytes, 32) : 0;
        if ((bitCount != 24 && bitCount != 32)
            || (compression != BI_RGB && compression != BI_BITFIELDS))
        {
            return false;
        }

        int pixelOffset = CalculateDibPixelOffset(headerSize, bitCount, compression, colorsUsed);
        long stride = (((long)width * bitCount + 31) / 32) * 4;
        long requiredBytes = pixelOffset + stride * height;
        if (pixelOffset < 0 || stride <= 0 || requiredBytes > dibBytes.Length)
        {
            return false;
        }

        Color32[] pixels = new Color32[width * height];
        bool bottomUp = rawHeight > 0;
        bool sawNonZeroAlpha = false;

        for (int y = 0; y < height; y++)
        {
            int sourceY = bottomUp ? height - 1 - y : y;
            int rowStart = pixelOffset + (int)(sourceY * stride);
            for (int x = 0; x < width; x++)
            {
                int source = rowStart + x * (bitCount / 8);
                byte b = dibBytes[source];
                byte g = dibBytes[source + 1];
                byte r = dibBytes[source + 2];
                byte a = bitCount == 32 ? dibBytes[source + 3] : (byte)255;
                if (a > 0)
                {
                    sawNonZeroAlpha = true;
                }

                pixels[y * width + x] = new Color32(r, g, b, a);
            }
        }

        if (bitCount == 32 && !sawNonZeroAlpha)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i].a = 255;
            }
        }

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            pngBytes = texture.EncodeToPNG();
            return pngBytes != null && pngBytes.Length > 0;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static int CalculateDibPixelOffset(
        int headerSize,
        ushort bitCount,
        uint compression,
        uint colorsUsed)
    {
        long offset = headerSize;
        if (bitCount <= 8)
        {
            long colorCount = colorsUsed > 0 ? colorsUsed : 1L << bitCount;
            offset += colorCount * 4;
        }
        else if (compression == BI_BITFIELDS && headerSize == 40)
        {
            offset += 12;
        }

        if (offset > int.MaxValue)
        {
            return -1;
        }

        return (int)offset;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClipboardFormatName(
        uint format,
        StringBuilder lpszFormatName,
        int cchMaxCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(
        IntPtr hDrop,
        uint iFile,
        StringBuilder lpszFile,
        uint cch);
#endif
}
