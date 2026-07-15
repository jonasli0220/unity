# PC Adaptation Preview Agent Context

## Scope

This directory contains the Unity Editor scene toolbar button for temporarily previewing UI PC adaptation.

## Placement

- Keep all PC adaptation preview editor code in this directory.
- Keep tool documentation in `README.md`.
- Do not add runtime gameplay code here.

## Behavior Rules

- The scene toolbar control works in Edit Mode and Play Mode.
- The scene toolbar control is a toggle: click once to apply the preview, click again to restore the original state.
- The button is hosted by the existing `Prefab导航` Scene View overlay.
- Preview temporarily applies `CanvasScaler.uiScaleMode = ConstantPixelSize` and `CanvasScaler.scaleFactor = 0.8`, then restores the original scaler mode and scale.
- Prefer existing enabled `CanvasScaler` components.
- In Prefab Stage, target the editing environment Canvas/CanvasScaler that contains `prefabContentsRoot`; do not scan child prefab `CanvasScaler` components as preview targets.
- In Prefab Stage, clicking the toolbar button must preview the currently opened prefab without requiring any specific hierarchy selection.
- If a UI prefab is opened inside a prefab editing environment that has a root `Canvas` but no effective `CanvasScaler`, temporarily add a `DontSave` `CanvasScaler` to that existing root `Canvas`, then remove it when preview ends.
- Do not add or delete `Canvas` components during preview. It is acceptable to remove old preview-created `DontSave` `CanvasScaler` components left by earlier tool versions; never delete editor environment `Canvas` components.
- Do not use a direct `Canvas.scaleFactor` fallback. `AdaptationLocking` and background adaptation scripts must see a real parent `CanvasScaler`.
- In regular scenes, if the current selection is under a `CanvasScaler`, preview that canvas only. If no scaler is selected, preview scalers in the open scene.
- Enabled `AdaptationLocking` nodes must be refreshed while previewing and restored exactly after preview ends. Disabled component checkboxes and nested-prefab removed-component overrides must be ignored.
- If a selected node has no effective `AdaptationLocking` but an ancestor has one, keep the inherited behavior and surface a Scene View notification for the ancestor instead of treating the child as the locking source.
- Enabled `AutomaticBackground` nodes must be refreshed while previewing and restored exactly after preview ends, including cached scaler fields. Disabled component checkboxes must be ignored.
- Do not create Undo records for preview-only changes.
- Restore preview state before assembly reload, play mode changes, or editor quit.
