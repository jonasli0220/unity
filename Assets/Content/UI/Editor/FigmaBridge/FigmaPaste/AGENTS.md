# Figma Paste Agent Context

## Scope

This directory contains the first-stage Unity Editor paste workflow for content copied from Figma into UI prefab editing scenes.

## Placement

- Keep Figma paste editor code in this directory.
- Keep usage notes and phase boundaries in `README.md`.
- Do not add runtime gameplay code here.

## Data Rules

- Clipboard inspection output is local editor data only.
- Persist clipboard reports under the Unity project `Library/Dragon/FigmaPaste`.
- Do not store pasted clipboard dumps, generated probe images, logs, or temporary files under `Assets/`.
- Imported clipboard images are real UI source assets and must be created only inside the active UI prefab's lowercase `resource` folder.

## Interaction Rules

- Scene paste is a designer-first shortcut for an active UI prefab editing context.
- Only intercept paste when the clipboard has supported content and a valid UI parent can be resolved.
- Let Unity's native paste continue when the clipboard content is unsupported or the editing context is invalid.
- Preserve a single Undo step for created UI hierarchy nodes.
- Prefer visible, actionable Scene view notifications over modal dialogs during paste.

## Phase 1 Boundary

- Supported paste targets: clipboard image to `SgrImage`, clipboard plain text to project TMP text.
- Supported diagnostics: list clipboard formats and save a PNG probe image when available.
- Do not attempt structural Figma group/frame reconstruction here yet.
- Do not assume Figma's private clipboard formats are stable; record them through the inspector first.
