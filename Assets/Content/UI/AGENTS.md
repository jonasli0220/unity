# UI Workspace Agent Context

This file is the handoff context for Codex agents working from `Assets/Content/UI`. Keep detailed Figma Bridge usage notes in `Editor/FigmaBridge/README.md`; keep this file focused on durable project background and development conventions.

## User Background

- The user is a game interaction designer and Unity UI developer.
- Daily workflow: receive design requirements, design interaction/UI in Figma, write interaction specs in Feishu Docs, and assemble Unity UI prefabs.
- The live game has mature UI structures and component libraries. Unity work usually means finding reusable components or prefab structures, copying them, and composing screens rather than building everything from scratch.
- The user wants AI help to improve the loop between Unity prefab assets and Figma design reuse.

## Project And Launch Context

- Unity project root: `G:/Dragon/trunk/dragon`
- Current UI workspace: `G:/Dragon/trunk/dragon/Assets/Content/UI`
- The team normally starts the project through Davinci, then launches the Unity Editor.
- Davinci executable: `G:/Dragon/trunk/bin/WorkHub/Davinci.exe`
- Unity editor automation is available through the Codex Unity Bridge described below. If the bridge is unavailable, ask the user to reload Unity through Davinci and paste compile errors.

## Unity Editor Automation Bridge

This workspace now has a lightweight file-based Editor automation bridge so Codex can drive an already-open Unity Editor session without relying on manual menu clicks for every action.

- Bridge script: `Assets/Content/UI/Editor/CodexUnityBridge.cs`
- Unity menu: `Tools/UI/Codex Bridge/Open Control Panel`
- Enable polling menu: `Tools/UI/Codex Bridge/Enable Command Polling`
- Temporary bridge root: `C:/tmp/CodexUnityBridge`
- Request file: `C:/tmp/CodexUnityBridge/request.json`
- Result file: `C:/tmp/CodexUnityBridge/last_result.json`
- Log file: `C:/tmp/CodexUnityBridge/bridge.log`

The bridge was verified on 2026-05-01 with Unity `2021.3.8f1`, product `Dragonheir: Silent Gods`, project path `G:/Dragon/trunk/dragon`, and active scene `Assets/LaunchScene.unity`.

Validated command loop:

- Codex writes `request.json`.
- Unity polling consumes the request.
- Unity writes `last_result.json`.
- Unity archives processed requests under `C:/tmp/CodexUnityBridge/processed`.

Currently supported bridge commands:

- `ping`: verify that Unity can receive and execute a request.
- `capture_state`: return editor state, Unity version, active scene, selection, compiling/playing state, and project path.
- `select_asset`: select and ping a Unity asset by `Assets/...` path.
- `open_asset`: select and open a Unity asset.
- `open_prefab`: select and open a prefab asset.
- `execute_menu`: run a Unity menu item by menu path.
- `save_assets`: call `AssetDatabase.SaveAssets()` and `AssetDatabase.Refresh()`.
- `open_scene`: open a scene path, prompting for unsaved scene changes unless `force` is true.
- `dump_prefab_tree`: export a prefab hierarchy summary to a text file.

Operational notes:

- The user should start Unity through Davinci, wait for compilation, then keep `Tools/UI/Codex Bridge/Enable Command Polling` enabled.
- If a request is not processed, first check `last_result.json`, `bridge.log`, whether Unity is compiling, and whether polling is enabled.
- This bridge is for operating the Unity Editor. It is separate from Figma Bridge export/import logic, but it can be extended to support prefab assembly, RectTransform edits, asset binding, screenshot capture, and validation for future UI composition work.

## Figma Bridge Goal

Build a Unity editor bridge that exports selected UI prefabs into a package that a Figma development plugin can import as editable Figma frames/components.

The current priority is Unity-to-Figma export for design reuse. Figma-to-Unity restore is a future direction, so exported data should preserve Unity source metadata and RectTransform information even when the Figma visual import is approximate.

This is not intended to be pixel-perfect runtime parity. The practical target is: imported Figma output should be structurally reasonable, visually usable, and carry enough source information for future restore tooling.

## Figma Bridge Paths

