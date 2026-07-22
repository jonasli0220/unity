# UI Prefab Preview

Shows cached static previews for UI prefabs directly in the Unity Project window.

## Entry Points

- `Tools/UI/UI Prefab Preview/Project Thumbnails Enabled`
- `Tools/UI/UI Prefab Preview/Project Grid Max/96 (Unity Default)`
- `Tools/UI/UI Prefab Preview/Project Grid Max/160`
- `Tools/UI/UI Prefab Preview/Project Grid Max/192 (Recommended)`
- `Tools/UI/UI Prefab Preview/Project Grid Max/256`
- `Tools/UI/UI Prefab Preview/Refresh Selected Previews`
- `Tools/UI/UI Prefab Preview/Clear Preview Cache`

## MVP Behavior

1. Open a Project window folder under `Assets/Content/UI/Prefab`.
2. The overlay requests previews for visible prefab items.
3. Unity generates previews one at a time during editor updates.
4. The Project window replaces the default blue prefab cube with the cached preview image when ready.

The Project window GUI only draws existing cached textures. Prefab loading and rendering happen later in the editor update loop so normal browsing stays responsive.

## Native Project Grid Size

Unity 2021.3 normally caps the two-column Project Browser grid at 96. This tool raises only that internal maximum while preserving Unity's native Project window, including selection, search, drag/drop, rename, context menus, and the bottom size slider.

The editor-local default maximum is 192. When the patch first raises the limit, a Project window already at or within one zoom step of Unity's old maximum expands to the new maximum immediately. Windows using a deliberately smaller grid keep their current size. The setting is stored in `EditorPrefs` and is reapplied after assembly reloads and to newly opened Project windows.

On Windows, `Ctrl + mouse wheel` over the right-hand asset list uses eight practical zoom levels instead of Unity's native seven-pixel increments. With the recommended 192 maximum, the levels are `16 → 32 → 56 → 88 → 112 → 136 → 168 → 192`, so list view reaches the largest thumbnails in seven wheel notches. Unity first performs its native wheel step; the editor update loop detects the grid change while Control is held and the left mouse button is up, then snaps it to the requested level. The bottom size slider remains continuous for fine adjustment; normal scrolling and wheel input over the folder tree are unchanged. If the slider is between two levels, the next wheel notch moves to the next level in the requested direction.

Choose `96 (Unity Default)` to restore the native limit. The hook uses version-specific UnityEditor reflection and fails safely: if the expected internals are unavailable, Unity keeps its normal 96 limit and the Console receives one warning for that assembly reload.

Project grid size and cached preview resolution are independent. A 192 grid can reuse the existing 256 or 512 preview cache without generating source assets under `Assets/`.

Prefabs with no visible UI graphics in their default active state are shown as plain black thumbnails. They are treated as intentionally empty previews instead of failed previews, because many runtime-driven UI prefabs are intentionally empty before data binding.

If the default state has no active `Graphic` and renders no non-transparent pixels, the generator attempts one preview-only fallback: it activates the first hidden branch that contains a renderable UI graphic, then renders that state. This is intended for multi-state prefabs whose states are hidden by default. Prefabs that already have active UI graphics keep their default state, even if that state is dark or subtle.

Popup-style prefabs are content-cropped when a full-screen dimmer or backdrop is detected, so the dialog body stays large enough to recognize in Project thumbnails.

The software fallback also applies parent `Mask`, `RectMask2D`, and project `SoftMask` components as approximate rectangular clip regions. This keeps scroll views and masked hero images from leaking outside their viewport in thumbnails.

For UI images that use `UIShaderParamHelper_vx_common_shader`, the fallback samples `MaskTex` and multiplies it into the image alpha. This makes common `_MASK` shader effects, such as horizontal fade masks, visible in Project thumbnails.

Mask sampling follows the shader's UV path: source texture UV plus `MaskUOffset` / `MaskVOffset`, then `_MaskTex` scale and offset. This keeps packed sprites and mask textures aligned in thumbnails.

Before camera capture, Spine `SkeletonGraphic` components are initialized and asked to update their mesh once. If the camera capture is still empty, the software fallback rasterizes the generated Spine mesh against its atlas texture.

Particle renderer world bounds are intentionally ignored for camera framing because their simulated bounds are often much larger than the UI card itself. Particles may still render if they appear inside the UI prefab's normal RectTransform bounds.

For material-driven effect nodes, the fallback now tries to sample the material texture before falling back to a solid UI rect. Custom-material images with no sprite and no usable texture are skipped so unsupported shader-only particles do not become black or white blocks.

Additive-like material textures are approximated with luminance alpha and additive blending. This keeps common glow, flow, and particle textures readable against the dark thumbnail background instead of turning their dark texture areas into opaque blocks.

When camera capture falls back to software rendering, `Text` and TMP-derived text components are rasterized separately and included in the thumbnail framing bounds. TMP rendering reads all `textInfo.meshInfo` submeshes so multi-language text using fallback font atlases, such as `MultiLanguageTMPText`, can still show in thumbnails.

## Cache

Preview PNGs are stored locally under:

```text
Library/Dragon/UIPrefabPreviewCache
```

Cache keys include prefab GUID, dependency hash, and preview size. Editing a prefab or one of its dependencies creates a new cache entry. Old entries can be removed with `Clear Preview Cache`.

## Current Limitations

- Static default active state only.
- No animation playback or state switching.
- Soft masks are approximated by their RectTransform bounds; feathered edges are not reproduced.
- `UIShaderParamHelper_vx_common_shader.MaskTex` is sampled as a static alpha mask; animated mask offsets, noise, dissolve, and fill-line effects remain approximate.
- Preview generation is skipped while Unity is entering or running Play Mode.
- Spine previews use the prefab's default skin and starting animation at time 0; animated playback is not captured.
- Particles, videos, shader-only effects, or custom render pipelines may be approximate; particle effects do not expand thumbnail framing bounds.
- Very large prefabs can still take a moment to generate the first time they become visible.
