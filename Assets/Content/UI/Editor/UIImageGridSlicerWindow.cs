using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Lossless grid slicing from original PNG/JPG bytes; never modifies the source importer.</summary>
public sealed class UIImageGridSlicerWindow : EditorWindow
{
    private Texture2D source;
    private string sourcePath;
    private int columns = 4;
    private int rows = 4;
    private string error;
    private string outputPath;
    private Vector2 scroll;

    [MenuItem("UITools/图片等分切图", false, 2100)]
    public static void Open()
    {
        var window = GetWindow<UIImageGridSlicerWindow>("图片等分切图");
        window.minSize = new Vector2(440, 540);
        string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (IsSupportedImage(selectedPath))
            window.LoadSource(selectedPath);
        window.Show();
    }

    private void OnDisable()
    {
        if (source != null) DestroyImmediate(source);
        source = null;
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        GUILayout.Label("图片等分切图", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("拖入一张图片，预览后生成独立 PNG 碎片。", EditorStyles.wordWrappedLabel);
        Rect dropRect = GUILayoutUtility.GetRect(100, 78, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "将 Project 或文件夹中的 PNG / JPG 拖到这里\n也可点击选择图片", EditorStyles.helpBox);
        HandleDrop(dropRect);
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && dropRect.Contains(Event.current.mousePosition))
        {
            string path = EditorUtility.OpenFilePanelWithFilters("选择切分原图", string.IsNullOrEmpty(sourcePath) ? Application.dataPath : Path.GetDirectoryName(sourcePath),
                new[] { "图片", "png,jpg,jpeg" });
            if (!string.IsNullOrEmpty(path)) LoadSource(path);
            Event.current.Use();
        }

        EditorGUILayout.BeginHorizontal();
        float previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 36;
        columns = Mathf.Clamp(EditorGUILayout.IntField("列数", columns), 1, 32);
        rows = Mathf.Clamp(EditorGUILayout.IntField("行数", rows), 1, 32);
        EditorGUIUtility.labelWidth = previousLabelWidth;
        EditorGUILayout.EndHorizontal();
        if (source != null)
        {
            EditorGUILayout.LabelField(Path.GetFileName(sourcePath), EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(sourcePath, EditorStyles.miniLabel, GUILayout.Height(32));
            EditorGUILayout.LabelField($"原图 {source.width} × {source.height}  ·  共 {columns * rows} 块");
            string reason = ValidateGrid(source.width, source.height, columns, rows);
            if (reason == null)
                EditorGUILayout.LabelField($"每块 {source.width / columns} × {source.height / rows} 像素");
            else
                EditorGUILayout.HelpBox(reason, MessageType.Warning);
            DrawPreview();
            string target = GetAvailableOutputFolder(sourcePath);
            EditorGUILayout.LabelField("输出文件夹（原图同级）", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(target, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(34));
            EditorGUILayout.LabelField($"piece_1.png ～ piece_{columns * rows}.png；从左到右、从上到下。", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUI.DisabledScope(reason != null || EditorApplication.isCompiling))
            {
                if (GUILayout.Button($"生成 {columns * rows} 张碎片", GUILayout.Height(34))) Generate();
            }
        }
        else
            EditorGUILayout.HelpBox("默认 4 × 4。1920 × 1080 原图会生成 16 张 480 × 270 碎片。", MessageType.Info);

        if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        if (!string.IsNullOrEmpty(outputPath))
        {
            EditorGUILayout.HelpBox("碎片已生成：" + outputPath, MessageType.Info);
            if (GUILayout.Button("打开输出文件夹")) EditorUtility.RevealInFinder(outputPath);
        }
        EditorGUILayout.EndScrollView();
    }

    private void HandleDrop(Rect rect)
    {
        Event e = Event.current;
        if (!rect.Contains(e.mousePosition) || (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)) return;
        string path = null;
        if (DragAndDrop.paths.Length == 1) path = DragAndDrop.paths[0];
        else if (DragAndDrop.paths.Length == 0 && DragAndDrop.objectReferences.Length == 1)
            path = AssetDatabase.GetAssetPath(DragAndDrop.objectReferences[0]);
        bool supported = IsSupportedImage(path);
        DragAndDrop.visualMode = supported ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
        if (e.type == EventType.DragPerform)
        {
            if (supported)
            {
                DragAndDrop.AcceptDrag();
                LoadSource(path);
            }
            else error = "请一次拖入一张 PNG 或 JPG 图片。";
        }
        e.Use();
    }

    private void LoadSource(string path)
    {
        Texture2D loaded = null;
        try
        {
            loaded = ReadOriginal(path);
            if (source != null) DestroyImmediate(source);
            source = loaded;
            sourcePath = Path.GetFullPath(path);
            error = null;
            outputPath = null;
        }
        catch (Exception ex)
        {
            if (loaded != null) DestroyImmediate(loaded);
            error = "无法读取图片：" + ex.Message;
        }
        Repaint();
    }

    private void DrawPreview()
    {
        Rect area = GUILayoutUtility.GetRect(100, 240, GUILayout.ExpandWidth(true));
        float scale = Mathf.Min(area.width / source.width, area.height / source.height);
        Rect image = new Rect(area.center.x - source.width * scale / 2, area.center.y - source.height * scale / 2,
            source.width * scale, source.height * scale);
        EditorGUI.DrawTextureTransparent(image, source, ScaleMode.ScaleToFit);
        Handles.BeginGUI();
        Color previous = Handles.color;
        Handles.color = new Color(1, 1, 1, 0.85f);
        for (int x = 0; x <= columns; x++)
        {
            float px = image.x + image.width * x / columns;
            Handles.DrawLine(new Vector3(px, image.y), new Vector3(px, image.yMax));
        }
        for (int y = 0; y <= rows; y++)
        {
            float py = image.y + image.height * y / rows;
            Handles.DrawLine(new Vector3(image.x, py), new Vector3(image.xMax, py));
        }
        Handles.color = previous;
        Handles.EndGUI();
        if (image.width / columns >= 22 && image.height / rows >= 16)
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < columns; col++)
                    GUI.Box(new Rect(image.x + image.width * col / columns + 2, image.y + image.height * row / rows + 2, 28, 18),
                        (row * columns + col + 1).ToString(), EditorStyles.helpBox);
    }

    private void Generate()
    {
        error = null;
        outputPath = null;
        try
        {
            // Re-read on generation in case an artist saved over the image after previewing.
            using (var image = new OriginalImage(sourcePath))
            {
                string target = WritePieces(image.Texture, sourcePath, columns, rows);
                outputPath = target;
                string assetFolder = ToAssetPath(target);
                if (assetFolder != null)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    for (int i = 1; i <= columns * rows; i++)
                    {
                        EditorUtility.DisplayProgressBar("导入切图碎片", $"piece_{i}.png", (float)i / (columns * rows));
                        ConfigureSprite(assetFolder + "/piece_" + i + ".png", image.Texture.width / columns, image.Texture.height / rows);
                    }
                    Selection.activeObject = AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetFolder);
                }
            }
        }
        catch (Exception ex)
        {
            error = outputPath == null ? "生成失败：" + ex.Message : "PNG 已保存，但 Unity 导入未完成：" + ex.Message;
            Debug.LogException(ex);
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    internal static bool IsSupportedImage(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    internal static string ValidateGrid(int width, int height, int columnCount, int rowCount)
    {
        if (columnCount < 1 || columnCount > 32 || rowCount < 1 || rowCount > 32) return "行列数请设为 1～32。";
        if (width < columnCount || height < rowCount) return "原图尺寸过小，请减少行数或列数。";
        if (width % columnCount != 0 || height % rowCount != 0)
            return $"原图 {width} × {height} 无法按 {columnCount} × {rowCount} 等分。请调整行列数，或使用宽度能被列数整除、高度能被行数整除的图片。";
        return null;
    }

    internal static Texture2D ReadOriginal(string path)
    {
        if (!IsSupportedImage(path)) throw new ArgumentException("仅支持 PNG / JPG 图片。");
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false)) throw new IOException("图片格式无效或文件已损坏。");
            return texture;
        }
        catch { DestroyImmediate(texture); throw; }
    }

    internal static string GetAvailableOutputFolder(string path)
    {
        string parent = Path.GetDirectoryName(Path.GetFullPath(path));
        string folder = Path.Combine(parent, "piece");
        int suffix = 2;
        while (Directory.Exists(folder) || File.Exists(folder) || File.Exists(folder + ".meta"))
            folder = Path.Combine(parent, "piece_" + suffix++);
        return folder;
    }

    internal static string WritePieces(Texture2D image, string path, int columnCount, int rowCount)
    {
        string reason = ValidateGrid(image.width, image.height, columnCount, rowCount);
        if (reason != null) throw new ArgumentException(reason);
        int width = image.width / columnCount;
        int height = image.height / rowCount;
        string staging = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), ".ui-grid-slice-" + Guid.NewGuid().ToString("N"));
        Texture2D piece = null;
        Directory.CreateDirectory(staging);
        try
        {
            piece = new Texture2D(width, height, TextureFormat.RGBA32, false);
            piece.hideFlags = HideFlags.HideAndDontSave;
            for (int row = 0; row < rowCount; row++)
                for (int col = 0; col < columnCount; col++)
                {
                    int index = row * columnCount + col + 1;
                    // Unity pixels start at the bottom left; puzzle numbering starts at the top left.
                    piece.SetPixels(image.GetPixels(col * width, image.height - (row + 1) * height, width, height));
                    piece.Apply(false, false);
                    File.WriteAllBytes(Path.Combine(staging, "piece_" + index + ".png"), piece.EncodeToPNG());
                }
            string target = GetAvailableOutputFolder(path);
            // Same-volume rename publishes the complete result atomically and refuses existing folders.
            Directory.Move(staging, target);
            return target;
        }
        finally
        {
            if (piece != null) DestroyImmediate(piece);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    internal static string ToAssetPath(string path)
    {
        string full = Path.GetFullPath(path).Replace('\\', '/');
        string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
        return full.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase) ? "Assets" + full.Substring(assets.Length) : null;
    }

    internal static void ConfigureSprite(string assetPath, int width, int height)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) throw new IOException("无法导入 " + assetPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(width, height)), 32, 16384);
        importer.SaveAndReimport();
    }

    private sealed class OriginalImage : IDisposable
    {
        public readonly Texture2D Texture;
        public OriginalImage(string path) { Texture = ReadOriginal(path); }
        public void Dispose() { DestroyImmediate(Texture); }
    }
}