- Tool directory: `Assets/Content/UI/Editor/FigmaBridge`
- Main docs: `Assets/Content/UI/Editor/FigmaBridge/README.md`
- Unity editor window: `Assets/Content/UI/Editor/FigmaBridge/FigmaBridgeWindow.cs`
- Exporter: `Assets/Content/UI/Editor/FigmaBridge/FigmaBridgeExporter.cs`
- Unity restore importer: `Assets/Content/UI/Editor/FigmaBridge/FigmaBridgeUnityImporter.cs`
- Sample validator: `Assets/Content/UI/Editor/FigmaBridge/FigmaBridgeSampleValidator.cs`
- Models: `Assets/Content/UI/Editor/FigmaBridge/FigmaBridgeModels.cs`
- JSON helper: `Assets/Content/UI/Editor/FigmaBridge/FigmaBridgeJson.cs`
- Image export helper: `Assets/Content/UI/Editor/FigmaBridge/FigmaBridgeImageExporter.cs`
- One-click local sync server: `Assets/Content/UI/Editor/FigmaBridge/FigmaBridgeLocalSyncServer.cs`
- Figma plugin: `Assets/Content/UI/Editor/FigmaBridge/FigmaPlugin`
- Package export root: `Assets/Content/UI/FigmaBridgeExports`

## Current Tool Behavior

- Entry point: `Tools/UI/Figma Bridge/Open Exporter`
- Restore entry point: `Tools/UI/Figma Bridge/Import Figma Restore JSON...`
- Sample validation entry point: `Tools/UI/Figma Bridge/Samples/Export Common And Plant Samples`
- Agent command bridge: write `*.request.json` under `Assets/Content/UI/FigmaBridgeCommands`; Unity writes matching `*.response.json`.
- Project context menu:
  - `Assets/UI/Figma Bridge/Export Selected Prefab to Figma Component Library`
  - `Assets/UI/Figma Bridge/Export Selected Prefab as Frame`
  - `Assets/UI/Figma Bridge/Export Selected Prefab as Component`
- Export input is the selected prefab asset. The tool exports the whole prefab, not manually selected modules.
- Export output contains `manifest.json`, `source/source_info.txt`, `source/nested_prefabs.txt`, and `images/`.
- The Figma plugin has a fixed `Target page` field, defaulting to `unity组件库`. Prefab/component imports should be created on that page, creating the page if needed.
- One-click component-library sync uses a local Unity HTTP server at `http://localhost:18732`. Unity queues the latest exported component package, persists the queue at `C:\tmp\FigmaBridgeLocalSync\latest.json`, and the Figma plugin auto-fetches `/latest` when `Auto import latest Unity export` is enabled. This is only a transport layer; imported nodes must still be created by `FigmaPlugin/code.js`.
- The Figma plugin manifest must include `http://localhost:18732` in `networkAccess.allowedDomains` with a reasoning. Do not leave `allowedDomains` as `["none"]` for this workflow; the UI may silently fail to fetch the local Unity package.
- Current development plugin name/id marker is `Unity Figma Bridge Importer v18733`. If Figma shows an older title, it is running a cached development plugin. The plugin UI must not assume `localStorage` is available because Figma may run it from a `data:` URL.
- Unity stores the component-library target page in EditorPrefs, defaulting to `unity组件库`. The Figma plugin stores its local target-page field in localStorage, but auto import should prefer the target page sent by Unity for the queued package.
- `Frame` mode is for screens/layouts. Nested prefab roots import as Figma components plus placed instances and carry source info.
- `Component` mode is for reusable UI components. Nested prefabs are expanded inline instead of becoming extra Figma components. Inactive descendants inside those nested prefab roots are skipped to avoid redundant hidden content.
- Nodes named `vx` or starting with `vx_` are excluded by default. These are usually visual-effect or shader-helper overlays and can cover exported button art in Figma as solid fills.
- `EditorOnly` tagged nodes can be excluded.
- Empty Raycast graphics should not create visible fills.
- `activeSelf` is preserved. Inactive Unity nodes import as hidden Figma nodes unless intentionally skipped by component-mode nested prefab cleanup.
- Inactive nodes can be initialized in a temporary preview scene before export so layout-driven sizes are captured while original active state remains in the manifest.
- TMP/project font mapping:
  - `uifont` exports to `uifont_zh-Hans`
  - `uifont_num` exports to `uifont_title`
  - `uifont_title`, `uifont_title_special`, and `uifont_title+special` export to `uifont_title_zh-Hans`
