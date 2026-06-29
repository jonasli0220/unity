# Figma Bridge Exporter

Unity editor tool for exporting UI prefabs into Figma Bridge packages. The tool is scoped to design-side reconstruction: it gives Figma editable frames/components, image previews, RectTransform metadata, and Unity source references for future Figma-to-Unity restore work.

## Document Location

This README lives next to the tool implementation:

```text
Assets/Content/UI/Editor/FigmaBridge/README.md
```

Keep Figma Bridge notes here unless the project gains a shared UI tooling documentation folder.

## Entry Points

- `Tools/UI/Figma Bridge/Open Exporter`
- `Tools/UI/Figma Bridge/Import Figma Restore JSON...`
- `Tools/UI/Figma Bridge/Samples/Export Common And Plant Samples`
- `Tools/UI/Figma Bridge/Figma Paste/Enable Scene Ctrl+V Paste`
- `Tools/UI/Figma Bridge/Figma Paste/Inspect Clipboard`
- Project window context menu: `Assets/UI/Figma Bridge/Export Selected Prefab to Figma Component Library`
- Project window context menu: `Assets/UI/Figma Bridge/Export Selected Prefab as Frame`
- Project window context menu: `Assets/UI/Figma Bridge/Export Selected Prefab as Component`
- Exporter button: `Export Dragon Color Styles`

## Workflow

1. Select a prefab asset in the Project window.
2. For component-library sync, keep the Figma plugin open, then use `Assets/UI/Figma Bridge/Export Selected Prefab to Figma Component Library`. This exports a `Component` package, queues it on the local sync server, and the Figma plugin imports it into the configured target page.
3. For manual mode, open `Tools/UI/Figma Bridge/Open Exporter`, choose `Frame` or `Component`, and click `Export to Figma Package`.
4. Find the generated package under `Assets/Content/UI/FigmaBridgeExports`.
5. Import the package with the local Figma development plugin.

### Figma Paste Phase 1

Figma Paste is a lightweight Scene-view paste shortcut under `FigmaPaste/`. It is separate from the package importer and only covers the first practical paste layer:

1. Copy an image or text from Figma.
2. Focus a Unity Scene view while editing a UI prefab under `Assets/Content/UI/Prefab`.
3. Press `Ctrl+V`.

Current behavior:

- Clipboard images are imported as PNG files into the active prefab's lowercase `resource` folder, configured as single Sprites, then pasted as `SgrImage` nodes.
- Clipboard text is pasted as project TMP text using `MultiLanguageTMPText` and the default `uifont` asset when available.
- `Inspect Clipboard` writes diagnostic reports under `Library/Dragon/FigmaPaste` and saves a probe PNG when the clipboard exposes an image.
- Unsupported clipboard content is not consumed, so Unity's native paste can continue.

Out of scope for phase 1: Figma frame/group hierarchy reconstruction, Auto Layout conversion, SVG rendering, and plugin-enhanced structured copy.

### Component Library One-Click Sync

The one-click path is intentionally a thin transport layer over the existing package importer. It must not implement a separate Figma renderer.

1. In Figma Desktop, install and run `Unity Figma Bridge Importer`.
2. Keep `Auto import latest Unity export` enabled.
3. In Unity, right-click a prefab and choose `Assets/UI/Figma Bridge/Export Selected Prefab to Figma Component Library`.
4. Unity writes the package and publishes it on the local sync server.
5. The Figma plugin fetches the package from the first healthy local sync endpoint, usually `http://localhost:18733/latest` with fallback ports `18734-18736` and imports it through the same `import-package` code path as manual folder import.

Configuration:

- Unity stores the component-library target page in EditorPrefs. The default is `unity组件库`.
- The Figma plugin also stores its target page in localStorage. Auto import prefers the target page sent by Unity for the queued package.
- Prefab/component import is locked to the `Dragon-通用组件` Figma file (`eteWowFyYB3NHQWsMjI2iP`). If the plugin is run from another open file, it fails before creating or updating pages so `unity组件库` is not accidentally created in the wrong file.
- Local sync only works while Unity is open. Figma security still requires the plugin to be running; Unity cannot launch the Figma plugin by itself.
- The latest queued package is persisted at `C:\tmp\FigmaBridgeLocalSync\latest.json`, so Unity domain reloads after asset refresh do not lose the pending import.
- Unity local sync self-checks `/status` before publishing. If `18733` is occupied by a stale non-responsive listener, Unity starts on the next available fallback port and the Figma plugin probes the same port range before fetching `/latest`.
- The Figma development plugin manifest must allow `http://localhost:18733` through `http://localhost:18736` in `networkAccess.allowedDomains` with a reasoning. Keeping `allowedDomains` as `["none"]` can leave the plugin UI stuck at `Waiting for package...`.
- The current development plugin appears as `Unity Figma Bridge Importer v18740`. If the plugin window title does not include `v18740`, Figma is still running an older cached development plugin.
- Figma plugin UI runs from a `data:` URL, so `localStorage` may be disabled. Use in-memory storage fallback in `ui.html` for target page and last package state.
- If Figma reports blocked local network access, allow the plugin to access `http://localhost:18733` through `http://localhost:18736`.

