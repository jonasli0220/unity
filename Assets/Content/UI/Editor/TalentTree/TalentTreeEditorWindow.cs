using System;
using System.Collections.Generic;
using Dragon.UI.TalentTree;
using UnityEditor;
using UnityEngine;

namespace Dragon.UI.TalentTree.Editor
{
    public sealed class TalentTreeEditorWindow : EditorWindow
    {
        private const float ToolbarHeight = 52f;
        private const float LeftWidth = 230f;
        private const float RightWidth = 280f;
        private const float MinZoom = 0.15f;
        private const float MaxZoom = 2.5f;
        private TalentTreeDefinition definition;
        private TalentTreeLayout layout;
        private string selectedNodeKey;
        private readonly HashSet<string> selectedNodeKeys = new HashSet<string>();
        private readonly Dictionary<string, Vector2> nodeDragStartPositions = new Dictionary<string, Vector2>();
        private readonly HashSet<string> marqueeBaseSelection = new HashSet<string>();
        private int selectedEdge = -1;
        private string pendingSourceKey;
        private Vector2 pan;
        private float zoom = 0.36f;
        private Vector2 lastMouse;
        private Vector2 rightDown;
        private Vector2 nodeDragStartBoard;
        private Vector2 marqueeStart;
        private Vector2 marqueeCurrent;
        private bool marqueeActive;
        private bool marqueeAdditive;
        private bool rightPanning;
        private bool draggingNode;
        private int draggingHandle;
        private string layoutPalettePressKey;
        private Vector2 layoutPalettePressPosition;
        private string status = "选择或新建天赋树配置";
        private readonly Dictionary<Sprite, Texture> iconCache = new Dictionary<Sprite, Texture>();

        [MenuItem("Tools/UI/Talent Tree/Open", false, 2330)]
        public static void Open()
        {
            var window = GetWindow<TalentTreeEditorWindow>("Talent Tree");
            window.minSize = new Vector2(1120f, 600f);
            window.Show();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            LoadSelectedAsset();
        }

        private void OnSelectionChange()
        {
            if (LoadSelectedAsset()) Repaint();
        }

        private bool LoadSelectedAsset()
        {
            if (Selection.activeObject is TalentTreeDefinition selected && selected != definition)
            {
                definition = selected;
                layout = TalentTreeLayoutUtility.Find(selected);
                ClearSelection();
                iconCache.Clear();
                return true;
            }
            if (Selection.activeObject is TalentTreeLayout selectedLayout && selectedLayout != layout)
            {
                layout = selectedLayout;
                definition = selectedLayout.definition;
                ClearSelection();
                iconCache.Clear();
                return true;
            }
            return false;
        }

        private void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;
        private void OnUndoRedo()
        {
            selectedNodeKeys.RemoveWhere(key => definition == null || definition.FindNode(key) == null);
            UpdatePrimarySelection();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();
            float height = position.height - ToolbarHeight;
            Rect left = new Rect(0, ToolbarHeight, LeftWidth, height);
            Rect right = new Rect(position.width - RightWidth, ToolbarHeight, RightWidth, height);
            Rect canvas = new Rect(left.xMax, ToolbarHeight, Mathf.Max(0, right.xMin - left.xMax), height);
            DrawLeft(left);
            DrawRight(right);
            if (definition != null)
            {
                HandleKeys();
                HandleCanvas(canvas);
                DrawCanvas(canvas);
            }
            else EditorGUI.DrawRect(canvas, new Color(0.1f, 0.12f, 0.15f));
        }

