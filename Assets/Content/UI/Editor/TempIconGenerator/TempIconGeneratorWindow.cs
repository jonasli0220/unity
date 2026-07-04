using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

using DrawingBitmap = System.Drawing.Bitmap;
using DrawingBrush = System.Drawing.SolidBrush;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPen = System.Drawing.Pen;
using DrawingPointF = System.Drawing.PointF;
using DrawingRectangleF = System.Drawing.RectangleF;
using DrawingSizeF = System.Drawing.SizeF;

public class TempIconGeneratorWindow : EditorWindow
{
    private const string ToolTitle = "Temp Icon Generator";
    private const string ProjectMenuPath = "Assets/UI/Temp Icon Generator";
    private const string ToolsMenuPath = "Tools/UI/Temp Icon Generator";
    private const string DefaultTargetFolder = "Assets/Content/UI";
    private const string DefaultFontName = "Microsoft YaHei";
    private const int MaxWatermarkFontPixelSize = 220;

    private readonly List<TempIconEntry> entries = new List<TempIconEntry>();
    private string targetFolder = DefaultTargetFolder;
    private int defaultWidth;
    private int defaultHeight;
    private Vector2 scroll;
    private GUIStyle placeholderStyle;
    private GUIStyle rowStyle;
    private GUIStyle textFieldStyle;
    private GUIStyle rowIndexStyle;
    private GUIStyle sizeLabelStyle;
    private GUIStyle rowButtonStyle;
    private GUIStyle footerButtonStyle;

    [MenuItem(ToolsMenuPath, false, 2010)]
    public static void OpenFromTools()
    {
        Open(ResolveSelectedFolderPath() ?? DefaultTargetFolder);
    }

    [MenuItem(ProjectMenuPath, false, 2110)]
    public static void OpenFromProject()
    {
        Open(ResolveSelectedFolderPath() ?? DefaultTargetFolder);
    }

    [MenuItem(ProjectMenuPath, true)]
    private static bool ValidateOpenFromProject()
    {
        return !string.IsNullOrEmpty(ResolveSelectedFolderPath());
    }

    private static void Open(string folderPath)
    {
        var window = GetWindow<TempIconGeneratorWindow>(ToolTitle);
        window.minSize = new Vector2(860, 500);
        window.SetTargetFolder(folderPath, true);
        window.Show();
    }

    private void OnEnable()
    {
        if (string.IsNullOrEmpty(targetFolder))
        {
            targetFolder = DefaultTargetFolder;
        }

        RefreshDefaultSize();

        if (entries.Count == 0)
        {
            AddEntry();
        }
    }

    private void OnGUI()
    {
        EnsureStyles();

        EditorGUILayout.Space(20);
        DrawTargetFolderBar();

        EditorGUILayout.Space(20);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawEntryRows();
        EditorGUILayout.EndScrollView();

        GUILayout.FlexibleSpace();
        DrawFooter();
        EditorGUILayout.Space(18);
    }

    private void DrawTargetFolderBar()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(28);

            EditorGUI.BeginChangeCheck();
            var editedPath = EditorGUILayout.DelayedTextField(targetFolder, textFieldStyle, GUILayout.Height(34), GUILayout.MinWidth(460));
            if (EditorGUI.EndChangeCheck())
            {
                SetTargetFolder(editedPath, false);
            }

            GUILayout.Space(16);
            if (GUILayout.Button("change folder", footerButtonStyle, GUILayout.Width(150), GUILayout.Height(34)))
            {
                ChangeTargetFolder();
            }

