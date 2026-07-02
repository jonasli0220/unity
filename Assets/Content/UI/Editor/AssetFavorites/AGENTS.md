# Asset Favorites Agent Context

## Scope

- Keep the Unity Editor-only Asset Favorites tool in this directory.
- Keep user-facing behavior and maintenance notes in `README.md`.
- Do not add runtime gameplay code or modify favorited source assets.

## Shared Data

- Store the team-shared favorites library at `Assets/Content/UI/Library/AssetFavoritesLibrary.asset`.
- Persist asset references by Unity GUID, not by direct object reference or absolute path.
- Treat the library asset as relationship data only. Removing an entry or folder must never delete, move, or edit the referenced source asset.
- Keep folders and favorite entries serializable with stable IDs so nested organization survives renames and asset moves.

## Interaction Rules

- Optimize for designer workflows: Project context-menu add, drag-in, search, grid/list views, multi-select, ping, open, and drag-out must remain direct actions.
- When adding without an explicit destination, classify assets by type automatically.
- Skip already-favorited assets instead of creating duplicate entries.
- Preserve standard Unity selection conventions: Ctrl/Cmd toggles, Shift ranges, single click pings, double click opens.
- Keep the folder tree keyboard navigable and accept dragged favorite entries or Project assets.

## Validation

- Validate on Unity 2021.3.8f1 and avoid APIs introduced after Unity 2021 LTS.
- After changes, confirm Editor compilation, menu registration, and shared library creation through the Codex Unity Bridge when available.
- Do not publish the mutable library asset to the reusable GitHub scripts repository; publish this tool's scripts, `.meta` files, and documentation only.

