# Figma Paste

Designer-facing Figma-to-Unity Scene paste support.

## Entry Points

- `Tools/UI/Figma Bridge/Figma Paste/Enable Scene Ctrl+V Paste`
- `Tools/UI/Figma Bridge/Figma Paste/Inspect Clipboard`

## What Works Now

- In Figma, run the development plugin and click `Copy Selection for Unity Paste`, then focus a Unity Scene view while editing a UI prefab and press `Ctrl+V`.
  - This is the reliable path for current Figma Desktop native selections because native Ctrl+C may expose only Figma private `data-buffer` HTML.
  - Single rectangles with solid fills paste as solid-color `SgrImage` nodes.
  - Image-filled selections paste as PNG-backed `SgrImage` nodes under the prefab-local `resource` folder.
  - Text selections paste as project TMP text and reverse-map the project body/title/number/special-title fonts.
  - Figma Frame/Group/Component Set nesting is preserved as a RectTransform hierarchy, including nested groups.
  - Horizontal flips are preserved as negative RectTransform local X scale.
  - Unity-exported components are restored as Prefab instances when their shared plugin data contains a Prefab path or GUID. The pasted root uses the Unity source Prefab name instead of Figma's generated variant label.
  - Figma component instances resolve their exact main component and carry exported descendant visibility states back as Unity active-state overrides, preserving variant layer switches when the paths still exist in the source Prefab.
- Copy an image from Figma, focus a Unity Scene view while editing a UI prefab, then press `Ctrl+V`.
  - Direct clipboard images and SVG-embedded `data:image` nodes are saved as PNG under the prefab-local `resource` folder.
  - Unity imports it as a single Sprite.
  - A new `SgrImage` is created at the Scene cursor position.
- Copy a single filled rectangle from Figma, focus the same Scene view, then press `Ctrl+V`.
  - When Figma exposes the rectangle as SVG/HTML clipboard data, Unity creates a solid-color `SgrImage`.
  - The RectTransform size follows the SVG rectangle width and height.
  - Rounded corners, strokes, effects, groups, and frames are still out of scope for this shortcut.
- Copy text from Figma or another app, focus the same Scene view, then press `Ctrl+V`.
  - A new project TMP text node is created at the Scene cursor position.
  - The project default TMP font is assigned when available.
- Run `Inspect Clipboard` after copying from Figma to record the available Windows clipboard formats.
  - Reports are written to `Library/Dragon/FigmaPaste`.
  - If the clipboard has an image, the probe PNG is saved next to the report.

## Rules

- Paste is only intercepted when there is a valid UI prefab editing parent.
- Image paste imports assets only in UI Prefab Stage, where the prefab-local `resource` destination is unambiguous.
- Unsupported ordinary clipboard content is not consumed, so Unity's native paste can still run.
- Unsupported Figma/design clipboard content is consumed with a Scene-view notification, preventing Unity from pasting an unrelated object from its own internal clipboard.
- Native Figma Ctrl+C private `figma`/`figmeta` buffers are not parsed. Use the plugin copy button when `Inspect Clipboard` shows only `HTML Format` with `data-buffer`.
- Structured JSON v2 preserves supported hierarchy and remains backward-compatible with v1 clipboard payloads.

## Next Phase

Add Auto Layout to Unity Layout Group reconstruction, strokes/effects, rounded-corner assets, and richer anchor/constraint inference for newly created Figma-only nodes.
