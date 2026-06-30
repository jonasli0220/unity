# Figma Paste

Phase 1 of Figma-to-Unity paste support.

## Entry Points

- `Tools/UI/Figma Bridge/Figma Paste/Enable Scene Ctrl+V Paste`
- `Tools/UI/Figma Bridge/Figma Paste/Inspect Clipboard`

## What Works Now

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
- Image paste imports assets only in UI Prefab Stage, because Live Board does not have an unambiguous prefab-local `resource` destination yet.
- Unsupported ordinary clipboard content is not consumed, so Unity's native paste can still run.
- Unsupported Figma/design clipboard content is consumed with a Scene-view notification, preventing Unity from pasting an unrelated object from its own internal clipboard.
- Structural Figma frame/group reconstruction is intentionally out of scope for phase 1; the rectangle path is only a single-shape convenience case.

## Next Phase

Use the clipboard reports to decide whether Figma exposes usable SVG/HTML/JSON data directly. If not, add a Figma plugin enhanced-copy channel that sends structured selection data to Unity.
