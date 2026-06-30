# Figma Paste Agent Context

## Scope

This directory contains the Unity Editor paste workflow for content copied from Figma into UI prefab editing scenes.

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
- Let Unity's native paste continue when ordinary clipboard content is unsupported or the editing context is invalid.
- If the clipboard looks like Figma/design data but the current content is unsupported, consume the paste and show an actionable Scene notification so Unity does not paste a stale Unity object from its own clipboard.
- Preserve a single Undo step for created UI hierarchy nodes.
- Prefer visible, actionable Scene view notifications over modal dialogs during paste.

## Structured Paste Boundary

- Supported paste targets: Figma plugin enhanced-copy JSON v2 for recursive groups/frames, rectangles, images, text, horizontal mirroring, and Unity Prefab source references; clipboard image or SVG-embedded data image to `SgrImage`; single filled SVG rectangle to solid-color `SgrImage`; clipboard plain text to project TMP text.
- Keep v1 structured JSON readable so a copied payload does not break during a Figma/Unity plugin version transition.
- Preserve every supported Figma container in the Unity RectTransform hierarchy. A group may also own a visual fill and child nodes.
- Text font mapping must reverse the Unity-to-Figma project mapping. Prefer stored Unity font path/GUID/name before inferring from the visible Figma family.
- Horizontal mirror is represented by negative RectTransform local scale. Parent group mirroring must remain on the parent so descendants inherit it naturally.
- A node with `figmaBridgeSource`, a nested prefab root reference, or an instance whose main component has source data must instantiate the referenced Unity Prefab instead of rebuilding its visual children. Resolve a Figma component instance to its exact main component so variants keep their own prefab path.
- Name a pasted Unity reference from its source Prefab name, not from Figma's generated instance/variant label such as `Property 1=Default`.
- For Figma component variants, carry descendant visibility states only when a node has a `figmaBridgeNode.path`. After Prefab instantiation, apply those states by exported Unity path so variant on/off overrides are preserved without guessing from layer names.
- Supported diagnostics: list clipboard formats and save a PNG probe image when available.
- Do not assume Figma's private clipboard formats are stable; native Figma `data-buffer` HTML is diagnostic-only unless Figma also exposes public image/SVG/text data.
- When native Figma Ctrl+C exposes only private `figma`/`figmeta` buffers, use the Figma plugin's `Copy Selection for Unity Paste` button to write the supported JSON marker into the system clipboard.
