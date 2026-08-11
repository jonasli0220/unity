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
- One `Ctrl+Z` must restore every grouped UI child's exact original parent, sibling index, anchors, pivot, anchored/local position, size, rotation, and scale; register the complete RectTransform state before reparenting.
- UI children become fixed-anchor children of the new group so their current appearance does not shift.
- Ordinary Transform objects should also be supported without adding UI components.
- Select the created group without calling `EditorGUIUtility.PingObject`; the yellow Ping overlay blocks immediate Hierarchy renaming.
- Never modify Project-window prefab assets directly; operate only on editable scene or Prefab Stage objects.
- During Play Mode, allow structural grouping only inside an open Prefab Stage.

## Validation

- Confirm there is exactly one `%g` menu shortcut in project C# sources.
- Compile against the project's Unity Editor assemblies or validate through the Codex Unity Bridge.
- In an isolated Unity project, verify Group, Redo, and each single Undo preserve world corners and restore the complete original RectTransform state.
- Verify the ordinary Transform branch remains a single `Ctrl+Z` operation.
