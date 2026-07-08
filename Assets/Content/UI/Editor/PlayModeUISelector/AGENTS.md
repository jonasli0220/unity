# Play Mode UI Selector Rules

## Scope

- This directory contains the editor-only Play Mode Game-view UI selection tool.
- Keep the implementation independent from runtime/player assemblies and do not add components to scene or prefab objects.

## Structure

- `PlayModeUISelector.cs`: input capture, visible UI picking, selection, and Hierarchy reveal behavior.
- `README.md`: designer-facing usage and limitations.

## Behavior Contract

- Respond only to `Alt + Left Click` inside the Game view while the Editor is in Play Mode.
- Select the topmost visible UI `Graphic` under the pointer even when `Raycast Target` is disabled.
- Create a hidden, non-serialized UGUI interception layer only while Play Mode is active and the tool is enabled.
- Let that layer participate in raycasts only while `Alt` is held, so ordinary runtime input remains untouched and the first `Alt + Left Click` press can be caught before runtime Buttons receive it.
- Route the matching pointer press to selection and keep it from reaching the runtime button underneath.
- Do not rewrite scene objects, prefabs, serialized data, or unrelated runtime input.
- Respect visible `Mask` and `RectMask2D` bounds when choosing a target.
- Reveal the selected object in Hierarchy by expanding only its ancestor chain.
- Keep a checked `UITools` menu toggle and default the feature to enabled for first-time use.

## Validation

- Verify Unity editor compilation after changes.
- Test in Play Mode with nested UI, overlapping UI, a runtime Button, and a `Graphic` whose `Raycast Target` is disabled.
