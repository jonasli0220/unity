# UI Design Board Agent Context

## Scope

This directory contains the Unity Editor phase-1 design board for browsing and organizing many UI prefab artboards on an infinite canvas.

## Placement

- Keep all UI Design Board editor code in this directory.
- Keep tool documentation in `README.md`.
- Do not add runtime gameplay code here.

## Data Rules

- Board state is local editor data, not game source data.
- Persist board state under the Unity project `Library/Dragon/UIDesignBoard`.
- Do not store board JSON, generated thumbnails, screenshots, or logs under `Assets/`.
- The tool may read UI prefab assets under `Assets/Content/UI/Prefab`, but phase 1 must not modify prefabs, scenes, sprites, animations, or generated gameplay resources.

## Interaction Rules

- The board is a navigation and organization surface. Opening a card should open the real prefab asset in Unity Prefab Mode.
- Treat each card as an artboard that points to one source prefab through GUID and asset path.
- Preserve simple designer-first actions: add selected prefab, search prefab, focus selected card, ping/open prefab, remove card.
- Keep the canvas virtual and editor-only. Do not create a giant Unity Canvas or instantiate all prefabs in the active scene for phase 1.

## Preview Rules

- Prefer the existing `UIPrefabPreviewGenerator` when available.
- Generate previews outside `OnGUI`, one at a time through editor update/delay callbacks.
- Keep generated preview textures in memory for this window session; do not write preview files from this tool.