        private void DrawToolbar()
        {
            using (new GUILayout.VerticalScope(EditorStyles.toolbar, GUILayout.Height(ToolbarHeight)))
            {
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("新建天赋树", EditorStyles.toolbarButton, GUILayout.Width(82))) CreateDefinition();
                    GUILayout.Label("逻辑数据", EditorStyles.miniLabel, GUILayout.Width(48));
                    var next = (TalentTreeDefinition)EditorGUILayout.ObjectField(definition, typeof(TalentTreeDefinition), false, GUILayout.Width(205));
                    if (next != definition)
                    {
                        definition = next;
                        layout = TalentTreeLayoutUtility.Find(next);
                        ClearSelection(); iconCache.Clear();
                    }
                    GUILayout.Label("表现数据", EditorStyles.miniLabel, GUILayout.Width(48));
                    var nextLayout = (TalentTreeLayout)EditorGUILayout.ObjectField(layout, typeof(TalentTreeLayout), false, GUILayout.Width(205));
                    if (nextLayout != layout)
                    {
                        layout = nextLayout;
                        if (layout != null) definition = layout.definition;
                        ClearSelection(); iconCache.Clear();
                    }
                    using (new EditorGUI.DisabledScope(definition == null))
                    {
                        if (layout == null)
                        {
                            if (GUILayout.Button("创建表现配置", EditorStyles.toolbarButton, GUILayout.Width(96))) CreateLayout();
                        }
                        else if (GUILayout.Button("同步新增节点/连线", EditorStyles.toolbarButton, GUILayout.Width(120))) SyncLayout();
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(status, EditorStyles.miniLabel, GUILayout.MaxWidth(240));
                }
                using (new GUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(definition == null))
                    {
                        if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(55))) AssetDatabase.SaveAssets();
                        if (GUILayout.Button("检查", EditorStyles.toolbarButton, GUILayout.Width(55))) ShowValidation();
                        if (GUILayout.Button("生成 Prefab + JSON", EditorStyles.toolbarButton, GUILayout.Width(140))) Export();
                        if (GUILayout.Button("居中", EditorStyles.toolbarButton, GUILayout.Width(55))) { pan = Vector2.zero; zoom = 0.36f; }
                    }
                    GUILayout.FlexibleSpace();
                }
            }
        }

        private void DrawLeft(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.17f, 0.18f, 0.21f));
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 10, rect.width - 20, rect.height - 20));
            GUILayout.Label("添加节点", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(definition == null))
            {
                if (GUILayout.Button("新增小节点", GUILayout.Height(36))) AddNode(TalentNodeType.Small);
                if (GUILayout.Button("新增核心节点", GUILayout.Height(36))) AddNode(TalentNodeType.Core);
            }
            if (layout == null)
                GUILayout.Label("创建表现配置后即可摆放节点、编辑图标和曲线。", EditorStyles.wordWrappedMiniLabel);
            else if (definition != null)
            {
                int shown = 0;
                foreach (var node in definition.nodes)
                {
                    if (layout.FindNode(node.key) != null) continue;
                    if (shown++ == 0) GUILayout.Label("待摆放节点（拖入画布）", EditorStyles.miniBoldLabel);
                    if (shown <= 6) DrawUnplacedNode(node);
                }
                if (shown > 6) GUILayout.Label("还有 " + (shown - 6) + " 个，点击上方同步按钮。", EditorStyles.wordWrappedMiniLabel);
            }
            GUILayout.Space(14);
            GUILayout.Label("画布操作", EditorStyles.boldLabel);
            GUILayout.Label("空白处框选；拖动选中节点整体移动", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Label("右键节点连接；Delete 删除节点或连线", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Label("Shift 框选追加；Shift/Ctrl 点击增减节点", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Label("右键拖动 / 中键拖动：平移", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Label("滚轮：缩放", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Label("Esc：取消连线", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(14);
            if (definition != null)
            {
                GUILayout.Label("节点 " + definition.nodes.Count + "    连线 " + definition.edges.Count, EditorStyles.miniBoldLabel);
                GUILayout.Label("六区仅作位置参考，不写入节点数据。", EditorStyles.wordWrappedMiniLabel);
            }
            GUILayout.EndArea();
        }

        private void DrawUnplacedNode(TalentTreeAuthorNode node)
        {
            Rect row = GUILayoutUtility.GetRect(190f, 28f, GUILayout.ExpandWidth(true));
            GUI.Box(row, "ID " + node.id + "  ·  " + (node.type == TalentNodeType.Core ? "核心" : "小节点"));
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
            {
                layoutPalettePressKey = node.key;
                layoutPalettePressPosition = e.mousePosition;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && layoutPalettePressKey == node.key &&
                     (e.mousePosition - layoutPalettePressPosition).sqrMagnitude > 25f)
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData("DragonTalentLayoutNodeKey", node.key);
                DragAndDrop.StartDrag("摆放节点 " + node.id);
                layoutPalettePressKey = null;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && layoutPalettePressKey == node.key)
            {
                PlaceNodeLayout(node.key, TalentTreeLayoutUtility.SuggestPosition(node.id, BoardSize));
                layoutPalettePressKey = null;
                e.Use();
            }
        }

        private void DrawRight(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.17f, 0.18f, 0.21f));
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 10, rect.width - 20, rect.height - 20));
            if (definition == null) { GUILayout.Label("请先创建配置", EditorStyles.boldLabel); GUILayout.EndArea(); return; }
            if (selectedNodeKeys.Count > 1) { DrawMultiNodeInspector(); GUILayout.EndArea(); return; }
            var node = selectedNodeKeys.Count == 1 ? definition.FindNode(selectedNodeKey) : null;
            if (node != null) DrawNodeInspector(node);
            else if (selectedEdge >= 0 && selectedEdge < definition.edges.Count) DrawEdgeInspector(definition.edges[selectedEdge]);
            else DrawTreeInspector();
            GUILayout.EndArea();
        }

        private void DrawNodeInspector(TalentTreeAuthorNode node)
        {
            GUILayout.Label("节点属性", EditorStyles.boldLabel);
            GUILayout.Label("配表 ID：" + node.id, EditorStyles.miniLabel);
            int id = EditorGUILayout.IntField("配表 ID", node.id);
            var type = (TalentNodeType)EditorGUILayout.EnumPopup("类型", node.type);
            if (id != node.id || type != node.type)
            {
                Undo.RecordObject(definition, "编辑天赋节点逻辑");
                node.id = id;
                node.type = type;
                EditorUtility.SetDirty(definition);
                Repaint();
            }
            int previousCount = 0;
            foreach (var edge in definition.edges) if (edge.toKey == node.key) previousCount++;
            GUILayout.Label("直接前置：" + previousCount + " 个（全部满足）", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(8);
            var visual = layout != null ? layout.FindNode(node.key) : null;
            if (visual == null)
            {
                EditorGUILayout.HelpBox("这个节点还没有 UI 布局。请创建表现配置或同步新增节点。", MessageType.Info);
            }
            else
            {
                var icon = (Sprite)EditorGUILayout.ObjectField("图标", visual.icon, typeof(Sprite), false);
                Vector2 location = EditorGUILayout.Vector2Field("画布位置", visual.position);
                if (icon != visual.icon || location != visual.position)
                {
                    Undo.RecordObject(layout, "编辑天赋节点表现");
                    visual.icon = icon;
                    visual.position = ClampToBoard(location, node.type);
                    EditorUtility.SetDirty(layout);
                    Repaint();
                }
            }
            GUILayout.Space(8);
            if (GUILayout.Button("删除节点")) DeleteSelection();
        }

        private void DrawMultiNodeInspector()
        {
            GUILayout.Label("已选中 " + selectedNodeKeys.Count + " 个节点", EditorStyles.boldLabel);
            GUILayout.Label("拖动任一选中节点可整体移动，连线会跟随节点更新。", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(8);
            if (GUILayout.Button("删除选中节点")) DeleteSelection();
        }

        private void DrawEdgeInspector(TalentTreeAuthorEdge edge)
        {
            GUILayout.Label("连线属性", EditorStyles.boldLabel);
            var from = definition.FindNode(edge.fromKey);
            var to = definition.FindNode(edge.toKey);
            GUILayout.Label((from != null ? from.id.ToString() : "?") + " → " + (to != null ? to.id.ToString() : "?"));
            var visual = layout != null ? layout.FindEdge(edge.fromKey, edge.toKey) : null;
            if (visual == null)
            {
                EditorGUILayout.HelpBox("这条连线还没有 UI 曲线配置。请同步新增连线。", MessageType.Info);
            }
            else
            {
                Vector2 fromHandle = EditorGUILayout.Vector2Field("起点控制柄", visual.fromHandle);
                Vector2 toHandle = EditorGUILayout.Vector2Field("终点控制柄", visual.toHandle);
                if (fromHandle != visual.fromHandle || toHandle != visual.toHandle)
                {
                    Undo.RecordObject(layout, "调整天赋曲线");
                    visual.fromHandle = fromHandle;
                    visual.toHandle = toHandle;
                    EditorUtility.SetDirty(layout);
                }
                if (GUILayout.Button("重置为直线") && from != null && to != null)
                {
                    Undo.RecordObject(layout, "重置天赋曲线");
                    Vector2 delta = NodePosition(to) - NodePosition(from);
                    visual.fromHandle = delta / 3f;
                    visual.toHandle = -delta / 3f;
                    EditorUtility.SetDirty(layout);
                }
            }
            GUILayout.Space(8);
            if (GUILayout.Button("删除连线")) DeleteSelection();
        }

        private void DrawTreeInspector()
        {
            GUILayout.Label("天赋树概览", EditorStyles.boldLabel);
            GUILayout.Label("节点 " + definition.nodes.Count + "，前置连线 " + definition.edges.Count, EditorStyles.wordWrappedMiniLabel);
            if (layout == null)
            {
                EditorGUILayout.HelpBox("请先创建 UI 表现配置。旧树的坐标和曲线会在创建时迁移。", MessageType.Info);
                return;
            }
            GUILayout.Label("画布样式", EditorStyles.boldLabel);
            float boardSize = EditorGUILayout.FloatField("画布直径", layout.boardSize);
            float smallSize = EditorGUILayout.FloatField("小节点尺寸", layout.smallNodeSize);
            float coreSize = EditorGUILayout.FloatField("核心节点尺寸", layout.coreNodeSize);
            float lineWidth = EditorGUILayout.FloatField("线宽", layout.lineWidth);
            Color lineColor = EditorGUILayout.ColorField("线条颜色", layout.lineColor);
            Color smallColor = EditorGUILayout.ColorField("小节点底色", layout.smallFillColor);
            Color coreColor = EditorGUILayout.ColorField("核心节点底色", layout.coreFillColor);
            Color rimColor = EditorGUILayout.ColorField("节点边框", layout.nodeRimColor);
            Sprite smallFrame = (Sprite)EditorGUILayout.ObjectField("小节点外框", layout.smallFrame, typeof(Sprite), false);
            Sprite coreFrame = (Sprite)EditorGUILayout.ObjectField("核心节点外框", layout.coreFrame, typeof(Sprite), false);
            if (boardSize != layout.boardSize || smallSize != layout.smallNodeSize || coreSize != layout.coreNodeSize ||
                lineWidth != layout.lineWidth || lineColor != layout.lineColor || smallColor != layout.smallFillColor ||
                coreColor != layout.coreFillColor || rimColor != layout.nodeRimColor || smallFrame != layout.smallFrame || coreFrame != layout.coreFrame)
            {
                Undo.RecordObject(layout, "编辑天赋树表现样式");
                layout.boardSize = Mathf.Max(1024f, boardSize);
                layout.smallNodeSize = Mathf.Max(24f, smallSize);
                layout.coreNodeSize = Mathf.Max(32f, coreSize);
                layout.lineWidth = Mathf.Max(1f, lineWidth);
                layout.lineColor = lineColor;
                layout.smallFillColor = smallColor;
                layout.coreFillColor = coreColor;
                layout.nodeRimColor = rimColor;
                layout.smallFrame = smallFrame;
                layout.coreFrame = coreFrame;
                EditorUtility.SetDirty(layout);
            }
        }

        private void DrawCanvas(Rect rect)
        {
            GUI.BeginGroup(rect);
            var local = new Rect(0, 0, rect.width, rect.height);
            EditorGUI.DrawRect(local, new Color(0.075f, 0.09f, 0.12f));
            Handles.BeginGUI();
            DrawSectors(local);
            DrawEdges(local);
            DrawNodes(local);
            if (marqueeActive)
            {
                Rect marquee = GetMarqueeRect();
                Handles.DrawSolidRectangleWithOutline(marquee, new Color(0.3f, 0.6f, 1f, 0.14f), new Color(0.45f, 0.72f, 1f, 0.9f));
            }
            if (!string.IsNullOrEmpty(pendingSourceKey))
            {
                var source = definition.FindNode(pendingSourceKey);
                if (source != null)
                {
                    Handles.color = Color.yellow;
                    Handles.DrawDottedLine(BoardToCanvas(source.position, local), Event.current.mousePosition, 4f);
                }
            }
            Handles.EndGUI();
            GUI.EndGroup();
        }

        private void DrawSectors(Rect canvas)
        {
            Vector2 center = BoardToCanvas(Vector2.zero, canvas);
            float radius = BoardSize * 0.5f * zoom;
            for (int sector = 0; sector < 6; sector++)
            {
                Handles.color = sector % 2 == 0 ? new Color(0.16f, 0.24f, 0.34f, 0.28f) : new Color(0.22f, 0.26f, 0.32f, 0.28f);
                var points = new Vector3[14];
                points[0] = center;
                for (int i = 0; i <= 12; i++)
                {
                    float angle = (sector * 60f + i * 5f) * Mathf.Deg2Rad;
                    points[i + 1] = center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * radius;
                }
                Handles.DrawAAConvexPolygon(points);
                Handles.color = new Color(0.58f, 0.55f, 0.44f, 0.35f);
                Handles.DrawLine(center, points[1]);
            }
            Handles.color = new Color(0.58f, 0.55f, 0.44f, 0.45f);
            Handles.DrawWireDisc(center, Vector3.forward, radius);
        }

        private void DrawEdges(Rect canvas)
        {
            for (int i = 0; i < definition.edges.Count; i++)
            {
                var edge = definition.edges[i];
                var from = definition.FindNode(edge.fromKey);
                var to = definition.FindNode(edge.toKey);
                if (from == null || to == null) continue;
                EdgeHandles(edge, out var fromHandle, out var toHandle);
                Vector2 p0 = BoardToCanvas(NodePosition(from), canvas);
                Vector2 p1 = BoardToCanvas(NodePosition(from) + fromHandle, canvas);
                Vector2 p2 = BoardToCanvas(NodePosition(to) + toHandle, canvas);
                Vector2 p3 = BoardToCanvas(NodePosition(to), canvas);
                if (!CurveVisible(canvas, p0, p1, p2, p3)) continue;
                Color tint = i == selectedEdge ? Color.yellow : LineColor;
                Handles.DrawBezier(p0, p3, p1, p2, tint, null, Mathf.Max(1f, LineWidth * zoom));
                Vector2 tip = Bezier(p0, p1, p2, p3, 0.84f);
                Vector2 tangent = (Bezier(p0, p1, p2, p3, 0.86f) - Bezier(p0, p1, p2, p3, 0.82f)).normalized;
                Vector2 normal = new Vector2(-tangent.y, tangent.x);
                Handles.color = tint;
                Handles.DrawAAConvexPolygon(tip + tangent * 7f, tip - tangent * 5f + normal * 4f, tip - tangent * 5f - normal * 4f);
                if (i != selectedEdge) continue;
                Handles.color = new Color(1f, 0.85f, 0.3f);
                Handles.DrawDottedLine(p0, p1, 4f);
                Handles.DrawDottedLine(p3, p2, 4f);
                Handles.DrawSolidDisc(p1, Vector3.forward, 6f);
                Handles.DrawSolidDisc(p2, Vector3.forward, 6f);
            }
        }

        private void DrawNodes(Rect canvas)
        {
            foreach (var node in definition.nodes)
            {
                Vector2 center = BoardToCanvas(NodePosition(node), canvas);
                float radius = NodeSize(node.type) * zoom * 0.5f;
                if (center.x + radius < 0 || center.y + radius < 0 || center.x - radius > canvas.width || center.y - radius > canvas.height) continue;
                Handles.color = node.type == TalentNodeType.Core ? CoreFillColor : SmallFillColor;
                Handles.DrawSolidDisc(center, Vector3.forward, radius);
                Handles.color = NodeRimColor;
                Handles.DrawWireDisc(center, Vector3.forward, radius);
                Sprite frame = node.type == TalentNodeType.Core ? CoreFrame : SmallFrame;
                if (frame != null && GetPreview(frame) is Texture frameTexture)
                {
                    float diameter = radius * 2f;
                    GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, diameter, diameter), frameTexture, ScaleMode.ScaleToFit, true);
                }
                Sprite icon = NodeIcon(node);
                if (icon != null && radius > 12f)
                {
                    Texture texture = GetPreview(icon);
                    if (texture != null)
                    {
                        float iconSize = radius * 1.18f;
                        GUI.DrawTexture(new Rect(center.x - iconSize * 0.5f, center.y - iconSize * 0.5f, iconSize, iconSize), texture, ScaleMode.ScaleToFit, true);
                    }
                }
                if (selectedNodeKeys.Contains(node.key))
                {
                    Handles.color = Color.yellow;
                    Handles.DrawWireDisc(center, Vector3.forward, radius + 3f);
                }
                if (zoom > 0.23f)
                    GUI.Label(new Rect(center.x - 35f, center.y + radius + 2f, 70f, 18f), node.id.ToString(), EditorStyles.centeredGreyMiniLabel);
            }
        }

        private void HandleCanvas(Rect rect)
        {
            Event e = Event.current;
            if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
            {
                string key = DragAndDrop.GetGenericData("DragonTalentLayoutNodeKey") as string;
                if (layout != null && !string.IsNullOrEmpty(key) &&
                    definition.FindNode(key) != null && rect.Contains(e.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                    if (e.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        var node = definition.FindNode(key);
                        Vector2 point = CanvasToBoard(e.mousePosition - rect.position, new Rect(0, 0, rect.width, rect.height));
                        PlaceNodeLayout(key, ClampToBoard(point, node.type));
                    }
                    e.Use();
                }
                return;
            }
            if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            {
                Vector2 local = e.mousePosition - rect.position;
                Rect localRect = new Rect(0, 0, rect.width, rect.height);
                Vector2 before = CanvasToBoard(local, localRect);
                zoom = Mathf.Clamp(zoom * Mathf.Pow(1.12f, -e.delta.y), MinZoom, MaxZoom);
                pan = local - localRect.center - new Vector2(before.x, -before.y) * zoom;
                e.Use(); Repaint(); return;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && !string.IsNullOrEmpty(pendingSourceKey))
            {
                pendingSourceKey = null; e.Use(); Repaint(); return;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && marqueeActive)
            {
                selectedNodeKeys.Clear();
                selectedNodeKeys.UnionWith(marqueeBaseSelection);
                UpdatePrimarySelection();
                marqueeActive = false;
                e.Use(); Repaint(); return;
            }
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                Vector2 local = e.mousePosition - rect.position;
                var localRect = new Rect(0, 0, rect.width, rect.height);
                if (e.button == 2 || e.button == 1)
                {
                    rightDown = e.mousePosition; lastMouse = e.mousePosition; rightPanning = e.button == 2;
                    e.Use(); return;
                }
                if (e.button != 0) return;
                if (!string.IsNullOrEmpty(pendingSourceKey))
                {
                    var target = HitNode(local, localRect);
                    if (target != null) AddEdge(pendingSourceKey, target.key);
                    pendingSourceKey = null; e.Use(); Repaint(); return;
                }
                if (layout != null && selectedEdge >= 0 && selectedEdge < definition.edges.Count)
                {
                    var edge = definition.edges[selectedEdge];
                    var from = definition.FindNode(edge.fromKey);
                    var to = definition.FindNode(edge.toKey);
                    var visual = layout.FindEdge(edge.fromKey, edge.toKey);
                    if (from != null && to != null && visual != null)
                    {
                        if (Vector2.Distance(local, BoardToCanvas(NodePosition(from) + visual.fromHandle, localRect)) < 10f) draggingHandle = 1;
                        else if (Vector2.Distance(local, BoardToCanvas(NodePosition(to) + visual.toHandle, localRect)) < 10f) draggingHandle = 2;
                        if (draggingHandle != 0) { Undo.RecordObject(layout, "调整天赋曲线"); e.Use(); return; }
                    }
                }
                var node = HitNode(local, localRect);
                if (node != null)
                {
                    selectedEdge = -1;
                    if (e.shift || e.control || e.command)
                    {
                        if (!selectedNodeKeys.Add(node.key)) selectedNodeKeys.Remove(node.key);
                        UpdatePrimarySelection();
                        e.Use(); Repaint(); return;
                    }
                    if (!selectedNodeKeys.Contains(node.key)) SelectSingleNode(node.key);
                    if (layout == null)
                    {
                        e.Use(); Repaint(); return;
                    }
                    draggingNode = true;
                    nodeDragStartBoard = CanvasToBoard(local, localRect);
                    nodeDragStartPositions.Clear();
                    foreach (var selectedNode in definition.nodes)
                        if (selectedNodeKeys.Contains(selectedNode.key) && layout.FindNode(selectedNode.key) is TalentTreeNodeLayout visual)
                            nodeDragStartPositions.Add(selectedNode.key, visual.position);
                    if (nodeDragStartPositions.Count != selectedNodeKeys.Count)
                    {
                        draggingNode = false;
                        status = "先同步新增节点布局，再移动选中节点";
                        e.Use(); Repaint(); return;
                    }
                    Undo.RecordObject(layout, selectedNodeKeys.Count > 1 ? "移动天赋节点组" : "移动天赋节点");
                    e.Use(); Repaint(); return;
                }
                selectedEdge = HitEdge(local, localRect);
                if (selectedEdge >= 0)
                {
                    selectedNodeKeys.Clear();
                    selectedNodeKey = null;
                }
                else
                {
                    marqueeActive = true;
                    marqueeStart = local;
                    marqueeCurrent = local;
                    marqueeAdditive = e.shift;
                    marqueeBaseSelection.Clear();
                    if (marqueeAdditive) marqueeBaseSelection.UnionWith(selectedNodeKeys);
                    else selectedNodeKeys.Clear();
                    UpdatePrimarySelection();
                }
                e.Use(); Repaint(); return;
            }
            if (e.type == EventType.MouseDrag)
            {
                if (rightPanning || (e.button == 1 && rightDown != Vector2.zero))
                {
                    if (e.button == 1 && (e.mousePosition - rightDown).sqrMagnitude > 16f) rightPanning = true;
                    if (rightPanning) pan += e.mousePosition - lastMouse;
                    lastMouse = e.mousePosition; e.Use(); Repaint(); return;
                }
                if (e.button == 0 && marqueeActive)
                {
                    marqueeCurrent = e.mousePosition - rect.position;
                    UpdateMarqueeSelection(new Rect(0, 0, rect.width, rect.height));
                    e.Use(); Repaint(); return;
                }
                if (e.button == 0 && (draggingNode || draggingHandle != 0))
                {
                    var localRect = new Rect(0, 0, rect.width, rect.height);
                    Vector2 world = CanvasToBoard(e.mousePosition - rect.position, localRect);
                    if (draggingNode)
                    {
                        Vector2 delta = ClampGroupDelta(world - nodeDragStartBoard);
                        foreach (var selectedNode in definition.nodes)
                            if (nodeDragStartPositions.TryGetValue(selectedNode.key, out var start)) layout.FindNode(selectedNode.key).position = start + delta;
                    }
                    else if (selectedEdge >= 0 && selectedEdge < definition.edges.Count)
                    {
                        var edge = definition.edges[selectedEdge];
                        var endpoint = definition.FindNode(draggingHandle == 1 ? edge.fromKey : edge.toKey);
                        if (endpoint != null)
                        {
                            var visual = layout.FindEdge(edge.fromKey, edge.toKey);
                            if (draggingHandle == 1) visual.fromHandle = world - NodePosition(endpoint);
                            else visual.toHandle = world - NodePosition(endpoint);
                        }
                    }
                    EditorUtility.SetDirty(layout); e.Use(); Repaint(); return;
                }
            }
            if (e.type == EventType.MouseUp)
            {
                if (e.button == 1 && rightDown != Vector2.zero)
                {
                    if (!rightPanning && rect.Contains(e.mousePosition))
                    {
                        var localRect = new Rect(0, 0, rect.width, rect.height);
                        ShowContextMenu(HitNode(e.mousePosition - rect.position, localRect), CanvasToBoard(e.mousePosition - rect.position, localRect));
                    }
                    rightDown = Vector2.zero; rightPanning = false; e.Use(); return;
                }
                if (e.button == 2 && rightPanning) { rightPanning = false; rightDown = Vector2.zero; e.Use(); return; }
                if (e.button == 0 && marqueeActive)
                {
                    marqueeCurrent = e.mousePosition - rect.position;
                    UpdateMarqueeSelection(new Rect(0, 0, rect.width, rect.height));
                    marqueeActive = false;
                    marqueeBaseSelection.Clear();
                    e.Use(); Repaint(); return;
                }
                if (e.button == 0 && (draggingNode || draggingHandle != 0))
                {
                    draggingNode = false; draggingHandle = 0; nodeDragStartPositions.Clear(); e.Use(); return;
                }
            }
        }

        private Rect GetMarqueeRect()
        {
            return Rect.MinMaxRect(
                Mathf.Min(marqueeStart.x, marqueeCurrent.x), Mathf.Min(marqueeStart.y, marqueeCurrent.y),
                Mathf.Max(marqueeStart.x, marqueeCurrent.x), Mathf.Max(marqueeStart.y, marqueeCurrent.y));
        }

        private void UpdateMarqueeSelection(Rect canvas)
        {
            Rect marquee = GetMarqueeRect();
            selectedNodeKeys.Clear();
            if (marqueeAdditive) selectedNodeKeys.UnionWith(marqueeBaseSelection);
            foreach (var node in definition.nodes)
                if (marquee.Contains(BoardToCanvas(NodePosition(node), canvas))) selectedNodeKeys.Add(node.key);
            UpdatePrimarySelection();
        }

        private Vector2 ClampGroupDelta(Vector2 delta)
        {
            if (CanTranslateSelection(delta)) return delta;
            float low = 0f;
            float high = 1f;
            for (int i = 0; i < 16; i++)
            {
                float middle = (low + high) * 0.5f;
                if (CanTranslateSelection(delta * middle)) low = middle;
                else high = middle;
            }
            return delta * low;
        }

        private bool CanTranslateSelection(Vector2 delta)
        {
            foreach (var node in definition.nodes)
            {
                if (!nodeDragStartPositions.TryGetValue(node.key, out var start)) continue;
                float nodeRadius = NodeSize(node.type) * 0.5f;
                float limit = Mathf.Max(1f, BoardSize * 0.5f - nodeRadius);
                if ((start + delta).sqrMagnitude > limit * limit) return false;
            }
            return true;
        }

        private void SelectSingleNode(string key)
        {
            selectedNodeKeys.Clear();
            selectedNodeKeys.Add(key);
            selectedNodeKey = key;
            selectedEdge = -1;
        }

        private void UpdatePrimarySelection()
        {
            selectedNodeKey = null;
            if (selectedNodeKeys.Count != 1) return;
            foreach (var key in selectedNodeKeys) selectedNodeKey = key;
        }

        private void HandleKeys()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete && GUIUtility.keyboardControl == 0)
            {
                DeleteSelection(); e.Use();
            }
        }

        private void ShowContextMenu(TalentTreeAuthorNode node, Vector2 world)
        {
            var menu = new GenericMenu();
            if (node != null)
            {
                menu.AddItem(new GUIContent("从此节点连接至..."), false, () => { pendingSourceKey = node.key; status = "点击目标节点完成连接，Esc 取消"; Repaint(); });
                if (layout != null && layout.FindNode(node.key) == null)
                    menu.AddItem(new GUIContent("摆放此节点"), false, () => PlaceNodeLayout(node.key, ClampToBoard(world, node.type)));
                bool deleteGroup = selectedNodeKeys.Count > 1 && selectedNodeKeys.Contains(node.key);
                menu.AddItem(new GUIContent(deleteGroup ? "删除选中节点" : "删除节点"), false, () =>
                {
                    if (!deleteGroup) SelectSingleNode(node.key);
                    DeleteSelection();
                });
            }
            else
            {
                menu.AddItem(new GUIContent("添加小节点"), false, () => AddNode(TalentNodeType.Small));
                menu.AddItem(new GUIContent("添加核心节点"), false, () => AddNode(TalentNodeType.Core));
            }
            if (layout != null) menu.AddItem(new GUIContent("同步新增节点/连线"), false, SyncLayout);
            menu.ShowAsContext();
        }

        private void AddNode(TalentNodeType type)
        {
            if (definition == null) return;
            Undo.RecordObject(definition, "添加天赋逻辑节点");
            var node = new TalentTreeAuthorNode { key = Guid.NewGuid().ToString("N"), id = definition.NextId(), type = type };
            definition.nodes.Add(node);
            SelectSingleNode(node.key);
            status = "已添加节点，可从左侧拖入画布或同步摆位";
            EditorUtility.SetDirty(definition); Repaint();
        }

        private void AddEdge(string fromKey, string toKey)
        {
            if (definition == null) return;
            var from = definition.FindNode(fromKey);
            var to = definition.FindNode(toKey);
            if (from == null || to == null || from == to) { status = "请选择另一个节点作为目标"; return; }
            if (definition.edges.Exists(edge => edge.fromKey == fromKey && edge.toKey == toKey)) { status = "这条连线已存在"; return; }
            Undo.RecordObject(definition, "连接天赋节点");
            definition.edges.Add(new TalentTreeAuthorEdge { fromKey = fromKey, toKey = toKey });
            var errors = TalentTreeValidator.Validate(definition);
            if (errors.Exists(error => error.Contains("存在环")))
            {
                definition.edges.RemoveAt(definition.edges.Count - 1);
                status = "已阻止成环连接";
            }
            else { selectedEdge = definition.edges.Count - 1; selectedNodeKeys.Clear(); selectedNodeKey = null; status = "已连接 " + from.id + " → " + to.id + "，可调整曲线"; }
            EditorUtility.SetDirty(definition); Repaint();
        }

        private void DeleteSelection()
        {
            if (definition == null) return;
            if (selectedNodeKeys.Count > 0)
            {
                Undo.RecordObject(definition, selectedNodeKeys.Count > 1 ? "删除天赋节点组" : "删除天赋节点");
                definition.nodes.RemoveAll(node => selectedNodeKeys.Contains(node.key));
                definition.edges.RemoveAll(edge => selectedNodeKeys.Contains(edge.fromKey) || selectedNodeKeys.Contains(edge.toKey));
                if (selectedNodeKeys.Contains(pendingSourceKey)) pendingSourceKey = null;
                selectedNodeKeys.Clear();
                selectedNodeKey = null;
            }
            else if (selectedEdge >= 0 && selectedEdge < definition.edges.Count)
            {
                Undo.RecordObject(definition, "删除天赋连线");
                definition.edges.RemoveAt(selectedEdge);
                selectedEdge = -1;
            }
            else return;
            EditorUtility.SetDirty(definition); Repaint();
        }

        private void ShowValidation()
        {
            var errors = TalentTreeValidator.Validate(definition, layout);
            status = errors.Count == 0 ? "检查通过" : "检查发现 " + errors.Count + " 个问题";
            EditorUtility.DisplayDialog("天赋树检查", errors.Count == 0 ? "节点与连线数据有效。" : string.Join("\n", errors), "确定");
        }

        private void Export()
        {
            AssetDatabase.SaveAssets();
            bool success = TalentTreeExporter.Export(definition, layout, out string message);
            status = success ? "生成成功" : "生成失败";
            EditorUtility.DisplayDialog(success ? "天赋树已生成" : "天赋树生成失败", message, "确定");
        }

        private void CreateDefinition()
        {
            const string definitionsFolder = "Assets/Content/UI/Editor/TalentTree/Definitions";
            if (!AssetDatabase.IsValidFolder(definitionsFolder))
                AssetDatabase.CreateFolder("Assets/Content/UI/Editor/TalentTree", "Definitions");
            string path = EditorUtility.SaveFilePanelInProject("新建天赋树", "NewTalentTree", "asset", "请选择配置资产名称",
                definitionsFolder);
            if (string.IsNullOrEmpty(path)) return;
            var asset = CreateInstance<TalentTreeDefinition>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            definition = asset;
            layout = TalentTreeLayoutUtility.Create(asset);
            Selection.activeObject = asset; ClearSelection();
            status = "已创建天赋树，可添加节点并摆放"; Repaint();
        }

        private void CreateLayout()
        {
            try
            {
                layout = TalentTreeLayoutUtility.Create(definition);
                ClearSelection();
                status = "已创建表现配置，旧坐标与曲线已尽可能迁移";
                Repaint();
            }
            catch (Exception exception)
            {
                status = "创建表现配置失败";
                EditorUtility.DisplayDialog("无法创建表现配置", exception.Message, "确定");
            }
        }

        private void SyncLayout()
        {
            if (definition == null || layout == null) return;
            var errors = TalentTreeValidator.Validate(definition);
            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("请先修正逻辑配置", string.Join("\n", errors), "确定");
                return;
            }
            if (layout.definition != definition) { status = "表现配置未关联当前逻辑配置"; return; }
            Undo.RecordObject(layout, "同步天赋表现配置");
            int added = TalentTreeLayoutUtility.SyncMissingEntries(definition, layout);
            if (added > 0) EditorUtility.SetDirty(layout);
            status = added > 0 ? "已同步 " + added + " 条新增布局记录" : "表现配置已与逻辑同步";
            Repaint();
        }

        private void PlaceNodeLayout(string key, Vector2 position)
        {
            if (definition == null || layout == null || layout.definition != definition) return;
            var node = definition.FindNode(key);
            if (node == null) return;
            Undo.RecordObject(layout, "摆放天赋节点");
            var visual = layout.FindNode(key);
            if (visual == null)
            {
                visual = new TalentTreeNodeLayout { key = key };
                layout.nodes.Add(visual);
            }
            visual.position = ClampToBoard(position, node.type);
            TalentTreeLayoutUtility.SyncMissingEdges(definition, layout);
            EditorUtility.SetDirty(layout);
            SelectSingleNode(key);
            status = "已摆放节点 " + node.id;
            Repaint();
        }

        private void ClearSelection()
        {
            selectedNodeKey = null;
            selectedNodeKeys.Clear();
            selectedEdge = -1;
            pendingSourceKey = null;
            draggingNode = false;
            draggingHandle = 0;
            nodeDragStartPositions.Clear();
            marqueeActive = false;
            marqueeBaseSelection.Clear();
            layoutPalettePressKey = null;
        }

        private Texture GetPreview(Sprite sprite)
        {
            if (sprite == null) return null;
            if (iconCache.TryGetValue(sprite, out Texture texture) && texture != null) return texture;
            texture = AssetPreview.GetAssetPreview(sprite) ?? AssetPreview.GetMiniThumbnail(sprite);
            if (texture != null) iconCache[sprite] = texture;
            return texture;
        }

        private float BoardSize => layout != null ? layout.boardSize : definition.boardSize;
        private float LineWidth => layout != null ? layout.lineWidth : definition.lineWidth;
        private Color LineColor => layout != null ? layout.lineColor : definition.lineColor;
        private Color SmallFillColor => layout != null ? layout.smallFillColor : definition.smallFillColor;
        private Color CoreFillColor => layout != null ? layout.coreFillColor : definition.coreFillColor;
        private Color NodeRimColor => layout != null ? layout.nodeRimColor : definition.nodeRimColor;
        private Sprite SmallFrame => layout != null ? layout.smallFrame : definition.smallFrame;
        private Sprite CoreFrame => layout != null ? layout.coreFrame : definition.coreFrame;

        private float NodeSize(TalentNodeType type) => type == TalentNodeType.Core
            ? (layout != null ? layout.coreNodeSize : definition.coreNodeSize)
            : (layout != null ? layout.smallNodeSize : definition.smallNodeSize);

        private Vector2 NodePosition(TalentTreeAuthorNode node)
        {
            var visual = layout != null ? layout.FindNode(node.key) : null;
            if (visual != null) return visual.position;
            if (layout == null && (node.position != Vector2.zero || node.id == 1)) return node.position;
            return TalentTreeLayoutUtility.SuggestPosition(node.id, BoardSize);
        }

        private Sprite NodeIcon(TalentTreeAuthorNode node)
        {
            var visual = layout != null ? layout.FindNode(node.key) : null;
            return visual != null ? visual.icon : layout == null ? node.icon : null;
        }

        private void EdgeHandles(TalentTreeAuthorEdge edge, out Vector2 fromHandle, out Vector2 toHandle)
        {
            var visual = layout != null ? layout.FindEdge(edge.fromKey, edge.toKey) : null;
            if (visual != null)
            {
                fromHandle = visual.fromHandle;
                toHandle = visual.toHandle;
                return;
            }
            if (layout == null && (edge.fromHandle != Vector2.zero || edge.toHandle != Vector2.zero))
            {
                fromHandle = edge.fromHandle;
                toHandle = edge.toHandle;
                return;
            }
            var from = definition.FindNode(edge.fromKey);
            var to = definition.FindNode(edge.toKey);
            Vector2 delta = from != null && to != null ? NodePosition(to) - NodePosition(from) : Vector2.zero;
            fromHandle = delta / 3f;
            toHandle = -delta / 3f;
        }

        private Vector2 ClampToBoard(Vector2 point, TalentNodeType type)
        {
            float nodeRadius = NodeSize(type) * 0.5f;
            float radius = Mathf.Max(1f, BoardSize * 0.5f - nodeRadius);
            return point.magnitude <= radius ? point : point.normalized * radius;
        }

        private TalentTreeAuthorNode HitNode(Vector2 point, Rect canvas)
        {
            for (int i = definition.nodes.Count - 1; i >= 0; i--)
            {
                var node = definition.nodes[i];
                float radius = NodeSize(node.type) * zoom * 0.5f;
                if (Vector2.Distance(point, BoardToCanvas(NodePosition(node), canvas)) <= Mathf.Max(8f, radius)) return node;
            }
            return null;
        }

        private int HitEdge(Vector2 point, Rect canvas)
        {
            float best = 9f;
            int result = -1;
            for (int i = 0; i < definition.edges.Count; i++)
            {
                var edge = definition.edges[i];
                var from = definition.FindNode(edge.fromKey);
                var to = definition.FindNode(edge.toKey);
                if (from == null || to == null) continue;
                EdgeHandles(edge, out var fromHandle, out var toHandle);
                Vector2 p0 = BoardToCanvas(NodePosition(from), canvas);
                Vector2 p1 = BoardToCanvas(NodePosition(from) + fromHandle, canvas);
                Vector2 p2 = BoardToCanvas(NodePosition(to) + toHandle, canvas);
                Vector2 p3 = BoardToCanvas(NodePosition(to), canvas);
                Vector2 previous = p0;
                for (int step = 1; step <= 20; step++)
                {
                    Vector2 current = Bezier(p0, p1, p2, p3, step / 20f);
                    float distance = HandleUtility.DistancePointToLineSegment(point, previous, current);
                    if (distance < best) { best = distance; result = i; }
                    previous = current;
                }
            }
            return result;
        }

        private Vector2 BoardToCanvas(Vector2 world, Rect canvas) => canvas.center + pan + new Vector2(world.x, -world.y) * zoom;
        private Vector2 CanvasToBoard(Vector2 point, Rect canvas)
        {
            Vector2 value = (point - canvas.center - pan) / zoom;
            return new Vector2(value.x, -value.y);
        }
        private static Vector2 Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        private static bool CurveVisible(Rect canvas, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            float minX = Mathf.Min(p0.x, Mathf.Min(p1.x, Mathf.Min(p2.x, p3.x)));
            float maxX = Mathf.Max(p0.x, Mathf.Max(p1.x, Mathf.Max(p2.x, p3.x)));
            float minY = Mathf.Min(p0.y, Mathf.Min(p1.y, Mathf.Min(p2.y, p3.y)));
            float maxY = Mathf.Max(p0.y, Mathf.Max(p1.y, Mathf.Max(p2.y, p3.y)));
            return maxX >= 0 && maxY >= 0 && minX <= canvas.width && minY <= canvas.height;
        }
    }
}
