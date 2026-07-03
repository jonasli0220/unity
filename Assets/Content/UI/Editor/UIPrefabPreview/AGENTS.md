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

## Rendering Rules

- Render in a temporary preview scene.
- Instantiate prefab instances only in the preview scene.
- Use a temporary world-space Canvas and RenderTexture.
- Clean up preview scenes, temporary GameObjects, and RenderTextures after every generation.
- MVP renders the prefab's default active state only; alternate animation/state previews are future work.