### Dragon Color Styles

1. Open `Tools/UI/Figma Bridge/Open Exporter`.
2. Keep `Figma Style Folder` as `style-color/Dragon` unless the Dragon Figma file needs another local style folder.
3. Click `Export Dragon Color Styles`.
4. Import the generated `dragon_style_color` package with the same Figma development plugin.

The color style export reads only Dragon's `game/cross/sgr_data/AB/ColorAreaData.py`. In Figma it creates or updates local Paint Styles named like:

```text
style-color/Dragon/{ColorArea prefix}/{ColorArea id}
```

This is intentionally separate from prefab export so Figma design styles come from the Unity preset source, not from whichever prefab happened to be sampled.

Important: `dragon_style_color/manifest.json` is a Figma Bridge data manifest, not a Figma development plugin manifest. Install the plugin only from `FigmaPlugin/manifest.json`; then run that plugin and select the whole `dragon_style_color` folder.

## Output

Each export creates a folder like:

```text
Assets/Content/UI/FigmaBridgeExports/{prefab_name}_{frame|component}/
  manifest.json
  source/source_info.txt
  source/nested_prefabs.txt
  images/
```

`manifest.json` contains:

- Unity prefab source path, GUID, local file ID, and source hash.
- Root node and child hierarchy.
- RectTransform layout data.
- Image, Text, TMP_Text, Button, Toggle, ScrollRect, Animator, Canvas data.
- Sprite, material, font, script, and controller references.
- Active state for each node. Inactive nodes import as hidden nodes in Figma.
- Inactive nodes can be initialized in a temporary preview scene before export so layout-driven sizes are captured without changing the saved prefab state.
- Nested prefab source information.
- Sliced Image metadata. The exporter writes one already-stretched PNG at the node RectTransform size for Figma preview while preserving the original sprite path, GUID, border, and RectTransform data for Unity restore.
- TMP font mapping for project fonts: `uifont` -> `uifont_zh-Hans`, `uifont_num` -> `uifont_title`, `uifont_title` / `uifont_title_special` -> `uifont_title_zh-Hans`.
- Unity font size is preserved in the manifest. The Figma importer displays text at the same font size as Unity.

The `images` folder contains exported sprite PNG files referenced by `manifest.json`. Because the export folder is under `Assets`, Unity may create `.meta` files beside PNGs; the Figma importer ignores those files.

## Export Modes

### Frame

Use `Frame` for a whole screen or a reusable layout that should stay editable in Figma. Nested prefab roots are imported as Figma components plus placed instances, and their Unity source data is stored in plugin data.

### Component

Use `Component` for common UI parts such as buttons, tabs, reward items, and activity entries. Nested prefabs inside the exported component are expanded inline instead of becoming additional Figma components. Inactive descendants inside those nested prefab roots are skipped to reduce redundant hidden states.

## Figma Import Plugin

The local Figma plugin is in:

```text
Assets/Content/UI/Editor/FigmaBridge/FigmaPlugin
```

In Figma Desktop:

1. Open `Plugins > Development > Import plugin from manifest...`.
2. Select `FigmaPlugin/manifest.json`.
3. Run `Unity Figma Bridge Importer`.
4. Set `Target page` to the fixed component-library page, for example `unity组件库`.
5. Select one exported package folder, for example:

```text
Assets/Content/UI/FigmaBridgeExports/btn_common_component
```

The plugin creates a Figma Frame or Component based on the package `targetKind` on the target page. If the page does not exist, the plugin creates it.

When importing a package whose Unity `prefabGuid` or `prefabPath` already exists in Figma, the plugin updates the existing matching root instead of creating a duplicate. It preserves the Figma root node identity and x/y position, then rebuilds the Unity-owned child layers through the same importer path. Matching prefers the current selection, then the configured target page, then other pages in the file.

Important: any automated Unity-to-Figma or Figma MCP workflow should reuse this importer path instead of reimplementing a simplified renderer. The importer is the source of truth for text, RectTransform, node visibility, Layout Group, LayoutElement, ContentSizeFitter, image fills, and shared plugin data mapping.

