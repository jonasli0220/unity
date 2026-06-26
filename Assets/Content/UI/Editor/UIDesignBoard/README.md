# UI Design Board

Unity Editor phase-1 design board for organizing many UI prefab artboards on a virtual infinite canvas.

## Entry Points

- `Tools/UI/UI Design Board/Open`
- `Tools/UI/UI Design Board/Add Selected Prefabs`
- Project context menu: `Assets/UI Design Board/Add Selected Prefabs to Board`

## What It Does

- Adds selected prefabs or prefab folders under `Assets/Content/UI/Prefab` as artboards.
- Shows many prefab artboards on one pan/zoom canvas.
- Uses each prefab GUID and path as the source link.
- Double-clicking an artboard opens the real prefab asset in Prefab Mode.
- Buttons on each artboard can open, ping, copy, or remove the source prefab.
- Search reads the Semantic UI Locator cache when available, then falls back to all UI prefab paths.

## Data Storage

Board layout is local editor data:

```text
Library/Dragon/UIDesignBoard/board.json
```

The tool does not write board data, screenshots, thumbnails, or generated files under `Assets/`.

## Controls

- Mouse wheel: zoom.
- Middle/right drag: pan.
- Drag an artboard card: rearrange it.
- Double-click an artboard: open prefab.
- `F`: focus selected artboard.
- `Ctrl+A`: fit all artboards.
- `Delete`: remove selected artboard from the board.

## Current Limits

- Phase 1 is a navigation and organization surface only.
- It does not edit UI contents directly on the board.
- It does not instantiate all prefabs into a real Unity Canvas.
- Preview generation is static and reuses the existing UI prefab preview generator when available.
