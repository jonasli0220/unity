using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR_WIN
using System.Drawing.Imaging;
using Forms = System.Windows.Forms;
#endif

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
    private static readonly string[] EncodedImageFormats =
    {
        "PNG",
        "image/png",
        "Portable Network Graphics",
        "JFIF",
        "JPEG",
        "image/jpeg"
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

#if UNITY_EDITOR_WIN
    private static FigmaPasteClipboardPayload ReadWindowsClipboard()
    {
        return RunOnStaThread(() =>
        {
            FigmaPasteClipboardPayload payload = new FigmaPasteClipboardPayload
            {
                CapturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            Forms.IDataObject data = Forms.Clipboard.GetDataObject();
            if (data == null)
            {
                return payload;
            }

            string[] formats = data.GetFormats(true);
            for (int i = 0; i < formats.Length; i++)
            {
                payload.Formats.Add(ReadFormatInfo(data, formats[i]));
            }

            payload.Text = ReadClipboardString(data, Forms.DataFormats.UnicodeText);
            if (string.IsNullOrEmpty(payload.Text))
            {
                payload.Text = ReadClipboardString(data, Forms.DataFormats.Text);
            }

            payload.Html = ReadClipboardString(data, Forms.DataFormats.Html);
            if (string.IsNullOrEmpty(payload.Html))
            {
                payload.Html = ReadClipboardString(data, "text/html");
            }

            payload.Svg = ReadClipboardString(data, "image/svg+xml");
            if (string.IsNullOrEmpty(payload.Svg))
            {
                payload.Svg = ReadClipboardString(data, "text/svg");
            }

            ReadFileDrops(data, payload);
            ReadImage(data, payload);
            return payload;
        });
    }

    private static FigmaPasteClipboardFormatInfo ReadFormatInfo(
        Forms.IDataObject data,
        string format)
    {
        FigmaPasteClipboardFormatInfo info = new FigmaPasteClipboardFormatInfo
        {
            Name = format,
            DataType = string.Empty,
            Preview = string.Empty
        };

        try
        {
            object value = data.GetData(format, true);
            if (value == null)
            {
                info.DataType = "null";
                return info;
            }

            info.DataType = value.GetType().FullName;

            string stringValue = value as string;
            if (stringValue != null)
            {
                info.Preview = TrimPreview(stringValue);
                return info;
            }

            string[] paths = value as string[];
            if (paths != null)
            {
                info.Preview = string.Join(" | ", paths);
                return info;
            }

            byte[] bytes = value as byte[];
            if (bytes != null)
            {
                info.Preview = bytes.Length + " bytes";
                return info;
            }

            Stream stream = value as Stream;
            if (stream != null)
            {
                info.Preview = stream.CanSeek ? stream.Length + " stream bytes" : "stream";
            }
        }
        catch (Exception ex)
        {
            info.DataType = "unreadable";
            info.Preview = ex.GetType().Name + ": " + ex.Message;
        }

        return info;
    }

    private static string ReadClipboardString(Forms.IDataObject data, string format)
    {
        try
        {
            if (!data.GetDataPresent(format, true))
            {
                return string.Empty;
            }

            object value = data.GetData(format, true);
            return value as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void ReadFileDrops(
        Forms.IDataObject data,
        FigmaPasteClipboardPayload payload)
    {
        try
        {
            if (!data.GetDataPresent(Forms.DataFormats.FileDrop, true))
            {
                return;
            }

            string[] paths = data.GetData(Forms.DataFormats.FileDrop, true) as string[];
            if (paths == null)
            {
                return;
            }

            payload.FileDrops.AddRange(paths);
        }
        catch
        {
            // File drops are diagnostic only.
        }
    }

    private static void ReadImage(
        Forms.IDataObject data,
        FigmaPasteClipboardPayload payload)
    {
        if (TryReadEncodedImage(data, payload))
        {
            return;
        }

        try
        {
            if (!Forms.Clipboard.ContainsImage())
            {
                return;
            }

            using (System.Drawing.Image image = Forms.Clipboard.GetImage())
            {
                if (image == null)
                {
                    return;
                }

                payload.ImagePngBytes = SaveImageAsPng(image);
                payload.ImageWidth = image.Width;
                payload.ImageHeight = image.Height;
                payload.ImageSourceFormat = Forms.DataFormats.Bitmap;
            }
        }
        catch
        {
            // Leave image empty; the format list still helps diagnose the clipboard.
        }
    }

    private static bool TryReadEncodedImage(
        Forms.IDataObject data,
        FigmaPasteClipboardPayload payload)
    {
        for (int i = 0; i < EncodedImageFormats.Length; i++)
        {
            string format = EncodedImageFormats[i];
            try
            {
                if (!data.GetDataPresent(format, true))
                {
                    continue;
                }

                object value = data.GetData(format, true);
                byte[] sourceBytes = ReadBytes(value);
                if (sourceBytes == null || sourceBytes.Length == 0)
                {
                    continue;
                }

                payload.ImagePngBytes = ConvertImageBytesToPng(
                    sourceBytes,
                    out payload.ImageWidth,
                    out payload.ImageHeight);
                payload.ImageSourceFormat = format;
                return payload.HasImage;
            }
            catch
            {
                // Try the next available image-like format.
            }
        }

        return false;
    }

    private static byte[] ReadBytes(object value)
    {
        byte[] bytes = value as byte[];
        if (bytes != null)
        {
            return bytes;
        }

        Stream stream = value as Stream;
        if (stream == null)
        {
            return null;
        }

        long originalPosition = 0;
        if (stream.CanSeek)
        {
            originalPosition = stream.Position;
            stream.Position = 0;
        }

        using (MemoryStream copy = new MemoryStream())
        {
            stream.CopyTo(copy);
            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }

            return copy.ToArray();
        }
    }

    private static byte[] ConvertImageBytesToPng(byte[] sourceBytes, out int width, out int height)
    {
        using (MemoryStream sourceStream = new MemoryStream(sourceBytes))
        using (System.Drawing.Image image = System.Drawing.Image.FromStream(sourceStream, true, true))
        {
            width = image.Width;
            height = image.Height;
            return SaveImageAsPng(image);
        }
    }

    private static byte[] SaveImageAsPng(System.Drawing.Image image)
    {
        using (MemoryStream output = new MemoryStream())
        {
            image.Save(output, ImageFormat.Png);
            return output.ToArray();
        }
    }

    private static T RunOnStaThread<T>(Func<T> action)
    {
        T result = default(T);
        Exception exception = null;
        Thread thread = new Thread(() =>
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    result = action();
                    return;
                }
                catch (Exception ex)
                {
                    exception = ex;
                    Thread.Sleep(50);
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(3)))
        {
            throw new TimeoutException("Reading the Windows clipboard timed out.");
        }

        if (exception != null)
        {
            throw exception;
        }

        return result;
    }
#endif
}