For color style packages with `targetKind: style-color`, the plugin does not create canvas nodes. It syncs local Paint Styles under the package `styleNamespace`, defaulting to `style-color/Dragon`, and stores the original ColorArea id/source hash in style plugin data.

For future Figma-to-Unity restore, imported Figma nodes also receive source plugin data. New imports write this data as Figma shared plugin data under namespace `unity.figmaBridge`, with read fallback for older private plugin data where the runtime supports it. This keeps the desktop plugin and Figma MCP scripts on the same metadata path.

- `figmaBridgeNode` stores node path, Unity local ID, active state, RectTransform, prefab reference, and compact graphic source data.
- `figmaBridgeGraphics` stores Unity graphic/sprite source references, including sprite asset path, GUID, local ID, rect, border, and image type.
- `figmaBridgeImageSource` is added to nodes with imported image fills. It stores the original sprite source plus the imported Figma `imageHash`, so the Unity importer can reuse the original Unity Sprite when the image is unchanged and only report/import a new image when the Figma fill was replaced.
- 9-slice preview layers created by the plugin also carry `figmaBridgeImageSource` when that path is used.

## Figma To Unity Restore

This is an early safe restore path. It is designed for iterative validation and does not overwrite the original prefab.

Fast validation path:

1. In Figma, choose the exported package folder.
2. Click `Import + Export Unity Restore JSON`.
3. The plugin imports the package with real image fills, then immediately downloads `{prefab_name}_figma_restore.json` from the newly created root node.
4. In Unity, run `Tools/UI/Figma Bridge/Import Figma Restore JSON...`.
5. Select the restore JSON.
6. Unity creates a copy under:

Manual restore path:

1. In Figma, select one root node that was imported by `Unity Figma Bridge Importer`.
2. Click `Export Selection for Unity` in the Figma plugin.
3. Save the downloaded `{prefab_name}_figma_restore.json`.
4. In Unity, run `Tools/UI/Figma Bridge/Import Figma Restore JSON...`.
5. Select the restore JSON.

```text
Assets/Content/UI/FigmaBridgeImports/{prefab_name}_figma_restored.prefab
Assets/Content/UI/FigmaBridgeImports/{prefab_name}_figma_import_report.txt
```

Current restore behavior:

- Uses `figmaBridgeSource.prefabPath` / `prefabGuid` to find the original prefab.
- Matches nodes by stored Unity path in `figmaBridgeNode`.
- Restores active state from Figma visibility.
- Restores original RectTransform payload from plugin data.
- Restores edited text content and maps Figma font size back to Unity directly when the Figma size changed.
- Reuses original Unity Sprite when the current Figma image hash still matches `figmaBridgeImageSource.importedImageHash`.
- Reports changed images instead of creating new Unity Sprite assets automatically.

Not implemented yet:

- Reverse-solving edited Figma x/y/size into new RectTransform anchors and offsets.
- Importing replaced Figma images as new Unity sprite assets.
- Rebuilding new nodes that did not exist in the source prefab.

## Mapping Rules