- Figma display font size matches the Unity font size; the original Unity font size remains in the manifest.
- Sliced/9-slice images export as already-stretched PNG previews at the node RectTransform size. The manifest preserves the original sprite path, GUID, border, and RectTransform data for future Unity restore.
- During Figma import, source metadata should be stored with Figma shared plugin data under namespace `unity.figmaBridge`. Keep read fallback for old private plugin data when possible, but write new data to shared plugin data so Figma MCP scripts and the desktop plugin can both read it.
- Image-bearing nodes should also store sprite source data in shared plugin data:
  - `figmaBridgeGraphics` keeps graphic/sprite source references.
  - `figmaBridgeImageSource` keeps the original sprite source plus the imported Figma `imageHash`.
  - Future Figma-to-Unity restore should reuse the original Unity Sprite when the current Figma image hash still matches the imported hash, and only create/import a new sprite when the image was replaced in Figma.
- Layout metadata is part of the source contract:
  - Unity export records `HorizontalLayoutGroup`, `VerticalLayoutGroup`, `GridLayoutGroup`, `LayoutElement`, and `ContentSizeFitter`.
  - Figma import maps horizontal/vertical layout groups to Auto Layout and maps grid layout to Auto Layout wrap when possible.
  - Children managed by a parent Layout Group should not use RectTransform anchoredPosition for Figma x/y; Auto Layout should place them.
  - `LayoutElement.ignoreLayout = true` children should stay absolute-positioned. Convert their RectTransform against the parent RectTransform directly, without adding or subtracting Layout Group padding.
  - Figma plugin data keys are `figmaBridgeLayoutGroup`, `figmaBridgeLayoutElement`, and `figmaBridgeContentSizeFitter`.
  - Future Figma-to-Unity restore should recreate Unity layout components from plugin data before applying RectTransform to non-layout-managed nodes.
- RectTransform data should remain source-of-truth metadata: anchors, pivot, sizeDelta, anchoredPosition, localScale, rotation, and rect.
- Figma import maps anchors to constraints and stores the full Unity payload in shared plugin data.
- Negative RectTransform scale should map to Figma mirroring.
- Z rotation should apply around the Unity pivot using Figma `relativeTransform`.
- The old `Figma Bridge Validation` report was removed because it was not useful for the user's desired validation level.
- Figma-to-Unity restore is currently a conservative copy workflow:
  - Figma plugin button: `Import + Export Unity Restore JSON`
    - Select an exported package folder, import it with real image fills, and immediately download a restore JSON from the newly created root node.
    - Use this for validating the real image hash and sprite source metadata path.
  - Figma plugin button: `Export Selection for Unity`
  - Unity menu: `Tools/UI/Figma Bridge/Import Figma Restore JSON...`
  - Output root: `Assets/Content/UI/FigmaBridgeImports`
  - It creates `{prefab_name}_figma_restored.prefab` and `{prefab_name}_figma_import_report.txt`.
  - It should not overwrite the source prefab.
  - It matches existing nodes by `figmaBridgeNode.path`, restores active state, original RectTransform payload, edited text content, and unchanged sprite references.
  - It detects changed image hashes and reports them instead of creating new sprite assets automatically.
- Sample validation exports top-level prefabs in `UI/Prefab/common` as Figma Components and `UI/Prefab/plant` as Figma Frames, then writes `sample_validation_report.txt` under `Assets/Content/UI/FigmaBridgeExports/_samples`.
- Supported command bridge commands:
  - `export_samples`
  - `import_restore`
- PowerShell must read exported manifest JSON as UTF-8, otherwise Chinese text can be mis-decoded and `ConvertFrom-Json` may report a false parse error.
- For Unity-prefab-to-Figma-component-library work, do not build a separate simplified MCP renderer. MCP/local sync can trigger, transport, or validate, but the actual Figma node creation should reuse `FigmaPlugin/code.js` importer behavior so text, RectTransform, node visibility, Layout Group, LayoutElement, ContentSizeFitter, image fills, and shared plugin data stay consistent with manual package import.

## Development Conventions

