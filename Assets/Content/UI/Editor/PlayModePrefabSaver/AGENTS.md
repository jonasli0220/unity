# Play Mode Prefab Saver Rules

## Scope

- This directory contains the editor-only tool that records manual Play Mode UI Prefab edits and writes only those recorded properties back to the source Prefab.
- Do not add runtime/player components or mutate gameplay logic.

## Behavior Contract

- Listen to Unity Undo/Inspector modifications only while Play Mode is stable. Runtime script changes that bypass the Editor Undo pipeline must never be inferred by full-object diffing.
- Record the latest manually assigned value at edit time, keyed by runtime Prefab instance, node locator, component locator, and serialized property path.
- Draw one compact `保存修改` action between the existing `打开 Prefab` and `重新加载` Inspector actions. Show the pending property count for the selected runtime Prefab instance and disable the action when no recorded changes exist.
- Resolve the source Prefab by native Prefab correspondence first, then exact `(Clone)` filename matching under the UI Prefab root. Never guess between duplicate paths.
- Write only recorded serialized properties on nodes and components that already exist in the source Prefab. Do not silently add/remove components, create/delete/rename/reparent nodes, or copy unrecorded runtime state.
- Remap Prefab-local `GameObject` and `Component` references from the runtime instance to the corresponding source-Prefab objects. Never persist references to unrelated runtime Scene objects.
- Do not copy Transform parent/child/root-order internals, script references, Prefab metadata, hide flags, editor-only identifiers, managed references, or structural array changes.
- Clear pending records when entering or exiting Play Mode and before an assembly reload. Never carry runtime object records into a later Play session.
- Source-Prefab saving is not a reliable Unity Undo operation. Before every write, create a recoverable backup under `Library/Dragon/PlayModePrefabSaver/Backups` and expose a one-click restore-last-save menu.
- After saving, reload the source Prefab and verify every recorded property. On verification failure, restore the backup automatically and report the exact mismatch.
- Keep the current runtime instance and Inspector selection intact after a successful source-Prefab save.
- Refuse to write while the same source Prefab is open in Prefab Mode; the user must save/close that Prefab Stage first so two editing contexts cannot overwrite each other.

## Structure

- `PlayModePrefabSaver.cs`: Undo-based recorder, source resolution, node/component mapping, inline save action, backup/restore, save, and verification.
- `README.md`: designer-facing usage and limitations.

## Validation

- Verify Unity Editor compilation on Unity 2021.3.
- Test Inspector edits, Scene-handle edits, repeated edits of one property, duplicate node names, asset references, Prefab-local references, duplicate Prefab names, runtime-script changes remaining unrecorded, automatic rollback, manual restore, and unchanged runtime selection.
