# Play Mode UI Selector Rules

## Scope

- This directory contains the editor-only Play Mode Game-view UI selection tool.
- Keep the implementation independent from runtime/player assemblies and do not add components to scene or prefab objects.

## Structure

- `PlayModeUISelector.cs`: input capture, visible UI picking, selection, Hierarchy reveal, and Play Mode drag behavior.
- `PlayModeUIPrefabOpener.cs`: Inspector and Hierarchy actions that resolve a selected runtime UI object back to its source prefab and open it.
- `README.md`: designer-facing usage and limitations.

## Behavior Contract

- Respond only to `Alt + Left Click` inside the Game view while the Editor is in Play Mode.
- Select the topmost visible UI `Graphic` under the pointer even when `Raycast Target` is disabled.
- Ignore the runtime click-feedback subtree named `common_click_feedback` / `common_click_feedback(Clone)` so transient click effects are never selected or dragged.
- Create a hidden, non-serialized UGUI interception layer only while Play Mode is active and the tool is enabled.
- Let that layer participate in raycasts only while `Alt` is held, so ordinary runtime input remains untouched and the first `Alt + Left Click` press can be caught before runtime Buttons receive it.
- Route the matching pointer press to selection and keep it from reaching the runtime button underneath.
- When a visible selected UI `RectTransform` is pressed with `Alt + Left Click`, dragging should move that selected node in Play Mode using the same plane-based world-position delta as prefab direct dragging.
- If the press does not start on the selected node, keep `Alt + Left Click` as "select under cursor"; dragging after that press may move the newly selected node.
- Do not write positions for direct children driven by an active parent `LayoutGroup`, unless an enabled `ILayoutIgnorer.ignoreLayout` component opts the child out of layout.
- Do not rewrite scene objects, prefabs, serialized data, or unrelated runtime input.
- Respect visible `Mask` and `RectMask2D` bounds when choosing a target.
- Reveal the selected object in Hierarchy by expanding only its ancestor chain.
- Keep a checked `UITools` menu toggle and default the feature to enabled for first-time use.
- While Play Mode is active, show a single-click Inspector action for a selected runtime UI object and keep a matching Hierarchy context-menu fallback.
- Resolve the source prefab without adding runtime components: prefer Unity's native prefab correspondence, then search exact prefab filenames from the nearest `(Clone)` ancestor upward.
- Open a unique match immediately. If exact duplicate filenames exist, let the user choose from the matching asset paths instead of guessing.

## Validation

- Verify Unity editor compilation after changes.
- Test in Play Mode with nested UI, overlapping UI, a runtime Button, a selected draggable `RectTransform`, a LayoutGroup-controlled child, and a `Graphic` whose `Raycast Target` is disabled.
