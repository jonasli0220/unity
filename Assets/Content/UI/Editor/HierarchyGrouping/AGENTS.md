# Hierarchy Grouping Tool Rules

## Scope

- This directory contains the editor-only hierarchy grouping command.
- Keep the tool independent from project runtime assemblies and game-specific components.

## Structure

- `HierarchyGrouping.cs`: Unity Editor menu command and grouping implementation.
- `README.md`: user-facing behavior, constraints, and verification notes.
- Keep matching Unity `.meta` files beside every asset in this directory.

## Behavior Contract

- `Ctrl+G` creates one empty parent named `group` for the current top-level selection.
- Selected objects must share the same immediate parent. Invalid selections must explain how to recover.
- UI grouping must preserve the current visible layout, selected-node order, layer, and one-step Undo.
- UI children become fixed-anchor children of the new group so their current appearance does not shift.
- Ordinary Transform objects should also be supported without adding UI components.
- Never modify Project-window prefab assets directly; operate only on editable scene or Prefab Stage objects.
- During Play Mode, allow structural grouping only inside an open Prefab Stage.

## Validation

- Confirm there is exactly one `%g` menu shortcut in project C# sources.
- Compile against the project's Unity Editor assemblies or validate through the Codex Unity Bridge.
- Verify both UI and ordinary Transform branches remain a single `Ctrl+Z` operation.
