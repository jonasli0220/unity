# Asset Favorites Agent Context

## Scope

- Keep the Unity Editor-only Asset Favorites tool in this directory.
- Keep user-facing behavior and maintenance notes in `README.md`.
- Do not add runtime gameplay code or modify favorited source assets.

## Local Data And Exchange

- Store each user's automatically created local library at `Assets/Content/UI/Library/AssetFavoritesLocalLibrary.asset`; do not treat it as team-authored source data or publish it with reusable scripts.
- On first creation, copy the legacy `Assets/Content/UI/Library/AssetFavoritesLibrary.asset` into the local library when it exists, without deleting or modifying the legacy asset.
- Persist project asset references by Unity GUID, not by direct object reference or absolute path.
- Store generated node-template prefabs only under `Assets/Content/UI/Library/AssetFavoritesNodeTemplates`.
- Export personal favorites as one `.assetfavorites` exchange file. Include custom-folder metadata, asset GUID records, and embedded node-template prefab bytes so node favorites remain usable on another machine.
- Import is additive only: never delete, overwrite, rename, or move local favorites. Keep local placement for duplicate asset GUIDs; deduplicate node templates by prefab-content hash or source global object ID; reuse matching custom folder paths and create unique names only for true conflicts.
- Treat the library asset as relationship data only. Removing an entry or folder must never delete, move, or edit the referenced source asset.
- Removing a node-template favorite may delete its generated template prefab, but must never delete or edit the original scene or prefab-stage node it was captured from.
- Keep folders and favorite entries serializable with stable IDs so nested organization survives renames and asset moves.

## Interaction Rules

- Optimize for designer workflows: Project context-menu add, drag-in, search, adaptive grid/list browsing, multi-select, ping, open, and drag-out must remain direct actions.
- When adding without an explicit destination, classify assets by type automatically.
- Keep the built-in automatic categories limited to Prefab, Node, Sprite, VFX, Animation, Material, and Other. Detect Visual Effect Graph assets directly; classify prefabs as VFX when their root is an effect component or when particle/trail/VFX components are paired with the project's VX/VFX/FX/Effect naming conventions. Preserve custom-folder placement during category migrations.
- Scene or prefab-stage GameObjects can be saved as node templates. They should feel like favorites to the user, while the generated prefab remains implementation detail.
- Node templates must be reusable by dragging or placing into a selected Hierarchy parent, and placed instances should be unpacked so designers are not forced into prefab-instance management.
- Dropping node templates into the Scene view should place them as siblings immediately after the scene node that was selected before the drag started; without a valid scene selection, place them at the scene root.
- Node template and regular prefab favorite previews should reuse `UIPrefabPreviewGenerator` from `Assets/Content/UI/Editor/UIPrefabPreview/UIPrefabPreviewProjectOverlay.cs` and keep only Asset Favorites-specific memory caching here; fall back to Unity's native preview when the UI renderer cannot produce an image.
- Skip already-favorited assets instead of creating duplicate entries.
- Preserve standard Unity selection conventions: Ctrl/Cmd toggles, Shift ranges, single click pings, double click opens.
- Keep the folder tree keyboard navigable and accept dragged favorite entries or Project assets.
- Treat preview size as one continuous browsing control, not separate clickable list/grid modes. At 64 px the content switches automatically to a compact single-line list; above 64 px it is a grid. Keep the endpoint icons informational only, support Ctrl/Cmd + mouse wheel over the content area, preserve ordinary wheel scrolling, and cap previews at 512 x 512.
- Keep the visual hierarchy close to Unity's Project browser: a fixed-width right-aligned search field, a clear left-side create-folder action, a compact folder tree with counts, an unboxed preview grid, and a shared bottom status bar with item count plus view/zoom controls.
- Keep Library import/export actions in the top toolbar beside a fixed-width Project-style search field.

## Validation

- Validate on Unity 2021.3.8f1 and avoid APIs introduced after Unity 2021 LTS.
- After changes, confirm Editor compilation, menu registration, local library creation, additive import behavior, and node-template restoration through the Codex Unity Bridge when available.
- Do not publish mutable local library assets, exported `.assetfavorites` files, or generated node templates to the reusable GitHub scripts repository; publish this tool's scripts, `.meta` files, and documentation only.