            GUILayout.FlexibleSpace();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(30);
            var sizeText = defaultWidth > 0 && defaultHeight > 0
                ? $"default size: {defaultWidth} x {defaultHeight}"
                : "default size: 0 x 0";
            EditorGUILayout.LabelField(sizeText, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
        }
    }

    private void DrawEntryRows()
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var rowRect = GUILayoutUtility.GetRect(0f, 72f, GUILayout.ExpandWidth(true));
            rowRect.x += 28f;
            rowRect.y += 4f;
            rowRect.width -= 56f;
            rowRect.height = 58f;

            GUI.Box(rowRect, GUIContent.none, rowStyle);

            const float controlHeight = 28f;
            const float innerPadding = 18f;
            const float indexWidth = 36f;
            const float gap = 16f;
            const float tightGap = 8f;
            const float sizeFieldWidth = 82f;
            const float sizeLabelWidth = 18f;
            const float removeButtonWidth = 36f;
            const float removeButtonHeight = 28f;

            var centerY = rowRect.y + (rowRect.height - controlHeight) * 0.5f;
            var x = rowRect.x + innerPadding;
            var rightX = rowRect.xMax - innerPadding;
            var hasRemoveButton = entries.Count > 1;
            if (hasRemoveButton)
            {
                rightX -= removeButtonWidth + gap;
            }

            var sizeBlockWidth = sizeFieldWidth + tightGap + sizeLabelWidth + tightGap + sizeFieldWidth;
            var sizeX = rightX - sizeBlockWidth;
            var textAreaWidth = Mathf.Max(320f, sizeX - x - indexWidth - gap - gap);
            var nameWidth = Mathf.Clamp(textAreaWidth * 0.48f, 200f, 345f);
            var watermarkWidth = Mathf.Max(200f, textAreaWidth - nameWidth - gap);

            GUI.Label(new Rect(x, centerY, indexWidth, controlHeight), (i + 1).ToString(), rowIndexStyle);
            x += indexWidth + gap;

            entry.Name = DrawTextFieldWithPlaceholder(new Rect(x, centerY, nameWidth, controlHeight), entry.Name, "输入资源命名");
            x += nameWidth + gap;

            entry.Watermark = DrawTextFieldWithPlaceholder(new Rect(x, centerY, watermarkWidth, controlHeight), entry.Watermark, "添加文本水印");

            entry.Width = Mathf.Max(0, EditorGUI.IntField(new Rect(sizeX, centerY, sizeFieldWidth, controlHeight), entry.Width, textFieldStyle));
            sizeX += sizeFieldWidth + tightGap;

            GUI.Label(new Rect(sizeX, centerY, sizeLabelWidth, controlHeight), "x", sizeLabelStyle);
            sizeX += sizeLabelWidth + tightGap;

            entry.Height = Mathf.Max(0, EditorGUI.IntField(new Rect(sizeX, centerY, sizeFieldWidth, controlHeight), entry.Height, textFieldStyle));

            if (hasRemoveButton)
            {
                var removeRect = new Rect(rowRect.xMax - innerPadding - removeButtonWidth, rowRect.y + (rowRect.height - removeButtonHeight) * 0.5f, removeButtonWidth, removeButtonHeight);
                if (GUI.Button(removeRect, "-", rowButtonStyle))
                {
                    entries.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }
            }
        }
    }

    private void DrawFooter()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(28);
            if (GUILayout.Button("+add", footerButtonStyle, GUILayout.Width(140), GUILayout.Height(34)))
            {
                AddEntry(true);
            }

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(!CanCreate()))
            {
                if (GUILayout.Button("create", footerButtonStyle, GUILayout.Width(120), GUILayout.Height(34)))
                {
                    CreateTempIcons();
                }
            }

            GUILayout.Space(24);
            if (GUILayout.Button("cancel", footerButtonStyle, GUILayout.Width(120), GUILayout.Height(34)))
            {
                Close();
            }

            GUILayout.Space(28);
        }
    }

    private string DrawTextFieldWithPlaceholder(string value, string placeholder, params GUILayoutOption[] options)
    {
        var rect = EditorGUILayout.GetControlRect(false, 28, options);
        return DrawTextFieldWithPlaceholder(rect, value, placeholder);
    }

    private string DrawTextFieldWithPlaceholder(Rect rect, string value, string placeholder)
    {
        value = EditorGUI.TextField(rect, value, textFieldStyle);

        if (string.IsNullOrEmpty(value) && Event.current.type == EventType.Repaint)
        {
            var placeholderRect = new Rect(rect.x + 6, rect.y + 2, rect.width - 12, rect.height - 4);
            GUI.Label(placeholderRect, placeholder, placeholderStyle);
        }

        return value;
    }

    private void ChangeTargetFolder()
    {
        var currentAbsolutePath = AssetPathToAbsolutePath(targetFolder);
        if (string.IsNullOrEmpty(currentAbsolutePath) || !Directory.Exists(currentAbsolutePath))
        {
            currentAbsolutePath = Application.dataPath;
        }

        var selectedAbsolutePath = EditorUtility.OpenFolderPanel("Select target folder", currentAbsolutePath, string.Empty);
        if (string.IsNullOrEmpty(selectedAbsolutePath))
        {
            return;
        }

        var assetPath = AbsolutePathToAssetPath(selectedAbsolutePath);
        if (string.IsNullOrEmpty(assetPath))
        {
            EditorUtility.DisplayDialog(ToolTitle, "Please select a folder under this Unity project's Assets directory.", "OK");
            return;
        }

        SetTargetFolder(assetPath, false);
    }

    private void SetTargetFolder(string folderPath, bool resetEntries)
    {
        var oldWidth = defaultWidth;
        var oldHeight = defaultHeight;
        var normalizedPath = NormalizeAssetFolderPath(folderPath);
        targetFolder = string.IsNullOrEmpty(normalizedPath) ? folderPath.Replace("\\", "/").Trim() : normalizedPath;
        RefreshDefaultSize();

        if (resetEntries || entries.Count == 0)
        {
            entries.Clear();
            AddEntry();
            return;
        }

        foreach (var entry in entries)
        {
            if ((entry.Width == 0 && entry.Height == 0) || (entry.Width == oldWidth && entry.Height == oldHeight))
            {
                entry.Width = defaultWidth;
                entry.Height = defaultHeight;
            }
        }
    }

    private void AddEntry(bool inferNameFromPrevious = false)
    {
        entries.Add(new TempIconEntry
        {
            Name = inferNameFromPrevious ? BuildNextEntryName() : string.Empty,
            Width = defaultWidth,
            Height = defaultHeight
        });
    }

    private string BuildNextEntryName()
    {
        var sourceName = GetLastFilledEntryName();
        if (string.IsNullOrEmpty(sourceName))
        {
            return string.Empty;
        }

        var candidate = IncrementNameSuffix(sourceName);
        for (var i = 0; i < 1000 && IsEntryNameUsed(candidate); i++)
        {
            candidate = IncrementNameSuffix(candidate);
        }

        return candidate;
    }

    private string GetLastFilledEntryName()
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var name = entries[i].Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }

        return string.Empty;
    }

    private static string IncrementNameSuffix(string sourceName)
    {
        var trimmedName = string.IsNullOrWhiteSpace(sourceName) ? string.Empty : sourceName.Trim();
        if (string.IsNullOrEmpty(trimmedName))
        {
            return string.Empty;
        }

        var extension = string.Empty;
        var stem = trimmedName;
        if (trimmedName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            extension = trimmedName.Substring(trimmedName.Length - 4);
            stem = trimmedName.Substring(0, trimmedName.Length - 4);
        }

        var digitEnd = stem.Length - 1;
        while (digitEnd >= 0 && char.IsDigit(stem[digitEnd]))
        {
            digitEnd--;
        }

        if (digitEnd == stem.Length - 1)
        {
            return $"{stem}_1{extension}";
        }

        var prefix = stem.Substring(0, digitEnd + 1);
        var numberText = stem.Substring(digitEnd + 1);
        if (!long.TryParse(numberText, out var number))
        {
            return $"{stem}_1{extension}";
        }

        return $"{prefix}{(number + 1).ToString($"D{numberText.Length}")}{extension}";
    }

    private bool IsEntryNameUsed(string name)
    {
        var key = NormalizeNameKey(name);
        foreach (var entry in entries)
        {
            if (string.Equals(NormalizeNameKey(entry.Name), key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var fileName = SanitizeFileName(string.IsNullOrWhiteSpace(name) ? "temp_icon" : name.Trim());
        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".png";
        }

        return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>($"{targetFolder}/{fileName}") != null;
    }

    private static string NormalizeNameKey(string name)
    {
        var key = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        return key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? key.Substring(0, key.Length - 4) : key;
    }

    private void RefreshDefaultSize()
    {
        var size = FindMostCommonDirectTextureSize(targetFolder);
        defaultWidth = size.Width;
        defaultHeight = size.Height;
    }

    private bool CanCreate()
    {
        return AssetDatabase.IsValidFolder(targetFolder)
            && entries.Any(entry => !string.IsNullOrWhiteSpace(entry.Name));
    }

    private void CreateTempIcons()
    {
        var normalizedPath = NormalizeAssetFolderPath(targetFolder);
        if (string.IsNullOrEmpty(normalizedPath) || !AssetDatabase.IsValidFolder(normalizedPath))
        {
            EditorUtility.DisplayDialog(ToolTitle, "Target folder must be a valid Assets folder.", "OK");
            return;
        }

        targetFolder = normalizedPath;
        var validEntries = entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)).ToList();
        if (validEntries.Count == 0)
        {
            EditorUtility.DisplayDialog(ToolTitle, "Please add at least one resource name.", "OK");
            return;
        }

        for (var i = 0; i < validEntries.Count; i++)
        {
            if (validEntries[i].Width <= 0 || validEntries[i].Height <= 0)
            {
                EditorUtility.DisplayDialog(ToolTitle, $"Row {i + 1} needs a size greater than 0.", "OK");
                return;
            }
        }

        var createdPaths = new List<string>();
        try
        {
            foreach (var entry in validEntries)
            {
                var assetPath = CreateSingleTempIcon(entry);
                createdPaths.Add(assetPath);
            }

            AssetDatabase.Refresh();
            foreach (var assetPath in createdPaths)
            {
                ConfigureTextureImporter(assetPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            TempIconGeneratorSuccessWindow.Open(createdPaths);
            Close();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Temp Icon Create Failed", exception.Message, "OK");
        }
    }

    private string CreateSingleTempIcon(TempIconEntry entry)
    {
        var fileName = SanitizeFileName(entry.Name.Trim());
        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".png";
        }

        var requestedAssetPath = $"{targetFolder}/{fileName}";
        var assetPath = AssetDatabase.GenerateUniqueAssetPath(requestedAssetPath);
        var absolutePath = AssetPathToAbsolutePath(assetPath);
        var folder = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var watermark = string.IsNullOrWhiteSpace(entry.Watermark) ? Path.GetFileNameWithoutExtension(fileName) : entry.Watermark.Trim();
        TempIconPngWriter.Write(absolutePath, entry.Width, entry.Height, watermark);
        return assetPath;
    }

    private static void ConfigureTextureImporter(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
    }

    private static IconSize FindMostCommonDirectTextureSize(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            return IconSize.Zero;
        }

        var sizes = new Dictionary<string, IconSizeCount>();
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath }))
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var parentFolder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (!string.Equals(parentFolder, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                continue;
            }

            importer.GetSourceTextureWidthAndHeight(out var width, out var height);
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            var key = $"{width}x{height}";
            if (!sizes.TryGetValue(key, out var count))
            {
                count = new IconSizeCount(width, height);
                sizes.Add(key, count);
            }

            count.Count++;
        }

        if (sizes.Count == 0)
        {
            return IconSize.Zero;
        }

        return sizes.Values
            .OrderByDescending(size => size.Count)
            .ThenByDescending(size => size.Width * size.Height)
            .First()
            .ToIconSize();
    }

    private static string ResolveSelectedFolderPath()
    {
        var activePath = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (AssetDatabase.IsValidFolder(activePath))
        {
            return activePath;
        }

        foreach (var guid in Selection.assetGUIDs)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return assetPath;
            }
        }

        return null;
    }

    private static string NormalizeAssetFolderPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var path = rawPath.Trim().Replace("\\", "/").TrimEnd('/');
        var absoluteAssetPath = AbsolutePathToAssetPath(path);
        if (!string.IsNullOrEmpty(absoluteAssetPath))
        {
            path = absoluteAssetPath;
        }
        else if (path.StartsWith("UI/", StringComparison.OrdinalIgnoreCase))
        {
            path = $"Assets/Content/{path}";
        }
        else if (string.Equals(path, "UI", StringComparison.OrdinalIgnoreCase))
        {
            path = "Assets/Content/UI";
        }
        else if (path.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
        {
            path = $"Assets/{path}";
        }

        return AssetDatabase.IsValidFolder(path) ? path : null;
    }

    private static string AssetPathToAbsolutePath(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return null;
        }

        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        return string.IsNullOrEmpty(projectRoot) ? null : Path.Combine(projectRoot, assetPath).Replace("\\", "/");
    }

    private static string AbsolutePathToAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalizedPath = path.Replace("\\", "/").TrimEnd('/');
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName.Replace("\\", "/").TrimEnd('/');
        if (string.IsNullOrEmpty(projectRoot))
        {
            return null;
        }

        if (!normalizedPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var assetPath = normalizedPath.Substring(projectRoot.Length + 1);
        return assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || string.Equals(assetPath, "Assets", StringComparison.OrdinalIgnoreCase)
            ? assetPath
            : null;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrEmpty(sanitized) ? "temp_icon" : sanitized;
    }

    private void EnsureStyles()
    {
        if (placeholderStyle == null)
        {
            placeholderStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                fontStyle = UnityEngine.FontStyle.Bold
            };
            placeholderStyle.normal.textColor = new Color(0.55f, 0.55f, 0.55f, 0.65f);
        }

        if (textFieldStyle == null)
        {
            textFieldStyle = new GUIStyle(EditorStyles.textField)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 13
            };
        }

        if (rowIndexStyle == null)
        {
            rowIndexStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14
            };
        }

        if (sizeLabelStyle == null)
        {
            sizeLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14
            };
        }

        if (rowButtonStyle == null)
        {
            rowButtonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = UnityEngine.FontStyle.Bold
            };
        }

        if (rowStyle == null)
        {
            rowStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(28, 28, 0, 0)
            };
        }

        if (footerButtonStyle == null)
        {
            footerButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                fontStyle = UnityEngine.FontStyle.Bold
            };
        }
    }

    private sealed class TempIconEntry
    {
        public string Name = string.Empty;
        public string Watermark = string.Empty;
        public int Width;
        public int Height;
    }

    private struct IconSize
    {
        public static readonly IconSize Zero = new IconSize(0, 0);

        public IconSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width;
        public int Height;
    }

    private sealed class IconSizeCount
    {
        public IconSizeCount(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width;
        public int Height;
        public int Count;

        public IconSize ToIconSize()
        {
            return new IconSize(Width, Height);
        }
    }

    private static class TempIconPngWriter
    {
        public static void Write(string absolutePath, int width, int height, string watermark)
        {
            using (var bitmap = new DrawingBitmap(width, height, PixelFormat.Format32bppArgb))
            using (var graphics = DrawingGraphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.Clear(DrawingColor.Transparent);

                DrawPlaceholderShape(graphics, width, height);
                DrawWatermark(graphics, width, height, watermark);

                bitmap.Save(absolutePath, ImageFormat.Png);
            }
        }

        private static void DrawPlaceholderShape(DrawingGraphics graphics, int width, int height)
        {
            var minSize = Math.Min(width, height);
            var inset = Math.Min(Math.Max(1f, minSize * 0.04f), minSize * 0.25f);
            var shapeWidth = width - inset * 2f;
            var shapeHeight = height - inset * 2f;
            var shapeRect = new DrawingRectangleF(inset, inset, shapeWidth, shapeHeight);
            var isIconLike = Math.Abs(width - height) <= Math.Max(width, height) * 0.18f;

            using (var fill = new DrawingBrush(DrawingColor.FromArgb(235, 217, 217, 217)))
            using (var border = new DrawingPen(DrawingColor.FromArgb(120, 92, 92, 92), Math.Max(1f, minSize / 72f)))
            {
                if (isIconLike)
                {
                    var circleSize = minSize - inset * 2f;
                    var circleRect = new DrawingRectangleF((width - circleSize) * 0.5f, (height - circleSize) * 0.5f, circleSize, circleSize);
                    graphics.FillEllipse(fill, circleRect);
                    graphics.DrawEllipse(border, circleRect);
                }
                else
                {
                    using (var path = CreateRoundedRectangle(shapeRect, Math.Max(4f, minSize * 0.08f)))
                    {
                        graphics.FillPath(fill, path);
                        graphics.DrawPath(border, path);
                    }
                }
            }
        }

        private static void DrawWatermark(DrawingGraphics graphics, int width, int height, string watermark)
        {
            var text = string.IsNullOrWhiteSpace(watermark) ? "TEMP" : watermark.Trim();
            var minSize = Math.Min(width, height);
            var textBounds = new DrawingRectangleF(width * 0.06f, height * 0.16f, width * 0.88f, height * 0.68f);
            var maxFontSize = Math.Min(MaxWatermarkFontPixelSize, Math.Max(9, (int)(minSize * 0.28f)));
            var minFontSize = Math.Min(maxFontSize, Math.Max(7, Math.Min(14, maxFontSize)));

            for (var fontSize = maxFontSize; fontSize >= minFontSize; fontSize--)
            {
                using (var font = new DrawingFont(DefaultFontName, fontSize, DrawingFontStyle.Bold, System.Drawing.GraphicsUnit.Pixel))
                {
                    var lineHeight = font.GetHeight(graphics) * 1.05f;
                    var textSize = MeasureText(graphics, text, font);
                    if (textSize.Width <= textBounds.Width && lineHeight <= textBounds.Height)
                    {
                        DrawCenteredLines(graphics, new List<string> { text }, font, textBounds, lineHeight, lineHeight);
                        return;
                    }
                }
            }

            for (var fontSize = maxFontSize; fontSize >= minFontSize; fontSize--)
            {
                using (var font = new DrawingFont(DefaultFontName, fontSize, DrawingFontStyle.Bold, System.Drawing.GraphicsUnit.Pixel))
                {
                    var lines = WrapText(graphics, text, font, textBounds.Width);
                    var lineHeight = font.GetHeight(graphics) * 1.05f;
                    var totalHeight = lineHeight * lines.Count;
                    var maxLineWidth = lines.Count == 0 ? 0f : lines.Max(line => MeasureText(graphics, line, font).Width);

                    if (maxLineWidth <= textBounds.Width && totalHeight <= textBounds.Height)
                    {
                        DrawCenteredLines(graphics, lines, font, textBounds, lineHeight, totalHeight);
                        return;
                    }
                }
            }

            using (var font = new DrawingFont(DefaultFontName, minFontSize, DrawingFontStyle.Bold, System.Drawing.GraphicsUnit.Pixel))
            {
                var lines = WrapText(graphics, text, font, textBounds.Width);
                var lineHeight = font.GetHeight(graphics) * 1.05f;
                var maxLines = Math.Max(1, (int)Math.Floor(textBounds.Height / lineHeight));
                if (lines.Count > maxLines)
                {
                    lines = lines.Take(maxLines).ToList();
                    lines[lines.Count - 1] = TrimToFit(graphics, lines[lines.Count - 1], font, textBounds.Width);
                }

                DrawCenteredLines(graphics, lines, font, textBounds, lineHeight, lineHeight * lines.Count);
            }
        }

        private static void DrawCenteredLines(DrawingGraphics graphics, List<string> lines, DrawingFont font, DrawingRectangleF bounds, float lineHeight, float totalHeight)
        {
            using (var brush = new DrawingBrush(DrawingColor.FromArgb(235, 20, 20, 20)))
            {
                var y = bounds.Y + (bounds.Height - totalHeight) * 0.5f;
                foreach (var line in lines)
                {
                    var textSize = MeasureText(graphics, line, font);
                    var x = bounds.X + (bounds.Width - textSize.Width) * 0.5f;
                    var lineY = y + (lineHeight - textSize.Height) * 0.5f;
                    graphics.DrawString(line, font, brush, new DrawingPointF(x, lineY));
                    y += lineHeight;
                }
            }
        }

        private static List<string> WrapText(DrawingGraphics graphics, string text, DrawingFont font, float maxWidth)
        {
            var lines = new List<string>();
            var paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            foreach (var paragraph in paragraphs)
            {
                var currentLine = string.Empty;
                foreach (var ch in paragraph)
                {
                    var candidate = currentLine + ch;
                    if (currentLine.Length > 0 && MeasureText(graphics, candidate, font).Width > maxWidth)
                    {
                        lines.Add(currentLine);
                        currentLine = ch.ToString();
                    }
                    else
                    {
                        currentLine = candidate;
                    }
                }

                if (!string.IsNullOrEmpty(currentLine))
                {
                    lines.Add(currentLine);
                }
            }

            if (lines.Count == 0)
            {
                lines.Add(string.Empty);
            }

            return lines;
        }

        private static string TrimToFit(DrawingGraphics graphics, string text, DrawingFont font, float maxWidth)
        {
            const string ellipsis = "...";
            var result = text;
            while (result.Length > 0 && MeasureText(graphics, result + ellipsis, font).Width > maxWidth)
            {
                result = result.Substring(0, result.Length - 1);
            }

            return result.Length == 0 ? ellipsis : result + ellipsis;
        }

        private static DrawingSizeF MeasureText(DrawingGraphics graphics, string text, DrawingFont font)
        {
            return graphics.MeasureString(text, font, new DrawingPointF(0f, 0f), System.Drawing.StringFormat.GenericDefault);
        }

        private static GraphicsPath CreateRoundedRectangle(DrawingRectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2f;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

public class TempIconGeneratorSuccessWindow : EditorWindow
{
    private List<string> createdPaths = new List<string>();
    private Vector2 scroll;
    private GUIStyle titleStyle;
    private GUIStyle rowStyle;
    private GUIStyle indexStyle;
    private GUIStyle pathStyle;
    private GUIStyle buttonStyle;

    public static void Open(List<string> paths)
    {
        var window = CreateInstance<TempIconGeneratorSuccessWindow>();
        window.titleContent = new GUIContent("Temp Icon Generation Result");
        window.createdPaths = new List<string>(paths);
        window.minSize = new Vector2(860, 360);
        window.ShowUtility();
    }

    private void OnGUI()
    {
        EnsureStyles();

        EditorGUILayout.Space(24);
        GUILayout.Label("Resources Created Successfully", titleStyle, GUILayout.Height(32));
        EditorGUILayout.Space(24);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        for (var i = 0; i < createdPaths.Count; i++)
        {
            var rowRect = GUILayoutUtility.GetRect(0f, 60f, GUILayout.ExpandWidth(true));
            rowRect.x += 42f;
            rowRect.y += 4f;
            rowRect.width -= 84f;
            rowRect.height = 52f;

            GUI.Box(rowRect, GUIContent.none, rowStyle);

            const float innerPadding = 18f;
            const float indexWidth = 44f;
            const float gap = 12f;
            const float buttonWidth = 104f;
            const float buttonHeight = 40f;
            const float pathHeight = 38f;

            var centerY = rowRect.y + rowRect.height * 0.5f;
            var x = rowRect.x + innerPadding;

            GUI.Label(new Rect(x, centerY - pathHeight * 0.5f, indexWidth, pathHeight), (i + 1).ToString(), indexStyle);
            x += indexWidth + gap;

            var copyRect = new Rect(rowRect.xMax - innerPadding - buttonWidth, centerY - buttonHeight * 0.5f, buttonWidth, buttonHeight);
            var pathRect = new Rect(x, centerY - pathHeight * 0.5f, copyRect.x - x - gap, pathHeight);
            EditorGUI.SelectableLabel(pathRect, createdPaths[i], pathStyle);

            if (GUI.Button(copyRect, "copy", buttonStyle))
            {
                EditorGUIUtility.systemCopyBuffer = createdPaths[i];
                ShowNotification(new GUIContent("copied"));
            }
        }

        EditorGUILayout.EndScrollView();
        GUILayout.FlexibleSpace();

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (createdPaths.Count > 1 && GUILayout.Button("copy all", buttonStyle, GUILayout.Width(110), GUILayout.Height(34)))
            {
                EditorGUIUtility.systemCopyBuffer = string.Join(Environment.NewLine, createdPaths);
                ShowNotification(new GUIContent("copied all"));
            }

            GUILayout.Space(16);
            if (GUILayout.Button("close", buttonStyle, GUILayout.Width(110), GUILayout.Height(34)))
            {
                Close();
            }

            GUILayout.Space(28);
        }

        EditorGUILayout.Space(18);
    }

    private void EnsureStyles()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = UnityEngine.FontStyle.Bold
            };
        }

        if (rowStyle == null)
        {
            rowStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(28, 28, 0, 0)
            };
        }

        if (indexStyle == null)
        {
            indexStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14
            };
        }

        if (pathStyle == null)
        {
            pathStyle = new GUIStyle(EditorStyles.textField)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 14,
                padding = new RectOffset(6, 6, 0, 0)
            };
        }

        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = UnityEngine.FontStyle.Bold
            };
        }
    }
}