- All newly created Unity C# scripts for this UI workspace must be placed under `Assets/Content/UI/Editor`; create a clearly named subdirectory there when a tool contains multiple files.
- `UICreator` has one active implementation at `Assets/Content/UI/Editor/UICreator.cs`. The SVN-managed legacy file at `Assets/Scripts/SgrProject/UI/Editor/UITools/UICreator.cs` is retained as a compile-disabled compatibility shell because deleting it causes SVN/update workflows to restore a second `UICreator` after restart. Never enable both implementations or define another global `UICreator` class.
- Because Unity compiles everything under an `Editor` directory into an editor-only assembly, do not create runtime/player scripts without first updating this convention and agreeing on a runtime script location.
- Prefer small, local edits inside `Assets/Content/UI/Editor/FigmaBridge`.
- Do not move Figma Bridge docs to project root. Put detailed tool docs in `Editor/FigmaBridge/README.md`; use this `AGENTS.md` only as startup context.
- Preserve user changes. Do not revert unrelated files.
- Before changing behavior, inspect the current exporter and Figma plugin code together; many fixes require matching Unity manifest fields with importer behavior.
- Keep new mappings source-preserving: even approximate Figma visuals should retain Unity source path, GUID/local ID when available, component references, and RectTransform payload.
- After code changes, run static checks where possible. Unity compile usually needs the user to reload the editor through Davinci.

## Sprite Drag-To-UI Convention

- Expose the feature as a checked menu toggle at `UITools/拖入图片自动创建UI`. The preference is editor-local, persists through `EditorPrefs`, and defaults to enabled on first use.
- When enabled and a Sprite asset is dragged into the Scene view, create a UI node instead of Unity's default `SpriteRenderer` whenever a valid Canvas/UI parent exists. This applies to ordinary scenes and Prefab Stages regardless of asset folder.
- The created node uses `RectTransform`, `CanvasRenderer`, and `SgrImage`, binds the dragged Sprite, disables `Raycast Target`, inherits the parent UI layer, and calls `SetNativeSize()`.
- Scene-view click selection must still be able to pick these generated images even though `Raycast Target` is disabled. Editor-only selection helpers should choose the topmost visible `Graphic` under the cursor by its rect/depth, not rely on Unity's default object picking that can fall through to wrapper `root` nodes.
- If the current selection is a `RectTransform` under a Canvas in the current editing context, use it as the parent. Otherwise use the current Prefab Stage Canvas or the active Scene's first Canvas. If no Canvas exists, do not intercept Unity's native drag behavior.
- During `DragUpdated`, render a non-persistent Sprite preview at the current Scene-view mouse position using the same native-size calculation as the final `SgrImage`. The preview must follow Scene zoom, support multiple Sprites, and disappear on drop, cancel, invalid target, or leaving the Scene view.
- Sprite previews must preserve transparent padding. A tight Sprite mesh or trimmed `textureRect` must not stretch the visible pixels across the Sprite's full native-size bounds.
- Drag previews must be GUI-only or otherwise guaranteed not to enter the Prefab hierarchy, dirty the Prefab, create an Undo record, or survive script reload.
- Register the Sprite drag interceptor through `SceneView.beforeSceneGui`, so it can consume `DragUpdated` and `DragPerform` before Unity's native `GameObjectInspector` creates a default scene object. Draw the GUI preview separately through `SceneView.duringSceneGui`; GUI drawn in `beforeSceneGui` is covered by the Scene view's own rendering.
- Only intercept a real Project asset drag: `DragAndDrop.paths` must contain at least one valid asset path in addition to Sprite object references. Never consume editor dock resizing, Hierarchy object dragging, or other drags that have no Project asset path.
- Preserve the Scene-view drop position and support Undo. Multiple dragged Sprites are placed with a small horizontal offset so they remain individually selectable.
- Scene and Prefab Stage object creation must follow Unity's editor-safe structural Undo pattern: create through `ObjectFactory`, parent through `Undo.SetTransformParent`, then register the final parent hierarchy state. Do not register a Scene-root object and later reparent it with direct `Transform.SetParent`; Unity 2021.3 can corrupt structural Undo and crash on Ctrl+Z.
- When supported image files are dragged from the operating-system file explorer into an open UI Prefab Stage, import copies into the lowercase `resource` folder beside that prefab's module assets. For example, a prefab at `Assets/Content/UI/Prefab/event_support/a_event_support_pk.prefab` imports into `Assets/Content/UI/Prefab/event_support/resource/`.
- External image import must preserve the source file. When the target `resource` folder already contains an asset with the same filename, prompt before replacing it; confirming replacement overwrites the existing asset file in place so references keep the same Unity GUID. Configure the imported texture as a single Sprite with transparency and mipmaps disabled, then create the same native-size `SgrImage` used by Project-window Sprite dragging.
- When exactly one external image is dragged while the active selection is an `Image` or `SgrImage` inside the current UI Prefab Stage, treat the operation as replacement instead of creation. Highlight the selected image and show the compact Scene-view prompt `松手替换` while dragging.
- On replacement drop, import the image through the same prefab-local `resource` workflow and replace only the selected component's Sprite. Preserve the existing RectTransform size, anchors, pivot, position, rotation, and scale. Record the Sprite change as one Undo operation; the imported asset itself remains after Undo.
- Multiple external images, or an external image dragged without a valid selected image target, must continue through the normal create-new-images workflow.
- Do not intercept external image dragging in an ordinary Scene because there is no unambiguous prefab-local `resource` destination. Imported assets remain after UI-node Undo; Undo only removes the created hierarchy nodes.
- During Play Mode, allow Sprite drag-to-UI only inside an open UI Prefab Stage, where changes are written to the prefab editing scene and can be saved. Keep ordinary runtime scenes protected because objects created there disappear when Play Mode ends.
- Do not intercept Sprite dragging while the toggle is disabled, during a Play Mode transition, or when no valid Canvas/UI parent can be resolved.

