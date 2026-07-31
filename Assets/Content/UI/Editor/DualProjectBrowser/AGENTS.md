# Dual Project Browser Rules

## Scope

- This directory contains the editor-only launcher for opening an additional native Unity Project window.
- Keep the implementation self-contained here; do not add runtime components, scenes, prefabs, or project assets.
- Keep user-facing behavior and setup notes in `README.md`.

## Native Project Window Integration

- Create another `UnityEditor.ProjectBrowser`; do not replace it with a custom asset browser.
- Resolve the starting folder from the currently interacted Project window, with the selected asset's containing folder and `Assets` as safe fallbacks.
- Navigate the new Project window before locking it so the resource folder is immediately usable.
- Preserve the native lock button, search, drag/drop, rename, Project history controls, and folder layout behavior.
- Reflection is version-specific. If Unity internals cannot be resolved, fail safely without changing or closing existing Project windows and show an actionable message only after the user invokes the command.

## Interaction

- Keep the primary command focused on the resource-replacement workflow: one action opens a second native Project window at the current folder and locks it.
- Also expose a Project context action so a folder or an asset's containing folder can be opened directly in the locked window.
- The created window must remain unlockable through Unity's native lock button.

## Validation

- Validate editor compilation against the project's Unity version.
- Confirm that two native Project windows can remain open with different folders.
- Confirm that selecting or opening a Prefab does not navigate the locked resource window.
- Confirm that Project-window asset drag/drop and replacement behavior remains native.
