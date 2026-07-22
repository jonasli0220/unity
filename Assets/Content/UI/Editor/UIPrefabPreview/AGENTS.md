# UI Prefab Preview Agent Context

## Scope

This directory contains the Unity Editor MVP for showing cached UI prefab previews in the Project window.

## Placement

- Keep all preview editor code in this directory.
- Keep tool documentation in `README.md`.
- Do not place generated preview PNGs under `Assets/`; preview output is local cache only.
- This tool is for editor browsing convenience. It must not modify source prefabs, sprite assets, scenes, or generated gameplay resources.

## Cache Rules

- Preview cache lives under the Unity project `Library/Dragon/UIPrefabPreviewCache`.
- Cache file names must be derived from prefab GUID, dependency hash, and preview size.
- Cache misses may be generated in the editor update loop, but never directly inside Project window `OnGUI`.
- Manual refresh may delete cached PNGs for selected UI prefabs only.
- Full cache clearing is allowed only through the explicit menu command.

## Project Window Rules

- Only draw previews for prefabs under `Assets/Content/UI/Prefab`.
- Skip list rows that are too small to show a useful image.
- If preview generation fails, show the normal Unity icon and avoid repeatedly retrying every repaint.
- The overlay must be optional through an EditorPrefs-backed menu toggle.

## Project Grid Size Rules

- Preserve Unity's native Project Browser. Extend only its internal maximum grid size; do not replace selection, search, drag/drop, rename, or context-menu behavior with a custom asset browser.
- The editor-local default maximum is 192, with explicit choices for 96 (Unity default), 160, 192, and 256.
- Store the configured maximum in `EditorPrefs` and reapply it after assembly reloads and to newly opened Project windows.
- When raising the maximum, automatically expand only Project windows already at or near the previous maximum. Preserve intentionally smaller grid sizes.
- Accelerate `Ctrl/Cmd + mouse wheel` zoom in the native Project content area to about eight discrete levels from list view to the configured maximum.
- Keep the bottom grid-size slider continuous. Do not consume ordinary scrolling, wheel input over the folder tree, or wheel input outside the native asset list area.
- Detect wheel-driven grid changes from `EditorApplication.update` after Unity's native `ObjectListArea.HandleZoomScrolling` has changed the grid size. Unity 2021.3 does not route Project scroll events through `EditorApplication.globalEventHandler` or `projectWindowItemOnGUI`.
- On Windows, distinguish wheel zoom from the bottom slider by requiring the Control key to be down and the left mouse button to be up through `GetAsyncKeyState`. If platform input state is unavailable, preserve Unity's native step.
- Treat the reflection hook as version-specific. If Unity internals cannot be resolved, keep the native 96 limit and emit at most one actionable warning per assembly reload.
- If the post-native wheel snap cannot run, preserve Unity's native seven-pixel wheel step and emit at most one warning per assembly reload.
- Keep preview render resolution independent from Project grid size. Larger cells should reuse the existing 128/256/512 preview cache sizes.

## Rendering Rules

- Render in a temporary preview scene.
- Instantiate prefab instances only in the preview scene.
- Use a temporary world-space Canvas and RenderTexture.
- Clean up preview scenes, temporary GameObjects, and RenderTextures after every generation.
- MVP renders the prefab's default active state only; alternate animation/state previews are future work.