## Project Image Import Convention

- When images are dragged from the operating-system file explorer into the Unity Project window under `Assets/Content/UI/`, intercept the Project-window drop before Unity's native unique-name import runs. Use each external file's exact filename to decide whether the target folder already has a same-name asset.
- Confirming replacement copies the dragged file bytes over the existing same-name asset file and reimports the original path so the original `.meta` / GUID and existing references are preserved. Cancelling skips that file.
- Multiple-file drops such as `name_2.png`, `name_3.png`, and `name_4.png` must be matched by exact filename only; do not infer relationships by decrementing numeric suffixes because those names can be intentional sequence assets.
- When the user drops onto Project-window whitespace and Unity's native import creates numbered duplicates, use the recorded external drag filenames and file hashes to resolve only exact same-name replacements. Never map `name_5.png` back to `name_2.png` merely because the numbers are consecutive.
- Keep the older post-import numbered-duplicate resolver only as a best-effort cleanup for simple Unity-created suffixes such as `name1.png`, `name 1.png`, or `name_1.png` when no exact external drag context is available.
- Replacement prompts should show the number of replaceable images detected in the current drop/import batch. When more than one replacement is detected, offer `全部替换（N）`; choosing it applies only to the current batch and must not persist as a global preference.
- Closing a replacement prompt with the window close button or cancel action must behave like `跳过`; it must never trigger single replace or replace-all.
- Provide `UITools/处理当前UI文件夹同名图片副本` for manually resolving simple numbered duplicates that were created before the Project-window drop interceptor existed.

## Scene UI Quick-Create Convention

- While editing a prefab under `Assets/Content/UI/Prefab`, a short right-click in the Scene view may open a project-specific UI quick-create menu.
- Right-button dragging must preserve Unity's native Scene-view pan/view behavior. Only a right-click released within a small drag threshold may open the menu.
- Quick-created UI nodes use the currently selected `RectTransform` in the same Prefab Stage as their parent. If there is no valid selected UI parent, fall back to the prefab root `RectTransform` or its first Canvas.
- Create the node at the Scene-view cursor position, inherit the parent UI layer, select it immediately, mark the Prefab Stage dirty, and support a single safe Ctrl+Z Undo step.
- Resolve and freeze the Scene cursor's world position before opening `GenericMenu`. Menu callbacks run after the original Scene GUI event has ended, so they must not call `HandleUtility.GUIPointToWorldRay` using the stale GUI mouse position.
- Project text creation uses `MultiLanguageTMPText`, disables `Raycast Target`, and assigns the default UI TMP font at `Assets/Content/UI/Mutilanguage/zh-Hans/TMP_Font/uifont.asset`.
- After creating TMP text from the Scene-view quick-create menu, start the same inline text editor on the next Editor frame with the default text fully selected, so the user can type the final copy immediately.
- Allow the Scene-view quick-create menu and its structural creation actions during Play Mode only while a UI Prefab Stage is open. Do not expose them during the transition into or out of Play Mode.
- Keep the first-level menu focused on frequent composition tasks: TMP text, `SgrImage`, empty `RectTransform`, and `EmptyRaycast`.
- Do not show or intercept the quick-create menu in ordinary scenes, non-UI prefabs, play mode, or prefabs outside `Assets/Content/UI/Prefab`.

