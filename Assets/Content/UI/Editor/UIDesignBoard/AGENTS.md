# UI Design Board Agent Context

## Scope

This directory contains the Unity Editor design board for browsing, organizing, and editing many UI prefab artboards through an infinite board and a temporary Live Board scene.

## Placement

- Keep all UI Design Board editor code in this directory.
- Keep tool documentation in `README.md`.
- Do not add runtime gameplay code here.

## Data Rules

- Board state is local editor data, not game source data.
- Persist board state under the Unity project `Library/Dragon/UIDesignBoard`.
- Do not store board JSON, generated thumbnails, screenshots, or logs under `Assets/`.
- The virtual board may read UI prefab assets under `Assets/Content/UI/Prefab` but must not modify source assets.
- Live Board may modify source prefabs only after an explicit user action such as `Apply`.
- Never save the temporary Live Board scene under `Assets/`; it is rebuildable editor state, not game content.

## Interaction Rules

- The virtual board remains the navigation and organization surface. `Open` opens the real prefab asset in Unity Prefab Mode; `Live` opens or focuses its editable instance in Scene view.
- Treat each card as an artboard that points to one source prefab through GUID and asset path.
- Preserve simple designer-first actions: add selected prefab, search prefab, focus selected card, ping/open prefab, live edit, apply/revert, remove card.
- Keep the virtual board editor-only. Do not instantiate board content into a gameplay scene.

## Live Board Rules

- Build Live Board in a dedicated temporary additive scene. Do not reuse or dirty the user's gameplay scene.
- Each artboard is a real prefab instance under an editor-created world-space Canvas wrapper.
- Store artboard placement on the wrapper, not on the prefab root, so moving an artboard never becomes a prefab override.
- Environment-only Canvas normalization, its automatic RectTransform changes, and zero-scale root preview correction must be restored before applying prefab overrides and reapplied afterward.
- Preserve every prefab object's source Layer. Live Board must not use layer changes as a preview mechanism.
- Direct selection, RectTransform editing, Inspector editing, inline TMP editing, and existing Sprite drag-to-UI should work inside a Live Board prefab instance.
- Structural edits belong inside the prefab instance. Do not allow quick-create or Sprite drops onto the board wrapper itself.
- Applying or reverting is always scoped to one artboard unless the user explicitly chooses an all-artboard action.
- Removing or closing an artboard with unapplied changes must ask before discarding work.
- Live Board state is disposable. Reopening it reconstructs instances from board JSON plus current prefab assets.

## Preview Rules

- Prefer the existing `UIPrefabPreviewGenerator` when available.
- Generate previews outside `OnGUI`, one at a time through editor update/delay callbacks.
- Keep generated preview textures in memory for this window session; do not write preview files from this tool.
