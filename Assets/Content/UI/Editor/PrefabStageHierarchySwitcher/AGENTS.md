# Prefab Stage Hierarchy Switcher Rules

## Scope

- This directory contains an editor-only tool that switches Hierarchy content by the last focused Scene or Game view while a Prefab Stage is open during Play Mode.
- Keep the implementation independent from runtime/player assemblies and game-specific components.

## Structure

- `PrefabStageHierarchySwitcher.cs`: focus tracking, runtime-scene discovery, Hierarchy override, and restoration.
- `README.md`: designer-facing behavior, limits, and verification steps.
- Keep matching Unity `.meta` files beside every asset in this directory.

## Behavior Contract

- Activate only while Unity is in Play Mode and a Prefab Stage is currently open.
- Focusing the Game view makes unlocked Hierarchy windows show loaded runtime scenes without leaving or closing the Prefab Stage.
- Focusing the Scene view restores those Hierarchy windows to their previous Prefab Stage content.
- Focusing Inspector, Project, Hierarchy, Console, or other windows must keep the last Scene/Game choice instead of causing extra switches.
- Preserve any pre-existing custom-scene configuration and restore it when the feature is disabled, Play Mode ends, the Prefab Stage closes, scripts reload, or Unity quits.
- Respect locked Hierarchy windows and do not modify scene objects, prefab objects, selection, serialized assets, or runtime input.
- Internal Unity Editor reflection must fail safely with one actionable warning and no repeated log spam.

## Validation

- Compile against the project's Unity 2021.3.8f1 Editor assemblies or validate through the Codex Unity Bridge.
- Verify Game-to-Scene and Scene-to-Game switching during Play Mode with an open Prefab Stage.
- Verify exiting the Prefab Stage, exiting Play Mode, disabling the menu toggle, and script reload all restore Unity's default Hierarchy behavior.
