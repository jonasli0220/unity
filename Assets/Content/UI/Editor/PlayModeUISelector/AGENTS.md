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
- Consume only the matching `Alt + Left Click` editor event so inspecting a runtime button does not also activate it.
- Do not rewrite scene objects, prefabs, serialized data, or unrelated runtime input.
- Respect visible `Mask` and `RectMask2D` bounds when choosing a target.
- Reveal the selected object in Hierarchy by expanding only its ancestor chain.
- Keep a checked `UITools` menu toggle and default the feature to enabled for first-time use.

## Validation

- Verify Unity editor compilation after changes.
- Test in Play Mode with nested UI, overlapping UI, and a `Graphic` whose `Raycast Target` is disabled.