- Exports the whole selected prefab. It does not split modules.
- Keeps image-bearing `vx` / `vx_` nodes, such as selected-state art. The default filter only skips image-less effect nodes or subtrees, such as particle-only `vx_` nodes that cannot be represented as editable Figma image layers.
- Skips `EditorOnly` tagged nodes when the option is enabled.
- Preserves `activeSelf`; inactive Unity nodes import as hidden Figma nodes.
- If `Initialize inactive nodes before export` is enabled, the exporter opens inactive nodes in a temporary preview scene to capture layout-driven sizes, then writes the original active state to the manifest.
- Ignores Empty Raycast graphics as visual fills.
- Bakes Unity RectTransform `localScale` into Figma visual width/height and position because Figma does not reliably preserve scale factors inside `relativeTransform`. The original Unity RectTransform size and independent scale remain preserved in shared plugin data. Negative scale still mirrors the node.
- Writes the independent Unity scale to shared plugin data key `figmaBridgeUnityScale` in addition to the full RectTransform payload.
- The Figma EX right-side inspector exposes selected-node Unity metadata: the original Unity RectTransform size, editable `Unity Scale` X/Y/Z fields, `figmaBridgeUnityScale`, `figmaBridgeNode.rectTransform.localScale`, and the visible Figma preview transform stay in sync.
- Applies Z rotation around the Unity pivot using Figma `relativeTransform`.
- Maps RectTransform anchors to Figma constraints and stores the full RectTransform payload in plugin data.
- Exports `HorizontalLayoutGroup`, `VerticalLayoutGroup`, `GridLayoutGroup`, `LayoutElement`, and `ContentSizeFitter` metadata.
- Maps horizontal and vertical Unity Layout Groups to Figma Auto Layout, including padding, spacing, child alignment, force-expand behavior, and reverse arrangement where practical.
- Maps Grid Layout Group to Figma Auto Layout with wrap when supported by the local Figma editor. Grid cell size, spacing, start axis, start corner, constraint, and constraint count remain in plugin data for restore.
- For children managed by a parent Layout Group, the Figma importer does not apply Unity anchoredPosition as absolute x/y. Layout-managed children are placed by Auto Layout to avoid drifting or "flying" nodes.
- `LayoutElement.ignoreLayout` children are imported as absolute-positioned children and keep their RectTransform position. Their transform is calculated against the parent RectTransform, without applying Layout Group padding.
- If a Unity Layout Group has direct `ignoreLayout` children, the importer does not convert that parent to Figma Auto Layout. It keeps the whole group in absolute positioning so decorative backgrounds and overlay nodes do not become layout items.
- `LayoutElement` min/preferred/flexible values influence Figma fixed/fill sizing where possible and are preserved in plugin data.
- `ContentSizeFitter` maps PreferredSize/MinSize to Figma hug-style sizing where possible and remains preserved for Unity restore.
- Stores Button, Toggle, ScrollRect, Animator, Canvas, sprite, material, font, script, and controller references for source tracing.
- Writes sprite source metadata into Figma plugin data during import so image fills can be traced back to existing Unity Sprite assets.

Layout plugin data keys:

- `figmaBridgeLayoutGroup`
- `figmaBridgeLayoutElement`
- `figmaBridgeContentSizeFitter`

Future Figma-to-Unity restore should recreate Unity layout components from these plugin data fields first, then restore RectTransform for nodes outside Layout Group control or nodes with `ignoreLayout = true`.

## Current Scope

- Exports the whole selected prefab.
- Does not split modules.
- Component-library sync should use `Component` exports and import them into the configured target page, such as `unity组件库`.
- Does not use manual node tags.
- Figma import is approximate and intended for design-library reconstruction, not pixel-perfect Unity runtime parity.
- Figma node plugin data keeps the full Unity RectTransform payload so a future Figma-to-Unity importer can restore anchors, pivot, sizeDelta, anchoredPosition, scale, and rotation.
- The Figma plugin scans available local fonts and loads the mapped font family with its first available style if `Regular` is not present.
- The old `Figma Bridge Validation` report was removed; validate by checking whether the imported frame/component is structurally reasonable and visually usable for design reuse.

## Quick Checks

After changing exporter or importer behavior:

1. Export one simple component prefab as `Component`.
2. Export one screen or complex prefab as `Frame`.
3. Import both packages in Figma Desktop with `Unity Figma Bridge Importer`.
4. Check fonts, hidden nodes, nested prefab handling, Layout Group auto layout, LayoutElement sizing, negative scale mirroring, Z rotation, and 9-sliced image previews.
5. Reopen Unity and confirm there are no compile errors after editor scripts reload.

After changing Figma Paste:

1. Run `Tools/UI/Figma Bridge/Figma Paste/Inspect Clipboard` after copying text and image content from Figma.
2. Open a UI prefab under `Assets/Content/UI/Prefab`, select a UI parent in Scene view, copy Figma text, and press `Ctrl+V`.
3. Copy a Figma image, press `Ctrl+V`, then confirm the PNG appears under the prefab-local `resource` folder and the created `SgrImage` has Undo support.

For broader sample coverage, run `Tools/UI/Figma Bridge/Samples/Export Common And Plant Samples`. It exports top-level prefabs from:

```text
Assets/Content/UI/Prefab/common  -> Component packages
Assets/Content/UI/Prefab/plant   -> Frame packages
```

The sample tool writes `sample_validation_report.txt` under `Assets/Content/UI/FigmaBridgeExports/_samples` and warns when exported sprites are missing source metadata.

Automated command bridge checks are available for Codex/agent-driven validation:

```text
Assets/Content/UI/FigmaBridgeCommands/*.request.json
Assets/Content/UI/FigmaBridgeCommands/*.response.json
```

Supported commands:

- `export_samples`: runs the common/plant sample export and writes the sample report.
- `import_restore`: imports one Figma restore JSON and writes a restored prefab copy plus report.

When parsing exported `manifest.json` with PowerShell, always read as UTF-8:

```powershell
Get-Content manifest.json -Raw -Encoding UTF8 | ConvertFrom-Json
```
