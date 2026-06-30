# UI Design Board

Unity Editor design board for organizing many UI prefab artboards on a virtual infinite canvas and editing them together in Scene view.

## Entry Points

- `Tools/UI/UI Design Board/Open`
- `Tools/UI/UI Design Board/Add Selected Prefabs`
- `Tools/UI/UI Design Board/Open Live Board`
- Project context menu: `Assets/UI Design Board/Add Selected Prefabs to Board`

## What It Does

- Adds selected prefabs or prefab folders under `Assets/Content/UI/Prefab` as artboards.
- Shows many prefab artboards on one pan/zoom canvas.
- Uses each prefab GUID and path as the source link.
- Double-clicking an artboard opens or focuses its editable Live Board instance.
- Buttons on each artboard can open, ping, copy, or remove the source prefab.
- Search reads the Semantic UI Locator cache when available, then falls back to all UI prefab paths.

## Live Board

- `Live Edit` creates a temporary additive scene and instantiates every board artboard as a real prefab instance.
- Every instance sits under its own world-space Canvas wrapper, so multiple screens can be edited side by side.
- Board placement lives on the wrapper and does not become a prefab override.
- Use normal Scene view handles and Inspector fields to edit UI. Existing direct visible-layer selection, inline TMP editing, and Sprite drag-to-UI are supported inside an artboard.
- `Apply` writes the selected artboard's overrides to its source prefab.
- `Revert` discards the selected artboard's unapplied overrides.
- `Close Live` checks for unapplied work before closing the temporary scene.
- The Live Board scene is never saved under `Assets/` and never becomes gameplay content.

## Data Storage

Board layout is local editor data:

```text
Library/Dragon/UIDesignBoard/board.json
```

The tool does not write board data, screenshots, thumbnails, or generated files under `Assets/`.

## Controls

- Mouse wheel: zoom.
- Middle/right drag: pan.
- Drag an artboard card: rearrange both the virtual card and its Live Board wrapper.
- Double-click an artboard: open/focus it in Live Board.
- `F`: focus selected artboard.
- `Ctrl+A`: fit all artboards.
- `Delete`: remove selected artboard from the board.

## Current Limits

- The custom window remains a thumbnail/navigation surface; actual editing happens in Unity Scene view and Inspector.
- Live Board normalizes source Canvas components to world-space and temporarily expands zero-scale prefab roots for editing. Those environment-only values are excluded when applying.
- Play Mode and multi-user collaboration are outside this tool's scope.
- Preview generation is static and reuses the existing UI prefab preview generator when available.
