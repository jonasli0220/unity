using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Dragon.UI.EditorTools
{
    [Serializable]
    internal sealed class ComicTextBox
    {
        public string text = "请输入文字";
        public Rect rect = new Rect(760, 450, 400, 150);
        public int anchor = 4;
        public bool followAnchor = true;
        public int horizontal = 1;
        public int vertical = 1;
        public Color color = new Color(0.8588236f, 1f, 0.6745098f, 1f);
        public float minSize = 0;
        public float maxSize = 30;
    }

    internal sealed class ComicPageDraft : ScriptableObject
    {
        public string folderPath;
        public string imagePath;
        public int pageNumber = 1;
        public int selectedText = -1;
        public bool imageChanged;
        public List<ComicTextBox> texts = new List<ComicTextBox>();
    }

    public sealed class ComicPageEditorWindow : EditorWindow
    {
        private const string BaseFolder = "Assets/Content/UI/Prefab/exterior_hero_story/resource";
        private const string TemplatePath = BaseFolder + "/a10_24370/comic/comic_1.prefab";
        private const string PrefsFolderKey = "Dragon.ComicPageEditor.TargetFolder";
        private const int PageWidth = 1920;
        private const int PageHeight = 1080;
        private const float InspectorWidth = 294f;
        private static readonly string[] AnchorLabels =
        {
            "左上", "上中", "右上", "左中", "居中", "右中", "左下", "下中", "右下"
        };
        private static readonly string[] HorizontalLabels = { "左", "中", "右" };
        private static readonly string[] VerticalLabels = { "上", "中", "下" };

        private ComicPageDraft draft;
        private Texture2D pageTexture;
        private Sprite pageSprite;
        private string loadedImagePath;
        private RenderTexture previewTexture;
        private GameObject cameraObject;
        private GameObject canvasObject;
        private GameObject previewPage;
        private Camera previewCamera;
        private Canvas previewCanvas;
        private bool previewDirty = true;
        private bool createTextMode;
        private int dragMode;
        private Vector2 dragStart;
        private Rect dragOriginal;
        private Vector2 inspectorScroll;
        private string errorMessage;

        [MenuItem("UITools/漫画页面编辑器", false, 2110)]
        private static void Open()
        {
            ComicPageEditorWindow window = GetWindow<ComicPageEditorWindow>("漫画页面编辑器");
            window.minSize = new Vector2(920, 600);
            window.Show();
        }

        private void OnEnable()
        {
            draft = CreateInstance<ComicPageDraft>();
            draft.hideFlags = HideFlags.HideAndDontSave;
            string savedFolder = EditorPrefs.GetString(PrefsFolderKey, string.Empty);
            if (IsTargetFolder(savedFolder))
            {
                draft.folderPath = savedFolder;
                draft.pageNumber = NextPageNumber(savedFolder);
            }
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            ReleasePreview();
            ReleaseImage();
            if (draft != null) DestroyImmediate(draft);
        }

        private void OnUndoRedo()
        {
            previewDirty = true;
            Repaint();
        }

        private void OnGUI()
        {
            if (draft == null) return;
            DrawTargetRow();
            DrawPageRow();
            DrawImageRow();
            if (!string.IsNullOrEmpty(errorMessage)) EditorGUILayout.HelpBox(errorMessage, MessageType.Error);

            EditorGUILayout.BeginHorizontal();
            Rect available = GUILayoutUtility.GetRect(400, 400, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawCanvas(available);
            EditorGUILayout.BeginVertical(GUILayout.Width(InspectorWidth));
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            DrawInspector();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTargetRow()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("目标资源文件夹", GUILayout.Width(104));
            DefaultAsset folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(draft.folderPath);
            DefaultAsset chosen = (DefaultAsset)EditorGUILayout.ObjectField(folder, typeof(DefaultAsset), false);
            if (chosen != folder) SetTargetFolder(AssetDatabase.GetAssetPath(chosen));
            if (GUILayout.Button("选择", EditorStyles.toolbarButton, GUILayout.Width(48)))
            {
                string initial = string.IsNullOrEmpty(draft.folderPath) ? ToFullPath(BaseFolder) : ToFullPath(draft.folderPath);
                string chosenPath = EditorUtility.OpenFolderPanel("选择赛季 + 英雄资源文件夹", initial, string.Empty);
                if (!string.IsNullOrEmpty(chosenPath))
                    SetTargetFolder(ToAssetPath(chosenPath));
            }
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(draft.folderPath))
                EditorGUILayout.LabelField(draft.folderPath, EditorStyles.miniLabel);
        }

        private void DrawPageRow()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(string.Format("当前：comic_{0}", draft.pageNumber), GUILayout.Width(130));
            int[] pages = GetPageNumbers(draft.folderPath);
            if (pages.Length > 0)
            {
                string[] labels = new[] { "打开已有页…" }.Concat(pages.Select(n => "comic_" + n)).ToArray();
                int current = Array.IndexOf(pages, draft.pageNumber);
                EditorGUI.BeginChangeCheck();
                int selected = EditorGUILayout.Popup(current + 1, labels, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck() && selected > 0 && selected <= pages.Length)
                    LoadPage(pages[selected - 1]);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("＋ 新页", EditorStyles.toolbarButton, GUILayout.Width(72))) NewPage();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawImageRow()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("背景图片", GUILayout.Width(65));
            GUILayout.Label(string.IsNullOrEmpty(draft.imagePath) ? "未选择 PNG / JPG" : Path.GetFileName(draft.imagePath), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("选择图片", EditorStyles.toolbarButton, GUILayout.Width(75)))
            {
                string initial = string.IsNullOrEmpty(draft.imagePath) ? Application.dataPath : Path.GetDirectoryName(ToFullPath(draft.imagePath));
                string path = EditorUtility.OpenFilePanelWithFilters("选择漫画背景图片", initial,
                    new[] { "图片", "png,jpg,jpeg" });
                if (!string.IsNullOrEmpty(path)) SetImage(path);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCanvas(Rect area)
        {
            if (pageTexture == null && !string.IsNullOrEmpty(draft.imagePath)) LoadImageForPreview();
            EditorGUI.DrawRect(area, new Color(0.12f, 0.12f, 0.12f));
            float scale = Mathf.Min((area.width - 24) / PageWidth, (area.height - 24) / PageHeight);
            Rect canvas = new Rect(area.x + (area.width - PageWidth * scale) * .5f,
                area.y + (area.height - PageHeight * scale) * .5f, PageWidth * scale, PageHeight * scale);
            EditorGUI.DrawRect(new Rect(canvas.x - 1, canvas.y - 1, canvas.width + 2, canvas.height + 2), Color.gray);
            if (pageTexture != null)
            {
                if (previewDirty && Event.current.type == EventType.Repaint) RenderPreview();
                if (previewTexture != null) GUI.DrawTexture(canvas, previewTexture, ScaleMode.StretchToFill, false);
            }
            else
            {
                EditorGUI.DrawRect(canvas, new Color(.20f, .20f, .20f));
                GUI.Label(canvas, "先选择背景图片，然后点击「＋ 文本框」并在画布上拖拽", CenteredLabel());
            }

            for (int i = 0; i < draft.texts.Count; i++)
            {
                Rect box = ToScreenRect(draft.texts[i].rect, canvas);
                Color border = i == draft.selectedText ? new Color(0.2f, .85f, 1f) : new Color(1f, 1f, 1f, .5f);
                DrawOutline(box, border, i == draft.selectedText ? 2 : 1);
                if (i == draft.selectedText)
                    EditorGUI.DrawRect(new Rect(box.xMax - 5, box.yMax - 5, 10, 10), border);
            }
            if (createTextMode) EditorGUIUtility.AddCursorRect(canvas, MouseCursor.Arrow);
            HandleCanvasEvent(canvas);
        }

        private void DrawInspector()
        {
            GUILayout.Space(5);
            EditorGUILayout.LabelField("页面内容", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1920 × 1080 · 拖动文本框移动，拖动右下角缩放", EditorStyles.wordWrappedMiniLabel);
            EditorGUI.BeginDisabledGroup(pageTexture == null);
            if (GUILayout.Button(createTextMode ? "在画布上拖出文本框…" : "＋ 文本框", GUILayout.Height(30)))
                createTextMode = !createTextMode;
            EditorGUI.EndDisabledGroup();

            for (int i = 0; i < draft.texts.Count; i++)
            {
                int index = i;
                string name = "txt_" + (index + 1);
                if (GUILayout.Toggle(draft.selectedText == index, name, EditorStyles.toolbarButton) && draft.selectedText != index)
                {
                    draft.selectedText = index;
                    Repaint();
                }
            }

            if (draft.selectedText >= 0 && draft.selectedText < draft.texts.Count)
            {
                GUILayout.Space(8);
                DrawTextInspector(draft.texts[draft.selectedText]);
            }
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(!CanExport());
            if (GUILayout.Button("导出 comic_" + draft.pageNumber + ".prefab", GUILayout.Height(35))) ExportPage();
            EditorGUI.EndDisabledGroup();
        }

        private void DrawTextInspector(ComicTextBox box)
        {
            EditorGUILayout.LabelField("文字", EditorStyles.boldLabel);
            string value = EditorGUILayout.TextArea(box.text, GUILayout.MinHeight(72));
            if (value != box.text) Change(() => box.text = value);

            EditorGUILayout.LabelField("位置和尺寸", EditorStyles.boldLabel);
            Vector2 position = EditorGUILayout.Vector2Field("左上坐标", box.rect.position);
            Vector2 size = EditorGUILayout.Vector2Field("宽 × 高", box.rect.size);
            if (position != box.rect.position || size != box.rect.size)
            {
                Change(() => box.rect = new Rect(position.x, position.y,
                    Mathf.Max(20, size.x), Mathf.Max(20, size.y)));
            }

            EditorGUILayout.LabelField("锚点和对齐", EditorStyles.boldLabel);
            int anchor = EditorGUILayout.Popup("锚点", box.anchor, AnchorLabels);
            if (anchor != box.anchor) Change(() => box.anchor = anchor);
            bool follow = EditorGUILayout.Toggle("文字对齐跟随锚点", box.followAnchor);
            if (follow != box.followAnchor) Change(() => box.followAnchor = follow);
            if (!box.followAnchor)
            {
                int horizontal = EditorGUILayout.Popup("水平对齐", box.horizontal, HorizontalLabels);
                int vertical = EditorGUILayout.Popup("垂直对齐", box.vertical, VerticalLabels);
                if (horizontal != box.horizontal || vertical != box.vertical)
                    Change(() => { box.horizontal = horizontal; box.vertical = vertical; });
            }

            EditorGUILayout.LabelField("文字样式", EditorStyles.boldLabel);
            Color color = EditorGUILayout.ColorField("字色", box.color);
            if (color != box.color) Change(() => box.color = color);
            float min = EditorGUILayout.FloatField("Auto Size 最小", box.minSize);
            float max = EditorGUILayout.FloatField("Auto Size 最大", box.maxSize);
            min = Mathf.Clamp(min, 0, 300);
            max = Mathf.Clamp(max, Mathf.Max(1, min), 300);
            if (!Mathf.Approximately(min, box.minSize) || !Mathf.Approximately(max, box.maxSize))
                Change(() => { box.minSize = min; box.maxSize = max; });
            EditorGUILayout.LabelField("描边沿用样例的 TMP 材质。", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(8);
            if (GUILayout.Button("删除 txt_" + (draft.selectedText + 1)))
            {
                Undo.RecordObject(draft, "删除漫画文本框");
                draft.texts.RemoveAt(draft.selectedText);
                draft.selectedText = Mathf.Clamp(draft.selectedText - 1, -1, draft.texts.Count - 1);
                MarkPreviewDirty();
            }
        }

        private void HandleCanvasEvent(Rect canvas)
        {
            Event evt = Event.current;
            if (evt.button != 0 || pageTexture == null) return;
            Vector2 pagePos = ToPagePosition(evt.mousePosition, canvas);
            if (evt.type == EventType.MouseDown && canvas.Contains(evt.mousePosition))
            {
                if (createTextMode)
                {
                    Undo.RecordObject(draft, "创建漫画文本框");
                    ComicTextBox box = new ComicTextBox { rect = new Rect(pagePos.x, pagePos.y, 1, 1) };
                    draft.texts.Add(box);
                    draft.selectedText = draft.texts.Count - 1;
                    dragMode = 3;
                    dragStart = pagePos;
                    createTextMode = false;
                    MarkPreviewDirty();
                    evt.Use();
                    return;
                }
                for (int i = draft.texts.Count - 1; i >= 0; i--)
                {
                    Rect boxRect = ToScreenRect(draft.texts[i].rect, canvas);
                    Rect handle = new Rect(boxRect.xMax - 10, boxRect.yMax - 10, 20, 20);
                    if (!boxRect.Contains(evt.mousePosition) && !(i == draft.selectedText && handle.Contains(evt.mousePosition))) continue;
                    draft.selectedText = i;
                    dragMode = handle.Contains(evt.mousePosition) ? 2 : 1;
                    dragStart = pagePos;
                    dragOriginal = draft.texts[i].rect;
                    Undo.RecordObject(draft, dragMode == 2 ? "缩放漫画文本框" : "移动漫画文本框");
                    evt.Use();
                    Repaint();
                    return;
                }
                draft.selectedText = -1;
                Repaint();
            }
            else if (evt.type == EventType.MouseDrag && dragMode != 0 && draft.selectedText >= 0)
            {
                ComicTextBox box = draft.texts[draft.selectedText];
                Vector2 delta = pagePos - dragStart;
                if (dragMode == 1) box.rect = new Rect(dragOriginal.position + delta, dragOriginal.size);
                else if (dragMode == 2) box.rect = new Rect(dragOriginal.position,
                    new Vector2(Mathf.Max(20, dragOriginal.width + delta.x), Mathf.Max(20, dragOriginal.height + delta.y)));
                else
                {
                    Vector2 min = Vector2.Min(dragStart, pagePos);
                    Vector2 max = Vector2.Max(dragStart, pagePos);
                    box.rect = new Rect(min.x, min.y, Mathf.Max(1, max.x - min.x), Mathf.Max(1, max.y - min.y));
                }
                MarkPreviewDirty();
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && dragMode != 0)
            {
                if (dragMode == 3 && draft.selectedText >= 0)
                {
                    ComicTextBox box = draft.texts[draft.selectedText];
                    if (box.rect.width < 20 || box.rect.height < 20)
                        box.rect = new Rect(dragStart.x, dragStart.y, 400, 150);
                }
                dragMode = 0;
                MarkPreviewDirty();
                evt.Use();
            }
        }

        private void SetTargetFolder(string path)
        {
            path = Normalize(path);
            if (!IsTargetFolder(path))
            {
                errorMessage = "请选择 Assets/Content/UI/Prefab/exterior_hero_story/resource 下的赛季 + 英雄文件夹。";
                return;
            }
            errorMessage = null;
            if (draft.folderPath == path) return;
            Undo.RecordObject(draft, "选择漫画资源文件夹");
            draft.folderPath = path;
            EditorPrefs.SetString(PrefsFolderKey, path);
            NewPage();
        }

        private void NewPage()
        {
            Undo.RecordObject(draft, "新建漫画页");
            draft.pageNumber = NextPageNumber(draft.folderPath);
            draft.imagePath = string.Empty;
            draft.imageChanged = false;
            draft.texts.Clear();
            draft.selectedText = -1;
            createTextMode = false;
            errorMessage = null;
            ReleaseImage();
            MarkPreviewDirty();
        }

        private void SetImage(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
            {
                errorMessage = "背景图片仅支持 PNG 或 JPG。";
                return;
            }
            if (!File.Exists(path))
            {
                errorMessage = "找不到选择的图片。";
                return;
            }
            Undo.RecordObject(draft, "选择漫画背景图片");
            draft.imagePath = Normalize(path);
            draft.imageChanged = true;
            errorMessage = null;
            ReleaseImage();
            MarkPreviewDirty();
        }

        private void LoadPage(int number)
        {
            string path = PageAssetPath(draft.folderPath, number);
            GameObject page = null;
            try
            {
                page = PrefabUtility.LoadPrefabContents(path);
                Transform root = page.transform.Find("root");
                if (root == null) throw new InvalidOperationException("prefab 缺少 root 节点。");
                Image image = root.Find("img_bg")?.GetComponent<Image>();
                if (image == null || image.sprite == null) throw new InvalidOperationException("prefab 缺少 img_bg 图片。");
                string imagePath = AssetDatabase.GetAssetPath(image.sprite);
                Undo.RecordObject(draft, "打开漫画页");
                draft.pageNumber = number;
                draft.imagePath = imagePath;
                draft.imageChanged = false;
                draft.texts.Clear();
                List<Transform> textNodes = new List<Transform>();
                foreach (Transform child in root)
                    if (Regex.IsMatch(child.name, @"^txt_\d+$")) textNodes.Add(child);
                textNodes.Sort((a, b) => TextNumber(a.name).CompareTo(TextNumber(b.name)));
                foreach (Transform child in textNodes)
                {
                    TMP_Text tmp = child.GetComponent<TMP_Text>();
                    RectTransform rect = child as RectTransform;
                    if (tmp == null || rect == null) continue;
                    Vector2 anchor = rect.anchorMin;
                    int anchorX = ClosestAnchor(anchor.x);
                    int anchorY = ClosestAnchor(1 - anchor.y);
                    int anchorIndex = anchorY * 3 + anchorX;
                    Rect pageRect = RectFromTransform(rect);
                    int horizontal = AlignmentHorizontal(tmp.alignment);
                    int vertical = AlignmentVertical(tmp.alignment);
                    draft.texts.Add(new ComicTextBox
                    {
                        text = tmp.text,
                        rect = pageRect,
                        anchor = anchorIndex,
                        followAnchor = horizontal == anchorX && vertical == anchorY,
                        horizontal = horizontal,
                        vertical = vertical,
                        color = tmp.color,
                        minSize = Mathf.Max(0, tmp.fontSizeMin),
                        maxSize = Mathf.Max(1, tmp.fontSizeMax)
                    });
                }
                draft.selectedText = draft.texts.Count > 0 ? 0 : -1;
                ReleaseImage();
                MarkPreviewDirty();
                errorMessage = null;
            }
            catch (Exception ex) { errorMessage = "打开页面失败：" + ex.Message; }
            finally { if (page != null) PrefabUtility.UnloadPrefabContents(page); }
        }

        private void ExportPage()
        {
            if (!CanExport()) return;
            string comicFolder = draft.folderPath + "/comic";
            string resourceFolder = comicFolder + "/resource";
            string prefabPath = PageAssetPath(draft.folderPath, draft.pageNumber);
            string imagePath = resourceFolder + "/" + draft.pageNumber + ".png";
            string backupFolder = null;
            GameObject page = null;
            try
            {
                EnsureFolder(comicFolder);
                EnsureFolder(resourceFolder);
                bool updateImage = draft.imageChanged || !File.Exists(ToFullPath(imagePath));
                if (updateImage)
                {
                    if (!LoadImageForPreview()) throw new InvalidOperationException(errorMessage ?? "无法读取背景图片。");
                    backupFolder = BackupAsset(imagePath, backupFolder);
                    File.WriteAllBytes(ToFullPath(imagePath), pageTexture.EncodeToPNG());
                    AssetDatabase.ImportAsset(imagePath, ImportAssetOptions.ForceUpdate);
                    TextureImporter importer = AssetImporter.GetAtPath(imagePath) as TextureImporter;
                    if (importer == null) throw new InvalidOperationException("背景图片导入失败。");
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.mipmapEnabled = false;
                    importer.maxTextureSize = 2048;
                    importer.alphaIsTransparency = true;
                    importer.SaveAndReimport();
                }
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(imagePath);
                if (sprite == null) throw new InvalidOperationException("无法加载导出的 Sprite。");
                backupFolder = BackupAsset(prefabPath, backupFolder);
                string sourcePath = File.Exists(ToFullPath(prefabPath)) ? prefabPath : TemplatePath;
                page = PrefabUtility.LoadPrefabContents(sourcePath);
                ApplyPage(page, sprite);
                page.name = "comic_" + draft.pageNumber;
                bool success;
                PrefabUtility.SaveAsPrefabAsset(page, prefabPath, out success);
                if (!success) throw new InvalidOperationException("保存 prefab 失败。");
                AssetDatabase.SaveAssets();
                draft.imagePath = imagePath;
                draft.imageChanged = false;
                errorMessage = null;
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                EditorGUIUtility.PingObject(Selection.activeObject);
                ShowNotification(new GUIContent("已导出 " + prefabPath));
            }
            catch (Exception ex)
            {
                errorMessage = "导出失败：" + ex.Message + (backupFolder == null ? string.Empty : "。覆盖前版本备份在 " + backupFolder);
                Debug.LogException(ex);
            }
            finally { if (page != null) PrefabUtility.UnloadPrefabContents(page); }
        }

        private bool CanExport()
        {
            return IsTargetFolder(draft.folderPath) && !string.IsNullOrEmpty(draft.imagePath) &&
                   File.Exists(ToFullPath(draft.imagePath)) && AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath) != null;
        }

        private void ApplyPage(GameObject page, Sprite sprite)
        {
            Transform root = page.transform.Find("root");
            if (root == null) throw new InvalidOperationException("样例 prefab 缺少 root 节点。");
            Transform background = root.Find("img_bg");
            Image image = background == null ? null : background.GetComponent<Image>();
            if (image == null) throw new InvalidOperationException("样例 prefab 缺少 img_bg 图片组件。");
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            RectTransform bgRect = (RectTransform)background;
            bgRect.anchorMin = bgRect.anchorMax = bgRect.pivot = new Vector2(.5f, .5f);
            bgRect.anchoredPosition = Vector2.zero;
            bgRect.sizeDelta = new Vector2(PageWidth, PageHeight);

            List<Transform> existing = new List<Transform>();
            foreach (Transform child in root)
                if (Regex.IsMatch(child.name, @"^txt_\d+$")) existing.Add(child);
            existing.Sort((a, b) => TextNumber(a.name).CompareTo(TextNumber(b.name)));
            if (existing.Count == 0 && draft.texts.Count > 0)
                throw new InvalidOperationException("样例 prefab 缺少可复制的 txt_1 文字组件。");
            GameObject textTemplate = existing.Count > 0 ? Instantiate(existing[0].gameObject) : null;
            try
            {
                for (int i = 0; i < draft.texts.Count; i++)
                {
                    GameObject textObject = i < existing.Count ? existing[i].gameObject : Instantiate(textTemplate, root, false);
                    textObject.hideFlags = HideFlags.None;
                    textObject.name = "txt_" + (i + 1);
                    textObject.transform.SetSiblingIndex(i + 1);
                    ApplyText(textObject, draft.texts[i]);
                }
                for (int i = existing.Count - 1; i >= draft.texts.Count; i--)
                    DestroyImmediate(existing[i].gameObject);
            }
            finally { if (textTemplate != null) DestroyImmediate(textTemplate); }
        }

        private static void ApplyText(GameObject textObject, ComicTextBox box)
        {
            RectTransform rect = textObject.GetComponent<RectTransform>();
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            if (rect == null || text == null) throw new InvalidOperationException("txt 节点缺少 RectTransform 或 TMP 组件。");
            int x = box.anchor % 3;
            int y = box.anchor / 3;
            Vector2 anchor = new Vector2(x * .5f, 1 - y * .5f);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.sizeDelta = new Vector2(Mathf.Max(20, box.rect.width), Mathf.Max(20, box.rect.height));
            rect.anchoredPosition = new Vector2(box.rect.x + anchor.x * box.rect.width - anchor.x * PageWidth,
                (1 - anchor.y) * PageHeight - box.rect.y - (1 - anchor.y) * box.rect.height);
            rect.localScale = Vector3.one;
            text.text = box.text;
            text.color = box.color;
            text.enableAutoSizing = true;
            text.fontSizeMin = box.minSize;
            text.fontSizeMax = box.maxSize;
            text.fontSize = box.maxSize;
            int horizontal = box.followAnchor ? x : box.horizontal;
            int vertical = box.followAnchor ? y : box.vertical;
            text.alignment = Alignment(vertical, horizontal);
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            text.ForceMeshUpdate();
        }

        private void RenderPreview()
        {
            previewDirty = false;
            if (!LoadImageForPreview()) return;
            try
            {
                EnsurePreviewObjects();
                if (previewPage != null) DestroyImmediate(previewPage);
                string existingPath = PageAssetPath(draft.folderPath, draft.pageNumber);
                string previewSource = File.Exists(ToFullPath(existingPath)) ? existingPath : TemplatePath;
                GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(previewSource);
                previewPage = Instantiate(template, canvasObject.transform, false);
                SetPreviewFlags(previewPage.transform);
                ApplyPage(previewPage, pageSprite);
                SetPreviewFlags(previewPage.transform);
                previewPage.name = "comic_" + draft.pageNumber;
                Canvas.ForceUpdateCanvases();
                previewCamera.Render();
                errorMessage = null;
            }
            catch (Exception ex) { errorMessage = "预览失败：" + ex.Message; }
        }

        private void EnsurePreviewObjects()
        {
            if (previewTexture == null)
            {
                previewTexture = new RenderTexture(PageWidth, PageHeight, 24, RenderTextureFormat.ARGB32);
                previewTexture.hideFlags = HideFlags.HideAndDontSave;
                previewTexture.Create();
            }
            if (cameraObject == null)
            {
                cameraObject = new GameObject("Comic Page Preview Camera", typeof(Camera));
                cameraObject.hideFlags = HideFlags.HideAndDontSave;
                cameraObject.transform.position = new Vector3(0, 0, -10);
                previewCamera = cameraObject.GetComponent<Camera>();
                previewCamera.enabled = false;
                previewCamera.orthographic = true;
                previewCamera.orthographicSize = PageHeight * .5f;
                previewCamera.clearFlags = CameraClearFlags.SolidColor;
                previewCamera.backgroundColor = Color.black;
                previewCamera.cullingMask = 1 << 5;
                previewCamera.targetTexture = previewTexture;
            }
            if (canvasObject == null)
            {
                canvasObject = new GameObject("Comic Page Preview Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                canvasObject.hideFlags = HideFlags.HideAndDontSave;
                canvasObject.layer = 5;
                previewCanvas = canvasObject.GetComponent<Canvas>();
                previewCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                previewCanvas.worldCamera = previewCamera;
                previewCanvas.planeDistance = 1;
                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.referenceResolution = new Vector2(PageWidth, PageHeight);
            }
        }

        private bool LoadImageForPreview()
        {
            if (pageTexture != null && loadedImagePath == draft.imagePath) return true;
            ReleaseImage();
            if (string.IsNullOrEmpty(draft.imagePath)) return false;
            try
            {
                Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                source.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    if (!source.LoadImage(File.ReadAllBytes(ToFullPath(draft.imagePath))))
                        throw new InvalidOperationException("无法解码 PNG / JPG。");
                    float sourceAspect = (float)source.width / source.height;
                    float pageAspect = (float)PageWidth / PageHeight;
                    Vector2 uvScale = sourceAspect > pageAspect
                        ? new Vector2(pageAspect / sourceAspect, 1)
                        : new Vector2(1, sourceAspect / pageAspect);
                    Vector2 uvOffset = (Vector2.one - uvScale) * .5f;
                    RenderTexture temp = RenderTexture.GetTemporary(PageWidth, PageHeight, 0, RenderTextureFormat.ARGB32);
                    RenderTexture previous = RenderTexture.active;
                    try
                    {
                        Graphics.Blit(source, temp, uvScale, uvOffset);
                        RenderTexture.active = temp;
                        pageTexture = new Texture2D(PageWidth, PageHeight, TextureFormat.RGBA32, false);
                        pageTexture.hideFlags = HideFlags.HideAndDontSave;
                        pageTexture.ReadPixels(new Rect(0, 0, PageWidth, PageHeight), 0, 0);
                        pageTexture.Apply();
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                        RenderTexture.ReleaseTemporary(temp);
                    }
                }
                finally { DestroyImmediate(source); }
                pageSprite = Sprite.Create(pageTexture, new Rect(0, 0, PageWidth, PageHeight), new Vector2(.5f, .5f), 100);
                pageSprite.hideFlags = HideFlags.HideAndDontSave;
                loadedImagePath = draft.imagePath;
                return true;
            }
            catch (Exception ex)
            {
                ReleaseImage();
                errorMessage = "读取背景图片失败：" + ex.Message;
                return false;
            }
        }

        private void ReleaseImage()
        {
            if (pageSprite != null) DestroyImmediate(pageSprite);
            if (pageTexture != null) DestroyImmediate(pageTexture);
            pageSprite = null;
            pageTexture = null;
            loadedImagePath = null;
        }

        private void ReleasePreview()
        {
            if (previewPage != null) DestroyImmediate(previewPage);
            if (canvasObject != null) DestroyImmediate(canvasObject);
            if (cameraObject != null) DestroyImmediate(cameraObject);
            if (previewTexture != null)
            {
                previewTexture.Release();
                DestroyImmediate(previewTexture);
            }
        }

        private void Change(Action mutation)
        {
            Undo.RecordObject(draft, "编辑漫画文本框");
            mutation();
            MarkPreviewDirty();
        }

        private void MarkPreviewDirty()
        {
            previewDirty = true;
            Repaint();
        }

        private static bool IsTargetFolder(string path)
        {
            return !string.IsNullOrEmpty(path) && Normalize(path).StartsWith(BaseFolder + "/", StringComparison.OrdinalIgnoreCase)
                   && Normalize(path).Substring(BaseFolder.Length + 1).IndexOf('/') < 0 && AssetDatabase.IsValidFolder(path);
        }

        private static int[] GetPageNumbers(string folder)
        {
            if (!IsTargetFolder(folder)) return new int[0];
            string comicFolder = ToFullPath(folder + "/comic");
            if (!Directory.Exists(comicFolder)) return new int[0];
            List<int> numbers = new List<int>();
            foreach (string path in Directory.GetFiles(comicFolder, "comic_*.prefab", SearchOption.TopDirectoryOnly))
            {
                Match match = Regex.Match(Path.GetFileName(path), @"^comic_(\d+)\.prefab$", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int number)) numbers.Add(number);
            }
            numbers.Sort();
            return numbers.ToArray();
        }

        private static int NextPageNumber(string folder)
        {
            int[] numbers = GetPageNumbers(folder);
            return numbers.Length == 0 ? 1 : numbers[numbers.Length - 1] + 1;
        }

        private static string PageAssetPath(string folder, int number) => folder + "/comic/comic_" + number + ".prefab";
        private static string Normalize(string path) => string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
        private static string ToFullPath(string path) => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path));

        private static string ToAssetPath(string path)
        {
            string root = Normalize(Directory.GetParent(Application.dataPath).FullName);
            string full = Normalize(Path.GetFullPath(path));
            return full.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length + 1) : string.Empty;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Normalize(Path.GetDirectoryName(path));
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static string BackupAsset(string assetPath, string backupFolder)
        {
            string fullPath = ToFullPath(assetPath);
            if (!File.Exists(fullPath)) return backupFolder;
            if (backupFolder == null)
            {
                backupFolder = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                    "Library/Dragon/ComicPageEditor/Backups", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
                Directory.CreateDirectory(backupFolder);
            }
            File.Copy(fullPath, Path.Combine(backupFolder, Path.GetFileName(assetPath)), true);
            return backupFolder;
        }

        private static Rect RectFromTransform(RectTransform transform)
        {
            Vector2 size = transform.rect.size;
            Vector2 anchor = transform.anchorMin;
            Vector2 pivot = transform.pivot;
            float pivotX = anchor.x * PageWidth + transform.anchoredPosition.x;
            float pivotY = (1 - anchor.y) * PageHeight - transform.anchoredPosition.y;
            return new Rect(pivotX - pivot.x * size.x, pivotY - (1 - pivot.y) * size.y, size.x, size.y);
        }

        private static int ClosestAnchor(float value) => Mathf.Clamp(Mathf.RoundToInt(value * 2), 0, 2);
        private static int TextNumber(string name) => int.TryParse(name.Substring(4), out int number) ? number : 0;

        private static TextAlignmentOptions Alignment(int vertical, int horizontal)
        {
            TextAlignmentOptions[,] options =
            {
                { TextAlignmentOptions.TopLeft, TextAlignmentOptions.Top, TextAlignmentOptions.TopRight },
                { TextAlignmentOptions.Left, TextAlignmentOptions.Center, TextAlignmentOptions.Right },
                { TextAlignmentOptions.BottomLeft, TextAlignmentOptions.Bottom, TextAlignmentOptions.BottomRight }
            };
            return options[Mathf.Clamp(vertical, 0, 2), Mathf.Clamp(horizontal, 0, 2)];
        }

        private static int AlignmentHorizontal(TextAlignmentOptions alignment)
        {
            if (alignment == TextAlignmentOptions.TopRight || alignment == TextAlignmentOptions.Right || alignment == TextAlignmentOptions.BottomRight) return 2;
            if (alignment == TextAlignmentOptions.Top || alignment == TextAlignmentOptions.Center || alignment == TextAlignmentOptions.Bottom) return 1;
            return 0;
        }

        private static int AlignmentVertical(TextAlignmentOptions alignment)
        {
            if (alignment == TextAlignmentOptions.BottomLeft || alignment == TextAlignmentOptions.Bottom || alignment == TextAlignmentOptions.BottomRight) return 2;
            if (alignment == TextAlignmentOptions.Left || alignment == TextAlignmentOptions.Center || alignment == TextAlignmentOptions.Right) return 1;
            return 0;
        }

        private static Rect ToScreenRect(Rect page, Rect canvas)
        {
            float scale = canvas.width / PageWidth;
            return new Rect(canvas.x + page.x * scale, canvas.y + page.y * scale, page.width * scale, page.height * scale);
        }

        private static Vector2 ToPagePosition(Vector2 screen, Rect canvas)
        {
            float scale = PageWidth / canvas.width;
            return (screen - canvas.position) * scale;
        }

        private static void DrawOutline(Rect rect, Color color, float width)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, width, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }

        private static GUIStyle CenteredLabel()
        {
            GUIStyle style = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            style.wordWrap = true;
            return style;
        }

        private static void SetPreviewFlags(Transform node)
        {
            node.gameObject.hideFlags = HideFlags.HideAndDontSave;
            node.gameObject.layer = 5;
            foreach (Transform child in node) SetPreviewFlags(child);
        }
    }
}