## Direct Visible UI Selection Convention

- In a UI Prefab Stage, hovering a visible `Graphic` highlights the topmost selectable UI layer under the Scene-view cursor.
- Treat component prefabs that contain `RectTransform` and `Graphic` nodes as UI Prefab Stages even when the prefab itself has no `Canvas` and relies on Unity's `Canvas (Environment)` for preview.
- Respect Unity Hierarchy scene visibility and picking controls. If a UI object or any ancestor is hidden with the eye icon or has Scene picking disabled, exclude its entire subtree from hover highlighting, click selection, direct dragging, and inline TMP editing.
- A short left click selects the highlighted layer directly.
- Left-dragging a highlighted layer that is not already selected should select and move it in the same gesture; the user must not need a separate selection click first.
- Direct hover-drag must preserve Undo, Prefab Stage dirty state, nested-prefab property modifications, and the target's existing RectTransform relationship.
- If a direct child is actively controlled by a parent `LayoutGroup` and is not excluded through `ILayoutIgnorer.ignoreLayout`, treat direct dragging as a non-destructive preview. Show a translated ghost rectangle with the compact hint `Layout控制` while the pointer is held, including when the node is already selected; do not write RectTransform or Layout properties, and remove the preview on mouse-up or `Esc` so the node remains at its layout-computed position.
- Never start direct dragging or Layout drag preview while a Unity Scene control already owns the mouse. Rect Tool resize, edge, corner, pivot, rotation, and other active handles must keep exclusive control and must not drag a visible layer behind the edited RectTransform.
- Unity's default Scene selection control may temporarily own the mouse during an ordinary press-drag gesture; it must not block direct dragging of the highlighted UI layer.
- When the highlighted layer is already selected, leave dragging, resizing handles, snapping, and other transform-tool behavior to Unity's native Scene-view controls.
- Double-clicking a highlighted `TextMeshProUGUI` or project `MultiLanguageTMPText` should open a temporary Scene-view inline text editor over the text bounds without adding runtime components to the prefab.
- Resolve a double-click text target independently from generic Graphic selection: prefer the currently selected visible TMP under the cursor, then choose among visible TMP components under the cursor. Outer Images or nested-prefab roots must not steal the second click.
- Allow inline TMP editing, direct RectTransform dragging, Scene quick-create, and Sprite drag-to-UI while a UI Prefab Stage is open during Play Mode; this workflow is used for live UI preview. Ordinary runtime scenes remain protected from structural creation because those changes are discarded when Play Mode ends.
- Inline TMP editing selects the complete existing text by default, so typing immediately replaces it. It updates the visible text live; clicking outside or pressing `Ctrl+Enter` commits, while `Esc` cancels and restores the original text. The whole edit session must remain a single Undo step.
- Keep the inline TMP editor visually minimal; do not render persistent shortcut instructions beneath the input box.
- `Ctrl+Z` during an active inline TMP edit cancels that edit and restores the original text. After the edit is committed, Unity's normal `Ctrl+Z` must also restore the pre-edit text in one step; record a complete TMP object snapshot and flush it before collapsing the Undo group.
- Inline TMP editing changes only the TMP text content. It must not silently change localization IDs, font assets, layout settings, or RectTransform data.
- Do not intercept modified clicks, the View tool, non-UI Prefab Stages, or another active Scene-view control.

## Useful Next Checks

When continuing Figma Bridge development in a new conversation, first verify:

1. Does Unity compile after editor reload?
2. Does component export avoid generating redundant nested prefab components?
3. Does frame export still convert nested prefab references into Figma components/instances with source info?
4. Do fonts, hidden nodes, 9-slice previews, negative scale, and Z rotation look reasonable after Figma import?
5. Does the manifest still preserve enough data for future Figma-to-Unity restore?
6. Does `Export Selection for Unity` produce restore JSON, and does Unity import create a restored prefab copy plus report without touching the source prefab?
7. Does sample validation finish without missing sprite source metadata warnings?
